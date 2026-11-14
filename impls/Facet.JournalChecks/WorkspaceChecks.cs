using System.IO;
using System.Windows;
using System.Windows.Controls;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckWorkspace(Controller controller)
    {
        using var runtime=new Controller(Path.Combine(controller.Store.Root,"workspace-ui"),Controller.ExitHotkey);
        var hud=new OverlayTabs();hud.Connect(runtime);
        var tabs=(TabControl)hud.FindName("Tabs");
        void Layout() { hud.Measure(new Size(1440,900));hud.Arrange(new Rect(0,0,1440,900));hud.UpdateLayout(); }
        Layout();
        CheckCalendarStore(controller);
        Assert(runtime.Data.Workspace==null && tabs.Items.Count==5,"default layout is not written until the user edits it");

        var unmeasured=new OverlayCarousel();unmeasured.Children.Add(new Grid());
        unmeasured.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(unmeasured.Children[0].RenderTransform is System.Windows.Media.TransformGroup,"a carousel unloaded before its first measure does not crash");

        // Default tiles stay inside the viewport and do not overlap.
        var viewport=(FrameworkElement)hud.FindName("DashboardViewport");
        Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(hud).TransformBounds(new Rect(element.RenderSize));
        var view=(OverlayDashboardView)hud.FindName("Dashboard");
        foreach(string name in new[]{"ClockGroup","WeatherHoverArea","AgendaSection"}) {
            var bounds=Bounds((FrameworkElement)view.FindName(name));
            Assert(bounds.Top>=Bounds(viewport).Top-.5 && bounds.Bottom<=Bounds(viewport).Bottom+.5,"default widget stays in the viewport: "+name+" "+bounds);
        }

        // A section moved into a tile still resolves the storyboard names declared in its XAML.
        var weatherArea=(FrameworkElement)view.FindName("WeatherHoverArea");
        weatherArea.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.Input.Mouse.MouseEnterEvent});
        weatherArea.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0){RoutedEvent=System.Windows.Input.Mouse.MouseLeaveEvent});

        // Edit mode: moves commit through the store, invalid drops and Escape keep the old place.
        hud.SetWorkspaceEditing(true);Layout();
        Assert(hud.IsEditingWorkspace,"edit mode opens");
        Assert(hud.AddWidget(WorkspaceLayouts.ClockWidget)==false && hud.WorkspaceError.Length>0 && WorkspaceLayouts.Current(runtime.Data).Tabs[0].Widgets.Count(w=>w.WidgetId==WorkspaceLayouts.ClockWidget)==1,"a single built-in widget cannot be placed twice");
        Assert(hud.AddWorkspaceTab("업무") && tabs.Items.Count==6 && new Store(runtime.Store.Root).Load().Workspace!.Tabs.Any(t=>t.Title=="업무"),"new tab is saved and shown");
        string added=runtime.Data.Workspace!.Tabs.First(t=>t.Title=="업무").Id;
        Assert(AutomationName(tabs.Items[tabs.Items.Count-1])=="설정" && AutomationName(tabs.Items[tabs.Items.Count-2])=="업무","new tab stays before settings");
        Assert(hud.RenameWorkspaceTab(added,"작업") && AutomationName(tabs.Items[tabs.Items.Count-2])=="작업","renaming updates the tab header");
        Assert(hud.MoveWorkspaceTab(added,-1) && AutomationName(tabs.Items[3])=="작업","tab order follows the layout");
        Assert(hud.SetWorkspaceTabHidden(WorkspaceLayouts.SleepTab,true) && ((TabItem)hud.FindName("SleepTab")).Visibility==Visibility.Collapsed,"built-in tabs can be hidden");
        tabs.SelectedIndex=0;hud.MoveTab(1);
        Assert(ReferenceEquals(tabs.SelectedItem,hud.FindName("ManagementTab")),"next tab goes to the following visible tab");
        hud.MoveTab(1);Assert(AutomationName(tabs.SelectedItem)=="작업","tab cycling skips hidden tabs");
        Assert(hud.SetWorkspaceTabHidden(WorkspaceLayouts.SleepTab,false),"hidden tabs can be restored");
        Assert(!hud.SetWorkspaceTabHidden(WorkspaceLayouts.SettingsTab,true) && ((TabItem)hud.FindName("SettingsTab")).Visibility==Visibility.Visible,"settings tab cannot be hidden");
        Assert(hud.HandleEscape() && !hud.IsEditingWorkspace,"Escape leaves edit mode");

        // Widget modal: the widget owns the view, the app presents it and decides when it may close.
        var modalLayer=(Grid)hud.FindName("WidgetModal");var modalContent=new TextBlock{Text="내용"};int modalClosed=0;bool allowClose=false,useEscape=true;
        var widgetModal=hud.WidgetModalFor("modal-owner");
        var modalHandle=widgetModal.Show(modalContent,new(){CanClose=()=>allowClose,HandleEscape=()=>useEscape})!;modalHandle.Closed+=()=>modalClosed++;
        Assert(modalHandle.IsOpen && modalLayer.Visibility==Visibility.Visible && hud.WidgetModalFor("other").Show(new TextBlock())==null,"one widget modal shows at a time");
        Assert(hud.WidgetModalFor("other").Show(modalContent)==null,"a view with a parent is refused");
        var modalTab=tabs.SelectedItem;hud.MoveTab(1);Assert(ReferenceEquals(tabs.SelectedItem,modalTab),"tab keys stay put under a modal");
        Assert(hud.HandleEscape() && modalHandle.IsOpen,"the content can use Escape first");
        useEscape=false;Assert(hud.HandleEscape() && modalHandle.IsOpen && modalClosed==0,"CanClose can keep the modal open");
        allowClose=true;hud.HandleEscape();
        Assert(!modalHandle.IsOpen && modalClosed==1 && modalLayer.Visibility==Visibility.Collapsed && modalContent.Parent==null,"closing raises Closed once and releases the view");
        modalHandle=hud.WidgetModalFor("throws").Show(modalContent,new(){CanClose=()=>throw new InvalidOperationException()})!;
        hud.HandleEscape();Assert(!modalHandle.IsOpen,"a throwing callback cannot trap the user");
        modalHandle=widgetModal.Show(modalContent)!;modalHandle.Close();Assert(!modalHandle.IsOpen && modalLayer.Visibility==Visibility.Collapsed,"the widget can close its modal");


        // Save failure keeps the previous layout.
        var before=WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Title;
        using(var locked=File.Open(runtime.Store.PathName+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            Assert(!hud.RenameWorkspaceTab(added,"실패") && WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Title==before && hud.WorkspaceError.Length>0,"failed save keeps the previous layout and reports it");

        // Plugins: nothing runs until enabled; a changed file blocks loading until re-enabled.
        string folder=Path.Combine(runtime.Store.Root,"plugins","sample");Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory,"Facet.SamplePlugin.dll"),Path.Combine(folder,"Facet.SamplePlugin.dll"),true);
        File.WriteAllText(Path.Combine(folder,"plugin.json"),"{\"id\":\"sample\",\"name\":\"예제\",\"version\":\"1.0\",\"assembly\":\"Facet.SamplePlugin.dll\"}");
        Directory.CreateDirectory(Path.Combine(runtime.Store.Root,"plugins","escape"));
        File.WriteAllText(Path.Combine(runtime.Store.Root,"plugins","escape","plugin.json"),"{\"id\":\"escape\",\"assembly\":\"..\\\\..\\\\evil.dll\"}");
        var plugin=hud.DiscoverPlugins().Single();
        Assert(plugin.Id=="sample" && !hud.IsPluginEnabled("sample"),"discovery reads manifests, skips paths outside the folder and enables nothing");
        const string widget="plugin.sample.greeting";
        tabs.SelectedItem=tabs.Items.OfType<TabItem>().First(t=>AutomationName(t)=="작업");
        Assert(!hud.AddWidget(widget),"a disabled plugin offers no widgets");
        Assert(hud.SetPluginEnabled(plugin,true) && hud.IsPluginEnabled("sample") && runtime.Data.EnabledPlugins!.Single().Fingerprint.Length==64,"enabling stores the file fingerprint");
        Assert(hud.AddWidget(widget) && hud.AddWidget(widget),"plugin widgets can be placed repeatedly");
        Layout();
        var items=WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Widgets;
        Assert(items.Count==2 && items[0].X==-6 && items[1].X==-3 && items[0].WidgetId==widget,"plugin widgets take free cells");
        void Capture(string name) {
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1440,900,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(hud);
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file=File.Create(Path.Combine(AppContext.BaseDirectory,name));png.Save(file);
        }
        hud.SetWorkspaceEditing(true);Layout();
        var editor=hud.SelectedWorkspace!;
        void Drag(string id,double x,double y){editor.BeginDragForCheck(id);editor.UpdateDrag(new Point(x*OverlayWorkspaceView.Pitch,y*OverlayWorkspaceView.Pitch));}
        Drag(items[1].Id,1,1);editor.FinishDrag();
        var moved=WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Widgets[1];
        Assert(moved.X==-2 && moved.Y==-3,"a drop snaps to cells and saves");
        Assert(new Store(runtime.Store.Root).Load().Workspace!.Tabs.Single(t=>t.Id==added).Widgets[1].Y==-3,"a drop survives reload");
        items=WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Widgets;
        Drag(items[1].Id,-4,-1);editor.FinishDrag();
        moved=WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Widgets[1];
        Assert(moved.X==-2 && moved.Y==-3,"a drop on another widget keeps the old place");
        Drag(items[1].Id,-1,0);Assert(editor.IsDragging && hud.HandleEscape() && !editor.IsDragging && hud.IsEditingWorkspace,"Escape cancels a drag and stays in edit mode");
        Assert(WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Widgets[1].X==-2,"a cancelled drag saves nothing");

        // The board fills the window, ends three screens across, and shows its zoom.
        Assert(editor.ActualWidth==1440 && editor.ActualHeight==900,"the board uses the whole window");
        Assert(editor.HalfColumns==(int)(1440*1.5/OverlayWorkspaceView.Pitch) && editor.HalfRows==(int)(900*1.5/OverlayWorkspaceView.Pitch),"the board reaches one and a half screens from the origin");
        Drag(items[1].Id,9999,9999);editor.CancelDrag();
        editor.PanTo(1e6,-1e6);Assert(editor.ViewX==editor.HalfColumns*OverlayWorkspaceView.Pitch*editor.Zoom-720 && editor.ViewY==-(editor.HalfRows*OverlayWorkspaceView.Pitch*editor.Zoom-450),"panning stops at the board's edge");
        editor.ReturnToOrigin();
        var zoomBefore=editor.Zoom;editor.ZoomAt(new Point(720,450),.0001);
        Assert(editor.Zoom==WorkspaceLayouts.MinZoom && ((TextBlock)hud.FindName("WorkspaceZoom")).Text=="33%","zoom stops where the whole board shows and is displayed");
        Assert(zoomBefore==1,"an unsaved board starts at 100%");
        editor.ZoomAt(new Point(720,450),9);Assert(editor.Zoom==1,"zooming in from below stops at 100%");
        editor.ZoomAt(new Point(720,450),9);Assert(editor.Zoom==WorkspaceLayouts.MaxZoom,"the next step leaves 100%");
        editor.ZoomAt(new Point(720,450),.0001);Assert(editor.Zoom==1,"zooming out from above stops at 100%");
        var coveredItem=WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Widgets[0];
        Assert(editor.IsCellCovered(coveredItem.X,coveredItem.Y) && !editor.IsCellCovered(coveredItem.X,coveredItem.Y+1),"cells under a widget draw no grid lines");

        // The view: moves only while editing, saves on leaving edit mode and returns on the next launch.
        var homeTools=(FrameworkElement)hud.FindName("WorkspaceTools");
        Assert(homeTools.Visibility==Visibility.Visible && hud.FindName("WorkspaceHome")!=null,"the origin button shows while editing");
        editor.PanTo(120,-40);editor.ZoomAt(new Point(editor.ActualWidth/2+120,editor.ActualHeight/2-40),2);
        Assert(editor.ViewX==120 && editor.ViewY==-40,"zooming at the origin keeps the origin under the pointer");
        double zoomed=editor.Zoom;Assert(zoomed>1,"wheel zoom enlarges the board");
        Assert(new Store(runtime.Store.Root).Load().Workspace!.Tabs.Single(t=>t.Id==added).View==null,"the view is not saved during editing");
        hud.SetWorkspaceEditing(false);Layout();
        Assert(homeTools.Visibility==Visibility.Collapsed,"the origin button hides outside edit mode");
        var savedView=new Store(runtime.Store.Root).Load().Workspace!.Tabs.Single(t=>t.Id==added).View;
        Assert(savedView is {X:120,Y:-40} && savedView.Zoom==zoomed && !editor.ViewDirty,"leaving edit mode saves the vector and zoom");
        var restarted=new OverlayTabs();restarted.Connect(runtime);
        var restartedTabs=(TabControl)restarted.FindName("Tabs");restartedTabs.SelectedItem=restartedTabs.Items.OfType<TabItem>().First(t=>AutomationName(t)=="작업");
        var restored=restarted.SelectedWorkspace!;
        Assert(restored.ViewX==120 && restored.ViewY==-40 && restored.Zoom==zoomed,"the next launch opens the saved view");
        restarted.StopWidgets();
        hud.SetWorkspaceEditing(true);Layout();
        editor.ReturnToOrigin();Assert(editor.ViewX==0 && editor.ViewY==0 && editor.Zoom==zoomed,"the origin button resets the vector and keeps the zoom");
        foreach(var page in new[]{"ManagementTab","SleepTab","ChatTab"}) {
            tabs.SelectedItem=hud.FindName(page);Layout();
            Assert(hud.SelectedWorkspace!=null && ((FrameworkElement)hud.FindName("WorkspaceBar")).Visibility==Visibility.Visible,"widgets can be edited on "+page);
        }
        Assert(hud.AddWidget(widget),"a widget can be placed on a fixed page");
        var pageTab=WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==WorkspaceLayouts.ChatTab);
        Assert(pageTab.Widgets.Count==1,"the page board keeps its widget");
        hud.SetWorkspaceEditing(false);Layout();
        Assert(hud.SelectedWorkspace!.Background==null,"a fixed page keeps its pointer input outside edit mode");
        hud.SetWorkspaceEditing(true);Layout();
        hud.RemoveWorkspaceWidgetForCheck(WorkspaceLayouts.ChatTab,pageTab.Widgets[0].Id);
        tabs.SelectedItem=hud.FindName("SettingsTab");Layout();
        Assert(hud.SelectedWorkspace==null && ((FrameworkElement)hud.FindName("WorkspaceBar")).Visibility==Visibility.Collapsed,"settings has no board");
        tabs.SelectedItem=tabs.Items.OfType<TabItem>().First(t=>AutomationName(t)=="작업");Layout();
        Layout();Capture("workspace-edit.png");
        tabs.SelectedIndex=0;Layout();Capture("workspace-dashboard-edit.png");
        hud.SetWorkspaceEditing(false);Layout();Capture("workspace-dashboard.png");
        tabs.SelectedItem=tabs.Items.OfType<TabItem>().First(t=>AutomationName(t)=="작업");Layout();
        Assert(!hud.SetWorkspaceTabHidden("없는 탭",true) && hud.WorkspaceError.Length>0,"unknown tab is rejected");
        Assert(hud.SetPluginEnabled(plugin,false) && items.Count==2 && WorkspaceLayouts.Current(runtime.Data).Tabs.Single(t=>t.Id==added).Widgets.Count==2 && !hud.AddWidget(widget),"disabling keeps placements as placeholders");
        var pluginPanel=hud.SettingsView.PluginPanel;
        Grid PluginRow()=>pluginPanel.Children.OfType<Grid>().First();
        Assert(PluginRow().Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(b=>b is not CheckBox).IsEnabled==false && PluginRow().Children.OfType<Button>().First().IsEnabled==false && PluginRow().Children.OfType<Button>().Last() is { IsEnabled:true,Content:Facet.Design.Icon { Kind:Facet.Design.IconKind.Trash } },"a disabled plugin cannot reload or fold but can be deleted");
        hud.SetPluginEnabled(plugin,true);
        Assert(PluginRow().Children.OfType<Button>().First() is { IsEnabled:true,Content:Facet.Design.Icon { Kind:Facet.Design.IconKind.Refresh } },"an enabled plugin reloads from a refresh icon");
        Assert(!PluginRow().Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(b=>b is not CheckBox).IsEnabled,"a plugin without settings cannot fold");
        var staleGrant=runtime.Data.EnabledPlugins!.Single(g=>g.Id=="sample").Fingerprint;
        File.AppendAllText(Path.Combine(folder,"Facet.SamplePlugin.dll"),"x");
        var reloaded=new OverlayTabs();reloaded.Connect(runtime);
        Assert(reloaded.IsPluginEnabled("sample") && reloaded.HasPluginGrant("sample") && reloaded.PluginState("sample").Length==0 && runtime.Data.EnabledPlugins!.Single(g=>g.Id=="sample").Fingerprint!=staleGrant,"an enabled plugin stays enabled and loads its changed files at launch");
        Assert(!reloaded.ReloadPlugin("없는 플러그인") && !reloaded.HasPluginGrant("없는 플러그인"),"only an enabled plugin can be reloaded");
        Assert(reloaded.ReloadPlugin("sample")&& reloaded.IsPluginEnabled("sample") && reloaded.PluginState("sample").Length==0,"reloading loads the changed files without a restart");
        Assert(runtime.Data.EnabledPlugins!.Single(g=>g.Id=="sample").Fingerprint!=staleGrant && new Store(runtime.Store.Root).Load().EnabledPlugins!.Single(g=>g.Id=="sample").Fingerprint!=staleGrant,"reloading approves and saves the new fingerprint");
        Assert(reloaded.ReloadPlugin("sample"),"reloading again keeps working");
        var brokenManifest=Path.Combine(folder,"Facet.SamplePlugin.dll");var goodBytes=File.ReadAllBytes(brokenManifest);File.WriteAllBytes(brokenManifest,[1,2,3]);
        var kept=runtime.Data.EnabledPlugins!.Single(g=>g.Id=="sample").Fingerprint;
        Assert(!reloaded.ReloadPlugin("sample") && reloaded.PluginState("sample").Length>0 && runtime.Data.EnabledPlugins!.Single(g=>g.Id=="sample").Fingerprint==kept,"a failed reload reports it and keeps the old approval");
        var brokenStart=new OverlayTabs();brokenStart.Connect(runtime);
        Assert(!brokenStart.IsPluginEnabled("sample") && brokenStart.HasPluginGrant("sample") && brokenStart.PluginState("sample").Length>0 && runtime.Data.EnabledPlugins!.Single(g=>g.Id=="sample").Fingerprint==kept,"a broken plugin at launch reports it and stays remembered as enabled");
        File.WriteAllBytes(brokenManifest,goodBytes);Assert(reloaded.ReloadPlugin("sample") && reloaded.PluginState("sample").Length==0,"a reload recovers after the files are fixed");
        Assert(hud.RemoveWorkspaceTab(added) && tabs.Items.Count==5,"added tabs can be deleted");
        Assert(!hud.RemoveWorkspaceTab(WorkspaceLayouts.SettingsTab) && tabs.Items.Count==5,"the settings tab cannot be deleted");
        var tabRows=((OverlaySettingsView)hud.FindName("SettingsView")).WorkspacePanel.Children.OfType<Grid>().ToArray();
        Assert(tabRows.Length==4 && tabRows.All(row=>System.Windows.Automation.AutomationProperties.GetName(row.Children.OfType<Button>().Last()).EndsWith("삭제") && !System.Windows.Automation.AutomationProperties.GetName(row.Children.OfType<Button>().Last()).StartsWith("설정")),"the tab list leaves out settings and every row can be deleted");
        Assert(tabRows.All(row=>row.Children.OfType<TextBox>().Single() is var input && input.Margin==new Thickness(0) && input.Height-input.Padding.Top-input.Padding.Bottom-input.BorderThickness.Top-input.BorderThickness.Bottom>=input.FontSize*1.2),"tab name inputs line up with their row and have room for the text");
        Assert(tabRows.All(row=>row.Children.OfType<Button>().Last() is {ContentTemplate:null,Content:Facet.Design.Icon{Kind:Facet.Design.IconKind.Trash}}),"tab rows delete with a trash icon");
        Assert(tabRows.All(row=>row.Children.OfType<TextBox>().Single() is var input && row.Children.OfType<Button>().First() is {HorizontalAlignment:HorizontalAlignment.Left,Content:Facet.Design.Icon} iconButton && iconButton.Margin.Left+iconButton.Width<=input.Padding.Left && Grid.GetColumn(iconButton)==0),"the icon button sits inside the input, before the text");
        Assert(TabIcons.Presets.Length==24 && TabIcons.Presets.Select(p=>p.Kind).Distinct().Count()==24,"24 distinct tab icon presets");
        foreach(var (kind,_) in TabIcons.Presets){var presetIcon=new Facet.Design.Icon{Kind=kind,Width=24,Height=24};presetIcon.Measure(new Size(24,24));presetIcon.Arrange(new Rect(0,0,24,24));new System.Windows.Media.Imaging.RenderTargetBitmap(24,24,96,96,System.Windows.Media.PixelFormats.Pbgra32).Render(presetIcon);}
        var firstTabs=new OverlayTabs();var firstStrip=(TabControl)firstTabs.FindName("Tabs");
        Assert(firstTabs.SetWorkspaceTabIcon(WorkspaceLayouts.ChatTab,Facet.Design.IconKind.Music) && ((StackPanel)((TabItem)firstStrip.Items[3]).Header).Children[0] is Facet.Design.Icon{Kind:Facet.Design.IconKind.Music},"choosing an icon updates the tab header");
        Assert(firstTabs.RenameWorkspaceTab(WorkspaceLayouts.ChatTab,"대화") && AutomationName(firstStrip.Items[3])=="대화","a first tab can be renamed");
        Assert(firstTabs.RemoveWorkspaceTab(WorkspaceLayouts.SleepTab) && firstStrip.Items.Count==4 && !firstStrip.Items.Contains(firstTabs.FindName("SleepTab")),"a first tab can be deleted");
        var seeded=new AppData();seeded.Settings.WeatherRegionId="kr-1";PluginData.Normalize(seeded);
        Assert(PluginData.Setting(seeded,"facet.weather","region")=="kr-1","the native region carries over once");
        seeded.Settings.WeatherRegionId="kr-2";PluginData.Normalize(seeded);
        Assert(PluginData.WeatherRegion(seeded)=="kr-1","the plugin setting wins after moving");
        // Uninstalling: the folder and placements go; records and settings only when asked.
        string doomed=Path.Combine(runtime.Store.Root,"plugins","doomed"),doomedData=Path.Combine(runtime.Store.Root,"plugin-data");Directory.CreateDirectory(doomed);
        File.Copy(Path.Combine(AppContext.BaseDirectory,"Facet.SamplePlugin.dll"),Path.Combine(doomed,"Facet.SamplePlugin.dll"),true);
        File.WriteAllText(Path.Combine(doomed,"plugin.json"),"{\"id\":\"doomed\",\"name\":\"삭제 예제\",\"version\":\"1.0\",\"assembly\":\"Facet.SamplePlugin.dll\"}");
        var doomedPlugin=hud.DiscoverPlugins().Single(p=>p.Id=="doomed");
        Assert(hud.SetPluginEnabled(doomedPlugin,true),"a plugin to uninstall runs: "+hud.PluginState("doomed")+hud.WorkspaceError);tabs.SelectedItem=hud.FindName("DashboardTab");Assert(hud.AddWidget("plugin.doomed.greeting"),"a plugin to uninstall is placed: "+hud.WorkspaceError);Assert(runtime.Commit(d=>PluginData.SetSetting(d,"doomed","k","v")),"a plugin setting saves");
        Directory.CreateDirectory(doomedData);File.WriteAllText(Path.Combine(doomedData,"doomed.dat"),"x");
        string uninstalled=hud.UninstallPlugin(doomedPlugin,false);
        Assert(uninstalled.StartsWith("삭제했습니다") && !Directory.Exists(doomed) && hud.DiscoverPlugins().All(p=>p.Id!="doomed") && !hud.HasPluginGrant("doomed")
            && WorkspaceLayouts.Current(runtime.Data).Tabs.SelectMany(t=>t.Widgets).All(w=>!w.WidgetId.StartsWith("plugin.doomed.")),"uninstalling removes the folder, the approval and the placements: "+uninstalled);
        Assert(File.Exists(Path.Combine(doomedData,"doomed.dat")) && PluginData.Setting(runtime.Data,"doomed","k")=="v","a plain uninstall keeps the plugin's records and settings");
        Directory.CreateDirectory(doomed);File.Copy(Path.Combine(AppContext.BaseDirectory,"Facet.SamplePlugin.dll"),Path.Combine(doomed,"Facet.SamplePlugin.dll"),true);
        File.WriteAllText(Path.Combine(doomed,"plugin.json"),"{\"id\":\"doomed\",\"name\":\"삭제 예제\",\"version\":\"1.0\",\"assembly\":\"Facet.SamplePlugin.dll\"}");
        Assert(hud.UninstallPlugin(hud.DiscoverPlugins().Single(p=>p.Id=="doomed"),true).StartsWith("삭제했습니다") && !File.Exists(Path.Combine(doomedData,"doomed.dat")) && PluginData.Setting(runtime.Data,"doomed","k")==null,"uninstalling with data removes the records and settings too");
        string inUse=Path.Combine(runtime.Store.Root,"plugins","locked");Directory.CreateDirectory(inUse);
        File.WriteAllText(Path.Combine(inUse,"plugin.json"),"{\"id\":\"locked\",\"assembly\":\"L.dll\"}");File.WriteAllText(Path.Combine(inUse,"L.dll"),"x");
        using(new FileStream(Path.Combine(inUse,"L.dll"),FileMode.Open,FileAccess.Read,FileShare.Read)) Assert(!PluginHost.RemoveFolder(inUse) && hud.DiscoverPlugins().All(p=>p.Id!="locked"),"a plugin with a file in use is hidden and marked");
        Assert(hud.DiscoverPlugins().All(p=>p.Id!="locked") && !Directory.Exists(inUse),"a marked plugin is deleted once nothing holds it");
        hud.StopWidgets();reloaded.StopWidgets();
    }

    private static string? AutomationName(object? item)=>System.Windows.Automation.AutomationProperties.GetName((TabItem)item!);
}
