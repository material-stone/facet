using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;

internal static partial class Program
{
    private static void CheckOverlaySettings(string qa)
    {
        var hud=new OverlayTabs();var tabs=(TabControl)hud.FindName("Tabs");hud.OpenSettings();
        var view=(OverlaySettingsView)hud.FindName("SettingsView");
        var search=(TextBox)view.FindName("SearchInput");var groups=(StackPanel)view.FindName("Groups");
        var scroll=(ScrollViewer)view.FindName("SettingsScroll");var status=(TextBlock)view.FindName("Status");
        Grid[] Rows()=>groups.Children.OfType<StackPanel>().SelectMany(group=>group.Children.OfType<Grid>()).ToArray();
        TextBlock Label(Grid row)=>(TextBlock)((StackPanel)row.Children[0]).Children[0];
        FrameworkElement Editor(string name)=>Rows().Select(row=>(FrameworkElement)((StackPanel)row.Children[1]).Children[0]).Single(editor=>AutomationProperties.GetName(editor)==name);
        bool Marked(TextBlock text)=>text.Inlines.OfType<Run>().Any(run=>run.Background!=null);
        void Layout(Size size) { hud.Measure(size);hud.Arrange(new Rect(size));hud.UpdateLayout(); }
        void Screenshot(string name,Size size) {
            // A fresh detached tree gives each offscreen bitmap a complete first render.
            var preview=new OverlayTabs();preview.OpenSettings();
            var previewView=(OverlaySettingsView)preview.FindName("SettingsView");((TextBox)previewView.FindName("SearchInput")).Text=search.Text;
            preview.Measure(size);preview.Arrange(new Rect(size));preview.UpdateLayout();
            var image=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);image.Render(preview);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(qa,name));encoder.Save(output);
        }
        Assert(AutomationProperties.GetName((TabItem)tabs.Items[tabs.Items.Count-1])=="설정","settings is the accessible rightmost overlay tab");
        foreach(var size in new[]{new Size(1440,900),new Size(1024,768),new Size(480,360)}) {
            Layout(size);
            var strip=(FrameworkElement)tabs.Template.FindName("TabStrip",tabs);
            var top=search.TranslatePoint(new Point(),hud);var bottom=strip.TranslatePoint(new Point(0,strip.ActualHeight),hud);
            var center=view.TranslatePoint(new Point(view.ActualWidth/2,0),hud);
            Assert(view.ActualWidth<=800 && Math.Abs(center.X-size.Width/2)<1 && top.Y>=bottom.Y+24 && top.Y+search.ActualHeight<size.Height,"settings stays centered within 800 DIP below the tabs at "+size);
            Assert(scroll.ViewportHeight>0 && (size.Height>400 || scroll.ExtentHeight>scroll.ViewportHeight),"settings scrolls inside the available height at "+size);
            var first=Rows()[0];var left=Label(first).TranslatePoint(new Point(),hud);var right=Editor("직접 실행 시 오버레이 표시").TranslatePoint(new Point(),hud);
            Assert(left.X<right.X && Label(first).ToolTip is string description && description.Contains("직접"),"setting name stays left of its editor and exposes a description tooltip");
            Assert(first.ActualHeight<=48,"compact toggle rows use at most 48 DIP without extra gaps at "+size);
        }
        Screenshot("overlay-settings.png",new Size(1440,900));
        Screenshot("overlay-settings-narrow.png",new Size(480,360));
        search.Text="나타나는 시간";
        Assert(Rows().Count(row=>row.Visibility==Visibility.Visible)==1 && Label(Rows().Single(row=>row.Visibility==Visibility.Visible)).Text=="나타나는 시간" && Marked(Label(Rows().Single(row=>row.Visibility==Visibility.Visible))),"live name search filters rows and highlights the keyword");
        Assert(groups.Children.OfType<StackPanel>().Count(group=>group.Visibility==Visibility.Visible)==1,"search hides empty settings groups");
        Screenshot("overlay-settings-search.png",new Size(1440,900));
        search.Text="즉시 표시";
        var match=Rows().Single(row=>row.Visibility==Visibility.Visible);
        Assert(Marked((TextBlock)((StackPanel)match.Children[0]).Children[1]),"description-only match appears and highlights its matching explanation");
        search.Text="  WIN+ALT+O  ";
        match=Rows().Single(row=>row.Visibility==Visibility.Visible);
        Assert(Marked((TextBlock)((StackPanel)match.Children[1]).Children[1]),"current value search ignores case and surrounding whitespace and highlights the matching value");
        search.Text="직접 오버레이";
        Assert(Rows().Count(row=>row.Visibility==Visibility.Visible)==1,"multiple search words match across setting names and descriptions");
        search.Text="[.*+?";
        Assert(Rows().All(row=>row.Visibility==Visibility.Collapsed) && ((TextBlock)view.FindName("EmptyMessage")).Visibility==Visibility.Visible,"search treats regex characters literally and explains empty results");
        search.Text="  ";
        Assert(view.CurrentSection=="일반" && groups.Children.OfType<StackPanel>().Count(group=>group.Visibility==Visibility.Visible)==2,"without search only the chosen side tab's groups show");
        search.Text="플러그인 받기";view.ShowSection("스킨");
        string[] Shown()=>groups.Children.OfType<StackPanel>().Where(group=>group.Visibility==Visibility.Visible).SelectMany(group=>group.Children.OfType<Grid>()).Where(row=>row.Visibility==Visibility.Visible).Select(row=>AutomationProperties.GetName((FrameworkElement)((StackPanel)row.Children[1]).Children[0])).ToArray();
        Assert(search.Text=="" && Shown().Contains("사용할 스킨") && !Shown().Contains("플러그인 받기"),"picking the skin tab clears search and shows only skin rows");
        view.ShowSection("일반");search.Text="  ";
        Assert(groups.Children.OfType<StackPanel>().Where(group=>group.Visibility==Visibility.Visible).SelectMany(group=>group.Children.OfType<Grid>()).All(row=>row.Visibility==Visibility.Visible),"blank search restores every setting of the chosen tab");

        var store=new Store(Path.Combine(qa,"overlay-settings-"+Guid.NewGuid().ToString("N")));
        var data=store.Load();bool fail=false,startup=false;int saves=0;
        view.Configure(()=>data.Settings,next=> { if(fail)return false;data.Settings=next;store.Save(data);saves++;return true; },()=>startup,enabled=>startup=enabled);
        Assert(saves==0 && !view.HasChanges && ((Button)view.FindName("RetryButton")).Visibility==Visibility.Collapsed,"loading settings never saves and normal UI requires no save button");
        var transparency=((StackPanel)Editor("투명도")).Children.OfType<Slider>().Single();
        Assert(transparency.Minimum==0 && transparency.Maximum==100 && Math.Abs(transparency.Value-43)<.001,"transparency uses the full percentage range and preserves legacy appearance");
        var wake=(TextBox)Editor("오버레이 열기·닫기");
        wake.Text="Ctrl+Alt+F1";search.Text="no-match";search.Text="";
        Assert(ReferenceEquals(wake,Editor("오버레이 열기·닫기")) && wake.Text=="Ctrl+Alt+F1" && view.HasChanges,"filtering preserves editor instances and unsaved input");
        wake.Text="invalid shortcut";
        Assert(!view.TrySave() && saves==0 && view.HasChanges && status.Text.Contains("단축키"),"invalid shortcut leaves the settings draft and store unchanged");
        wake.Text="Ctrl+Alt+F1";fail=true;
        Assert(!view.TrySave() && view.HasChanges && wake.Text=="Ctrl+Alt+F1" && saves==0,"failed persistence retains input for retry");
        fail=false;data.Settings.OverlayVisible=false;data.Settings.OverlayWidth=499;
        ((CheckBox)Editor("Windows 로그인 시 자동 실행")).IsChecked=true;
        Assert(!view.HasChanges && startup && saves==1,"toggle change immediately saves pending valid settings and applies startup through the injected writer");
        var reloaded=new Store(store.Root).Load().Settings;
        Assert(reloaded.OverlayHotkey=="Ctrl+Alt+F1" && !reloaded.OverlayVisible && reloaded.OverlayWidth==499,"changed settings survive encrypted reload while unrelated latest values are preserved");
        search.Text="Ctrl+Alt+F1";
        Assert(Rows().Count(row=>row.Visibility==Visibility.Visible)==1,"search uses the newly saved value");
        search.Text="";
        var launchShow=(CheckBox)Editor("직접 실행 시 오버레이 표시");bool showBefore=data.Settings.ShowOverlayOnLaunch;launchShow.IsChecked=!showBefore;
        Assert(data.Settings.ShowOverlayOnLaunch!=showBefore && !view.HasChanges && new Store(store.Root).Load().Settings.ShowOverlayOnLaunch!=showBefore,"checkbox changes apply and persist immediately without a save action");
        var fade=((StackPanel)Editor("나타나는 시간")).Children.OfType<Slider>().Single();fade.Value=400;
        Assert(data.Settings.OverlayShowMilliseconds==400 && !view.HasChanges && ReferenceEquals(fade,((StackPanel)Editor("나타나는 시간")).Children.OfType<Slider>().Single()),"slider changes persist immediately without recreating the dragged control");
        Assert(Rows().All(row=>row.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<TextBlock>()).All(t=>t.Text!="날씨 지역")),"the weather region moved to the plugin section");
        var skins=(SkinsPanel)Editor("사용할 스킨");
        Assert(skins.SelectedSkin=="default","settings show the default skin for existing data");
        var midnight=VisualElements(skins).OfType<RadioButton>().Single(radio=>AutomationProperties.GetName(radio)==Facet.Design.Skins.Options.Single(option=>option.Id=="midnight").Name+" 적용");
        midnight.IsChecked=true;
        Assert(!view.HasChanges && new Store(store.Root).Load().Settings.Skin=="midnight","skin selection autosaves for the next start");
        VisualElements(skins).OfType<RadioButton>().Single(radio=>AutomationProperties.GetName(radio)=="기본 적용").IsChecked=true;
        Assert(Rows().All(row=>AutomationProperties.GetName((FrameworkElement)((StackPanel)row.Children[1]).Children[0]) is not ("검색 엔진" or "탐색기 alias" or "블러 강도" or "화면 공유 모드")),"hidden launcher, blur and reminder settings stay off the screen");
        fail=true;fade.Value=800;
        Assert(view.HasChanges && data.Settings.OverlayShowMilliseconds==400,
            "failed slider autosave keeps the draft and the last saved value");
        fail=false;Assert(view.TrySave() && data.Settings.OverlayShowMilliseconds==800,
            "retry saves the pending slider value");

        int before=saves;wake.Text="1";Pump(450);
        Assert(saves==before && data.Settings.OverlayHotkey=="Ctrl+Alt+F1" && wake.Text=="1" && view.HasChanges,"incomplete shortcut input is retained without overwriting the last valid setting");
        wake.Text="Ctrl+";wake.Text="Ctrl+Alt+";wake.Text="Ctrl+Alt+F2";wake.CaretIndex=2;
        Assert(saves==before && view.HasChanges,"continuous text edits wait for the brief idle interval");
        Pump(450);
        Assert(saves==before+1 && data.Settings.OverlayHotkey=="Ctrl+Alt+F2" && !view.HasChanges && wake.Text=="Ctrl+Alt+F2" && wake.CaretIndex==2 && ReferenceEquals(wake,Editor("오버레이 열기·닫기")),"text changes automatically save once and preserve editor text and caret");
        fail=true;wake.Text="Ctrl+Alt+F3";Pump(450);
        Assert(data.Settings.OverlayHotkey=="Ctrl+Alt+F2" && view.HasChanges && ((Button)view.FindName("RetryButton")).Visibility==Visibility.Visible,"failed autosave retains the draft and offers retry");
        fail=false;wake.Text="Ctrl+Alt+F4";Pump(450);
        Assert(data.Settings.OverlayHotkey=="Ctrl+Alt+F4" && !view.HasChanges && ((Button)view.FindName("RetryButton")).Visibility==Visibility.Collapsed,"correcting input automatically retries and clears the error");
        fail=true;launchShow.IsChecked=showBefore;
        Assert(data.Settings.ShowOverlayOnLaunch!=showBefore && view.HasChanges,"failed immediate autosave keeps the persisted checkbox value");
        fail=false;((Button)view.FindName("RetryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(data.Settings.ShowOverlayOnLaunch==showBefore && !view.HasChanges,"retry action persists a retained draft without changing it");
        before=saves;search.Text="나타나는 시간";search.Text="";Pump(450);
        Assert(saves==before,"searching settings never triggers persistence");
        wake.Text="Ctrl+Alt+F5";
        wake.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,wake,search) { RoutedEvent=Keyboard.LostKeyboardFocusEvent });
        Assert(data.Settings.OverlayHotkey=="Ctrl+Alt+F5" && !view.HasChanges,"leaving a text field flushes its pending save immediately");
        wake.Text="Ctrl+Alt+F6";view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(data.Settings.OverlayHotkey=="Ctrl+Alt+F6" && !view.HasChanges,"unloading the settings tab flushes pending text edits");
        foreach(double percent in new[]{0d,50d,100d}) {
            transparency.Value=percent;
            var appearance=new Store(store.Root).Load().Settings;
            Assert(appearance.OverlayTransparencyPercent==percent && Math.Abs(OverlayAppearance.Strength(appearance)-(1-percent/100))<.001 && !view.HasChanges,"transparency immediately persists its defined background alpha at "+percent+"%");
        }
        var palette=(OverlayColorPalette)Editor("배경색");
        var swatches=(System.Windows.Controls.Primitives.UniformGrid)((Viewbox)((StackPanel)palette.Content).Children[0]).Child;
        var white=swatches.Children.OfType<Button>().Single(button=>AutomationProperties.GetName(button).EndsWith("#FFFFFF"));
        white.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(data.Settings.OverlayBackgroundColor=="#FFFFFF" && new Store(store.Root).Load().Settings.OverlayBackgroundColor=="#FFFFFF" && AutomationProperties.GetItemStatus(white)=="선택됨" && !view.HasChanges,"palette click selects, indicates and immediately persists the background color");
        fail=true;palette.SelectedColor="#3265A8";
        Assert(data.Settings.OverlayBackgroundColor=="#FFFFFF" && palette.SelectedColor=="#3265A8" && view.HasChanges,"failed color autosave retains the selected palette draft");
        fail=false;Assert(view.TrySave() && new Store(store.Root).Load().Settings.OverlayBackgroundColor=="#3265A8","retry persists the selected background color");
        search.Text="#3265a8";
        Assert(Rows().Count(row=>row.Visibility==Visibility.Visible)==1,"settings search finds the selected color without case sensitivity");
        search.Text="";
    }
}
