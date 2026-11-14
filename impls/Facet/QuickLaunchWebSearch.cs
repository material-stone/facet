using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Facet;

internal static class QuickLaunchWebSearch
{
    internal sealed record EngineOption(WebSearchEngine Id, string Name);
    internal static IReadOnlyList<EngineOption> Engines { get; } = [new(WebSearchEngine.Google, "Google"), new(WebSearchEngine.Naver, "네이버"), new(WebSearchEngine.Bing, "Bing")];
    private static bool HasPrefix(string text) => text.Length >= 2 && text[0] == '?' && char.IsWhiteSpace(text[1]);
    internal static bool IsInput(string text) => HasPrefix(text) || AbsoluteAddress(text.Trim()) != null;

    internal static ProcessStartInfo StartInfo(string input, WebSearchEngine engine = WebSearchEngine.Google)
    {
        if (!IsInput(input)) throw new ArgumentException("검색어나 웹 주소를 입력해 주세요.");
        string value = HasPrefix(input) ? input[2..].Trim() : input.Trim();
        if (value.Length == 0) throw new ArgumentException("검색어나 웹 주소를 입력해 주세요.");
        var address = Address(value);
        if (address != null) return new(address.AbsoluteUri) { UseShellExecute = true };
        string endpoint = engine switch {
            WebSearchEngine.Google => "https://www.google.com/search?q=",
            WebSearchEngine.Naver => "https://search.naver.com/search.naver?query=",
            WebSearchEngine.Bing => "https://www.bing.com/search?q=",
            _ => throw new ArgumentException("검색 엔진을 선택해 주세요.")
        };
        return new(endpoint + Uri.EscapeDataString(value)) { UseShellExecute = true };
    }

    private static Uri? AbsoluteAddress(string value)
        => (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) &&
            !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri : null;

    private static Uri? Address(string value)
    {
        var absolute = AbsoluteAddress(value);
        if (absolute != null) return absolute;
        // Bare hosts are recognized only in ? mode, so normal PATH app names keep their meaning.
        if (value.Any(char.IsWhiteSpace) || value.Contains("://", StringComparison.Ordinal) || value.IndexOfAny(['\\', '@']) >= 0 ||
            !Uri.TryCreate("https://" + value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0) return null;
        string host = uri.IdnHost.TrimEnd('.');
        bool hostAddress = uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.HostNameType == UriHostNameType.Dns && host.Contains('.') && host[(host.LastIndexOf('.') + 1)..].Any(char.IsLetter);
        return hostAddress ? uri : null;
    }

    internal static string? DefaultBrowserExecutable()
    {
        try {
            const uint protocol = 0x1000, executable = 2; // ASSOCF_IS_PROTOCOL / ASSOCSTR_EXECUTABLE
            uint length = 0;
            AssocQueryString(protocol, executable, "https", "open", null, ref length);
            if (length is 0 or > 32768) return null;
            var path = new StringBuilder((int)length);
            if (AssocQueryString(protocol, executable, "https", "open", path, ref length) != 0) return null;
            string value = path.ToString();
            return Path.IsPathFullyQualified(value) && File.Exists(value) ? value : null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or System.Security.SecurityException) {
            return null;
        }
    }

    [DllImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryString(uint flags, uint kind, string association, string extra, StringBuilder? output, ref uint length);
}
