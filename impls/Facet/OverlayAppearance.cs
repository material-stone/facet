using System.Globalization;

namespace Facet;

internal static class OverlayAppearance
{
    internal const string DefaultColor = "#141A1D";
    internal static double Transparency(Settings settings) => settings.OverlayTransparencyPercent is double value && double.IsFinite(value)
        ? Math.Clamp(value, 0, 100)
        : 100 * (1 - .6 * Math.Clamp(double.IsFinite(settings.Opacity) ? settings.Opacity : .95, .4, 1));
    internal static double Strength(Settings settings) => 1 - Transparency(settings) / 100;
    internal static Color BackgroundColor(Settings settings) => TryColor(settings.OverlayBackgroundColor, out var color)
        ? color : Color.FromRgb(20, 26, 29);
    internal static bool TryColor(string? value, out Color color)
    {
        color = default;
        if (value is not { Length: 7 } || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); return true;
    }
    internal static void Validate(Settings settings)
    {
        if (settings.OverlayTransparencyPercent is double value && (!double.IsFinite(value) || value < 0 || value > 100))
            throw new ArgumentException("투명도는 0%부터 100%까지 설정하세요.");
        if (!TryColor(settings.OverlayBackgroundColor, out _)) throw new ArgumentException("배경색을 팔레트에서 선택하세요.");
    }
}
