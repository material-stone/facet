using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Facet.Design;

namespace Facet;

public sealed class OverlayCalendarView : Border
{
    private readonly TextBlock heading = new() { FontSize = 28, FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid dates = new() { Columns = 7, Rows = 6 };
    private readonly Button previous, next;
    private DateOnly? renderedToday;
    private DateOnly month;
    private DateOnly? selectedDate;
    public DateOnly? SelectedDate => selectedDate;
    private HashSet<DateOnly> recordedDates = [];
    private Func<DateOnly,bool>? recordedDateLookup;
    public event Action<DateOnly>? DateSelected;

    public OverlayCalendarView()
    {
        Width = 644; Padding = new(32); CornerRadius = new(20); Focusable = true; FocusVisualStyle = null;
        Background = Brush("Panel"); BorderBrush = Brush("Edge"); BorderThickness = new(1);
        var body = new StackPanel();
        var navigation = new Grid();
        foreach (double width in new[] { 40d, 0d, 40d })
            navigation.ColumnDefinitions.Add(new() { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        previous = MonthButton(IconKind.Left, "이전 달", -1);
        next = MonthButton(IconKind.Right, "다음 달", 1);
        UIElement[] controls = [previous, heading, next];
        for (int column = 0; column < controls.Length; column++) {
            Grid.SetColumn(controls[column], column); navigation.Children.Add(controls[column]);
        }
        var header = new StackPanel(); header.Children.Add(navigation);
        var todayButton = new Button { Content = "오늘", FontSize = 12, Padding = new(14,5,14,5),
            IsTabStop = false,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0,12,0,20) };
        AutomationProperties.SetName(todayButton, "오늘이 있는 달로 돌아가기");
        AutomationProperties.SetAcceleratorKey(todayButton, "Home");
        todayButton.Click += (_, _) => GoToToday(); header.Children.Add(todayButton);
        body.Children.Add(header);
        body.Children.Add(new Border { Height = 1, Background = Brush("Edge"), Margin = new(0,0,0,18) });
        var weekdays = new UniformGrid { Columns = 7, Margin = new(0,0,0,12) };
        string[] names = ["일", "월", "화", "수", "목", "금", "토"];
        for (int column = 0; column < 7; column++)
            weekdays.Children.Add(new TextBlock { Text = names[column], Foreground = DayBrush(column), FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold });
        body.Children.Add(weekdays); body.Children.Add(dates); Child = body;
        Refresh(DateOnly.FromDateTime(DateTime.Now));
    }

    public void Refresh(DateOnly today)
    {
        if (renderedToday == today) return;
        renderedToday = today;
        month = new(today.Year, today.Month, 1);
        RenderMonth();
    }

    public void MoveMonth(int direction)
    {
        long index = (month.Year - 1) * 12L + month.Month - 1 + direction;
        if (index < 0 || index >= 9999 * 12L) return;
        month = month.AddMonths(direction); RenderMonth();
    }

    public void GoToToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        renderedToday = today; month = new(today.Year, today.Month, 1); RenderMonth();
        SetSelectedDate(today);
    }

    public void SetRecordedDates(IEnumerable<DateOnly> datesWithContent)
    {
        bool hadLookup=recordedDateLookup!=null;recordedDateLookup=null;
        var nextDates = datesWithContent.ToHashSet();
        if (!hadLookup && recordedDates.SetEquals(nextDates)) return;
        recordedDates = nextDates; RenderMonth();
    }

    public void SetRecordedDateLookup(Func<DateOnly,bool> lookup){recordedDateLookup=lookup;RenderMonth();}

    public void SetSelectedDate(DateOnly? date) { selectedDate = date; RenderMonth(); }

    internal FrameworkElement SelectedDateFocusTarget()
    {
        if (selectedDate is not { } selected) return this;
        var selectedMonth = new DateOnly(selected.Year,selected.Month,1);
        if (month != selectedMonth) { month = selectedMonth; RenderMonth(); }
        return dates.Children.OfType<Border>().Single(cell => Equals(cell.Tag,selected));
    }

    private void RenderMonth()
    {
        heading.Text = month.ToString("yyyy년 M월", CultureInfo.InvariantCulture);
        previous.IsEnabled = month.Year != 1 || month.Month != 1;
        next.IsEnabled = month.Year != 9999 || month.Month != 12;
        AutomationProperties.SetName(this, heading.Text + " 달력");
        dates.Children.Clear();
        int offset = (int)month.DayOfWeek, count = DateTime.DaysInMonth(month.Year, month.Month);
        for (int slot = 0; slot < 42; slot++)
        {
            int day = slot - offset + 1;
            if (day < 1 || day > count) { dates.Children.Add(new Border()); continue; }
            var date = new DateOnly(month.Year, month.Month, day);
            bool hasContent=recordedDateLookup?.Invoke(date) ?? recordedDates.Contains(date);
            bool isToday = date == renderedToday;
            bool isSelected = date == selectedDate;
            var cell = new Border { Width = 56, Height = 56, Tag = date, Margin = new(0,3,0,3), CornerRadius = new(12), Focusable = true, Cursor = Cursors.Hand,
                BorderThickness = new(isSelected ? 2 : 0), BorderBrush = Brush("Mint"),
                Background = isToday ? Brush("Hover") : Brushes.Transparent,
                Child = new TextBlock { Text = day.ToString(CultureInfo.InvariantCulture), FontSize = 20,
                    FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal, Foreground = DayBrush(slot % 7),
                    TextDecorations = hasContent ? TextDecorations.Underline : null,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            string label = date.ToString("yyyy년 M월 d일", CultureInfo.InvariantCulture);
            KeyboardNavigation.SetIsTabStop(cell,isSelected);
            AutomationProperties.SetName(cell, label + (isToday ? " · 오늘" : "") + (isSelected ? " · 선택됨" : "") + (hasContent ? " · 기록 있음" : ""));
            cell.MouseEnter+=(_,_)=>cell.Background=Brush(isToday?"Selected":"Hover");
            cell.MouseLeave+=(_,_)=>cell.Background=isToday?Brush("Hover"):Brushes.Transparent;
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; DateSelected?.Invoke(date); };
            cell.PreviewKeyDown += (_, e) => {
                if (Keyboard.Modifiers != ModifierKeys.None) return;
                int days = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -7, Key.Down => 7, _ => 0 };
                if (days != 0) {
                    e.Handled = true;
                    int dayNumber = date.DayNumber + days;
                    if (dayNumber >= DateOnly.MinValue.DayNumber && dayNumber <= DateOnly.MaxValue.DayNumber) {
                        SetSelectedDate(DateOnly.FromDayNumber(dayNumber));
                        var target=SelectedDateFocusTarget();FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target),target);target.Focus();
                    }
                } else if (e.Key is Key.Enter or Key.Space) { e.Handled = true; DateSelected?.Invoke(date); }
            };
            cell.GotKeyboardFocus += (_, _) => cell.BorderThickness = new(isSelected ? 2 : 1);
            cell.LostKeyboardFocus += (_, _) => cell.BorderThickness = new(isSelected ? 2 : 0);
            dates.Children.Add(cell);
        }
    }

    private Button MonthButton(IconKind icon, string label, int direction)
    {
        string shortcut = direction < 0 ? "Page Up" : "Page Down";
        var button = new Button { ContentTemplate = null, Content = new Icon { Kind = icon, Width = 18, Height = 18, Foreground = Brush("Ink") },
            Width = 36, Height = 36, Padding = new(8), Margin = new(0), IsTabStop = false };
        AutomationProperties.SetName(button, label); AutomationProperties.SetAcceleratorKey(button, shortcut);
        button.Click += (_, _) => MoveMonth(direction); return button;
    }

    private Brush Brush(string key) => (Brush)FindResource(key);
    private Brush DayBrush(int column) => Brush(column == 0 ? "CalendarSunday" : column == 6 ? "CalendarSaturday" : "Ink");
}
