using System.Windows;
using NoticeMemo.App.Services;
using NoticeMemo.App.ViewModels;
using NoticeMemo.Core.Services;
using NoticeMemo.Core.Settings;

namespace NoticeMemo.App;

public sealed class AppHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _settingsStore;
    private readonly ClipboardMonitor _clipboardMonitor;
    private AppSettings _settings;
    private TrayService? _tray;
    private bool _monitoringActive;
    private readonly List<PendingCapture> _pendingCaptures = [];
    private bool _disposed;
    private bool _viewModelDisposed;

    public AppHost(
        MainWindow window,
        MainViewModel viewModel,
        SettingsStore settingsStore,
        AppSettings settings,
        ClipboardMonitor clipboardMonitor)
    {
        _window = window;
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        _settings = settings.Clone();
        _clipboardMonitor = clipboardMonitor;

        _window.SettingsRequested += OnSettingsRequested;
        _window.HiddenToTray += OnHiddenToTray;
        _clipboardMonitor.TextCaptured += OnClipboardTextCaptured;
    }

    public void Start(bool startHidden)
    {
        _viewModel.Initialize();
        _window.DefaultSourceGroup = _settings.SourceGroup;
        _window.CloseToTrayEnabled = true;
        CreateTray();

        if (_settings.ClipboardMonitoringEnabled)
        {
            TryStartMonitoring(showError: false);
        }

        UpdateCaptureStatus();
        if (!startHidden)
        {
            _window.ShowAndActivate();
        }
    }

    public void ShowWindow()
    {
        if (_disposed)
        {
            return;
        }

        _window.ShowAndActivate();
    }

    public void Exit()
    {
        if (_disposed)
        {
            return;
        }

        _window.PrepareForExit();
        _window.Close();
        Application.Current.Shutdown();
    }

    private void CreateTray()
    {
        _tray = new TrayService();
        _tray.OpenRequested += (_, _) => ShowWindow();
        _tray.ImportClipboardRequested += (_, _) => _window.ImportClipboardNow();
        _tray.ConfirmPendingRequested += (_, _) => ConfirmPendingCapture();
        _tray.ToggleMonitoringRequested += (_, _) => ToggleMonitoring();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.ExitRequested += (_, _) => Exit();
        _tray.SetMonitoringEnabled(false);
        _tray.SetPendingCapture(0);
    }

    private void OnClipboardTextCaptured(object? sender, ClipboardTextCapturedEventArgs e)
    {
        if (_disposed || !_monitoringActive || !_settings.ClipboardMonitoringEnabled)
        {
            return;
        }

        try
        {
            HandleCapturedText(e.Text);
        }
        catch (Exception exception)
        {
            _viewModel.StatusMessage = $"处理剪贴板通知失败：{exception.Message}";
            if (_settings.ShowTrayNotifications)
            {
                _tray?.ShowError("NoticeMemo 捕获失败", exception.Message);
            }
        }
    }

    private void HandleCapturedText(string text)
    {
        var normalized = text.Trim();
        var assessment = NoticeCaptureFilter.Evaluate(normalized);
        if (!assessment.IsNotice)
        {
            return;
        }

        var hash = NoticeParser.ComputeSourceHash(_settings.SourceGroup, normalized);
        if (_viewModel.ContainsSourceHash(hash))
        {
            RemovePendingCapture(hash);
            return;
        }

        if (_pendingCaptures.Any(pending =>
                string.Equals(pending.Hash, hash, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var shouldAutoSave = _settings.AutoSaveHighConfidence &&
                             assessment.Confidence == CaptureConfidence.High;
        if (!shouldAutoSave)
        {
            EnqueuePendingCapture(normalized, hash);
            return;
        }

        var memo = NoticeParser.Parse(normalized, _settings.SourceGroup).ToMemo();
        var result = _viewModel.AddImportedMemo(
            memo,
            focus: false,
            successMessage: $"后台已自动收录：{memo.Title}");

        if (!result.Success)
        {
            return;
        }

        RemovePendingCapture(hash);
        if (_settings.ShowTrayNotifications)
        {
            _tray?.ShowNotification("已自动收录通知", memo.Title, ShowWindow);
        }
    }

    private void EnqueuePendingCapture(string text, string hash)
    {
        _pendingCaptures.Add(new PendingCapture(text, hash));
        UpdatePendingCaptureMenu();
        _viewModel.StatusMessage = _pendingCaptures.Count == 1
            ? "已捕获一条通知，可在托盘菜单中确认解析结果。"
            : $"已捕获 {_pendingCaptures.Count} 条待确认通知。";

        if (_settings.ShowTrayNotifications)
        {
            _tray?.ShowNotification(
                "捕获到一条通知",
                "点击确认并检查解析结果。",
                ConfirmPendingCapture);
        }
    }

    private void RemovePendingCapture(string hash)
    {
        if (_pendingCaptures.RemoveAll(pending =>
                string.Equals(pending.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            UpdatePendingCaptureMenu();
        }
    }

    private void UpdatePendingCaptureMenu()
    {
        _tray?.SetPendingCapture(_pendingCaptures.Count);
    }

    private void ConfirmPendingCapture()
    {
        if (_pendingCaptures.Count == 0)
        {
            return;
        }

        var pending = _pendingCaptures[0];
        if (_window.ImportPendingText(pending.Text))
        {
            _pendingCaptures.RemoveAt(0);
            UpdatePendingCaptureMenu();
        }
    }

    private void ToggleMonitoring()
    {
        if (_monitoringActive)
        {
            _clipboardMonitor.Stop();
            _monitoringActive = false;
            _settings.ClipboardMonitoringEnabled = false;
            _viewModel.StatusMessage = "自动捕获已暂停，已保留托盘入口。";
        }
        else
        {
            if (!TryStartMonitoring(showError: true))
            {
                return;
            }

            _settings.ClipboardMonitoringEnabled = true;
            _viewModel.StatusMessage = "自动捕获已恢复。";
        }

        SaveSettings(showError: true);
        UpdateCaptureStatus();
    }

    private bool TryStartMonitoring(bool showError)
    {
        try
        {
            _clipboardMonitor.Start();
            _monitoringActive = true;
            return true;
        }
        catch (Exception exception)
        {
            _monitoringActive = false;
            if (showError)
            {
                ShowErrorMessage("无法启动自动捕获", exception.Message);
            }

            return false;
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        ShowWindow();
        var dialog = new SettingsDialog(_settings) { Owner = _window };
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            ApplySettings(result);
        }
    }

    private void ApplySettings(AppSettings requested)
    {
        requested.Normalize();
        var currentStartup = IsStartupEnabledSafe();
        string? startupError = null;
        if (requested.StartWithWindows != currentStartup)
        {
            try
            {
                WindowsStartupService.SetEnabled(requested.StartWithWindows);
            }
            catch (Exception exception)
            {
                requested.StartWithWindows = currentStartup;
                startupError = exception.Message;
            }
        }

        var previous = _settings;
        _settings = requested.Clone();
        _window.DefaultSourceGroup = _settings.SourceGroup;

        if (_settings.ClipboardMonitoringEnabled)
        {
            if (!_monitoringActive)
            {
                TryStartMonitoring(showError: true);
            }
        }
        else
        {
            _clipboardMonitor.Stop();
            _monitoringActive = false;
        }

        SaveSettings(showError: true);
        UpdateCaptureStatus();

        if (startupError is not null)
        {
            MessageBox.Show(
                _window,
                $"Windows 开机启动设置失败：{startupError}",
                "NoticeMemo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (previous.SourceGroup != _settings.SourceGroup)
        {
            _viewModel.StatusMessage = "来源群聊设置已更新，仅影响之后捕获的通知。";
        }
        else
        {
            _viewModel.StatusMessage = "后台捕获设置已保存。";
        }
    }

    private void OnHiddenToTray(object? sender, EventArgs e)
    {
        if (!_settings.ShowTrayNotifications || _settings.TrayHintShown)
        {
            return;
        }

        _settings.TrayHintShown = true;
        SaveSettings(showError: false);
        _tray?.ShowNotification(
            "NoticeMemo 正在后台运行",
            "关闭窗口不会退出；双击托盘图标可以重新打开。",
            ShowWindow);
    }

    private void UpdateCaptureStatus()
    {
        var active = _monitoringActive && _settings.ClipboardMonitoringEnabled;
        _viewModel.UpdateCaptureStatus(active, _settings.AutoSaveHighConfidence);
        _tray?.SetMonitoringEnabled(active);
    }

    private void SaveSettings(bool showError)
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            if (showError)
            {
                ShowErrorMessage("无法保存设置", exception.Message);
            }
        }
    }

    private static bool IsStartupEnabledSafe()
    {
        try
        {
            return WindowsStartupService.IsEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ShowErrorMessage(string title, string message)
    {
        if (_window.IsVisible)
        {
            MessageBox.Show(_window, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (_settings.ShowTrayNotifications)
        {
            _tray?.ShowError(title, message);
        }
    }

    private sealed record PendingCapture(string Text, string Hash);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.SettingsRequested -= OnSettingsRequested;
        _window.HiddenToTray -= OnHiddenToTray;
        _clipboardMonitor.TextCaptured -= OnClipboardTextCaptured;
        _clipboardMonitor.Dispose();
        _tray?.Dispose();
        _tray = null;

        if (!_viewModelDisposed)
        {
            _viewModelDisposed = true;
            _viewModel.Dispose();
        }
    }
}
