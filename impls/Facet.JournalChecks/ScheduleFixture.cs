using System.Windows.Controls;
using Facet;
using Facet.Core;
using Facet.SchedulePlugin;

internal static partial class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint flags,uint dx,uint dy,uint data,nuint extra);

    // The calendar is the schedule plugin's widget now (ADR 0101); checks host its view on the dashboard tab of an overlay,
    // with the plugin's real encrypted store in that overlay's data folder and the overlay as its full-screen layer.
    private static ScheduleBoard ScheduleBoardFor(OverlayTabs hud, string root)
    {
        var host = (Grid)hud.FindName("DashboardViewport");
        if (host.Children.OfType<Canvas>().SelectMany(c => c.Children.OfType<ScheduleBoard>()).FirstOrDefault() is { } existing) return existing;
        // Sized like the default calendar tile at the board's origin.
        var board = new ScheduleBoard(new ScheduleRecords(PluginFileStore.For(root, LegacySchedule.Plugin)), hud) {
            Width = WorkspaceLayouts.CalendarColumns * OverlayWorkspaceView.Pitch - OverlayWorkspaceView.Gap, Height = WorkspaceLayouts.CalendarRows * OverlayWorkspaceView.Pitch - OverlayWorkspaceView.Gap };
        // Centered on the viewport like a tile at the board's origin, free to overflow a small window.
        var origin = new Canvas { Width = 0, Height = 0, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        Canvas.SetLeft(board, -board.Width / 2); Canvas.SetTop(board, -board.Height / 2);
        origin.Children.Add(board); host.Children.Add(origin);
        return board;
    }

    // What the plugin's store holds, read fresh like a later start would.
    private static AppData ScheduleData(string root) => new ScheduleRecords(PluginFileStore.For(root, LegacySchedule.Plugin)).Data;
}