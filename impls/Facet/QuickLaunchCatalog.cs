using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Facet;

internal enum LaunchCommand { Help }
internal sealed record LaunchCandidate(string Text, string Target, string Completion, bool Explore, LaunchCommand? Command = null);

internal sealed class QuickLaunchCatalog
{
    private static readonly LaunchCandidate[] Commands = [new("help", "사용법 열기 / 닫기", "> help", false, LaunchCommand.Help)];
    private readonly string searchPath;
    private readonly string extensions;
    private List<LaunchCandidate>? apps;
    private DateTime indexedAt;
    private readonly SemaphoreSlim gate = new(1);

    internal QuickLaunchCatalog(string? searchPath = null, string? extensions = null)
    {
        this.searchPath = searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        this.extensions = extensions ?? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
    }

    internal async Task<IReadOnlyList<LaunchCandidate>> SearchAsync(string input, IReadOnlyDictionary<string, string> aliases, CancellationToken token)
    {
        // Both enumeration and matching stay off the UI thread, including network PATH entries.
        return await Task.Run(async () => {
            token.ThrowIfCancellationRequested();
            if (input.StartsWith('>')) return Match(Commands, input[1..].Trim(), true);
            if (input.StartsWith("@@", StringComparison.Ordinal))
                return Match(aliases.Select(x => new LaunchCandidate(x.Key, x.Value, "@@" + x.Key, true)), input[2..], true);
            if (input.StartsWith('@')) return Paths(input[1..], token);
            if (string.IsNullOrWhiteSpace(input)) return (IReadOnlyList<LaunchCandidate>)Array.Empty<LaunchCandidate>();
            await gate.WaitAsync(token);
            try {
                if (apps == null || DateTime.UtcNow - indexedAt > TimeSpan.FromMinutes(1)) {
                    var found = new List<LaunchCandidate>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var allowed = extensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (string entry in searchPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                        token.ThrowIfCancellationRequested();
                        string directory = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
                        if (!Path.IsPathFullyQualified(directory)) continue;
                        foreach (string extension in allowed) {
                            if (!extension.StartsWith('.') || extension.IndexOfAny(['*', '?', '/', '\\']) >= 0) continue;
                            try {
                                foreach (string file in Directory.EnumerateFiles(directory, "*" + extension)) {
                                    token.ThrowIfCancellationRequested();
                                    if (!string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase)) continue;
                                    string name = Path.GetFileNameWithoutExtension(file);
                                    if (seen.Add(name)) found.Add(new(name, file, name, false));
                                }
                            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
                        }
                    }
                    apps = found; indexedAt = DateTime.UtcNow;
                }
                return Match(apps, input.Trim(), false);
            } finally { gate.Release(); }
        }, token).ConfigureAwait(false);
    }

    private static IReadOnlyList<LaunchCandidate> Match(IEnumerable<LaunchCandidate> source, string text, bool allowEmpty)
        => !allowEmpty && text.Length == 0 ? [] : source
            .Where(x => x.Text.Contains(text, StringComparison.OrdinalIgnoreCase) || (x.Command == null && !x.Explore && Path.GetFileName(x.Target).Equals(text, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Text.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0 : x.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(x => x.Text.Length).ThenBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Take(8).ToArray();

    private static IReadOnlyList<LaunchCandidate> Paths(string text, CancellationToken token)
    {
        text = text.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(text)) return [];
        var result = new List<LaunchCandidate>();
        try {
            string path = Path.GetFullPath(text);
            if (Directory.Exists(path) || File.Exists(path)) result.Add(new(path, path, "@" + path, true));
            string? parent = Path.EndsInDirectorySeparator(path) ? path : Path.GetDirectoryName(path);
            string prefix = Path.EndsInDirectorySeparator(path) ? "" : Path.GetFileName(path);
            if (parent != null && Directory.Exists(parent)) {
                var matches = new List<string>();
                foreach (string child in Directory.EnumerateFileSystemEntries(parent)) {
                    token.ThrowIfCancellationRequested();
                    if (child.Equals(path, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    matches.Add(child);
                    // Keep a bounded, deterministic prefix set without recursively walking the disk.
                    if (matches.Count > 16) matches = matches.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(8).ToList();
                }
                result.AddRange(matches.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(8 - result.Count)
                    .Select(x => new LaunchCandidate(x, x, "@" + x + (Directory.Exists(x) ? Path.DirectorySeparatorChar : ""), true)));
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { }
        return result;
    }

    internal static Dictionary<string, string> ParseAliases(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string row in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            int separator = row.IndexOf('=');
            if (separator <= 0) throw new ArgumentException("alias는 한 줄에 이름=절대경로 형식으로 입력하세요.");
            string name = row[..separator].Trim(), target = row[(separator + 1)..].Trim().Trim('"');
            if (name.Length == 0 || name.StartsWith('@') || name.Length > 80 || !Path.IsPathFullyQualified(target))
                throw new ArgumentException("alias 이름은 @ 없이 80자 이내, 대상은 절대경로로 입력하세요.");
            if (!result.TryAdd(name, Path.GetFullPath(target))) throw new ArgumentException("중복 alias: " + name);
        }
        return result;
    }

    internal static ProcessStartInfo StartInfo(LaunchCandidate candidate)
    {
        if (candidate.Command != null) throw new ArgumentException("예약 명령어는 앱 내부에서만 실행할 수 있습니다.");
        if (!candidate.Explore) {
            if (!File.Exists(candidate.Target)) throw new IOException("앱을 찾을 수 없습니다. 다시 검색해 주세요.");
            return new(candidate.Target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(candidate.Target)! };
        }
        if (!Path.IsPathFullyQualified(candidate.Target) || (!Directory.Exists(candidate.Target) && !File.Exists(candidate.Target)))
            throw new IOException("파일이나 폴더를 찾을 수 없습니다. 경로를 확인하세요.");
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true };
        // Explorer's /select syntax requires its own single command-line fragment.
        info.Arguments = (Directory.Exists(candidate.Target) ? "" : "/select,") + "\"" + candidate.Target.TrimEnd('\\') + "\"";
        if (Directory.Exists(candidate.Target) && Path.GetPathRoot(candidate.Target) == candidate.Target)
            info.Arguments = "\"" + candidate.Target + "\"";
        return info;
    }
}
