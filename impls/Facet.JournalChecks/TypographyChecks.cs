using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Facet;
using Facet.SchedulePlugin;
using Facet.Design;

internal static partial class Program
{
    private static void CheckTypography(Controller controller)
    {
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold }) {
            Assert(new Typeface(Typography.Family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var face) &&
                face.FontUri.OriginalString.Contains("Pretendard-", System.StringComparison.OrdinalIgnoreCase) && face.Weight == weight &&
                face.CharacterToGlyphMap.ContainsKey('한') && face.CharacterToGlyphMap.ContainsKey('A'),
                "bundled Pretendard resolves Korean/Latin glyphs at weight " + weight);
        }
        bool UsesAppFont(FontFamily family) => family.Source == Typography.Family.Source;
        Assert(UsesAppFont(controller.Overlay.FontFamily), "overlay root use the shared font");
        var panel = new StackPanel();
        Control[] controls = [new Button { Content="버튼" }, new TextBox { Text="입력" }, new ComboBox { ItemsSource=new[]{"선택"}, SelectedIndex=0 },
            new CheckBox { Content="선택" }, new DatePicker(), new ListBox { ItemsSource=new[]{"목록"} }, new ToolTip { Content="도움말" }];
        foreach (var control in controls) if (control is not ToolTip) panel.Children.Add(control);
        panel.Measure(new Size(800,800)); panel.Arrange(new Rect(0,0,800,800)); panel.UpdateLayout();
        Assert(controls.All(control => UsesAppFont(control.FontFamily)), "controls, popup owners and tooltips share Pretendard");
        var check = controls.OfType<CheckBox>().Single();
        var box = (System.Windows.Controls.Border)check.Template.FindName("Box",check);
        var mark = (System.Windows.Shapes.Path)check.Template.FindName("Mark",check);
        Assert(box.Width==18 && mark.Visibility==Visibility.Collapsed && box.Background==check.FindResource("Input"), "an unchecked box uses the shared template without a mark");
        check.IsChecked=true; check.UpdateLayout();
        Assert(mark.Visibility==Visibility.Visible && box.Background==check.FindResource("Mint"), "a checked box fills with the accent and shows the mark");
        var standalone = new TextBlock { Text="본문" }; panel.Children.Add(standalone);
        panel.Measure(new Size(800,800)); panel.Arrange(new Rect(0,0,800,800)); panel.UpdateLayout();
        Assert(UsesAppFont(standalone.FontFamily), "text in a programmatic panel uses Pretendard without a window ancestor");
        var markdown = OverlayNoteMarkdown.Render("일반 **굵게** `inline`\n\n```\n코드 block\n```");
        markdown.Measure(new Size(800,800)); markdown.Arrange(new Rect(0,0,800,800)); markdown.UpdateLayout();
        bool TextUsesAppFont(DependencyObject owner) {
            if (owner is TextBlock text && (!UsesAppFont(text.FontFamily) || text.Inlines.OfType<System.Windows.Documents.Run>().Any(run => !UsesAppFont(run.FontFamily)))) return false;
            for (int i=0;i<VisualTreeHelper.GetChildrenCount(owner);i++) if (!TextUsesAppFont(VisualTreeHelper.GetChild(owner,i))) return false;
            return true;
        }
        Assert(TextUsesAppFont(markdown), "markdown text, fenced code and inline code use the shared font");
        using var tray = Typography.CreateTrayMenu();
        Assert(tray.Font.FontFamily.Name == "Pretendard", "WinForms tray menu resolves the bundled private font");
        tray.Items.Add("메뉴 글꼴 확인");
        using var bitmap = new System.Drawing.Bitmap(300,80); tray.Size = new System.Drawing.Size(300,80);
        tray.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0,0,300,80));
        Assert(tray.Items[0].Font.FontFamily.Name == "Pretendard", "tray items inherit the private menu font during rendering");
    }
}
