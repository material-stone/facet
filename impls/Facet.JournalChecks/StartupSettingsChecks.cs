using System.IO;
using System.Windows.Controls;
using Facet;
using Facet.Core;
using Facet.Deployment;
using Microsoft.Win32;

internal static partial class Program
{
    private static void CheckStartupRegistration()
    {
        Assert(typeof(Store).Assembly.GetName().Name == "Facet" && typeof(AppData).Assembly.GetName().Name == "Facet.Core"
            && LaunchProtocol.DisplayName == "Facet" && LaunchProtocol.LauncherName == "Facet.Launcher.exe",
            "renamed app, core and fixed launcher use Facet identities");
        string local = Path.Combine(Path.GetTempPath(), "facet-root-" + Guid.NewGuid().ToString("N")), oldRoot = Path.Combine(local, "MaterialStone", "DailyDock"), newRoot = Path.Combine(local, "MaterialStone", "Facet");
        Assert(LaunchProtocol.DefaultRoot(local) == newRoot, "a fresh PC keeps records in MaterialStone/Facet");
        Directory.CreateDirectory(oldRoot); File.WriteAllText(Path.Combine(oldRoot, "records.dat"), "x");
        Assert(LaunchProtocol.DefaultRoot(local) == oldRoot, "records from before the rename stay in use until moved");
        using (new Mutex(true, LaunchProtocol.Instance(oldRoot))) Assert(!LaunchProtocol.MoveRenamedRoot(local) && Directory.Exists(oldRoot), "a running app keeps its folder");
        Assert(LaunchProtocol.MoveRenamedRoot(local) && File.Exists(Path.Combine(newRoot, "records.dat")) && !Directory.Exists(oldRoot) && LaunchProtocol.DefaultRoot(local) == newRoot, "the old folder moves once to MaterialStone/Facet");
        Directory.CreateDirectory(oldRoot);
        Assert(!LaunchProtocol.MoveRenamedRoot(local) && LaunchProtocol.DefaultRoot(local) == newRoot, "an existing Facet folder wins over a stray old one");
        Directory.Delete(local, true);
        Assert(LaunchProtocol.AppId == "MaterialStone.DailyDock"
            && LaunchProtocol.LauncherEnvironment == "DAILYDOCK_LAUNCHER",
            "Facet continues the existing data store and persistent Windows identities");
        const string value = "MaterialStoneDailyDock";
        string testPath = @"Software\MaterialStone\Facet.Checks\" + Guid.NewGuid().ToString("N");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(testPath);
            string launcher = @"C:\Program Files\Facet\Facet.Launcher.exe";
            string command = LaunchProtocol.AutostartCommand(launcher);
            key.SetValue("UnrelatedStartup", "keep");
            SettingsEditing.RepairStartup(key, launcher);
            Assert(key.GetValue(value) is null, "startup repair preserves disabled autostart");
            key.SetValue(value, "\"C:\\old\\DailyDock-1.2-dark\\DailyDock.exe\" --tray");
            SettingsEditing.RepairStartup(key, launcher);
            Assert((string?)key.GetValue(value) == command, "startup repair replaces legacy executable with the fixed launcher and background flags");
            SettingsEditing.RepairStartup(key, launcher);
            Assert((string?)key.GetValue(value) == command && (string?)key.GetValue("UnrelatedStartup") == "keep", "repeated startup repair preserves canonical command and unrelated registrations");
            key.SetValue(value, "\"" + launcher + "\" --tray");
            SettingsEditing.SetStartup(key, true, launcher);
            Assert((string?)key.GetValue(value) == command, "explicit enable corrects an already enabled command instead of returning early");
            const string legacy = "\"C:\\old\\DailyDock.exe\" --tray";
            key.SetValue(value, legacy);
            SettingsEditing.RepairStartup(key, null);
            Assert((string?)key.GetValue(value) == legacy, "missing fixed launcher leaves existing registration unchanged during repair");
            bool rejected = false;
            try { SettingsEditing.SetStartup(key, true, null); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && (string?)key.GetValue(value) == legacy, "explicit enable rejects a missing launcher without registering a version executable");
            SettingsEditing.SetStartup(key, false, null);
            SettingsEditing.RepairStartup(key, launcher);
            Assert(key.GetValue(value) is null && (string?)key.GetValue("UnrelatedStartup") == "keep", "disable removes only app registration and repair does not enable it again");
            SettingsEditing.SetStartup(key, true, launcher);
            Assert((string?)key.GetValue(value) == command && key.GetValueKind(value) == RegistryValueKind.String, "fresh enable registers the quoted stable command");
            SettingsEditing.RepairStartup(null, launcher);
            SettingsEditing.RepairStartup(["--data-dir", "synthetic"]);
            SettingsEditing.RepairStartup(["--storage-check"]);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testPath, throwOnMissingSubKey: false); }
    }

    private static void CheckStartupSettings(Controller controller)
    {
        var store=new Store(Path.Combine(controller.Store.Root,"startup-settings"));
        var data=store.Load();bool startup=false;
        var view=new OverlaySettingsView();
        view.Configure(()=>data.Settings,next=>{data.Settings=next;store.Save(data);return true;},()=>startup,value=>startup=value);
        SettingEditor<CheckBox>(view,"직접 실행 시 오버레이 표시").IsChecked=false;
        Assert(!new Store(store.Root).Load().Settings.ShowOverlayOnLaunch,"overlay startup toggle persists without a manager");
        SettingEditor<CheckBox>(view,"Windows 로그인 시 자동 실행").IsChecked=true;
        Assert(startup && !data.Settings.ShowOverlayOnLaunch,"login startup is independent from direct launch visibility");
        string command=LaunchProtocol.AutostartCommand(@"C:\Program Files\Facet\Facet.Launcher.exe");
        Assert(command=="\"C:\\Program Files\\Facet\\Facet.Launcher.exe\" --autostart --tray" && LaunchProtocol.IsBackgroundStart(["--autostart"]) && LaunchProtocol.IsBackgroundStart(["--tray"]) && !LaunchProtocol.IsBackgroundStart([]),"login command quotes fixed launcher and stays quiet");
    }
}
