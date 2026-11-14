using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Facet;
using Facet.SchedulePlugin;

internal static partial class Program
{
    private static Cursor ResolvedCursor(UIElement target)
    {
        var query = new QueryCursorEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.QueryCursorEvent };
        target.RaiseEvent(query);
        return query.Cursor ?? Cursors.Arrow;
    }

    private static void CheckDisplayCursors(Controller controller)
    {
        var hud=new OverlayTabs();hud.Connect(controller);var board=ScheduleBoardFor(hud,controller.Store.Root);board.AnimateDayTransitions=false;
        var tabs=(TabControl)hud.FindName("Tabs");
        void Layout() { hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout(); }
        Layout();
        foreach(TabItem tab in tabs.Items) {
            var frame=(UIElement)tab.Template.FindName("Frame",tab);
            Assert(ResolvedCursor(frame)==Cursors.Hand && ResolvedCursor(VisualElements((UIElement)tab.Header).OfType<TextBlock>().First())==Cursors.Hand,
                "tab hand is scoped to its clickable header frame and label");
        }
        tabs.SelectedItem=hud.FindName("DashboardTab");Layout();var calendar=(OverlayCalendarView)board.FindName("Calendar");
        Assert(VisualElements(calendar).OfType<TextBlock>().Where(text=>text.Text.Contains("년") || new[]{"일","월","화","수","목","금","토"}.Contains(text.Text)).All(text=>ResolvedCursor(text)==Cursors.Arrow),
            "calendar month and weekday labels resolve arrow");
        Assert(ResolvedCursor(calendar.SelectedDateFocusTarget())==Cursors.Hand,"clickable calendar dates retain hand");
        board.OpenDay(DateOnly.FromDateTime(DateTime.Now));Layout();
        Assert(ResolvedCursor((UIElement)board.FindName("DayHeading"))==Cursors.Arrow && ResolvedCursor((UIElement)board.FindName("DayEditor"))==Cursors.Arrow,
            "day heading and passive right panel resolve arrow");
        var legend=(StackPanel)board.FindName("ScheduleLegend");var label=new TextBlock {Text="일정 범례"};legend.Children.Add(label);
        Assert(ResolvedCursor(label)==Cursors.Arrow,"diagram legend text resolves arrow");
        var dial=(OverlayScheduleDial)board.FindName("ScheduleDial");dial.MovePointer(new Point(0,0));
        Assert(ResolvedCursor(dial)==Cursors.Arrow,"diagram space outside the interactive circle resolves arrow");
        tabs.SelectedItem=hud.FindName("SettingsTab");Layout();var settings=(OverlaySettingsView)hud.FindName("SettingsView");
        var groups=(StackPanel)settings.FindName("Groups");
        var labels=groups.Children.OfType<StackPanel>().SelectMany(group=>group.Children.OfType<Grid>())
            .Select(row=>((StackPanel)row.Children[0]).Children[0]).OfType<TextBlock>().ToArray();
        Assert(labels.Length>0 && labels.All(text=>ResolvedCursor(text)==Cursors.Arrow) && ResolvedCursor((UIElement)settings.FindName("Status"))==Cursors.Arrow,
            "settings names descriptions and status resolve arrow while keeping help tooltips");
        var search=(TextBox)settings.FindName("SearchInput");
        var inputTarget=search.InputHitTest(new Point(search.ActualWidth/2,search.ActualHeight/2)) as UIElement ?? search;
        Assert(ResolvedCursor(inputTarget)==Cursors.IBeam,$"editable settings input retains the text cursor at its actual hit target ({inputTarget.GetType().Name}, {ResolvedCursor(inputTarget)})");
    }
}
