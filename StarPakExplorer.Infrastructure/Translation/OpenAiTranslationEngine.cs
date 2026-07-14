using System.Net;
using System.Text;
using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

#pragma warning disable SYSLIB0014 // WebRequest is obsolete – intentionally bypassing HttpClient for proxy compatibility

namespace StarPakExplorer.Infrastructure.Translation;

public sealed class OpenAiTranslationEngine : ITranslationEngine
{
    public TranslationEngineType EngineType => TranslationEngineType.OpenAI;

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> sourceTexts,
        TranslationProviderSettings settings,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenAi.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        var model = string.IsNullOrWhiteSpace(settings.OpenAi.Model) ? "gpt-4.1-mini" : settings.OpenAi.Model;
        var baseUrl = string.IsNullOrWhiteSpace(settings.OpenAi.BaseUrl)
            ? "https://api.openai.com/v1"
            : settings.OpenAi.BaseUrl;

        var url = CombineUrl(baseUrl, "chat/completions");

        var payload = new
        {
            model,
            temperature = 0.1,
            stream = true,
            messages = new object[]
            {
                new { role = "system", content = BuildSystemPrompt() },
                new { role = "user",   content = BuildUserPrompt(sourceTexts, glossary) }
            }
        };

        var jsonBody = JsonSerializer.Serialize(payload);

        try
        {
            var responseText = await SendHttpRequestAsync(url, settings.OpenAi.ApiKey, jsonBody, cancellationToken)
                .ConfigureAwait(false);

            // The proxy may return SSE even with stream=false – handle both formats.
            if (IsSseResponse(responseText))
            {
                responseText = ExtractFromSseResponse(responseText);
            }

            // After SSE extraction, the result may already be the LLM's direct
            // output (e.g. a JSON array of translated strings) rather than a
            // wrapped OpenAI chat-completion object.  Try direct array parse first.
            var trimmedResponse = StripCodeFences(responseText).Trim();
            if (trimmedResponse.StartsWith('['))
            {
                return ParseArrayResponse(trimmedResponse, sourceTexts.Count);
            }

            if (!TryParseOpenAiJson(responseText, out var content, out var parseError))
            {
                throw new InvalidOperationException(
                    $"无法解析 OpenAI 响应。\n原始响应: {Truncate(responseText, 500)}\n解析错误: {parseError}");
            }

            return ParseArrayResponse(content, sourceTexts.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WebException ex)
        {
            var errorBody = "";
            try
            {
                if (ex.Response is HttpWebResponse errorResp)
                    errorBody = await ReadStreamToEndGracefullyAsync(
                        errorResp.GetResponseStream(), cancellationToken).ConfigureAwait(false);
            }
            catch { }

            var statusCode = ex.Response is HttpWebResponse r ? (int)r.StatusCode : 0;
            throw new InvalidOperationException(
                $"API 请求失败。URL: {url}\n" +
                (statusCode > 0 ? $"HTTP {statusCode}\n" : "") +
                (string.IsNullOrEmpty(errorBody) ? $"网络错误: {ex.Message}" : $"服务端返回: {Truncate(errorBody, 500)}"));
        }
        catch (Exception ex)
        {
            var detail = UnwrapExceptionMessage(ex);
            throw new InvalidOperationException(
                $"翻译请求异常。URL: {url}\n异常类型: {ex.GetType().FullName}\n错误: {detail}");
        }
    }

    // ── Raw HTTP (HttpWebRequest) ─────────────────────────────────
    // Bypasses .NET's HttpClient Content-Length validation entirely.
    // The local proxy (127.0.0.1:8787) closes the connection early,
    // which HttpClient rejects as "ResponseEnded".  HttpWebRequest
    // gives us direct stream access so we can read partial data.

    private static async Task<string> SendHttpRequestAsync(
        string url, string apiKey, string jsonBody, CancellationToken ct)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Accept = "application/json";
        request.Headers["Authorization"] = $"Bearer {apiKey}";
        request.Timeout = 300_000;            // 5 minutes
        request.ReadWriteTimeout = 300_000;
        request.KeepAlive = false;
        request.ProtocolVersion = HttpVersion.Version11;
        request.Proxy = null;                  // bypass system proxy detection
        request.AllowReadStreamBuffering = false;

        // Write request body
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        using (var reqStream = await request.GetRequestStreamAsync().ConfigureAwait(false))
        {
            await reqStream.WriteAsync(bodyBytes, 0, bodyBytes.Length, ct).ConfigureAwait(false);
        }

        using var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);

        // Non-200 → read error body then throw
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var errBody = await ReadStreamToEndGracefullyAsync(
                response.GetResponseStream(), ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"API 返回 HTTP {(int)response.StatusCode}:\n{Truncate(errBody, 500)}");
        }

        return await ReadStreamToEndGracefullyAsync(
            response.GetResponseStream(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Read stream to string.  Tolerates IOException (connection closed
    /// mid-stream) and ObjectDisposedException – returns whatever data
    /// was received before the error.
    /// </summary>
    private static async Task<string> ReadStreamToEndGracefullyAsync(
        Stream? stream, CancellationToken ct)
    {
        if (stream is null)
            return "";

        var ms = new MemoryStream();
        var buffer = new byte[8192];

        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)
                .ConfigureAwait(false)) > 0)
            {
                await ms.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Server closed the connection mid-stream – keep what we have.
        }
        catch (ObjectDisposedException)
        {
            // Stream already disposed – keep what we have.
        }

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    private static bool IsSseResponse(string text)
    {
        return text.TrimStart().StartsWith("data:", StringComparison.Ordinal);
    }

    private static string ExtractFromSseResponse(string sseText)
    {
        var lines = sseText.Split('\n');
        var allContent = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = trimmed["data:".Length..].Trim();
            if (data == "[DONE]")
                continue;

            // Try extracting "message.content" or "delta.content" from JSON chunk.
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    // Non-streaming: message.content
                    if (choice.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var msgContent))
                    {
                        allContent.Append(msgContent.GetString());
                        continue;
                    }
                    // Streaming: delta.content
                    if (choice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var deltaContent))
                    {
                        allContent.Append(deltaContent.GetString());
                        continue;
                    }
                }
            }
            catch
            {
                // unparseable chunk – ignore
            }
        }

        return allContent.Length > 0 ? allContent.ToString() : sseText;
    }

    private static bool TryParseOpenAiJson(string json, out string content, out string error)
    {
        content = "";
        error = "";

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // Must be a JSON object – arrays are handled by caller via ParseArrayResponse.
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = $"Expected JSON object but got {root.ValueKind}.";
                return false;
            }

            if (root.TryGetProperty("error", out var errorObj))
            {
                error = errorObj.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? "Unknown API error"
                    : errorObj.ToString();
                return false;
            }

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                error = "Response missing 'choices' array.";
                return false;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message))
            {
                error = "Choice missing 'message' object.";
                return false;
            }

            content = message.TryGetProperty("content", out var c) &&
                      c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (InvalidOperationException ex)
        {
            // JsonDocument operations can throw InvalidOperationException for type mismatches.
            error = ex.Message;
            return false;
        }
    }

    private static string BuildSystemPrompt()
    {
        return """
You are a Starbound mod translation expert.
Translate the following English game text into Simplified Chinese.
Rules:
1. Keep gameplay terminology consistent.
2. Keep item names short and punchy.
3. Keep the tone of descriptive text close to the original.
4. Do not translate obvious internal IDs or code-like text.
5. Preserve color codes, escape sequences, placeholders, and special markers.
6. Output only a JSON array. No explanations, no markdown, no extra text.
""";
    }

    private static string BuildUserPrompt(IReadOnlyList<string> sourceTexts, IReadOnlyDictionary<string, string> glossary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Translate the following JSON array from English to Simplified Chinese.");
        builder.AppendLine("Return ONLY a JSON array of translated strings with the same length and order.");
        builder.AppendLine("Source array:");
        builder.AppendLine(JsonSerializer.Serialize(sourceTexts));
        builder.AppendLine("Glossary:");
        builder.AppendLine(JsonSerializer.Serialize(glossary));
        return builder.ToString();
    }

    private static IReadOnlyList<string> ParseArrayResponse(string content, int expectedCount)
    {
        var trimmed = StripCodeFences(content).Trim();
        using var document = JsonDocument.Parse(trimmed);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("OpenAI response did not return a JSON array.");
        }

        var results = document.RootElement.EnumerateArray()
            .Select(element => WebUtility.HtmlDecode(element.GetString() ?? element.ToString()))
            .ToList();

        if (results.Count != expectedCount)
        {
            throw new InvalidOperationException($"OpenAI returned {results.Count} translations for {expectedCount} source strings.");
        }

        return results;
    }

    private static string StripCodeFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence <= firstNewline)
        {
            return trimmed;
        }

        return trimmed[(firstNewline + 1)..lastFence].Trim();
    }

    private static string CombineUrl(string baseUrl, string relative)
    {
        return $"{baseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }

    private static string UnwrapExceptionMessage(Exception ex)
    {
        var parts = new List<string>();
        var current = ex;
        while (current is not null)
        {
            var msg = current.Message.Trim();
            if (!string.IsNullOrEmpty(msg) && (parts.Count == 0 || parts[^1] != msg))
            {
                parts.Add(msg);
            }
            current = current.InnerException;
        }
        return string.Join(" → ", parts);
    }
}
