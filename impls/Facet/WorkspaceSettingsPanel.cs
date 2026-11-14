namespace Facet;

// Settings rows for tab order, visibility and names. Changes save immediately.
internal sealed class WorkspaceSettingsPanel : StackPanel
{
    private OverlayTabs? owner;
    private bool refreshing;

    internal void Connect(OverlayTabs tabs) { owner = tabs; Refresh(); }

    internal void Refresh()
    {
        if (owner == null || refreshing) return;
        refreshing = true;
        Children.Clear();
        foreach (var tab in owner.WorkspaceTabs.Where(t => t.Id != WorkspaceLayouts.SettingsTab)) Children.Add(TabRow(tab));
        var add = new Button { Content = "새 탭 추가", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 12) };
        add.Click += (_, _) => owner.AddWorkspaceTab("새 탭 " + (owner.WorkspaceTabs.Count + 1));
        Children.Add(add);
        if (owner.WorkspaceError.Length > 0) Children.Add(Note(owner.WorkspaceError));
        refreshing = false;
    }

    private TextBlock Note(string text) => new() { Text = text, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 4) };

    private Button IconButton(WorkspaceTab tab)
    {
        var button = new Button { Width = 26, Height = 26, Padding = new(0), Margin = new(5, 0, 0, 0), ContentTemplate = null, Background = Brushes.Transparent, BorderThickness = new(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, ToolTip = "아이콘 변경",
            Content = new Design.Icon { Kind = TabIcons.For(tab), Width = 16, Height = 16 } };
        System.Windows.Automation.AutomationProperties.SetName(button, tab.Title + " 아이콘 변경");
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6 };
        var picker = new System.Windows.Controls.Primitives.Popup { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Edge"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(8), Child = grid } };
        foreach (var (kind, label) in TabIcons.Presets) {
            var choice = new Button { Width = 32, Height = 32, Padding = new(0), Margin = new(2), ContentTemplate = null, ToolTip = label, Content = new Design.Icon { Kind = kind, Width = 18, Height = 18 } };
            if (kind == TabIcons.For(tab)) choice.Background = (Brush)FindResource("Selected");
            System.Windows.Automation.AutomationProperties.SetName(choice, label);
            choice.Click += (_, _) => { picker.IsOpen = false; owner!.SetWorkspaceTabIcon(tab.Id, kind); };
            grid.Children.Add(choice);
        }
        button.Click += (_, _) => picker.IsOpen = true;
        return button;
    }

    private Grid TabRow(WorkspaceTab tab)
    {
        var row = new Grid { Margin = new(0, 0, 0, 4) };
        // Plugins point at tabs by this id.
        row.ToolTip = "탭 ID: " + tab.Id;
        row.ColumnDefinitions.Add(new());
        for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var input = new TextBox { Text = tab.Title, MaxLength = WorkspaceLayouts.MaxTitleLength, MinWidth = 100, Height = 32, Margin = new(0), Padding = new(36, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(input, "탭 이름");
        input.LostKeyboardFocus += (_, _) => { if (input.Text.Trim() != tab.Title) owner!.RenameWorkspaceTab(tab.Id, input.Text); };
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && input.Text.Trim() != tab.Title) owner!.RenameWorkspaceTab(tab.Id, input.Text); };
        row.Children.Add(input);
        // Lies inside the input's left padding, so the title starts right after it.
        row.Children.Add(IconButton(tab));
        void Cell(int column, FrameworkElement element) { Grid.SetColumn(element, column); element.Margin = new(4, 0, 0, 0); row.Children.Add(element); }
        Button Small(object text, string label, Action click)
        {
            var button = new Button { Content = text, Width = 32, Height = 32, Padding = new(0), VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            // The shared button template shows text only, so an icon needs it cleared.
            if (text is not string) button.ContentTemplate = null;
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => click();
            return button;
        }
        Cell(1, Small("↑", tab.Title + " 앞으로", () => owner!.MoveWorkspaceTab(tab.Id, -1)));
        Cell(2, Small("↓", tab.Title + " 뒤로", () => owner!.MoveWorkspaceTab(tab.Id, 1)));
        var visible = new CheckBox { Content = "표시", IsChecked = !tab.Hidden, VerticalAlignment = VerticalAlignment.Center };
        visible.Click += (_, _) => owner!.SetWorkspaceTabHidden(tab.Id, visible.IsChecked != true);
        Cell(3, visible);
        Cell(4, Small(new Design.Icon { Kind = Design.IconKind.Trash, Width = 16, Height = 16 }, tab.Title + " 삭제", () => owner!.RemoveWorkspaceTab(tab.Id)));
        return row;
    }
}
