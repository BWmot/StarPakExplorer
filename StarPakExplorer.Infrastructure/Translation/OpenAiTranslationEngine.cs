using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

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
            tool_choice = "none",
            messages = new object[]
            {
                new { role = "system", content = BuildSystemPrompt(settings.TargetLanguage) },
                new { role = "user",   content = BuildUserPrompt(sourceTexts, glossary, settings.TargetLanguage) }
            }
        };

        var jsonBody = JsonSerializer.Serialize(payload);

        try
        {
            var responseText = await SendHttpRequestAsync(url, settings.OpenAi.ApiKey, jsonBody, cancellationToken)
                .ConfigureAwait(false);

            // Some proxies may return SSE even with stream=false – handle both formats.
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

            // Reasoning models (DeepSeek R1/V4) may only return reasoning_content
            // before the connection drops.  Try to find a JSON array embedded in
            // the reasoning text (many models place the answer at the end).
            var extractedArray = TryExtractJsonArrayFromText(responseText);
            if (extractedArray is not null)
            {
                return ParseArrayResponse(extractedArray, sourceTexts.Count);
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
        catch (HttpRequestException ex)
        {
            var inner = ex.InnerException;
            var detail = inner?.Message ?? ex.Message;
            throw new InvalidOperationException(
                $"API 请求失败。URL: {url}\n网络错误: {detail}");
        }
        catch (Exception ex)
        {
            var detail = UnwrapExceptionMessage(ex);
            throw new InvalidOperationException(
                $"翻译请求异常。URL: {url}\n异常类型: {ex.GetType().FullName}\n错误: {detail}");
        }
    }

    // ── Raw HTTP (HttpClient with SSL bypass for localhost) ───────
    // The local proxy (127.0.0.1:8787) may use a self-signed certificate
    // and closes the connection early, which can cause "ResponseEnded".
    // SocketsHttpHandler with ResponseHeadersRead gives us direct stream
    // access, and we tolerate read errors (same as the old HttpWebRequest path).

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            // Keep proxy auto-detection disabled for localhost.
            UseProxy = false,
            // Accept self-signed or invalid certs for localhost.
            ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None)
                    return true;

                var host = request?.RequestUri?.Host;
                if (host is not null &&
                    (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                     host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                     host.Equals("::1", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                return false;
            }
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    private static async Task<string> SendHttpRequestAsync(
        string url, string apiKey, string jsonBody, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        // ResponseHeadersRead: start reading the stream as soon as headers
        // arrive, without waiting for the entire body to be buffered (which
        // is what fails with "ResponseEnded" when the proxy closes early).
        using var response = await HttpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await ReadResponseBodyGracefullyAsync(response, ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"API 返回 HTTP {(int)response.StatusCode}:\n{Truncate(errBody, 500)}");
        }

        return await ReadResponseBodyGracefullyAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Read response stream to string.  Tolerates IOException / HttpRequestException
    /// (connection closed mid-stream) – returns whatever data was received before the error.
    /// </summary>
    private static async Task<string> ReadResponseBodyGracefullyAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        Stream stream;
        try
        {
            stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return "";
        }

        return await ReadStreamToEndGracefullyAsync(stream, ct).ConfigureAwait(false);
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
#if NET
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
#else
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
#endif
            {
                await ms.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Server closed the connection mid-stream – keep what we have.
        }
        catch (HttpRequestException)
        {
            // Server closed the connection before response was complete –
            // keep what we have (common with streaming + reasoning models).
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
        var contentBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        string? finalMessageContent = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = trimmed["data:".Length..].Trim();
            if (data == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                    choices.ValueKind != JsonValueKind.Array ||
                    choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];

                // Non-streaming format mixed into SSE: message.content (final answer).
                if (choice.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var msgContent) &&
                    msgContent.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(msgContent.GetString()))
                {
                    finalMessageContent = msgContent.GetString();
                    continue;
                }

                if (!choice.TryGetProperty("delta", out var delta))
                    continue;

                // Standard streaming: delta.content
                if (delta.TryGetProperty("content", out var deltaContent) &&
                    deltaContent.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(deltaContent.GetString()))
                {
                    contentBuilder.Append(deltaContent.GetString());
                    continue;
                }

                // Reasoning models (DeepSeek R1/V4): delta.reasoning_content.
                // Collect as fallback — some proxies merge the final answer
                // into reasoning_content, or the connection drops before
                // content chunks arrive.
                if (delta.TryGetProperty("reasoning_content", out var rc) &&
                    rc.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(rc.GetString()))
                {
                    reasoningBuilder.Append(rc.GetString());
                    continue;
                }
            }
            catch
            {
                // unparseable chunk – ignore
            }
        }

        // Priority: 1) collected delta.content  2) final message.content
        // 3) reasoning_content fallback  4) raw SSE text (caller will report error)
        if (contentBuilder.Length > 0)
            return contentBuilder.ToString();

        if (!string.IsNullOrEmpty(finalMessageContent))
            return finalMessageContent;

        if (reasoningBuilder.Length > 0)
            return reasoningBuilder.ToString();

        return sseText;
    }

    /// <summary>
    /// Reasoning models (DeepSeek R1/V4) may only return reasoning_content
    /// before the proxy closes the connection.  Try to find the final JSON
    /// array embedded in the text – many models place the answer at the end
    /// of their reasoning, sometimes wrapped in markdown fences.
    /// </summary>
    private static string? TryExtractJsonArrayFromText(string text)
    {
        // First, try to find JSON array inside markdown code fences at the
        // very end of the text (common pattern for reasoning models).
        var fenceStart = text.LastIndexOf("```json", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var fenceEnd = text.IndexOf("```", fenceStart + 7, StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                var inside = text[(fenceStart + 7)..fenceEnd].Trim();
                if (inside.StartsWith('['))
                    return inside;
            }
        }

        // Try any code fence.
        var anyFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (anyFence >= 0)
        {
            var after = text[(anyFence + 3)..].TrimStart();
            var nl = after.IndexOf('\n');
            if (nl >= 0)
                after = after[(nl + 1)..];
            var closeFence = after.LastIndexOf("```", StringComparison.Ordinal);
            if (closeFence >= 0)
            {
                var inside = after[..closeFence].Trim();
                if (inside.StartsWith('['))
                    return inside;
            }
        }

        // Last resort: find the last JSON array in the raw text.
        // Walk backwards looking for "]".
        var lastBracket = text.LastIndexOf(']');
        if (lastBracket < 0)
            return null;

        var depth = 0;
        var start = -1;
        for (int i = lastBracket; i >= 0; i--)
        {
            if (text[i] == ']')
                depth++;
            else if (text[i] == '[')
            {
                depth--;
                if (depth == 0)
                {
                    start = i;
                    break;
                }
            }
        }

        if (start < 0 || start >= lastBracket)
            return null;

        var candidate = text[start..(lastBracket + 1)];
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return candidate;
        }
        catch
        {
            // Not valid JSON
        }

        return null;
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

    private static string BuildSystemPrompt(string targetLanguage)
    {
        return $"""
You are a Starbound mod translation expert. You output translation results directly — you have NO tools, NO retrieval, NO compression. Everything you need is in the user message below.

Translate the following English game text into {targetLanguage} ({DescribeLanguage(targetLanguage)}).
Rules:
1. Keep gameplay terminology consistent.
2. Keep item names short and punchy.
3. Keep the tone of descriptive text close to the original.
4. Do not translate obvious internal IDs or code-like text.
5. Preserve color codes, escape sequences, placeholders, and special markers.
6. Output only a JSON array. No explanations, no markdown, no extra text.
""";
    }

    private static string BuildUserPrompt(IReadOnlyList<string> sourceTexts, IReadOnlyDictionary<string, string> glossary, string targetLanguage)
    {
        // Only include glossary entries whose keys appear as substrings in any
        // source text.  Sending a massive glossary triggers context compression
        // in some proxies (DeepSeek V4), which confuses the model into thinking
        // it needs to "retrieve" compressed content.
        var relevantGlossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (glossary.Count <= 50)
        {
            // 大小写不敏感去重，避免 "apex"/"Apex" 等仅大小写不同的重复源词导致异常。
            foreach (var (key, value) in glossary)
            {
                relevantGlossary.TryAdd(key, value);
            }
        }
        else
        {
            foreach (var (key, value) in glossary)
            {
                foreach (var text in sourceTexts)
                {
                    if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
                    {
                        relevantGlossary[key] = value;
                        break;
                    }
                }

                if (relevantGlossary.Count >= 50)
                    break;
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"The content below is NOT compressed. Translate this JSON array from English to {targetLanguage} ({DescribeLanguage(targetLanguage)}):");
        builder.AppendLine("Return ONLY a JSON array of translated strings with the same length and order.");
        builder.AppendLine("Source array:");
        builder.AppendLine(JsonSerializer.Serialize(sourceTexts));
        if (relevantGlossary.Count > 0)
        {
            builder.AppendLine("Glossary:");
            builder.AppendLine(JsonSerializer.Serialize(relevantGlossary));
        }
        return builder.ToString();
    }

    /// <summary>把 BCP-47 语言代码转成人类可读的语言名，便于 LLM 理解目标语言。</summary>
    private static string DescribeLanguage(string languageCode)
    {
        var code = languageCode.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return "Simplified Chinese";
        }

        return code.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" => "Simplified Chinese",
            "zh-tw" or "zh-hk" or "zh-hant" => "Traditional Chinese",
            "ja" => "Japanese",
            "ko" => "Korean",
            "en" => "English",
            "de" => "German",
            "fr" => "French",
            "es" => "Spanish",
            "ru" => "Russian",
            _ => code
        };
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
