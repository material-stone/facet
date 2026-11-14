using System.Threading;
using Facet.Deployment;
using System.Diagnostics;

namespace Facet;
public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? listener;
    private EventWaitHandle? update, cancelUpdate, runningVersion;
    private RegisteredWaitHandle? updateListener;
    private bool ownsMutex;
    private string? offeredRelease;
    public Controller? Controller { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        TaskbarIdentity.Initialize();
        base.OnStartup(e);
        string root = LaunchProtocol.DataRoot(e.Args);
        if (e.Args.Contains("--storage-check"))
        {
            try { StorageCheck.Run(root); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(root, "check-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        string? startupError = null;
        try { SettingsEditing.RepairStartup(e.Args); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { startupError = "로그인 자동 실행 경로를 갱신하지 못했습니다. " + ex.Message; }
        mutex = new Mutex(true, LaunchProtocol.Instance(root), out bool created);
        ownsMutex = created;
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, LaunchProtocol.Open(root));
        if (!created) { if (!LaunchProtocol.IsBackgroundStart(e.Args)) activate.Set(); Shutdown(); return; }
        try
        {
            Controller = new Controller(root);
            MainWindow = Controller.Overlay;
            listener = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (Controller.Exiting || Controller.ExitPending || !Controller.Overlay.IsEnabled) return;
                if (!CheckForNewRelease(e.Args)) Controller.HandleLaunch(mayOpen: true);
            }), null, -1, false);
            Controller.Start(!LaunchProtocol.IsBackgroundStart(e.Args));
            if (startupError is not null) Controller.ReportError(startupError);
            cancelUpdate = new EventWaitHandle(false, EventResetMode.AutoReset, LaunchProtocol.Cancel(root));
            update = new EventWaitHandle(false, EventResetMode.AutoReset, LaunchProtocol.Update(root));
            runningVersion = new EventWaitHandle(false, EventResetMode.ManualReset, LaunchProtocol.Version(root, AppContext.BaseDirectory));
            updateListener = ThreadPool.RegisterWaitForSingleObject(update, (_, _) => Dispatcher.BeginInvoke(() =>
            {
                Controller.Exit();
                if (!Controller.Exiting) cancelUpdate.Set();
            }), null, -1, false);
            Controller.Overlay.Activated += (_, _) => CheckForNewRelease(e.Args);
            if (e.Args.Contains("--diagnostics"))
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { timer.Stop(); Diagnostics.Capture(Controller); };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"{LaunchProtocol.DisplayName}을 시작하지 못했습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private bool CheckForNewRelease(string[] args)
    {
        if (Controller is null || Controller.Exiting || Controller.ExitPending || !Controller.Overlay.IsEnabled) return false;
        if (LaunchProtocol.FindLauncher() is not { } launcher) return false;
        try
        {
            string target = ReleaseManifest.Resolve(Path.GetDirectoryName(launcher)!);
            string current = Path.Combine(AppContext.BaseDirectory, "Facet.exe");
            if (target.Equals(current, StringComparison.OrdinalIgnoreCase) || target.Equals(offeredRelease, StringComparison.OrdinalIgnoreCase)) return false;
            offeredRelease = target;
            var info = new ProcessStartInfo(launcher) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in args.Where(arg => arg is not ("--tray" or "--autostart"))) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Controller.ReportError("새 버전을 실행하지 못했습니다. " + ex.Message);
        }
        return false;
    }
    protected override void OnExit(ExitEventArgs e)
    {
        listener?.Unregister(null); updateListener?.Unregister(null); Controller?.Dispose();
        runningVersion?.Dispose(); update?.Dispose(); cancelUpdate?.Dispose(); activate?.Dispose();
        if (ownsMutex) mutex?.ReleaseMutex();
        mutex?.Dispose(); base.OnExit(e);
    }
}
