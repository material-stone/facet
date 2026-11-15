namespace Facet.SchedulePlugin;

// The app's body font, read from its resources like every other plugin brush.
internal static class ScheduleText
{
    internal static FontFamily Family => Application.Current?.TryFindResource("AppFontFamily") as FontFamily ?? new FontFamily("Segoe UI");
    internal static Typeface Regular => new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
}
