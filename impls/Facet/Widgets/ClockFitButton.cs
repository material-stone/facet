namespace Facet.Widgets;

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Facet.Plugins;

// Owned by the app and source-linked into the clock plugin, like ReminderProtocol: both clocks share one face rule (ADR 0082).
// Fills its tile as a button; the content keeps its ratio at the largest size that fits and sits at the chosen spot.
internal sealed class ClockFitButton : Button
{
    internal const string AlignmentKey = "alignment", DefaultAlignment = "top-left";
    internal const double HoverOpacity = .4;
    private static readonly string[] Rows = ["top", "center", "bottom"], Columns = ["left", "center", "right"];
    private static readonly string[] RowNames = ["위", "가운데", "아래"], ColumnNames = ["왼쪽", "가운데", "오른쪽"];
    private readonly Viewbox fit;
    private readonly Func<IWidgetModal> modal;
    private readonly Func<string, bool> save;
    private IModalHandle? open;

    internal ClockFitButton(FrameworkElement face, Func<IWidgetModal> modal, Func<string, bool> save)
    {
        this.modal = modal; this.save = save;
        fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = face };
        Content = fit;
        Template = Chrome(new CornerRadius(12));
        Background = Brushes.Transparent; BorderThickness = new Thickness(0); Padding = new Thickness(0);
        Focusable = false; IsTabStop = false; FocusVisualStyle = null; Cursor = System.Windows.Input.Cursors.Hand;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "시계 정렬 설정");
        Alignment = DefaultAlignment;
        Click += (_, _) => OpenChooser();
    }

    internal string Alignment
    {
        get;
        set {
            field = Parse(value) is var (row, column) ? Rows[row] + "-" + Columns[column] : DefaultAlignment;
            var (r, c) = Parse(field)!.Value;
            fit.VerticalAlignment = (VerticalAlignment)r;
            fit.HorizontalAlignment = (HorizontalAlignment)c;
        }
    } = DefaultAlignment;

    internal static (int Row, int Column)? Parse(string? value)
    {
        var parts = (value ?? "").Split('-');
        if (parts.Length != 2) return null;
        int row = Array.IndexOf(Rows, parts[0]), column = Array.IndexOf(Columns, parts[1]);
        return row < 0 || column < 0 ? null : (row, column);
    }

    private void OpenChooser()
    {
        if (open != null) return;
        var chooser = BuildChooser();
        open = modal().Show(chooser);
        if (open != null) open.Closed += () => open = null;
    }

    internal FrameworkElement BuildChooser()
    {
        var root = new StackPanel { Width = 312 };
        var title = new TextBlock { Text = "시계 정렬", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 312, Height = 216 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        void Mark()
        {
            foreach (Button button in grid.Children)
                if ((string)button.Tag == Alignment) button.SetResourceReference(BorderBrushProperty, "Mint");
                else button.SetResourceReference(BorderBrushProperty, "Edge");
        }
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++) {
                string value = Rows[row] + "-" + Columns[column];
                string name = row == 1 && column == 1 ? "가운데" : ColumnNames[column] + " " + RowNames[row];
                // The label sits where the clock will: its place in the cell repeats what its words say.
                var label = new TextBlock { Text = name, FontSize = 13, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = (VerticalAlignment)row, HorizontalAlignment = (HorizontalAlignment)column };
                label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
                var button = new Button { Tag = value, Margin = new Thickness(4), Padding = new Thickness(6), BorderThickness = new Thickness(2), Background = Brushes.Transparent, Template = Chrome(new CornerRadius(8)), ContentTemplate = null, Content = label };
                AutomationProperties.SetName(button, name);
                button.Click += (_, _) => {
                    string previous = Alignment;
                    Alignment = value;
                    bool saved = save(value);
                    if (!saved) Alignment = previous;
                    error.Text = saved ? "" : "저장하지 못했습니다. 다시 시도해 주세요.";
                    error.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
                    Mark();
                };
                grid.Children.Add(button);
            }
        Mark();
        root.Children.Add(title); root.Children.Add(grid); root.Children.Add(error);
        return root;
    }

    private static BeginStoryboard Fade(double to, double seconds)
    {
        var fade = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds));
        Storyboard.SetTargetName(fade, "HoverChrome");
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        var board = new Storyboard();
        board.Children.Add(fade);
        return new BeginStoryboard { Storyboard = board };
    }

    private static ControlTemplate Chrome(CornerRadius corner)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, corner);
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(content);
        // Same hover as the weather widget: the Hover token at 40%, fading in over 0.14s and out over 0.1s.
        var shade = new FrameworkElementFactory(typeof(Border));
        shade.SetValue(Border.CornerRadiusProperty, corner);
        shade.SetResourceReference(Border.BackgroundProperty, "Hover");
        shade.SetValue(OpacityProperty, HoverOpacity);
        var hoverChrome = new FrameworkElementFactory(typeof(Grid), "HoverChrome");
        hoverChrome.SetValue(OpacityProperty, 0d);
        hoverChrome.SetValue(IsHitTestVisibleProperty, false);
        hoverChrome.AppendChild(shade);
        var surface = new FrameworkElementFactory(typeof(Grid));
        surface.AppendChild(hoverChrome);
        surface.AppendChild(border);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.EnterActions.Add(Fade(1, .14));
        hover.ExitActions.Add(Fade(0, .1));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Ink"), "Chrome"));
        template.Triggers.Add(focus);
        return template;
    }
}
