using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Facet.SleepPlugin;

internal sealed class SleepRecordEditor : UserControl
{
    private readonly TextBlock heading = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new(0,12,0,0) };
    private SleepEntry? original;
    private DateOnly date;
    private string baseline = "";
    internal SleepTimeInput Attempt { get; } = new("잠 시도");
    internal SleepTimeInput Wake { get; } = new("최종 기상");
    internal TextBox Latency { get; } = new() { MaxLength = 4 };
    internal TextBox Awake { get; } = new() { MaxLength = 4 };
    internal TextBox Note { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top, MinHeight = 70, MaxHeight = 130, MaxLength = 4000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal Button SaveButton { get; } = new() { Content = "저장", MinWidth = 88, Margin = new(8,0,0,0) };
    internal Button CancelButton { get; } = new() { Content = "취소", Margin = new(0) };
    internal Button DeleteButton { get; } = new() { Content = TrashIcon(), ContentTemplate = null, Width = 32, Height = 32, Padding = new(7), BorderThickness = new(0), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, ToolTip = "수면 기록 삭제", Margin = new(0), Visibility = Visibility.Collapsed };
    internal Expander Details { get; } = new() { Header = "추가 기록", Margin = new(0,12,0,0) };
    internal Func<Action<SleepBook>, bool>? Commit { get; init; }
    internal Func<DateOnly, SleepEntry?>? Read { get; init; }
    internal event Action? Closed;
    internal string Error => error.Text;
    internal DateOnly Date => date;
    internal bool IsOpen { get; private set; }
    private string Fingerprint => string.Join("|", Attempt.Fingerprint, Wake.Fingerprint, Latency.Text, Awake.Text, Note.Text);

    public SleepRecordEditor()
    {
        Width = 424; KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        foreach (var input in new[] { Latency, Awake, Note }) input.SetResourceReference(StyleProperty, "TextBoxWithoutFocusOutline");
        Details.SetResourceReference(StyleProperty, "ExpanderWithoutFocusOutline");
        var body = new StackPanel { Margin = new(8) }; body.Children.Add(heading);
        var hint = new TextBlock { Text = "기상한 날짜의 기록 · 모르는 값은 비워 두세요", FontSize = 12, Margin = new(0,6,0,20) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(hint);
        body.Children.Add(Field("잠자려 한 시각", Attempt)); body.Children.Add(Field("최종 기상 시각", Wake));
        var extra = new StackPanel(); extra.Children.Add(Field("잠드는 데 걸린 시간 (분)", Latency));
        extra.Children.Add(Field("밤중에 깨어 있던 시간 (분)", Awake)); extra.Children.Add(Field("메모", Note));
        Details.Content = extra; body.Children.Add(Details);
        var meaning = new TextBlock { Text = "잠 시도~기상 구간은 실제 수면 시간과 다릅니다.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0,12,0,0) };
        meaning.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(meaning);
        error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); body.Children.Add(error);
        var actions = new Grid { Margin = new(0,20,0,0) }; actions.ColumnDefinitions.Add(new()); actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        DeleteButton.HorizontalAlignment = HorizontalAlignment.Left; actions.Children.Add(DeleteButton);
        DeleteButton.SetResourceReference(StyleProperty, "ButtonWithoutFocusOutline");
        AutomationProperties.SetName(DeleteButton, "이 수면 기록 삭제"); DeleteButton.Click += (_, _) => DeleteRecord();
        var saveActions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(saveActions, 1); actions.Children.Add(saveActions);
        saveActions.Children.Add(CancelButton); saveActions.Children.Add(SaveButton); body.Children.Add(actions);
        foreach (var button in new[] { SaveButton, CancelButton }) {
            button.SetResourceReference(StyleProperty, "ButtonWithoutFocusOutline"); button.BorderBrush = Brushes.Transparent; button.BorderThickness = new(0);
        }
        SaveButton.SetResourceReference(BackgroundProperty, "Mint"); SaveButton.SetResourceReference(ForegroundProperty, "OnAccent"); SaveButton.FontWeight = FontWeights.SemiBold;
        SaveButton.Click += (_, _) => TrySave(); CancelButton.Click += (_, _) => Close();
        // The window's Ctrl+S route skips an open widget modal, so the editor takes the shortcut itself.
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; TrySave(); } };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(Latency, "잠드는 데 걸린 시간 분"); AutomationProperties.SetName(Awake, "밤중 각성 시간 분"); AutomationProperties.SetName(Note, "수면 메모");
    }

    // The app's icon set is not part of the plugin contract.
    private static System.Windows.Shapes.Path TrashIcon()
    {
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse("M3,6 H21 M8,6 V3 H16 V6 M5,6 L6,21 H18 L19,6 M10,11 V17 M14,11 V17"), StrokeThickness = 2, Stretch = Stretch.Uniform, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        path.SetResourceReference(Shape.StrokeProperty, "Danger");
        return path;
    }

    private static StackPanel Field(string label, UIElement input)
    {
        var field = new StackPanel { Margin = new(0,0,0,8) };
        field.Children.Add(new TextBlock { Text = label, FontSize = 13, Margin = new(0,0,0,6) }); field.Children.Add(input); return field;
    }

    internal void Open(DateOnly value)
    {
        date = value; original = Read?.Invoke(date)?.Copy(); var draft = original ?? new SleepEntry { Date = date };
        heading.Text = date.ToString("M월 d일 (ddd) 수면", CultureInfo.GetCultureInfo("ko-KR"));
        Attempt.Value = draft.Attempt; Wake.Value = draft.FinalWake;
        Latency.Text = draft.Latency?.ToString(CultureInfo.InvariantCulture) ?? ""; Awake.Text = draft.AwakeMinutes?.ToString(CultureInfo.InvariantCulture) ?? ""; Note.Text = draft.Note;
        Details.IsExpanded = draft.Latency != null || draft.AwakeMinutes != null || draft.Note.Length > 0;
        baseline = Fingerprint; error.Visibility = Visibility.Collapsed; SaveButton.Content = "저장"; IsOpen = true;
        DeleteButton.Visibility = original == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // Saves any change without closing; the modal host closes after this returns true.
    internal bool SaveDraft()
    {
        if (!IsOpen || baseline == Fingerprint) return true;
        try {
            var draft = original?.Copy() ?? new SleepEntry { Date = date };
            draft.Attempt = Attempt.Value; draft.FinalWake = Wake.Value;
            draft.Latency = SleepTracking.ParseMinutes(Latency.Text, "잠드는 시간"); draft.AwakeMinutes = SleepTracking.ParseMinutes(Awake.Text, "각성 시간"); draft.Note = Note.Text;
            if (Commit?.Invoke(book => SleepTracking.Save(book, draft, original, DateTime.Now)) != true) { ShowError("저장하지 못했습니다. 입력을 유지했습니다. 다시 시도해 주세요."); return false; }
            baseline = Fingerprint;
            return true;
        }
        catch (ArgumentException ex) { ShowError(ex.Message); return false; }
    }

    internal bool TrySave()
    {
        if (!SaveDraft()) return false;
        Close(); return true;
    }

    private void ShowError(string message) { error.Text = message; error.Visibility = Visibility.Visible; SaveButton.Content = "다시 시도"; }

    internal bool DeleteRecord()
    {
        if (!IsOpen || original == null) return false;
        try {
            if (Commit?.Invoke(book => SleepTracking.Delete(book, original)) != true) {
                ShowDeleteError("삭제하지 못했습니다. 기록과 입력을 유지했습니다. 기록 삭제를 다시 눌러 주세요."); return false;
            }
            Close(); return true;
        }
        catch (ArgumentException ex) { ShowDeleteError(ex.Message); return false; }
    }

    private void ShowDeleteError(string message) { error.Text = message; error.Visibility = Visibility.Visible; DeleteButton.ToolTip = "삭제 다시 시도"; AutomationProperties.SetName(DeleteButton, "수면 기록 삭제 다시 시도"); }
    internal bool CloseDropDown() => Attempt.CloseDropDown() || Wake.CloseDropDown();
    internal void Finish() { Attempt.CloseDropDown(); Wake.CloseDropDown(); IsOpen = false; }
    private void Close() { Finish(); Closed?.Invoke(); }
}

internal sealed class SleepTimeInput : Grid
{
    internal ComboBox Hour { get; } = new() { Margin = new(0) };
    internal ComboBox Minute { get; } = new() { Margin = new(0) };
    private bool setting;
    private readonly string label;
    internal string Fingerprint => $"{Hour.SelectedIndex}:{Minute.SelectedIndex}";
    internal string? Value
    {
        get {
            if ((Hour.SelectedIndex > 0) != (Minute.SelectedIndex > 0)) throw new ArgumentException(label + ": 시와 분을 모두 선택하거나 비워 주세요.");
            return Hour.SelectedIndex <= 0 ? null : $"{Hour.SelectedIndex - 1:00}:{Minute.SelectedIndex - 1:00}";
        }
        set {
            setting = true;
            bool known = TimeOnly.TryParseExact(value, "HH:mm", out var time);
            Hour.SelectedIndex = known ? time.Hour + 1 : 0; Minute.SelectedIndex = known ? time.Minute + 1 : 0; setting = false;
        }
    }

    internal SleepTimeInput(string name)
    {
        label = name;
        ColumnDefinitions.Add(new()); ColumnDefinitions.Add(new() { Width = new(28) }); ColumnDefinitions.Add(new()); ColumnDefinitions.Add(new() { Width = new(64) });
        Hour.Items.Add("— 시"); Minute.Items.Add("— 분");
        for (int i = 0; i < 24; i++) Hour.Items.Add(i.ToString("00"));
        for (int i = 0; i < 60; i++) Minute.Items.Add(i.ToString("00"));
        foreach (var input in new[] { Hour, Minute }) {
            input.SetResourceReference(StyleProperty, "CalendarTimeInput"); input.BorderBrush = Brushes.Transparent; input.BorderThickness = new(0);
        }
        AutomationProperties.SetName(Hour, name + " 시"); AutomationProperties.SetName(Minute, name + " 분");
        Children.Add(Hour); var separator = new TextBlock { Text = ":", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; SetColumn(separator, 1); Children.Add(separator);
        SetColumn(Minute, 2); Children.Add(Minute);
        var clear = new Button { Content = "비움", Padding = new(6), Margin = new(8,0,0,0), FontSize = 12 }; SetColumn(clear, 3); Children.Add(clear);
        clear.SetResourceReference(StyleProperty, "ButtonWithoutFocusOutline"); clear.BorderBrush = Brushes.Transparent; clear.BorderThickness = new(0);
        AutomationProperties.SetName(clear, name + " 비우기"); clear.Click += (_, _) => Value = null;
        // A chosen hour defaults to :00; choosing a minute first leaves the hour explicitly unknown.
        Hour.SelectionChanged += (_, _) => { if (!setting && Hour.SelectedIndex > 0 && Minute.SelectedIndex == 0) Minute.SelectedIndex = 1; };
        Value = null;
    }

    internal bool CloseDropDown()
    {
        if (Hour.IsDropDownOpen) { Hour.IsDropDownOpen = false; return true; }
        if (Minute.IsDropDownOpen) { Minute.IsDropDownOpen = false; return true; }
        return false;
    }
}
