using System.Reflection;
using System.Windows.Media.Imaging;

namespace Facet.ConditionPlugin;

internal sealed record ConditionStyle(string Id, string Name, string Color);

internal static class ConditionIcons
{
    internal static readonly IReadOnlyList<ConditionStyle> All = Array.AsReadOnly(new[] {
        new ConditionStyle("good", "좋음", "#79CCAE"), new("okay", "괜찮음", "#90BDE5"),
        new("tired", "피곤함", "#B69ACF"), new("sleepy", "졸림", "#8795D6"),
        new("unwell", "아픔", "#E9B18E"), new("stressed", "스트레스", "#E58D9A") });
    // The warm palette of the soft widget form.
    private static readonly Dictionary<string, string> SoftColors = new() { ["good"] = "#8CCB9B", ["okay"] = "#C2D67E", ["tired"] = "#F2D06B", ["sleepy"] = "#EFA05F", ["unwell"] = "#DE6D57", ["stressed"] = "#A98BBF" };
    private static readonly Dictionary<string, BitmapImage> Cache = new();
    internal static ConditionStyle? Find(string? id) => All.FirstOrDefault(item => item.Id == id);
    internal static Brush Color(string kind)
    {
        string color = Facet.Plugins.WidgetTheme.IsSoftHud && SoftColors.TryGetValue(kind, out string? soft) ? soft : Find(kind)!.Color;
        var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color));
        brush.Freeze(); return brush;
    }
    // Lit or shaded faces of a voxel block.
    internal static Brush Shade(Brush brush, double factor)
    {
        var color = ((SolidColorBrush)brush).Color;
        byte Scale(byte value) => (byte)Math.Clamp(value * factor, 0, 255);
        var shaded = new SolidColorBrush(System.Windows.Media.Color.FromRgb(Scale(color.R), Scale(color.G), Scale(color.B)));
        shaded.Freeze(); return shaded;
    }
    internal static ImageSource Load(string kind)
    {
        if (!ConditionTracking.IsKnown(kind)) throw new ArgumentException("알 수 없는 컨디션입니다.", nameof(kind));
        if (Cache.TryGetValue(kind, out var cached)) return cached;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"icons/{kind}.png") ?? throw new InvalidDataException("컨디션 아이콘이 없습니다: " + kind);
        var bitmap = new BitmapImage(); bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 228;
        bitmap.EndInit(); bitmap.Freeze(); Cache[kind] = bitmap; return bitmap;
    }
}
