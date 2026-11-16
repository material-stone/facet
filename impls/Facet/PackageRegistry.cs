using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Facet;

internal sealed record RegistryEntry(PackageKind Kind, string Id, string Name, string Version, string Author, string Description, string Url, string Sha256);

// Reads the GitHub-hosted package list (ADR 0088). It sends nothing about the user and runs only when asked.
// A package is installed only when its SHA-256 and identity match the reviewed list.
internal static class PackageRegistry
{
    internal const string Repository = "material-stone/facet";
    internal const string SubmitUrl = "https://github.com/" + Repository + "/issues/new?template=submit.yml";
    // The API serves main as soon as it moves; raw.githubusercontent caches it for minutes, so it is only the fallback.
    private const string FreshIndexUrl = "https://api.github.com/repos/" + Repository + "/contents/registry/index.json?ref=main";
    private const string IndexUrl = "https://raw.githubusercontent.com/" + Repository + "/main/registry/index.json";
    private const int MaxIndexBytes = 4 << 20;
    private static readonly Regex ValidUrl = new("^https://github\\.com/" + Regex.Escape(Repository) + "/releases/download/[A-Za-z0-9._%-]+/[A-Za-z0-9._%-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex ValidSha = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex ValidId = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly HttpClient Shared = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Facet");
        return client;
    }

    // Entries that fail validation are dropped so one bad row does not hide the list.
    internal static IReadOnlyList<RegistryEntry> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array) throw new JsonException("packages is missing.");
        var found = new List<RegistryEntry>();
        foreach (var item in packages.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            PackageKind? kind = Text("kind") switch { "plugin" => PackageKind.Plugin, "skin" => PackageKind.Skin, _ => null };
            if (kind == null || !ValidId.IsMatch(Text("id")) || Text("name").Trim().Length == 0 || Text("version").Length == 0 || !ValidUrl.IsMatch(Text("url")) || !ValidSha.IsMatch(Text("sha256"))) continue;
            found.Add(new(kind.Value, Text("id"), Text("name").Trim(), Text("version"), Text("author"), Text("description"), Text("url"), Text("sha256")));
        }
        return found;
    }

    internal static async Task<IReadOnlyList<RegistryEntry>> FetchAsync(CancellationToken cancel, HttpClient? client = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var http = client ?? Shared;
        using var fresh = new HttpRequestMessage(HttpMethod.Get, FreshIndexUrl);
        fresh.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
        HttpResponseMessage? response = null;
        try { response = await http.SendAsync(fresh, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (HttpRequestException) { }
        if (response is not { IsSuccessStatusCode: true }) {
            // Rate-limited or unreachable API: the cached copy is still a reviewed list.
            response?.Dispose();
            response = await http.GetAsync(IndexUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        using var _ = response;
        response.EnsureSuccessStatusCode();
        using var memory = new MemoryStream();
        await CopyLimited(await response.Content.ReadAsStreamAsync(timeout.Token), memory, MaxIndexBytes, timeout.Token);
        return Parse(System.Text.Encoding.UTF8.GetString(memory.ToArray()));
    }

    internal static async Task<PackageInfo> InstallAsync(RegistryEntry entry, string dataRoot, CancellationToken cancel, HttpClient? client = null)
    {
        Directory.CreateDirectory(dataRoot);
        string download = Path.Combine(dataRoot, ".package-download-" + Guid.NewGuid().ToString("N") + ".zip");
        try {
            using (var response = await (client ?? Shared).GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, cancel)) {
                response.EnsureSuccessStatusCode();
                await using var output = new FileStream(download, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await CopyLimited(await response.Content.ReadAsStreamAsync(cancel), output, Packages.MaxBytes, cancel);
            }
            return await Task.Run(() => InstallVerified(entry, download, dataRoot), cancel);
        }
        finally {
            try { File.Delete(download); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Package download cleanup failed: " + ex.Message); }
        }
    }

    internal static PackageInfo InstallVerified(RegistryEntry entry, string file, string dataRoot)
    {
        string actual;
        using (var stream = File.OpenRead(file)) actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (actual != entry.Sha256) throw new InvalidDataException("내려받은 파일의 해시가 목록과 달라 설치하지 않았습니다.");
        return Packages.Install(file, dataRoot, info => info.Kind == entry.Kind && info.Id == entry.Id && info.Version == entry.Version);
    }

    // Plain version order where both parse; any other difference counts as an update.
    internal static bool IsNewer(string available, string installed) =>
        Version.TryParse(available, out var next) && Version.TryParse(installed, out var current) ? next > current : available != installed;

    private static async Task CopyLimited(Stream input, Stream output, long limit, CancellationToken cancel)
    {
        var buffer = new byte[81920];
        long total = 0;
        await using (input)
            for (int read; (read = await input.ReadAsync(buffer, cancel)) > 0;) {
                if ((total += read) > limit) throw new InvalidDataException("받은 데이터가 허용 크기를 넘었습니다.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancel);
            }
    }
}
