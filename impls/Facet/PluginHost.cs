using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Facet.Plugins;

namespace Facet;

internal sealed record PluginManifest(string Id, string Name, string Version, string Assembly, string Directory);

// Reads manifests without running code; code runs only through Load for a plugin the user enabled (ADR 0073).
internal sealed class PluginHost(string dataRoot, Func<string, IPluginShell>? shells = null) : IDisposable
{
    private sealed class LoadContext(string path) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(path))
    {
        private readonly AssemblyDependencyResolver resolver = new(path);
        // The contract must come from the app so plugin types implement the same interfaces, and the WinRT projection
        // keeps process-wide state that must not exist twice.
        protected override Assembly? Load(AssemblyName name) =>
            name.Name is "Facet.Plugins" or "WinRT.Runtime" or "Microsoft.Windows.SDK.NET" || resolver.ResolveAssemblyToPath(name) is not { } file ? null : LoadUnlocked(file);

        // Native dependencies such as ONNX Runtime stay in the plugin's folder (ADR 0084).
        protected override IntPtr LoadUnmanagedDll(string name)
        {
            string? file = resolver.ResolveUnmanagedDllToPath(name);
            if (file == null && Path.Combine(Path.GetDirectoryName(path)!, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll") is var beside && File.Exists(beside)) file = beside;
            return file == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(file);
        }

        // Loading from memory leaves the files replaceable while the app runs.
        internal Assembly LoadUnlocked(string file)
        {
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            return LoadFromStream(stream);
        }
    }

    private sealed class Registry(string directory, string dataDirectory, IPluginSettings settings, IPluginStore store, IPluginShell shell) : IWidgetRegistry
    {
        public string Directory => directory; public string DataDirectory => dataDirectory;
        public IPluginShell Shell => shell;
        internal readonly List<LauncherMode> LauncherModes = [];
        internal readonly List<PluginHotkey> Hotkeys = [];
        internal readonly List<(string Label, Action Run)> TrayCommands = [];
        public void AddLauncherMode(LauncherMode mode) => LauncherModes.Add(mode ?? throw new ArgumentNullException(nameof(mode)));
        public void AddHotkey(PluginHotkey hotkey) => Hotkeys.Add(hotkey ?? throw new ArgumentNullException(nameof(hotkey)));
        public void AddTrayCommand(string label, Action run) => TrayCommands.Add((label, run ?? throw new ArgumentNullException(nameof(run))));
        IPluginSettings IWidgetRegistry.Settings => settings;
        IPluginStore IWidgetRegistry.Store => store;
        internal readonly List<PluginAgentTool> Tools = [];
        internal readonly List<IFacetPlugin> Instances = [];
        public void AddAgentTool(PluginAgentTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (!ToolName.IsMatch(tool.Name ?? "") || tool.Run == null) throw new InvalidDataException("에이전트 도구 정의가 올바르지 않습니다.");
            System.Text.Json.Nodes.JsonNode.Parse(tool.InputSchema);
            Tools.Add(tool);
        }
        internal readonly List<WidgetDescriptor> Widgets = [];
        internal PluginSettingsSection? Settings;
        public void AddWidget(WidgetDescriptor widget) => Widgets.Add(widget ?? throw new ArgumentNullException(nameof(widget)));
        public void AddSettings(PluginSettingsSection section) => Settings = section ?? throw new ArgumentNullException(nameof(section));
    }

    private static readonly Regex ValidId = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private sealed record ManifestFile(string? Id, string? Name, string? Version, string? Assembly);
    private static readonly Regex ToolName = new("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, Registry> loaded = [];

    // Agent tools of running plugins; the app's own tools win a name clash.
    internal IReadOnlyList<PluginAgentTool> AgentTools => loaded.Values.SelectMany(r => r.Tools).Where(t => AgentToolCatalog.Tools.All(own => own.Name != t.Name)).ToArray();

    internal IReadOnlyList<LauncherMode> LauncherModes => loaded.Values.SelectMany(r => r.LauncherModes).ToArray();
    internal IReadOnlyList<(string PluginId, PluginHotkey Hotkey)> Hotkeys => loaded.SelectMany(p => p.Value.Hotkeys.Select(h => (p.Key, h))).ToArray();
    internal IReadOnlyList<(string Label, Action Run)> TrayCommands => loaded.Values.SelectMany(r => r.TrayCommands).ToArray();
    // Raised when a plugin starts or stops, so the app can rebind what it hosts for them.
    internal event Action? Changed;

    internal string Root { get; } = Path.Combine(dataRoot, "plugins");

    internal IReadOnlyList<PluginManifest> Discover()
    {
        if (!Directory.Exists(Root)) return [];
        var found = new List<PluginManifest>();
        foreach (string folder in Directory.GetDirectories(Root).Order(StringComparer.OrdinalIgnoreCase)) {
            if (File.Exists(Path.Combine(folder, RemovedMarker))) { try { RemoveFolder(folder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } continue; }
            if (ReadManifest(folder) is { } manifest && found.All(p => p.Id != manifest.Id)) found.Add(manifest);
        }
        return found;
    }

    // A removed plugin whose native files are still loaded is marked, hidden from Discover and deleted on a later
    // Discover once nothing holds it, at the latest after a restart (ADR 0097).
    internal const string RemovedMarker = ".removed";

    // Deletes a plugin's folder; returns false when only the marker could be left for a later attempt.
    internal static bool RemoveFolder(string folder)
    {
        if (TryDelete(folder)) return true;
        File.WriteAllText(Path.Combine(folder, RemovedMarker), "");
        return false;
    }

    private static bool TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // Reads only the manifest, so package installation can check a folder without running anything in it.
    internal static PluginManifest? ReadManifest(string folder)
    {
        try {
            var file = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(Path.Combine(folder, "plugin.json")), Json);
            if (file?.Id is not { } id || !ValidId.IsMatch(id) || file.Assembly is not { Length: > 0 } assembly || Path.GetFileName(assembly) != assembly
                || !File.Exists(Path.Combine(folder, assembly))) return null;
            return new(id, string.IsNullOrWhiteSpace(file.Name) ? id : file.Name.Trim(), file.Version ?? "", assembly, folder);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    // Covers every DLL in the folder so a swapped dependency also requires re-enabling.
    internal static string Fingerprint(PluginManifest plugin)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in Directory.GetFiles(plugin.Directory, "*.dll", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)) {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(plugin.Directory, file).ToUpperInvariant()));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // The next Load reads the files again. The old assembly stays in memory until the app restarts.
    internal void Forget(string pluginId) { if (loaded.Remove(pluginId, out var registry)) { Stop(registry); Changed?.Invoke(); } }

    private static void Stop(Registry registry)
    {
        foreach (var plugin in registry.Instances) {
            // One failing plugin must not stop the others or the app's exit.
            try { (plugin as IDisposable)?.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Plugin dispose failed: " + ex.Message); }
        }
    }

    public void Dispose() { foreach (var id in loaded.Keys.ToArray()) Forget(id); }

    // A running plugin is registered once; an overlay rebuilt later gets the same widgets and keeps its work running.
    // Throws on any plugin failure; the caller reports it and keeps the app running.
    internal (IReadOnlyList<WidgetDescriptor> Widgets, PluginSettingsSection? Settings) Load(PluginManifest plugin, IPluginSettings settings)
    {
        if (loaded.TryGetValue(plugin.Id, out var running)) return (running.Widgets, running.Settings);
        string path = Path.GetFullPath(Path.Combine(plugin.Directory, plugin.Assembly));
        var assembly = new LoadContext(path).LoadUnlocked(path);
        var registry = new Registry(plugin.Directory, System.IO.Directory.CreateDirectory(Path.Combine(dataRoot, "plugin-data", plugin.Id)).FullName, settings, PluginFileStore.For(dataRoot, plugin.Id), shells?.Invoke(plugin.Id) ?? new DetachedShell(dataRoot));
        try {
            foreach (var type in assembly.GetExportedTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFacetPlugin).IsAssignableFrom(t))) {
                var instance = (IFacetPlugin)Activator.CreateInstance(type)!;
                registry.Instances.Add(instance);
                instance.Register(registry);
            }
            if (registry.Widgets.Count == 0) throw new InvalidDataException("등록된 위젯이 없습니다.");
        }
        catch { Stop(registry); throw; }
        loaded[plugin.Id] = registry;
        Changed?.Invoke();
        return (registry.Widgets, registry.Settings);
    }

    // For a host without an app around it.
    private sealed class DetachedShell(string root) : IPluginShell
    {
        public string DataRoot => root;
        public string? AgentToolServer => null;
        public bool Privacy => false;
        public bool HideOverlay() => true;
        public bool ShowWidget(string widgetId) => false;
        public void OpenSettings() { }
        public void ReportError(string message) => System.Diagnostics.Trace.TraceWarning(message);
        public string? SetHotkey(string id, string shortcut) => null;
    }
}
