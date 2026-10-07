using System.IO;
using System.Windows;

namespace HsAuto;

public partial class App : Application
{
    Mutex? singleInstance;
    bool ownsInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HsAuto.OpenSource.Glass");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "errors-0.1.8.log"), DateTime.Now.ToString("O") + " " + HsAuto.Open.UiText.Normalize(args.Exception.ToString()) + Environment.NewLine);
            args.Handled = true;
            MessageBox.Show("操作失败，已写入本地日志。\n" + args.Exception.Message, "HearthLeap", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        bool smoke = e.Args.Contains("--smoke");
        if (smoke) ShutdownMode = ShutdownMode.OnExplicitShutdown;
        else
        {
            singleInstance = new Mutex(true, @"Local\HsAuto.OpenSource.Glass", out ownsInstance);
            if (!ownsInstance)
            {
                MessageBox.Show("HearthLeap 已经在运行，请使用原窗口，避免重复提交游戏动作。", "HearthLeap", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0); return;
            }
        }
        var window = new MainWindow(smoke);
        MainWindow = window;
        if (smoke) _ = window.SmokeAsync(e.Args.SkipWhile(a => a != "--smoke").Skip(1).FirstOrDefault());
        else window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsInstance) singleInstance?.ReleaseMutex();
        singleInstance?.Dispose(); base.OnExit(e);
    }
}
