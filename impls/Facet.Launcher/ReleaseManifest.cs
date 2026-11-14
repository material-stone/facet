using System.Text.Json;
using System;
using System.IO;

namespace Facet.Deployment;

internal sealed record ReleaseManifest(int SchemaVersion, string Executable)
{
    public static string Resolve(string directory)
    {
        string manifestPath = Path.Combine(directory, "latest.json");
        if (new FileInfo(manifestPath).Length > 8192) throw new InvalidDataException("최신 버전 정보가 너무 큽니다.");
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Executable))
            throw new InvalidDataException("최신 버전 정보가 올바르지 않습니다. 앱을 다시 빌드해 주세요.");
        var parts = manifest.Executable.Replace('\\', '/').Split('/');
        if (parts.Length != 3 || parts[0] != "releases" || parts[1] is "" or "." or ".."
            || parts[1].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || parts[2] != "Facet.exe")
            throw new InvalidDataException("최신 버전 경로는 releases 아래의 Facet.exe여야 합니다.");
        string path = Path.GetFullPath(directory);
        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("배포 경로에 링크를 사용할 수 없습니다.");
        }
        string release = Path.GetDirectoryName(path)!;
        foreach (string file in new[] { "Facet.dll", "Facet.Core.dll", "Facet.deps.json", "Facet.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(release, file))) throw new InvalidDataException("최신 버전 파일이 누락되었습니다. 앱을 다시 빌드해 주세요.");
        return path;
    }
}
