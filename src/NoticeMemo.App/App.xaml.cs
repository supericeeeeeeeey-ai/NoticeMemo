using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NoticeMemo.App.Services;
using NoticeMemo.App.ViewModels;
using NoticeMemo.Core.Settings;
using NoticeMemo.Core.Storage;

namespace NoticeMemo.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            if (TryGetArgumentValue(e.Args, "--data-dir", out var dataDirectoryOverride))
            {
                AppPaths.SetDataDirectoryOverride(dataDirectoryOverride);
            }

            if (TryRenderImport(e.Args))
            {
                return;
            }

            if (TryRenderSettings(e.Args))
            {
                return;
            }

            var isDemo = HasArgument(e.Args, "--demo");
            var isBackground = HasArgument(e.Args, "--background");
            var isExitRequest = HasArgument(e.Args, "--exit");
            if (TryGetArgumentValue(e.Args, "--render", out var renderPath))
            {
                RenderMainWindow(renderPath, isDemo);
                return;
            }

            if (!isDemo)
            {
                _singleInstance = SingleInstanceGuard.Create();
                if (!_singleInstance.IsPrimary)
                {
                    if (isExitRequest)
                    {
                        _singleInstance.SignalExit();
                    }
                    else if (!isBackground)
                    {
                        _singleInstance.SignalPrimary();
                    }

                    _singleInstance.Dispose();
                    _singleInstance = null;
                    Shutdown();
                    return;
                }

                if (isExitRequest)
                {
                    _singleInstance.Dispose();
                    _singleInstance = null;
                    Shutdown();
                    return;
                }
            }

            var dataDirectory = isDemo
                ? Path.Combine(Path.GetTempPath(), "NoticeMemoDemo", DateTime.Now.ToString("yyyyMMddHHmmssfff"))
                : AppPaths.DataDirectory;

            var store = MemoStoreFactory.Create(dataDirectory);
            if (isDemo)
            {
                DemoData.Seed(store);
            }

            var viewModel = new MainViewModel(store);
            var clipboardMonitor = new ClipboardMonitor();
            var window = new MainWindow(viewModel, clipboardMonitor);
            MainWindow = window;

            if (isDemo)
            {
                viewModel.Initialize();
                viewModel.UpdateCaptureStatus(false, autoSaveHighConfidence: false);
                window.CloseToTrayEnabled = false;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Show();
                return;
            }

            var settingsStore = new SettingsStore(AppPaths.SettingsFile);
            var settings = settingsStore.Load();
            _host = new AppHost(window, viewModel, settingsStore, settings, clipboardMonitor);
            _host.Start(startHidden: isBackground);
            _singleInstance!.ActivationRequested += (_, _) => _host?.ShowWindow();
            _singleInstance.ExitRequested += (_, _) => _host?.Exit();
        }
        catch (Exception exception)
        {
            try
            {
                _host?.Dispose();
                _host = null;
            }
            catch (Exception)
            {
            }

            MessageBox.Show(
                $"NoticeMemo 启动失败：{exception.Message}",
                "NoticeMemo",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Dispose();
            _host = null;
            _singleInstance?.Dispose();
            _singleInstance = null;
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private void RenderMainWindow(string path, bool isDemo)
    {
        var dataDirectory = isDemo
            ? Path.Combine(Path.GetTempPath(), "NoticeMemoDemo", DateTime.Now.ToString("yyyyMMddHHmmssfff"))
            : AppPaths.DataDirectory;
        var store = MemoStoreFactory.Create(dataDirectory);
        if (isDemo)
        {
            DemoData.Seed(store);
        }

        var viewModel = new MainViewModel(store);
        var clipboardMonitor = new ClipboardMonitor();
        var window = new MainWindow(viewModel, clipboardMonitor)
        {
            CloseToTrayEnabled = false
        };
        MainWindow = window;
        viewModel.Initialize();
        viewModel.UpdateCaptureStatus(
            enabled: true,
            autoSaveHighConfidence: true);
        window.Show();
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                RenderWindow(window, path);
                window.Close();
                clipboardMonitor.Dispose();
                viewModel.Dispose();
                Shutdown();
            }),
            DispatcherPriority.ApplicationIdle);
    }

    private bool TryRenderImport(string[] arguments)
    {
        if (!TryGetArgumentValue(arguments, "--render-import", out var path))
        {
            return false;
        }

        var preview = new ImportDialog("""
            【讲座预告】人工智能与未来教育
            本周三晚上7:00在图书馆报告厅举行专题讲座，欢迎同学们参加。
            请于本周二17:00前扫码报名，座位有限。
            详情：https://example.edu.cn/lecture
            #讲座 #活动
            """);
        preview.Show();
        preview.ContentRendered += (_, _) => preview.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                RenderWindow(preview, path);
                preview.Close();
                Shutdown();
            }),
            DispatcherPriority.ApplicationIdle);
        return true;
    }

    private bool TryRenderSettings(string[] arguments)
    {
        if (!TryGetArgumentValue(arguments, "--render-settings", out var path))
        {
            return false;
        }

        var settings = new AppSettings
        {
            ClipboardMonitoringEnabled = true,
            AutoSaveHighConfidence = true,
            ShowTrayNotifications = true,
            StartWithWindows = true,
            SourceGroup = "学院通知群"
        };
        var preview = new SettingsDialog(settings);
        preview.Show();
        preview.ContentRendered += (_, _) => preview.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                RenderWindow(preview, path);
                preview.Close();
                Shutdown();
            }),
            DispatcherPriority.ApplicationIdle);
        return true;
    }

    private static bool HasArgument(string[] arguments, string expected)
    {
        return arguments.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetArgumentValue(string[] arguments, string name, out string value)
    {
        var index = Array.FindIndex(arguments, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < arguments.Length)
        {
            value = arguments[index + 1];
            return true;
        }

        value = "";
        return false;
    }

    private static void RenderWindow(Window window, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
