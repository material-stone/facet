using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Facet.Design;

namespace Facet;

internal enum PackageKind { Plugin, Skin }
internal sealed record PackageInfo(PackageKind Kind, string Id, string Name, string Version);

// A package is a zip with plugin.json or skin.json at its root (ADR 0088).
// Installing only places files: a plugin still runs only after the user enables it (ADR 0073).
internal static class Packages
{
    private const long MaxPluginBytes = 1L << 30, MaxSkinBytes = 64L << 20;
    private const int MaxEntries = 5000;
    private static readonly string[] SkinExtensions = [".json", ".png", ".txt", ".md", ".html", ".ttf", ".otf"];
    private static readonly Regex ReservedName = new(@"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static long MaxBytes => MaxPluginBytes;

    // Throws InvalidDataException with a message for the user; existing files stay as they were on any failure.
    internal static PackageInfo Install(string zipFile, string dataRoot, Func<PackageInfo, bool>? accept = null)
    {
        Directory.CreateDirectory(dataRoot);
        string staging = Path.Combine(dataRoot, ".package-staging-" + Guid.NewGuid().ToString("N"));
        try {
            string extracted = Path.Combine(staging, "new");
            Extract(zipFile, extracted);
            string root = ManifestRoot(extracted);
            var info = Read(root) ?? throw new InvalidDataException("plugin.json 또는 skin.json이 올바르지 않습니다.");
            if (info.Kind == PackageKind.Skin) CheckSkinFiles(root);
            if (accept?.Invoke(info) == false) throw new InvalidDataException("패키지 내용이 목록의 정보와 다릅니다.");
            if (ReservedName.IsMatch(info.Id) || info.Id.EndsWith('.')) throw new InvalidDataException("패키지 ID를 폴더 이름으로 쓸 수 없습니다.");
            string parent = Path.Combine(dataRoot, info.Kind == PackageKind.Plugin ? "plugins" : "skins");
            Directory.CreateDirectory(parent);
            string target = ExistingFolder(parent, info) ?? Path.Combine(parent, info.Id);
            if (Directory.Exists(target) && Read(target)?.Id != info.Id) throw new InvalidDataException($"같은 이름의 다른 폴더가 있습니다: {Path.GetFileName(target)}");
            PortablePlugins.Swap(root, target, Path.Combine(staging, "old"));
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new InvalidDataException("패키지를 설치하지 못했습니다: " + ex.Message, ex); }
        finally {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Package staging cleanup failed: " + ex.Message); }
        }
    }

    // Builds the zip a registry submission attaches. Debug symbols stay out.
    internal static string Pack(string folder, string outputDirectory)
    {
        var info = Read(folder) ?? throw new InvalidDataException("선택한 폴더에 올바른 plugin.json 또는 skin.json이 없습니다.");
        if (info.Kind == PackageKind.Skin) CheckSkinFiles(folder);
        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, $"{info.Id}-{(info.Version.Length > 0 ? info.Version : "0")}.zip"), pending = output + ".tmp";
        File.Delete(pending);
        using (var zip = ZipFile.Open(pending, ZipArchiveMode.Create))
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)))
                zip.CreateEntryFromFile(file, Path.GetRelativePath(folder, file).Replace('\\', '/'), CompressionLevel.Optimal);
        File.Move(pending, output, true);
        return output;
    }

    internal static PackageInfo? Read(string folder)
    {
        bool plugin = File.Exists(Path.Combine(folder, "plugin.json")), skin = File.Exists(Path.Combine(folder, ExternalSkins.ManifestFile));
        if (plugin == skin) return null;
        if (plugin) return PluginHost.ReadManifest(folder) is { } manifest ? new(PackageKind.Plugin, manifest.Id, manifest.Name, manifest.Version) : null;
        return ExternalSkins.Read(folder) is { } theme ? new(PackageKind.Skin, theme.Id, theme.Name, theme.Version) : null;
    }

    private static string? ExistingFolder(string parent, PackageInfo info) =>
        Directory.GetDirectories(parent).FirstOrDefault(folder => Read(folder) is { } other && other.Id == info.Id && other.Kind == info.Kind);

    // People often zip the folder itself, so one wrapping folder is accepted.
    private static string ManifestRoot(string extracted)
    {
        if (Directory.GetFiles(extracted).Length == 0 && Directory.GetDirectories(extracted) is [var only]) return only;
        return extracted;
    }

    // Only a world page may run, inside the locked WebView2 of Design/WorldBackdrop; nothing else in a skin can execute (ADR 0091).
    private static void CheckSkinFiles(string folder)
    {
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            if (!SkinExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("스킨에 넣을 수 없는 파일입니다: " + Path.GetFileName(file));
        foreach (string asset in ExternalSkins.Read(folder)?.Theme is { } theme ? new[] { theme.Background, theme.Frame, theme.WorldPage, theme.BodyFont?.File, theme.HeadingFont?.File }.OfType<string>() : [])
            if (!File.Exists(Path.Combine(folder, asset))) throw new InvalidDataException("스킨 파일이 없습니다: " + asset);
    }

    private static void Extract(string zipFile, string destination)
    {
        ZipArchive archive;
        try { archive = ZipFile.OpenRead(zipFile); }
        catch (InvalidDataException ex) { throw new InvalidDataException("zip 파일이 아니거나 손상되었습니다.", ex); }
        using (archive) {
            if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("패키지의 파일이 너무 많습니다.");
            Directory.CreateDirectory(destination);
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            long total = 0;
            var buffer = new byte[81920];
            foreach (var entry in archive.Entries) {
                string name = entry.FullName.Replace('\\', '/');
                string[] parts = name.TrimEnd('/').Split('/');
                if (name.StartsWith('/') || parts.Any(part => part.Length == 0 || part == ".." || part.Contains(':') || part.EndsWith('.') || part.EndsWith(' ') || ReservedName.IsMatch(part)))
                    throw new InvalidDataException("패키지에 안전하지 않은 경로가 있습니다: " + entry.FullName);
                string target = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("패키지에 안전하지 않은 경로가 있습니다: " + entry.FullName);
                if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                // Counted while copying: the sizes a zip declares can lie.
                for (int read; (read = input.Read(buffer)) > 0; output.Write(buffer, 0, read))
                    if ((total += read) > MaxPluginBytes) throw new InvalidDataException("패키지가 너무 큽니다.");
            }
            // A skin's smaller limit needs the kind, known only after extraction.
            if (File.Exists(Path.Combine(ManifestRoot(destination), ExternalSkins.ManifestFile)) && total > MaxSkinBytes) throw new InvalidDataException("스킨 패키지가 너무 큽니다.");
        }
    }
}
