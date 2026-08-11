using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

/// <summary>
/// Google 翻译免费引擎（非官方）。
/// 使用 Google 网页翻译小组件背后的同一接口：
/// https://translate.googleapis.com/translate_a/single?client=gtx
/// 无需 API Key / Project ID / Service Account，零配置即可使用。
///
/// 注意：
/// - 该接口非官方，可能被限流或调整，请保持较低请求频率。
/// - 接口本身不支持官方术语表，术语表通过本地占位符技术套用。
/// </summary>
public sealed class GoogleFreeTranslationEngine : ITranslationEngine
{
    private const string EndpointBase = "https://translate.googleapis.com/translate_a/single";
    private const string SourceLanguage = "en";

    // 免费接口对默认 HttpClient UA 较敏感，模拟浏览器 UA 降低被拒概率。
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>连续请求之间的小间隔（毫秒），避免触发限流。</summary>
    private const int RequestIntervalMs = 120;

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    public TranslationEngineType EngineType => TranslationEngineType.GoogleFree;

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> sourceTexts,
        TranslationProviderSettings settings,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken)
    {
        var results = new List<string>(sourceTexts.Count);

        foreach (var sourceText in sourceTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (textToSend, placeholders) = ApplyGlossaryLocally(sourceText, glossary);
            var translated = await TranslateSingleAsync(textToSend, settings.TargetLanguage, cancellationToken).ConfigureAwait(false);
            results.Add(RestoreGlossary(translated, placeholders));

            await Task.Delay(TimeSpan.FromMilliseconds(RequestIntervalMs), cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private static async Task<string> TranslateSingleAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var url = $"{EndpointBase}?client=gtx&sl={SourceLanguage}&tl={targetLanguage}&dt=t&q={WebUtility.UrlEncode(text)}";

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                // 限流(429)或服务器错误(5xx) → 退避后重试。
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    lastError = new InvalidOperationException(
                        $"Google free translate returned HTTP {(int)response.StatusCode}.");
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Google free translate request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return ParseTranslation(json);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException(
            $"Google free translate failed after retries: {lastError?.Message}", lastError);
    }

    /// <summary>
    /// 解析免费接口响应。响应结构：
    /// [ [[translatedSegment, originalSegment, ...], ...], "en", "zh-CN", ... ]
    /// 将所有已翻译句子段拼接成完整译文。
    /// </summary>
    private static string ParseTranslation(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return "";
        }

        var sentences = root[0];
        if (sentences.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var sentence in sentences.EnumerateArray())
        {
            // sentence = [translated, original, transliteration, ...]
            if (sentence.ValueKind == JsonValueKind.Array &&
                sentence.GetArrayLength() > 0 &&
                sentence[0].ValueKind == JsonValueKind.String)
            {
                builder.Append(sentence[0].GetString());
            }
        }

        return builder.ToString();
    }

    // ── 本地术语表（免费接口无官方术语表支持）────────────────────────────

    /// <summary>
    /// 将出现在文本中的术语表源词替换为唯一占位符，让机器翻译保持原样；
    /// 翻译完成后按占位符映射恢复为目标词，从而保证术语一致。
    /// </summary>
    private static (string Text, Dictionary<int, string> Placeholders) ApplyGlossaryLocally(
        string text,
        IReadOnlyDictionary<string, string> glossary)
    {
        if (string.IsNullOrWhiteSpace(text) || glossary is null || glossary.Count == 0)
        {
            return (text, new Dictionary<int, string>());
        }

        // 长词优先，确保同一位置匹配最长术语。
        var terms = glossary
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderByDescending(pair => pair.Key.Length)
            .ToList();

        if (terms.Count == 0)
        {
            return (text, new Dictionary<int, string>());
        }

        // 术语表里可能出现仅大小写不同的重复源词（如 "apex"/"Apex"）。
        // 用 TryAdd 去重（长词优先、先到先得），避免 ToDictionary 抛出重复键异常。
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            lookup.TryAdd(term.Key, term.Value);
        }

        var pattern = string.Join("|", terms.Select(pair => @"\b" + Regex.Escape(pair.Key) + @"\b"));
        var placeholders = new Dictionary<int, string>();
        var counter = 0;

        var builder = Regex.Replace(text, pattern, match =>
        {
            var token = $"[[[{counter}]]]";
            placeholders[counter] = lookup[match.Value];
            counter++;
            return token;
        }, RegexOptions.IgnoreCase);

        return (builder, placeholders);
    }

    private static string RestoreGlossary(string translated, IReadOnlyDictionary<int, string> placeholders)
    {
        var result = translated;
        foreach (var (key, value) in placeholders)
        {
            result = result.Replace($"[[[{key}]]]", value);
        }

        // 清理 Google 未能保留的残留占位符。
        return Regex.Replace(result, @"\[\[\[\d+\]\]\]", "");
    }
}
