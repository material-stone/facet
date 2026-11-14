using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Facet.Plugins;

namespace Facet;

// A plugin's own encrypted file at <data>/plugin-data/<id>.dat, kept when the plugin's folder is replaced or deleted (ADR 0081).
internal sealed class PluginFileStore(string path) : IPluginStore
{
    internal string PathName => path;
    internal static PluginFileStore For(string dataRoot, string pluginId) => new(Path.Combine(dataRoot, "plugin-data", pluginId + ".dat"));

    private static bool IsUnreadable(Exception e) => e is IOException or JsonException or Win32Exception or InvalidDataException or ArgumentException or UnauthorizedAccessException;

    public string? Read()
    {
        if (!File.Exists(path)) return null;
        try { return Encoding.UTF8.GetString(Store.Protect(File.ReadAllBytes(path), false)); }
        catch (Exception original) when (IsUnreadable(original)) {
            foreach (string backup in Enumerable.Range(1, 5).Select(i => path + $".bak{i}").Where(File.Exists)) {
                try { return Encoding.UTF8.GetString(Store.Protect(File.ReadAllBytes(backup), false)); }
                catch (Exception e) when (IsUnreadable(e)) { }
            }
            throw new InvalidDataException("플러그인 저장소와 백업을 읽을 수 없습니다. 기존 파일을 덮어쓰지 않았습니다.", original);
        }
    }

    public bool Write(string text)
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Store.WriteProtected(path, Store.Protect(Encoding.UTF8.GetBytes(text), true));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception) { return false; }
    }
}
