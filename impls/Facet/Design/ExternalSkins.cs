using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Facet.Design;

internal sealed record ExternalSkin(string Id,string Name,string Version,IReadOnlyDictionary<string,Color> Colors,ThemeManifest? Theme,string Directory);

// A skin from <data>/skins/<folder>/skin.json. It is data only: validated colors, validated theme slots and PNG files, never XAML (ADR 0088).
internal static class ExternalSkins
{
    internal const string ManifestFile="skin.json";
    private const int MaxImageSide=8192;
    internal static readonly IReadOnlySet<string> ColorTokens=new HashSet<string> { "Bg","OverlayBackdrop","ModalBackdrop","DangerSoft","Panel","Edge","Ink","Muted","ScheduleNow","Mint","Danger","CalendarSunday","CalendarSaturday","Input","Selected","Hover","OnAccent" };
    private static readonly Regex ValidId=new("^[a-z0-9][a-z0-9._-]{0,63}$",RegexOptions.CultureInvariant);
    private static readonly Regex ValidColor=new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$",RegexOptions.CultureInvariant);

    // Null means the manifest is unusable, so one broken folder never hides the other skins.
    internal static ExternalSkin? Parse(string json,string directory)
    {
        try {
            using var document=JsonDocument.Parse(json);
            var root=document.RootElement;
            string? id=Text(root,"id"),name=Text(root,"name");
            if(id==null || !ValidId.IsMatch(id) || string.IsNullOrWhiteSpace(name)) return null;
            var colors=new Dictionary<string,Color>();
            if(root.TryGetProperty("colors",out var values)) {
                if(values.ValueKind!=JsonValueKind.Object) return null;
                foreach(var value in values.EnumerateObject()) {
                    if(!ColorTokens.Contains(value.Name) || value.Value.ValueKind!=JsonValueKind.String || !ValidColor.IsMatch(value.Value.GetString()!)) return null;
                    colors[value.Name]=(Color)ColorConverter.ConvertFromString(value.Value.GetString()!);
                }
            }
            ThemeManifest? theme=null;
            if(root.TryGetProperty("theme",out var slots) && (theme=ThemeManifest.Parse(slots.GetRawText()))==null) return null;
            return new(id,name.Trim(),Text(root,"version") ?? "",colors,theme,directory);
        }
        catch(Exception ex) when(ex is JsonException or FormatException) { return null; }
    }

    internal static ExternalSkin? Read(string directory)
    {
        try { return Parse(File.ReadAllText(Path.Combine(directory,ManifestFile)),directory); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static IReadOnlyList<ExternalSkin> Discover(string root)
    {
        if(!Directory.Exists(root)) return [];
        var found=new List<ExternalSkin>();
        foreach(string folder in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            if(Read(folder) is { } skin && found.All(other=>other.Id!=skin.Id)) found.Add(skin);
        return found;
    }

    // Tokens a skin leaves out keep the default look, because this merges over Tokens.xaml.
    internal static ResourceDictionary Dictionary(ExternalSkin skin)
    {
        var result=new ResourceDictionary();
        foreach(var (key,color) in skin.Colors) result[key]=Brush(color);
        void System(object key,string token) { if(skin.Colors.TryGetValue(token,out var color)) result[key]=Brush(color); }
        System(SystemColors.WindowBrushKey,"Input");
        System(SystemColors.WindowTextBrushKey,"Ink");
        System(SystemColors.ControlBrushKey,"Panel");
        System(SystemColors.ControlTextBrushKey,"Ink");
        System(SystemColors.HighlightBrushKey,"Selected");
        System(SystemColors.HighlightTextBrushKey,"Ink");
        return result;
    }

    private static SolidColorBrush Brush(Color color) { var brush=new SolidColorBrush(color);brush.Freeze();return brush; }

    internal static ThemeVisuals? Visuals(ExternalSkin skin)=>skin.Theme is { } theme ? ThemeVisuals.From(theme,name=>OpenImage(Path.Combine(skin.Directory,name))) : null;

    // The header alone gives the size, so an oversized image is refused before it is decoded.
    private static Stream OpenImage(string path)
    {
        var stream=File.OpenRead(path);
        try {
            var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0];
            if(frame.PixelWidth>MaxImageSide || frame.PixelHeight>MaxImageSide) throw new InvalidOperationException("스킨 이미지가 너무 큽니다.");
            stream.Position=0;
            return stream;
        }
        catch { stream.Dispose();throw; }
    }

    private static string? Text(JsonElement root,string name)=>root.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString() : null;
}
