using Facet.Plugins;

namespace Facet;

// What one plugin may ask of the app (ADR 0084).
internal sealed class PluginShell(Controller app, string pluginId) : IPluginShell
{
    public string DataRoot => app.Store.Root;
    public bool Privacy => app.Data.Settings.Privacy;
    public bool HideOverlay() { app.HideOverlay(); return !app.Data.Settings.OverlayVisible; }
    public bool ShowWidget(string widgetId) => app.ShowWidget(widgetId);
    public void OpenSettings() => app.OpenPluginSettings(pluginId);
    public void ReportError(string message) => app.Dispatcher.BeginInvoke(() => app.ReportError(message));
    public string? SetHotkey(string id, string shortcut) => app.SetPluginHotkey(pluginId, id, shortcut);
}
