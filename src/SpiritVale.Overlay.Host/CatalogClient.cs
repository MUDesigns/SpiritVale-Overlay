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

internal sealed class CatalogFile
{
    public List<CatalogMod> Mods { get; set; } = new();
    public bool? Paused { get; set; }
}

internal sealed class CatalogCheckResult
{
    public int Checked { get; init; }
    public int Updates { get; init; }
    public string Message { get; init; } = "";
}

internal sealed class CatalogClient : IDisposable
{
    public const string DefaultBaseUrl = "https://www.spiritvalemods.com";
    public const string UserAgent = "SpiritVale-Overlay/0.1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private CatalogFile? _cache;

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
        return catalog;
    }

    public CatalogFile? Cached => _cache;

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
