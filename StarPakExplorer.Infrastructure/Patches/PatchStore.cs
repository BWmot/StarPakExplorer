using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Infrastructure.Patches;

public sealed class PatchStore : IPatchStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppSettings appSettings;

    public PatchStore(AppSettings appSettings)
    {
        this.appSettings = appSettings;
    }

    public string GetPatchRoot()
    {
        var configuredRoot = appSettings.PatchRootDirectory;
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarPakExplorer", "Patches")
            : configuredRoot;

        Directory.CreateDirectory(root);
        return root;
    }

    public string GetPatchKey(PakManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.WorkshopId))
        {
            return $"workshop_{manifest.WorkshopId}";
        }

        var baseName = !string.IsNullOrWhiteSpace(manifest.ModName)
            ? manifest.ModName!
            : Path.GetFileNameWithoutExtension(manifest.PakPath);
        var hash = ShortHash(manifest.PakPath);
        return $"{SanitizeSegment(baseName)}_{hash}";
    }

    public string GetPatchSetDirectory(string patchKey)
    {
        return Path.Combine(GetPatchRoot(), patchKey);
    }

    public string GetPatchFilePath(string patchKey, string relativePath)
    {
        return Path.Combine(GetPatchSetDirectory(patchKey), NormalizeRelativePath(relativePath));
    }

    public async Task EnsurePatchSetAsync(PakManifest manifest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patchKey = GetPatchKey(manifest);
        var patchSetDirectory = GetPatchSetDirectory(patchKey);
        Directory.CreateDirectory(patchSetDirectory);

        var existing = await ReadManifestAsync(patchKey, cancellationToken);
        var now = DateTimeOffset.Now;
        var next = new PatchSetManifest
        {
            PatchKey = patchKey,
            WorkshopId = manifest.WorkshopId,
            ModName = manifest.ModName,
            Author = manifest.Author,
            SourcePakPath = manifest.PakPath,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };

        await WriteManifestAsync(next, cancellationToken);
    }

    public async Task SaveTextAsync(
        string patchKey,
        string relativePath,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var patchFilePath = GetPatchFilePath(patchKey, relativePath);
        var directory = Path.GetDirectoryName(patchFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(patchFilePath, content, encoding, cancellationToken);
        await TouchManifestAsync(patchKey, cancellationToken);
    }

    public async Task SaveReplacementAsync(
        string patchKey,
        string relativePath,
        string replacementPath,
        CancellationToken cancellationToken)
    {
        var patchFilePath = GetPatchFilePath(patchKey, relativePath);
        var directory = Path.GetDirectoryName(patchFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var source = File.OpenRead(replacementPath);
        await using var destination = File.Create(patchFilePath);
        await source.CopyToAsync(destination, cancellationToken);

        await TouchManifestAsync(patchKey, cancellationToken);
    }

    public Task RemoveAsync(string patchKey, string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patchFilePath = GetPatchFilePath(patchKey, relativePath);
        if (File.Exists(patchFilePath))
        {
            File.Delete(patchFilePath);
            PruneEmptyDirectories(Path.GetDirectoryName(patchFilePath), GetPatchSetDirectory(patchKey));
        }

        return TouchManifestAsync(patchKey, cancellationToken);
    }

    public async Task<IReadOnlyList<PatchSetSummary>> GetPatchSetsAsync(int maxEntries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = GetPatchRoot();
        if (!Directory.Exists(root))
        {
            return [];
        }

        var patchSets = new List<PatchSetSummary>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var summary = await ReadPatchSetSummaryAsync(manifestPath, cancellationToken);
                if (summary is not null)
                {
                    patchSets.Add(summary);
                }
            }
            catch
            {
                // Skip broken patch sets.
            }
        }

        return patchSets
            .OrderByDescending(item => item.UpdatedAt)
            .Take(Math.Max(0, maxEntries))
            .ToList();
    }

    public Task<IReadOnlyList<PatchFileRecord>> GetFilesAsync(string patchKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patchSetDirectory = GetPatchSetDirectory(patchKey);
        if (!Directory.Exists(patchSetDirectory))
        {
            return Task.FromResult<IReadOnlyList<PatchFileRecord>>([]);
        }

        var files = new List<PatchFileRecord>();
        foreach (var filePath in Directory.EnumerateFiles(patchSetDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsManifestFile(filePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(patchSetDirectory, filePath).Replace(Path.DirectorySeparatorChar, '/');
            var info = new FileInfo(filePath);
            files.Add(new PatchFileRecord
            {
                RelativePath = relativePath,
                FullPath = filePath,
                SizeBytes = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc
            });
        }

        return Task.FromResult<IReadOnlyList<PatchFileRecord>>(files
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    public Task DeletePatchSetAsync(string patchKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var patchSetDirectory = GetPatchSetDirectory(patchKey);
        if (Directory.Exists(patchSetDirectory))
        {
            Directory.Delete(patchSetDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private async Task TouchManifestAsync(string patchKey, CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(patchKey, cancellationToken);
        if (manifest is null)
        {
            manifest = new PatchSetManifest
            {
                PatchKey = patchKey,
                CreatedAt = DateTimeOffset.Now
            };
        }

        manifest.UpdatedAt = DateTimeOffset.Now;
        await WriteManifestAsync(manifest, cancellationToken);
    }

    private async Task<PatchSetManifest?> ReadManifestAsync(string patchKey, CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath(patchKey);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<PatchSetManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PatchSetSummary?> ReadPatchSetSummaryAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<PatchSetManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.PatchKey))
        {
            return null;
        }

        var patchSetDirectory = Path.GetDirectoryName(manifestPath) ?? GetPatchRoot();
        var files = Directory.Exists(patchSetDirectory)
            ? Directory.EnumerateFiles(patchSetDirectory, "*", SearchOption.AllDirectories).Where(file => !IsManifestFile(file)).ToList()
            : [];

        long totalBytes = 0;
        foreach (var filePath in files)
        {
            try
            {
                totalBytes += new FileInfo(filePath).Length;
            }
            catch
            {
                // ignore inaccessible file
            }
        }

        return new PatchSetSummary
        {
            PatchKey = manifest.PatchKey,
            WorkshopId = manifest.WorkshopId,
            ModName = manifest.ModName,
            Author = manifest.Author,
            SourcePakPath = manifest.SourcePakPath,
            CreatedAt = manifest.CreatedAt,
            UpdatedAt = manifest.UpdatedAt,
            FileCount = files.Count,
            TotalBytes = totalBytes
        };
    }

    private async Task WriteManifestAsync(PatchSetManifest manifest, CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath(manifest.PatchKey);
        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private string GetManifestPath(string patchKey)
    {
        return Path.Combine(GetPatchSetDirectory(patchKey), "manifest.json");
    }

    private static bool IsManifestFile(string filePath)
    {
        return string.Equals(Path.GetFileName(filePath), "manifest.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void PruneEmptyDirectories(string? directory, string stopDirectory)
    {
        while (!string.IsNullOrWhiteSpace(directory) &&
               !string.Equals(directory, stopDirectory, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(directory) &&
               !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "patch" : result;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
}
