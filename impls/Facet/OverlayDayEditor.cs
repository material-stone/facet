using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Facet.Design;

namespace Facet;

public sealed class OverlayDayEditor : Border
{
    private readonly TextBox newTodo = new() { MaxLength = 200, Tag = "할 일을 입력하고 Enter를 누르세요", Margin = new(0,0,0,12) };
    private readonly TextBox note = new() { MaxLength = 20000, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap,
        VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new(0), Visibility = Visibility.Collapsed };
    private readonly Border notePreview = new() { Padding = new(13,11,13,11), CornerRadius = new(6),
        BorderThickness = new(0), Focusable = true, Cursor = Cursors.Hand };
    private readonly StackPanel tasks = new();
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer autoSave = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Func<DateOnly, CalendarDayEntry>? load;
    private Func<CalendarDayEntry, bool>? save;
    private CalendarDayEntry draft = new();
    private Func<DateOnly,IReadOnlyList<DaySchedule>>? projectedSchedules;
    private Func<CalendarDayEntry,DaySchedule,bool,bool>? writeSchedule;
    internal DateOnly CurrentDate => draft.Date;
    private bool loading, dirty, changingNoteMode;
    public bool HasPendingChanges => dirty || !string.IsNullOrWhiteSpace(newTodo.Text);
    public bool IsNoteEditing => note.Visibility == Visibility.Visible;
    internal TextBox TodoInput => newTodo;
    internal IReadOnlyList<DaySchedule> Schedules => (projectedSchedules?.Invoke(draft.Date)??draft.Schedules).Select(s => s.Copy()).ToArray();
    internal event Action? DayChanged;
    internal IEnumerable<FrameworkElement> TabStops => new FrameworkElement[] { newTodo }
        .Concat(tasks.Children.OfType<FrameworkElement>().Where(row => row.Focusable))
        .Append(IsNoteEditing ? note : notePreview);

    public OverlayDayEditor()
    {
        MaxWidth = 740; Padding = new(20); CornerRadius = new(20);
        Background = Brush("Panel"); BorderBrush = Brush("Edge"); BorderThickness = new(1);
        newTodo.SetResourceReference(StyleProperty,"TextBoxWithoutFocusOutline");
        note.SetResourceReference(StyleProperty,"TextBoxWithoutFocusOutline");
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new());
        var todos = new StackPanel();
        todos.Children.Add(new TextBlock { Text = "할 일", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,12) });
        todos.Children.Add(newTodo);
        var taskScroll = new ScrollViewer { Content = tasks, MaxHeight = 180, Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        newTodo.SetBinding(WidthProperty,new System.Windows.Data.Binding(nameof(ScrollViewer.ViewportWidth)) {Source=taskScroll});
        newTodo.HorizontalAlignment=HorizontalAlignment.Left;
        todos.Children.Add(taskScroll);layout.Children.Add(todos);
        var recordHeader = new Grid { Margin = new(0,24,0,8) };
        recordHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });recordHeader.ColumnDefinitions.Add(new());
        recordHeader.Children.Add(new TextBlock { Text = "기록", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        status.FontSize=12;status.TextAlignment=TextAlignment.Right;status.TextWrapping=TextWrapping.Wrap;status.Margin=new(8,0,0,0);
        Grid.SetColumn(status,1);recordHeader.Children.Add(status);Grid.SetRow(recordHeader,1);layout.Children.Add(recordHeader);
        notePreview.Background = Brushes.Transparent;
        var noteArea = new Grid();
        var noteHover = new Border { Background = Brush("Hover"), CornerRadius = new(6), Opacity = 0, IsHitTestVisible = false };
        noteArea.Children.Add(noteHover);
        void HighlightPreview(bool active) => noteHover.BeginAnimation(OpacityProperty,
            new DoubleAnimation(active ? .4 : 0, TimeSpan.FromMilliseconds(active ? 140 : 100)));
        notePreview.MouseEnter += (_, _) => HighlightPreview(true);
        notePreview.MouseLeave += (_, _) => HighlightPreview(false);
        notePreview.IsVisibleChanged += (_, _) => HighlightPreview(notePreview.IsVisible && notePreview.IsMouseOver);
        noteArea.Children.Add(notePreview); noteArea.Children.Add(note);Grid.SetRow(noteArea,2);layout.Children.Add(noteArea);Child=layout;
        AutomationProperties.SetName(newTodo, "새 할 일"); AutomationProperties.SetName(note, "날짜별 노트");
        AutomationProperties.SetName(notePreview, "날짜별 노트 보기 · 클릭하거나 Enter로 편집");
        notePreview.MouseLeftButtonDown += (_, e) => { e.Handled = true; BeginNoteEdit(); };
        notePreview.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; BeginNoteEdit(); } };
        note.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; FinishNoteEdit(); } };
        note.LostKeyboardFocus += (_, _) => { if (!loading && !changingNoteMode) FinishNoteEdit(restoreFocus: false); };
        newTodo.LostKeyboardFocus += (_, _) => { if (!loading && !string.IsNullOrWhiteSpace(newTodo.Text)) TrySave(); };
        SizeChanged += (_, _) => {
            bool compact = ActualHeight < 300;
            Padding = new(compact ? 12 : 20); taskScroll.MaxHeight=Math.Min(180,ActualHeight*.25);
        };
        newTodo.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; if (AddPendingTodo()) RequestSave(); } };
        note.TextChanged += (_, _) => { if (!loading) { draft.Note = note.Text; RequestSave(); } };
        autoSave.Tick += (_, _) => { autoSave.Stop(); TrySave(includePendingTodo: false); };
        Unloaded += (_, _) => autoSave.Stop();
    }

    public void Configure(Func<DateOnly, CalendarDayEntry> read, Func<CalendarDayEntry, bool> write,
        Func<DateOnly,IReadOnlyList<DaySchedule>>? project=null,Func<CalendarDayEntry,DaySchedule,bool,bool>? scheduleWrite=null)
    { load=read;save=write;projectedSchedules=project;writeSchedule=scheduleWrite; }

    public bool Open(DateOnly date)
    {
        if (!TrySave()) return false;
        loading = true;
        draft = load?.Invoke(date) ?? new() { Date = date };
        AutomationProperties.SetName(this,date.ToString("yyyy년 M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR"))+" 할 일과 기록");
        newTodo.Clear(); note.Text = draft.Note; RenderTasks(); dirty = false; ShowNotePreview(); loading = false;
        SetStatus("자동 저장 중", false); DayChanged?.Invoke(); return true;
    }

    internal bool SaveSchedule(DaySchedule item, bool delete = false)
    {
        try {
            if (!delete) DaySchedule.Validate(item);
            if(writeSchedule!=null) {
                autoSave.Stop();
                if(!string.IsNullOrWhiteSpace(newTodo.Text) && !AddPendingTodo())return false;
                if(!writeSchedule(CalendarNotes.Copy(draft),item.Copy(),delete)) {SetStatus("저장하지 못했습니다. 일정 입력을 유지했습니다.",true);return false;}
                loading=true;draft=load!(draft.Date);dirty=false;RenderTasks();loading=false;
                if(!IsNoteEditing)ShowNotePreview();SetStatus("저장됨",false);DayChanged?.Invoke();return true;
            }

            var next = draft.Schedules.Where(s => s.Id != item.Id).Select(s => s.Copy()).ToList();
            if (!delete) next.Add(item.Copy());
            var candidate = CalendarNotes.Copy(draft); candidate.Schedules = next; CalendarNotes.Validate(candidate);
            var previous = draft.Schedules;
            draft.Schedules = next; dirty = true;
            bool saved = TrySave();
            if (!saved) draft.Schedules = previous;
            DayChanged?.Invoke(); return saved;
        } catch (Exception ex) { SetStatus(ex.Message, true); return false; }
    }

    private bool AddPendingTodo()
    {
        if (string.IsNullOrWhiteSpace(newTodo.Text)) return false;
        if (draft.Todos.Count >= 200) { SetStatus("할 일은 하루 200개까지 작성할 수 있습니다.", true); return false; }
        var todo = new CalendarTodo { Title = newTodo.Text.Trim() };
        if (draft.Todos.Count == 0) tasks.Children.Clear();
        draft.Todos.Add(todo); newTodo.Clear(); dirty = true; tasks.Children.Add(CreateTaskRow(todo)); return true;
    }

    private void RenderTasks()
    {
        tasks.Children.Clear();
        if (draft.Todos.Count == 0) tasks.Children.Add(new TextBlock { Text = "아직 할 일이 없습니다.", Foreground = Brush("Muted"), Margin = new(0,4,0,0) });
        foreach (var todo in draft.Todos) tasks.Children.Add(CreateTaskRow(todo));
    }

    private Border CreateTaskRow(CalendarTodo todo)
    {
        var text = new TextBlock { Text = todo.Title, TextWrapping = TextWrapping.Wrap };
        var content = new Grid();content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        text.VerticalAlignment=VerticalAlignment.Center;content.Children.Add(text);
        var delete = new Button { Content=new Icon{Kind=IconKind.Trash,Width=18,Height=18,Foreground=Brush("Muted")},ContentTemplate=null,
            Width=32,Height=32,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,0,0),Padding=new(7),ToolTip="삭제",
            BorderBrush=Brushes.Transparent,Background=Brushes.Transparent };
        delete.SetResourceReference(StyleProperty,"ButtonWithoutFocusOutline");
        AutomationProperties.SetName(delete,todo.Title+" 삭제");Grid.SetColumn(delete,1);content.Children.Add(delete);
        var surface = new Grid();
        var hover = new Border {Background=Brush("Hover"),CornerRadius=new(6),Opacity=0,IsHitTestVisible=false};
        surface.Children.Add(hover);content.Margin=new(12,10,12,10);surface.Children.Add(content);
        var row = new Border { Child = surface, Tag = todo.Id, Focusable = true, FocusVisualStyle=null, Cursor = Cursors.Hand,
            Background = Brush("Input"), BorderBrush = Brushes.Transparent, BorderThickness = new(0),
            CornerRadius = new(6), Margin = new(0,0,0,8) };
        void RenderState() {
            text.TextDecorations = todo.Done ? TextDecorations.Strikethrough : null;
            text.Foreground = Brush(todo.Done ? "Muted" : "Ink");
            AutomationProperties.SetName(row,todo.Title + (todo.Done ? " · 완료" : " · 미완료"));
        }
        void Toggle() { todo.Done = !todo.Done; RenderState(); RequestSave(); }
        delete.Click += (_, e) => {e.Handled=true;DeleteTask(todo,row);};
        RenderState();
        AutomationProperties.SetHelpText(row,"클릭 또는 Space/Enter로 완료 전환, Del로 삭제");
        row.MouseLeftButtonUp += (_, e) => {
            e.Handled = true; FocusManager.SetFocusedElement(FocusManager.GetFocusScope(row),row); row.Focus(); Toggle();
        };
        row.PreviewKeyDown += (_, e) => {
            if(delete.IsKeyboardFocusWithin)return;
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Toggle(); }
            else if (e.Key == Key.Delete) { e.Handled = true; DeleteTask(todo,row); }
        };
        row.LostKeyboardFocus += (_, _) => { if (!loading) TrySave(includePendingTodo: false); };
        void Highlight(bool active) => hover.BeginAnimation(OpacityProperty,new DoubleAnimation(active?.4:0,TimeSpan.FromMilliseconds(active?140:100)));
        row.MouseEnter += (_, _) => Highlight(true);
        row.MouseLeave += (_, _) => Highlight(false);
        return row;
    }

    private void DeleteTask(CalendarTodo todo, Border row)
    {
        int index = tasks.Children.IndexOf(row);
        if (index < 0) return;
        draft.Todos.Remove(todo); tasks.Children.Remove(row);
        if (draft.Todos.Count == 0) RenderTasks();
        RequestSave();
        FrameworkElement target = draft.Todos.Count == 0 ? newTodo : (Border)tasks.Children[Math.Min(index,tasks.Children.Count-1)];
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target),target); target.Focus();
    }

    private void RequestSave()
    {
        if (loading) return;
        dirty = true; SetStatus("저장 대기 중", false); autoSave.Stop(); autoSave.Start();
    }

    public void BeginNoteEdit()
    {
        notePreview.Visibility = Visibility.Collapsed; note.Visibility = Visibility.Visible;
        note.Focus(); note.CaretIndex = note.Text.Length;
    }

    public bool FinishNoteEdit(bool restoreFocus = true)
    {
        if (!IsNoteEditing || changingNoteMode) return false;
        changingNoteMode = true;
        try {
            if (TrySave(includePendingTodo: restoreFocus)) {
                ShowNotePreview();
                if (restoreFocus) {
                    FocusManager.SetFocusedElement(FocusManager.GetFocusScope(notePreview),notePreview);
                    notePreview.Focus();
                }
            }
        } finally { changingNoteMode = false; }
        return true;
    }

    private void ShowNotePreview()
    {
        notePreview.Child = new ScrollViewer { Content=OverlayNoteMarkdown.Render(note.Text), Background=Brushes.Transparent, Focusable=false,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        note.Visibility = Visibility.Collapsed; notePreview.Visibility = Visibility.Visible;
    }

    public bool TrySave(bool includePendingTodo = true)
    {
        autoSave.Stop();
        if (includePendingTodo && !string.IsNullOrWhiteSpace(newTodo.Text) && !AddPendingTodo()) return false;
        if (!dirty) return true;
        try {
            CalendarNotes.Validate(draft);
            if (save == null || !save(CalendarNotes.Copy(draft))) { SetStatus("저장하지 못했습니다. 입력을 유지했습니다. Ctrl+S로 다시 시도해 주세요.", true); return false; }
            dirty = false; if (!IsNoteEditing) ShowNotePreview(); SetStatus("저장됨", false); return true;
        } catch (Exception ex) { SetStatus(ex.Message, true); return false; }
    }

    private void SetStatus(string text, bool error) { status.Text = text; status.Foreground = Brush(error ? "Danger" : "Muted"); }
    private Brush Brush(string key) => (Brush)FindResource(key);
}
