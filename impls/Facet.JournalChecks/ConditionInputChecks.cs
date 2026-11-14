using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Facet;
using Facet.ConditionPlugin;
using PluginEntry=Facet.ConditionPlugin.ConditionEntry;

internal static partial class Program
{
    private static void CheckConditionInput()
    {
        var cursor=System.Windows.Forms.Cursor.Position;var foreground=Native.ForegroundWindow;
        var history=new ConditionHistory {Width=800,VerticalAlignment=VerticalAlignment.Top};
        var data=new List<PluginEntry>();var now=DateTimeOffset.Now;
        ConditionTracking.Record(data,"good",new DateTimeOffset(DateTime.SpecifyKind(now.Date.AddDays(-5),DateTimeKind.Local)));
        history.Commit=change=>{change(data);return true;};history.Changed+=()=>history.Refresh(data,DateTimeOffset.Now);
        history.Refresh(data,now);
        var area=SystemParameters.WorkArea;
        var host=new Window {Title="Facet · 컨디션 클릭 검사",Width=848,Height=220,Content=new Border {Padding=new(24),Child=history},
            ShowInTaskbar=false,Topmost=true,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.Manual,
            Left=area.Left+(area.Width-848)/2,Top=area.Top+48};
        void Move(UIElement element,Point point) {var target=element.PointToScreen(point);System.Windows.Forms.Cursor.Position=new((int)Math.Round(target.X),(int)Math.Round(target.Y));Pump(60);}
        void Click(UIElement element) {Move(element,new Point(((FrameworkElement)element).ActualWidth/2,((FrameworkElement)element).ActualHeight/2));mouse_event(0x0002,0,0,0,0);Pump(40);mouse_event(0x0004,0,0,0,0);Pump(180);}
        try {
            host.Show();host.Activate();Pump(160);
            Move(history.Bar,new Point(300,8));mouse_event(0x0002,0,0,0,0);Pump(80);
            Assert(Mouse.LeftButton==MouseButtonState.Pressed && !history.RecordPopup.IsOpen,"native condition mouse-down captures the gauge without opening a popup");
            mouse_event(0x0004,0,0,0,0);Pump(250);
            Assert(Mouse.LeftButton==MouseButtonState.Released && history.RecordPopup.IsOpen,"native condition mouse-up opens the record popup and does not immediately dismiss it");
            Pump(500);Assert(history.RecordPopup.IsOpen,"condition popup stays open after the complete press-release input sequence");
            Click(history.Editor.Options[2]);
            Assert(history.RecordPopup.IsOpen && data.Single().Kind=="tired","native condition choice auto saves while leaving the editor open");
            Click(history.Editor.Timestamp);
            Assert(history.RecordPopup.IsOpen && history.Editor.TimePopup.IsOpen,"native unified timestamp click opens the single date-time picker without dismissing its parent");
            history.Editor.TimePicker.Minute.SelectedIndex=(history.Editor.TimePicker.Minute.SelectedIndex+1)%60;Pump(100);
            Assert(history.RecordPopup.IsOpen && history.Editor.TimePopup.IsOpen && data.Single().At.LocalDateTime.Minute==history.Editor.TimePicker.Minute.SelectedIndex,
                "single timestamp choice auto saves and keeps both editing popups open");
            history.Editor.CloseTimePicker();Pump(120);
            Assert(history.RecordPopup.IsOpen,"closing the nested date-time picker preserves its parent editor");
            Move(host,new Point(12,host.ActualHeight-12));mouse_event(0x0002,0,0,0,0);Pump(40);mouse_event(0x0004,0,0,0,0);Pump(180);
            Assert(!history.RecordPopup.IsOpen,"a separate outside click dismisses the condition popup");
            Move(history.Bar,new Point(300,8));mouse_event(0x0002,0,0,0,0);Pump(50);
            Move(host,new Point(12,host.ActualHeight-12));mouse_event(0x0004,0,0,0,0);Pump(120);
            Assert(!history.RecordPopup.IsOpen,"pressing the gauge and releasing outside cancels the click");
        }
        finally {
            mouse_event(0x0004,0,0,0,0);history.RecordPopup.IsOpen=false;history.Editor.CloseTimePicker();host.Close();
            System.Windows.Forms.Cursor.Position=cursor;Native.RestoreForeground(foreground);Pump(80);
        }
    }
}
