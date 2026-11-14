using System.Diagnostics;
using System.Runtime.InteropServices;
using Facet.Deployment;

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(LaunchProtocol.AppId));
            string target = ReleaseManifest.Resolve(AppContext.BaseDirectory);
            string root = LaunchProtocol.DataRoot(args);
            using var gate = new Mutex(false, LaunchProtocol.Instance(root) + "-launcher");
            bool acquired;
            try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return 0;
            try
            {
                if (EventWaitHandle.TryOpenExisting(LaunchProtocol.Version(root, Path.GetDirectoryName(target)!), out var running))
                {
                    using (running)
                    {
                        if (!LaunchProtocol.IsBackgroundStart(args))
                            using (var open = EventWaitHandle.OpenExisting(LaunchProtocol.Open(root)))
                            { AllowSetForegroundWindow(uint.MaxValue); open.Set(); }
                    }
                    return 0;
                }
                if (Mutex.TryOpenExisting(LaunchProtocol.Instance(root), out var instance))
                {
                    using (instance)
                    {
                        if (LaunchProtocol.IsBackgroundStart(args)) return 0;
                        if (!EventWaitHandle.TryOpenExisting(LaunchProtocol.Update(root), out var update))
                            throw new InvalidOperationException("이전 앱을 트레이의 종료 메뉴에서 한 번 종료한 뒤 다시 실행해 주세요. 이후 버전부터는 실행기가 자동으로 전환합니다.");
                        using (update)
                        using (var cancel = EventWaitHandle.OpenExisting(LaunchProtocol.Cancel(root)))
                        {
                            cancel.Reset(); AllowSetForegroundWindow(uint.MaxValue); update.Set();
                            int result;
                            try { result = WaitHandle.WaitAny([instance, cancel], TimeSpan.FromMinutes(2)); }
                            catch (AbandonedMutexException) { result = 0; }
                            if (result == 1) return 0;
                            if (result == WaitHandle.WaitTimeout) throw new TimeoutException("기존 앱의 종료를 기다리고 있습니다. 저장 확인을 마친 뒤 다시 실행해 주세요.");
                            instance.ReleaseMutex();
                        }
                    }
                }
                var info = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)!, CreateNoWindow = true };
                foreach (string arg in args) info.ArgumentList.Add(arg);
                info.Environment[LaunchProtocol.LauncherEnvironment] = Path.Combine(AppContext.BaseDirectory, LaunchProtocol.LauncherName);
                using var child = Process.Start(info) ?? throw new InvalidOperationException("앱을 실행하지 못했습니다.");
                AllowSetForegroundWindow((uint)child.Id);
                return 0;
            }
            finally { gate.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(ex.Message, $"{LaunchProtocol.DisplayName} 실행", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            return 1;
        }
    }
}
