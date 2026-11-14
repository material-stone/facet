using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Facet;

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
        var hud=new OverlayTabs();hud.AnimateDayTransitions=false;hud.Connect(controller);
        var tabs=(TabControl)hud.FindName("Tabs");
        void Layout() { hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout(); }
        Layout();
        var dashboard=(OverlayDashboardView)hud.FindName("Dashboard");
        Assert(ResolvedCursor((UIElement)dashboard.FindName("AgendaSection"))==Cursors.Arrow,"display-only dashboard element resolves arrow: AgendaSection");
        foreach(string name in new[]{"DateText","TimeText","ClockGroup"})
            Assert(ResolvedCursor((UIElement)dashboard.FindName(name))==Cursors.Hand,"the clock is a button and resolves hand: "+name);
        Assert(VisualElements((UIElement)dashboard.FindName("AgendaItems")).OfType<TextBlock>().All(text=>ResolvedCursor(text)==Cursors.Arrow),
            "notification card text resolves arrow rather than inheriting the tab header hand");
        Assert(ResolvedCursor((UIElement)dashboard.FindName("TemperatureText"))==Cursors.Hand,
            "weather refresh text retains the clickable parent button hand");
        foreach(TabItem tab in tabs.Items) {
            var frame=(UIElement)tab.Template.FindName("Frame",tab);
            Assert(ResolvedCursor(frame)==Cursors.Hand && ResolvedCursor(VisualElements((UIElement)tab.Header).OfType<TextBlock>().First())==Cursors.Hand,
                "tab hand is scoped to its clickable header frame and label");
        }
        tabs.SelectedIndex=1;Layout();var calendar=(OverlayCalendarView)hud.FindName("Calendar");
        Assert(VisualElements(calendar).OfType<TextBlock>().Where(text=>text.Text.Contains("년") || new[]{"일","월","화","수","목","금","토"}.Contains(text.Text)).All(text=>ResolvedCursor(text)==Cursors.Arrow),
            "calendar month and weekday labels resolve arrow");
        Assert(ResolvedCursor(calendar.SelectedDateFocusTarget())==Cursors.Hand,"clickable calendar dates retain hand");
        hud.OpenDay(DateOnly.FromDateTime(DateTime.Now));Layout();
        Assert(ResolvedCursor((UIElement)hud.FindName("DayHeading"))==Cursors.Arrow && ResolvedCursor((UIElement)hud.FindName("DayEditor"))==Cursors.Arrow,
            "day heading and passive right panel resolve arrow");
        var legend=(StackPanel)hud.FindName("ScheduleLegend");var label=new TextBlock {Text="일정 범례"};legend.Children.Add(label);
        Assert(ResolvedCursor(label)==Cursors.Arrow,"diagram legend text resolves arrow");
        var dial=(OverlayScheduleDial)hud.FindName("ScheduleDial");dial.MovePointer(new Point(0,0));
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
