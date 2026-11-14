using System.IO;

namespace Facet;

// Copies the plugins a portable package carries into the data folder PluginHost reads (ADR 0087).
// Each package installs once, so a plugin the user removed stays removed until a newer package arrives.
// Nothing is enabled here: running a plugin stays the user's choice (ADR 0073).
internal static class PortablePlugins
{
    internal const string Folder = "bundled-plugins", PackageFile = "package-id.txt", MarkerFile = ".bundled-package";

    // Returns a message when installing failed; the next start retries because the marker is written last.
    internal static string? Install(string? packageRoot, string dataRoot)
    {
        if (packageRoot == null) return null;
        string source = Path.Combine(packageRoot, Folder), idFile = Path.Combine(source, PackageFile);
        if (!File.Exists(idFile)) return null;
        string staging = Path.Combine(dataRoot, ".plugin-staging-" + Guid.NewGuid().ToString("N"));
        try {
            string package = File.ReadAllText(idFile).Trim();
            string target = Path.Combine(dataRoot, "plugins"), marker = Path.Combine(target, MarkerFile);
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == package) return null;
            Directory.CreateDirectory(target);
            foreach (string plugin in Directory.GetDirectories(source)) {
                string name = Path.GetFileName(plugin), fresh = Path.Combine(staging, name), old = Path.Combine(staging, name + ".old"), installed = Path.Combine(target, name);
                Copy(plugin, fresh);
                Swap(fresh, installed, old);
            }
            File.WriteAllText(marker, package);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "포함된 플러그인을 설치하지 못했습니다: " + ex.Message; }
        finally {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Plugin staging cleanup failed: " + ex.Message); }
        }
    }

    // First-party plugins installed under the app's old name (old id or old contract assembly) are replaced once by the
    // deployment's plugin with the current id, in that plugin's folder. Their data lives in plugin-data and stays (ADR 0089, 0096).
    internal static string? UpgradeRenamed(string? deployRoot, string dataRoot)
    {
        string plugins = Path.Combine(dataRoot, "plugins");
        if (deployRoot == null || !Directory.Exists(plugins)) return null;
        var shipped = new[] { "plugins", Folder }.Select(source => Path.Combine(deployRoot, source)).Where(Directory.Exists).SelectMany(Directory.GetDirectories).ToArray();
        foreach (string installed in Directory.GetDirectories(plugins)) {
            if (PluginHost.ReadManifest(installed) is not { } old) continue;
            string id = Core.PluginIds.Current(old.Id);
            if (id == old.Id && !old.Assembly.StartsWith(Core.PluginIds.RenamedPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            string? fresh = shipped.FirstOrDefault(folder => PluginHost.ReadManifest(folder)?.Id == id);
            if (fresh == null) continue;
            string name = Path.GetFileName(fresh), target = Path.Combine(plugins, name);
            if (target != installed && Directory.Exists(target)) continue;
            string staging = Path.Combine(dataRoot, ".plugin-staging-" + Guid.NewGuid().ToString("N"));
            try {
                Copy(fresh, Path.Combine(staging, name));
                if (target != installed) Directory.Move(installed, Path.Combine(staging, name + ".old"));
                Swap(Path.Combine(staging, name), target, Path.Combine(staging, name + ".old"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "플러그인을 새 이름으로 교체하지 못했습니다: " + ex.Message; }
            finally {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Plugin staging cleanup failed: " + ex.Message); }
            }
        }
        return null;
    }

    // Replaces a folder, restoring the old one if the new one cannot take its place. The caller removes the old copy.
    internal static void Swap(string fresh, string installed, string old)
    {
        if (Directory.Exists(installed)) Directory.Move(installed, old);
        try { Directory.Move(fresh, installed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (Directory.Exists(old)) Directory.Move(old, installed); throw; }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (string folder in Directory.GetDirectories(from)) Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
}
