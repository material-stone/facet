using System.IO;
using System.IO.Compression;

namespace Facet;

// Backs the records folder up to a zip and restores one at the next start, before anything opens the folder (ADR 0099).
// Records stay DPAPI-encrypted inside the zip, so a backup restores only for the same Windows user.
internal static class DataBackup
{
    internal const string PendingSuffix = ".restore", MarkerEntry = "records.dat";
    internal static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Facet 백업");

    // Returns the zip and how many files were in use and left out.
    internal static (string Zip, int Skipped) Create(string root, string folder)
    {
        Directory.CreateDirectory(folder);
        string zip = Path.Combine(folder, $"facet-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip"), pending = zip + ".tmp";
        int skipped = 0;
        try {
            using (var archive = ZipFile.Open(pending, ZipArchiveMode.Create))
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
                    string relative = Path.GetRelativePath(root, file);
                    string top = relative.Split(Path.DirectorySeparatorChar)[0];
                    if (top == "backups" || top.StartsWith('.') || file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    try { archive.CreateEntryFromFile(file, relative.Replace(Path.DirectorySeparatorChar, '/'), CompressionLevel.Optimal); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                }
            File.Move(pending, zip);
            return (zip, skipped);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    // Unpacks a backup beside the records folder; Apply swaps it in at the next start.
    internal static void Stage(string root, string zip)
    {
        string staging = root.TrimEnd(Path.DirectorySeparatorChar) + PendingSuffix, full = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zip);
        if (archive.GetEntry(MarkerEntry) == null) throw new InvalidDataException("Facet 백업 파일이 아닙니다.");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        try {
            foreach (var entry in archive.Entries) {
                string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("백업 파일의 경로가 올바르지 않습니다.");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
            }
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }

    internal static bool IsStaged(string root) => Directory.Exists(root.TrimEnd(Path.DirectorySeparatorChar) + PendingSuffix);

    // Keeps the current folder beside it and puts the staged backup in its place. Returns the kept folder, or null.
    internal static string? Apply(string root)
    {
        string staging = root.TrimEnd(Path.DirectorySeparatorChar) + PendingSuffix;
        if (!Directory.Exists(staging)) return null;
        string kept = root.TrimEnd(Path.DirectorySeparatorChar) + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (Directory.Exists(root)) Directory.Move(root, kept);
        try { Directory.Move(staging, root); }
        catch { if (Directory.Exists(kept) && !Directory.Exists(root)) Directory.Move(kept, root); throw; }
        return kept;
    }
}
