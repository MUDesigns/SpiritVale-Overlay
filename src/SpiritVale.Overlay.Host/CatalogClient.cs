using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace SpiritVale.Overlay.Host;

internal sealed class CatalogMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string Filename { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public long DownloadCount { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
}

internal sealed class AppArtifact
{
    public string Filename { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = "";
}

internal sealed class AppRelease
{
    public string Version { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string? PublishedAt { get; set; }
    public AppArtifact? Installer { get; set; }
    public AppArtifact? Portable { get; set; }
}

internal sealed class CatalogFile
{
    public List<CatalogMod> Mods { get; set; } = new();
    public AppRelease? App { get; set; }
    public bool? Paused { get; set; }
}

internal sealed class CatalogCheckResult
{
    public int Checked { get; init; }
    public int Updates { get; init; }
    public string Message { get; init; } = "";
}

internal sealed class AppUpdateInfo
{
    public string CurrentVersion { get; init; } = "";
    public string? LatestVersion { get; init; }
    public bool UpdateAvailable { get; init; }
    public string? Changelog { get; init; }
    public AppArtifact? Installer { get; init; }
    public AppArtifact? Portable { get; init; }
}

internal sealed class CatalogClient : IDisposable
{
    public const string DefaultBaseUrl = "https://www.spiritvalemods.com";
    public const string UserAgent = "SpiritVale-Plugin-Manager/0.3.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private CatalogFile? _cache;
    private AppRelease? _appCache;

    public CatalogClient(string? baseUrl = null)
    {
        var root = (baseUrl ?? DefaultBaseUrl).Trim().TrimEnd('/');
        if (root.Equals("https://spiritvalemods.com", StringComparison.OrdinalIgnoreCase))
            root = DefaultBaseUrl;

        _http = new HttpClient
        {
            BaseAddress = new Uri(root + "/"),
            Timeout = TimeSpan.FromMinutes(2),
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? DefaultBaseUrl;

    public async Task<CatalogFile> FetchCatalogAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("api/catalog", ct);
        response.EnsureSuccessStatusCode();
        var catalog = await response.Content.ReadFromJsonAsync<CatalogFile>(JsonOptions, ct)
            ?? new CatalogFile();
        _cache = catalog;
        if (catalog.App is not null)
            _appCache = catalog.App;
        return catalog;
    }

    public CatalogFile? Cached => _cache;

    public async Task<AppUpdateInfo> CheckAppUpdateAsync(CancellationToken ct = default)
    {
        var current = GetCurrentVersion();
        AppRelease? release = null;
        try
        {
            using var response = await _http.GetAsync("api/app", ct);
            if (response.IsSuccessStatusCode)
            {
                release = await response.Content.ReadFromJsonAsync<AppRelease>(JsonOptions, ct);
                if (release is not null)
                    _appCache = release;
            }
        }
        catch
        {
            release = _appCache ?? _cache?.App;
        }

        release ??= _appCache ?? _cache?.App;
        if (release is null || string.IsNullOrWhiteSpace(release.Version))
        {
            return new AppUpdateInfo
            {
                CurrentVersion = current,
                UpdateAvailable = false,
            };
        }

        return new AppUpdateInfo
        {
            CurrentVersion = current,
            LatestVersion = release.Version,
            UpdateAvailable = IsVersionNewer(release.Version, current),
            Changelog = string.IsNullOrWhiteSpace(release.Changelog) ? null : release.Changelog,
            Installer = release.Installer,
            Portable = release.Portable,
        };
    }

    public async Task<string> DownloadAppArtifactAsync(
        AppArtifact artifact,
        string destinationPath,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Invoke($"Downloading {artifact.Filename}…");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var response = await _http.GetAsync(ToAbsolute(artifact.DownloadUrl), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(destinationPath))
        {
            await input.CopyToAsync(output, ct);
        }

        var hash = ComputeSha256(destinationPath);
        var expected = artifact.Sha256.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(expected) && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destinationPath);
            throw new InvalidOperationException(
                $"SHA-256 mismatch for {artifact.Filename}: expected {expected}, got {hash}");
        }

        progress?.Invoke($"Verified {artifact.Filename}");
        return destinationPath;
    }

    public async Task<(string ZipPath, string Sha256)> DownloadModAsync(
        CatalogMod mod,
        string destinationZipPath,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Invoke($"Downloading {mod.Name}…");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationZipPath)!);

        using var response = await _http.GetAsync(ToAbsolute(mod.DownloadUrl), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(destinationZipPath))
        {
            await input.CopyToAsync(output, ct);
        }

        var hash = ComputeSha256(destinationZipPath);
        var expected = mod.Sha256.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(expected) && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destinationZipPath);
            throw new InvalidOperationException(
                $"SHA-256 mismatch for {mod.Id}: expected {expected}, got {hash}");
        }

        progress?.Invoke($"Verified {mod.Name}");
        return (destinationZipPath, hash);
    }

    public static string GetCurrentVersion()
    {
        var asm = typeof(CatalogClient).Assembly;
        var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public static bool IsVersionNewer(string remote, string local)
    {
        static int[] Parts(string v)
        {
            var cleaned = v.Split('-', 2)[0].TrimStart('v', 'V');
            return cleaned.Split('.')
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .ToArray();
        }

        var a = Parts(remote);
        var b = Parts(local);
        var len = Math.Max(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    private Uri ToAbsolute(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var abs) ? abs : new Uri(_http.BaseAddress!, url.TrimStart('/'));

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose() => _http.Dispose();
}
