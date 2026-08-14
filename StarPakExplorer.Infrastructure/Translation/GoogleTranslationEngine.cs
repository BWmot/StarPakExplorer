using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Translation;

public sealed class GoogleTranslationEngine : ITranslationEngine
{
    private static readonly HttpClient HttpClient = new();

    public TranslationEngineType EngineType => TranslationEngineType.Google;

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> sourceTexts,
        TranslationProviderSettings settings,
        TranslationGlossary glossary,
        CancellationToken cancellationToken)
    {
        var google = settings.Google;
        if (string.IsNullOrWhiteSpace(google.ProjectId))
        {
            throw new InvalidOperationException("Google project id is not configured.");
        }

        if (string.IsNullOrWhiteSpace(google.ServiceAccountJsonPath) || !File.Exists(google.ServiceAccountJsonPath))
        {
            throw new FileNotFoundException("Google service account JSON file was not found.", google.ServiceAccountJsonPath);
        }

        var credentials = await ReadServiceAccountAsync(google.ServiceAccountJsonPath, cancellationToken).ConfigureAwait(false);
        var accessToken = await GetAccessTokenAsync(credentials, cancellationToken).ConfigureAwait(false);

        var request = new Dictionary<string, object?>
        {
            ["contents"] = sourceTexts,
            ["mimeType"] = "text/plain",
            ["sourceLanguageCode"] = "en",
            ["targetLanguageCode"] = settings.TargetLanguage
        };

        if (!string.IsNullOrWhiteSpace(google.GlossaryName))
        {
            request["glossaryConfig"] = new Dictionary<string, object?>
            {
                ["glossary"] = BuildGlossaryResourceName(google, credentials.ProjectId)
            };
        }

        var endpoint = $"https://translation.googleapis.com/v3/projects/{google.ProjectId}/locations/{google.Location.Trim()}:translateText";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google translate request failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("translations", out var translations))
        {
            throw new InvalidOperationException("Google response did not include translations.");
        }

        var results = translations.EnumerateArray()
            .Select(element =>
            {
                if (element.TryGetProperty("translatedText", out var translated))
                {
                    return WebUtility.HtmlDecode(translated.GetString() ?? "");
                }

                return "";
            })
            .ToList();

        if (results.Count != sourceTexts.Count)
        {
            throw new InvalidOperationException($"Google returned {results.Count} translations for {sourceTexts.Count} source strings.");
        }

        return results;
    }

    private static string BuildGlossaryResourceName(GoogleTranslationSettings google, string projectId)
    {
        if (google.GlossaryName.Contains('/'))
        {
            return google.GlossaryName;
        }

        var location = string.IsNullOrWhiteSpace(google.Location) ? "global" : google.Location.Trim();
        return $"projects/{projectId}/locations/{location}/glossaries/{google.GlossaryName}";
    }

    private static async Task<ServiceAccountCredentials> ReadServiceAccountAsync(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new ServiceAccountCredentials
        {
            ClientEmail = root.GetProperty("client_email").GetString() ?? "",
            PrivateKey = root.GetProperty("private_key").GetString() ?? "",
            TokenUri = root.TryGetProperty("token_uri", out var tokenUri)
                ? tokenUri.GetString() ?? "https://oauth2.googleapis.com/token"
                : "https://oauth2.googleapis.com/token",
            ProjectId = root.TryGetProperty("project_id", out var projectId)
                ? projectId.GetString() ?? ""
                : ""
        };
    }

    private static async Task<string> GetAccessTokenAsync(ServiceAccountCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.ClientEmail) || string.IsNullOrWhiteSpace(credentials.PrivateKey))
        {
            throw new InvalidOperationException("Google service account JSON is missing client_email or private_key.");
        }

        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = iat + 3600;
        var header = Base64UrlEncode(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.Serialize(new
        {
            iss = credentials.ClientEmail,
            scope = "https://www.googleapis.com/auth/cloud-translation",
            aud = credentials.TokenUri,
            exp,
            iat
        }));

        var unsignedJwt = $"{header}.{payload}";
        var signedJwt = SignJwt(unsignedJwt, credentials.PrivateKey);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = signedJwt
        });

        using var response = await HttpClient.PostAsync(credentials.TokenUri, form, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google token request failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        return document.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google token response missing access_token.");
    }

    private static string SignJwt(string unsignedJwt, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var bytes = Encoding.UTF8.GetBytes(unsignedJwt);
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsignedJwt}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(string value)
    {
        return Base64UrlEncode(Encoding.UTF8.GetBytes(value));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class ServiceAccountCredentials
    {
        public string ClientEmail { get; set; } = "";
        public string PrivateKey { get; set; } = "";
        public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";
        public string ProjectId { get; set; } = "";
    }
}
