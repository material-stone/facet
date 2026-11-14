using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;

internal static partial class Program
{
    private static void CheckSharedScrolling(string qa)
    {
        static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject owner) {
            for (int i=0;i<VisualTreeHelper.GetChildrenCount(owner);i++) {
                var child=VisualTreeHelper.GetChild(owner,i);yield return child;
                foreach(var next in Descendants(child))yield return next;
            }
        }
        static void Layout(FrameworkElement control,double width,double height) {
            if(control is Control templated)templated.ApplyTemplate();
            control.Measure(new Size(width,height));control.Arrange(new Rect(0,0,width,height));control.UpdateLayout();
        }
        static ScrollBar Bar(ScrollViewer viewer,string name) => (ScrollBar)viewer.Template.FindName(name,viewer);
        static ScrollContentPresenter Presenter(ScrollViewer viewer) => (ScrollContentPresenter)viewer.Template.FindName("PART_ScrollContentPresenter",viewer);
        var both=new ScrollViewer {HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Content=new Border {Width=900,Height=600,Background=(Brush)Application.Current.FindResource("Input"),Child=new TextBlock {Text=string.Join("\n",Enumerable.Range(1,24).Select(i=>$"항목 {i} · 공통 스크롤 영역")),Margin=new Thickness(16),FontSize=18}}};
        Layout(both,400,240);
        var vertical=Bar(both,"PART_VerticalScrollBar");var horizontal=Bar(both,"PART_HorizontalScrollBar");var presenter=Presenter(both);
        Assert(vertical.Visibility==Visibility.Visible && horizontal.Visibility==Visibility.Visible,"programmatic scroll viewers inherit shared scrolling on both axes");
        Assert(presenter.TranslatePoint(new Point(presenter.ActualWidth,0),both).X<=vertical.TranslatePoint(new Point(),both).X+.1 &&
            presenter.TranslatePoint(new Point(0,presenter.ActualHeight),both).Y<=horizontal.TranslatePoint(new Point(),both).Y+.1,
            "scrollbars occupy separate gutters without covering content");
        both.ScrollToHorizontalOffset(120);both.ScrollToVerticalOffset(100);both.UpdateLayout();
        Assert(both.HorizontalOffset>0 && both.VerticalOffset>0,"both-axis scrolling reaches offscreen content");
        double beforeWheel=both.VerticalOffset;
        both.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) {RoutedEvent=Mouse.MouseWheelEvent});both.UpdateLayout();
        Assert(both.VerticalOffset>beforeWheel,"mouse wheel continues to scroll the shared viewer");
        double beforeDrag=both.HorizontalOffset;
        var track=(Track)horizontal.Template.FindName("PART_Track",horizontal);
        track.Thumb.RaiseEvent(new DragStartedEventArgs(0,0) {RoutedEvent=Thumb.DragStartedEvent});
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(30,0) {RoutedEvent=Thumb.DragDeltaEvent});
        track.Thumb.RaiseEvent(new DragCompletedEventArgs(30,0,false) {RoutedEvent=Thumb.DragCompletedEvent});both.UpdateLayout();
        Assert(both.HorizontalOffset>beforeDrag,"shared horizontal thumb drag remains connected to scroll offsets");
        double contentWidth=presenter.ActualWidth,contentHeight=presenter.ActualHeight;
        both.Content=new Border {Width=20,Height=20};Layout(both,400,240);
        Assert(vertical.Visibility==Visibility.Collapsed && horizontal.Visibility==Visibility.Collapsed &&
            presenter.ActualWidth==400 && presenter.ActualHeight==240,"Auto gutters release their space when bars disappear");
        both.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;both.HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden;Layout(both,400,240);
        Assert(presenter.ActualWidth==400 && presenter.ActualHeight==240,"disabled and hidden axes reserve no gutter space");

        var single=new TextBox {Text="한 줄 입력",Style=(Style)Application.Current.FindResource(typeof(TextBox))};Layout(single,380,48);
        var singleScroll=Descendants(single).OfType<ScrollViewer>().First();
        var singleGrid=(Grid)VisualTreeHelper.GetChild(singleScroll,0);
        Assert(singleGrid.ColumnDefinitions[1].ActualWidth==0 && singleGrid.RowDefinitions[1].ActualHeight==0,"single-line text inputs retain their full content area without unused gutters");
        var note=new TextBox {Text=string.Join("\n",Enumerable.Range(1,30).Select(i=>$"메모 {i} · 자유롭게 작성하는 내용")),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0),Style=(Style)Application.Current.FindResource(typeof(TextBox))};
        Layout(note,380,180);note.Select(3,7);string selection=note.SelectedText;note.ScrollToEnd();note.UpdateLayout();
        var noteScroll=Descendants(note).OfType<ScrollViewer>().First();
        Assert(noteScroll.VerticalOffset>0 && note.SelectedText==selection && Bar(noteScroll,"PART_VerticalScrollBar").Visibility==Visibility.Visible,"multiline input keeps editable selection and vertical scrolling with the shared style");
        var list=new ListBox {ItemsSource=Enumerable.Range(1,100).Select(i=>$"기록 {i}").ToArray(),Style=(Style)Application.Current.FindResource(typeof(ListBox))};Layout(list,380,180);
        var listHost=new Window {Content=list,Left=-30000,Top=-30000,Width=380,Height=180,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        try {
            listHost.Show();Pump(25);list.SelectedIndex=99;list.ScrollIntoView(list.SelectedItem);Pump(25);list.UpdateLayout();
            var listScroll=Descendants(list).OfType<ScrollViewer>().First();
            Assert(listScroll.CanContentScroll && listScroll.VerticalOffset>0 && list.SelectedItem?.ToString()=="기록 100",$"shared list template preserves logical scrolling and selected-item navigation (logical={listScroll.CanContentScroll}, offset={listScroll.VerticalOffset}, extent={listScroll.ExtentHeight}, viewport={listScroll.ViewportHeight})");
        } finally {listHost.Content=null;listHost.Close();}
        var combo=new ComboBox {ItemsSource=Enumerable.Range(1,50).ToArray(),Style=(Style)Application.Current.FindResource(typeof(ComboBox))};combo.ApplyTemplate();
        var popup=(Popup)combo.Template.FindName("PART_Popup",combo);var dropdown=(ScrollViewer)((Border)popup.Child).Child;
        Assert(ReferenceEquals(dropdown.Style,Application.Current.FindResource("RoundedScrollViewer")),"dropdown list uses the same shared scroll viewer");
        foreach(var comboStyle in new[]{(Style)Application.Current.FindResource(typeof(ComboBox)),(Style)Application.Current.FindResource("CalendarMonthInput")}) {
            var labelled=new ComboBox {ItemsSource=new[]{new KeyValuePair<string,string>("off","숨김")},DisplayMemberPath="Value",SelectedValuePath="Key",SelectedIndex=0,Style=comboStyle};Layout(labelled,380,60);
            var shown=Descendants(labelled).OfType<TextBlock>().Select(text=>text.Text).ToArray();
            Assert(shown.Contains("숨김") && !shown.Any(text=>text.Contains("KeyValuePair") || text.StartsWith("[off")),$"closed combo boxes show the DisplayMemberPath value, not the item's ToString ({string.Join("|",shown)})");
        }

        both.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;both.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        both.Content=new Border {Width=900,Height=600,Background=(Brush)Application.Current.FindResource("Input"),Child=new TextBlock {Text=string.Join("\n",Enumerable.Range(1,24).Select(i=>$"항목 {i} · 세로와 가로 스크롤")),Margin=new Thickness(16),FontSize=18}};
        var horizontalOnly=new ScrollViewer {HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,
            Content=new StackPanel {Orientation=Orientation.Horizontal}};
        var cards=(StackPanel)horizontalOnly.Content;
        for(int i=1;i<=6;i++)cards.Children.Add(new Border {Width=180,Height=110,Margin=new Thickness(0,0,12,0),CornerRadius=new CornerRadius(12),Background=(Brush)Application.Current.FindResource("Selected"),Child=new TextBlock {Text=$"일정 {i}",Margin=new Thickness(16)}});
        var gallery=new Grid {Width=1000,Height=680,Background=(Brush)Application.Current.FindResource("Bg")};
        gallery.ColumnDefinitions.Add(new ColumnDefinition());gallery.ColumnDefinitions.Add(new ColumnDefinition());gallery.RowDefinitions.Add(new RowDefinition());gallery.RowDefinitions.Add(new RowDefinition());
        var examples=new (string title,FrameworkElement control)[]{("세로·가로 스크롤",both),("대시보드 가로 목록",horizontalOnly),("여러 줄 메모",note),("기록 목록",list)};
        for(int i=0;i<examples.Length;i++) {
            var card=new DockPanel {Margin=new Thickness(20)};
            var label=new TextBlock {Text=examples[i].title,FontSize=18,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(label,Dock.Top);card.Children.Add(label);card.Children.Add(examples[i].control);
            Grid.SetColumn(card,i%2);Grid.SetRow(card,i/2);gallery.Children.Add(card);
        }
        var galleryHost=new Window {Content=gallery,Left=-30000,Top=-30000,Width=1000,Height=680,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        try {
            galleryHost.Show();Pump(25);Layout(gallery,1000,680);list.SelectedIndex=0;list.ScrollIntoView(list.SelectedItem);Pump(25);gallery.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1000,680,96,96,PixelFormats.Pbgra32);bitmap.Render(gallery);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(qa,"shared-scrollbars.png"));png.Save(output);
        } finally {galleryHost.Content=null;galleryHost.Close();}
    }
}
