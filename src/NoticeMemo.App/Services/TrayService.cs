using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace NoticeMemo.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _toggleMonitoringItem;
    private readonly Forms.ToolStripMenuItem _pendingCaptureItem;
    private readonly Drawing.Icon _icon;
    private Action? _balloonClickAction;
    private bool _disposed;

    public TrayService()
    {
        _icon = LoadApplicationIcon();
        _menu = new Forms.ContextMenuStrip();
        _toggleMonitoringItem = new Forms.ToolStripMenuItem("暂停自动捕获");
        _pendingCaptureItem = new Forms.ToolStripMenuItem("确认刚捕获的通知") { Enabled = false };

        var openItem = new Forms.ToolStripMenuItem("打开 NoticeMemo");
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var importItem = new Forms.ToolStripMenuItem("立即从剪贴板导入");
        importItem.Click += (_, _) => ImportClipboardRequested?.Invoke(this, EventArgs.Empty);

        _pendingCaptureItem.Click += (_, _) => ConfirmPendingRequested?.Invoke(this, EventArgs.Empty);
        _toggleMonitoringItem.Click += (_, _) => ToggleMonitoringRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new Forms.ToolStripMenuItem("设置...");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new Forms.ToolStripMenuItem("退出 NoticeMemo");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu.Items.Add(openItem);
        _menu.Items.Add(importItem);
        _menu.Items.Add(_pendingCaptureItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_toggleMonitoringItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "NoticeMemo · 后台通知捕获",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonClickAction;
            _balloonClickAction = null;
            if (action is not null)
            {
                action();
            }
            else
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ImportClipboardRequested;

    public event EventHandler? ConfirmPendingRequested;

    public event EventHandler? ToggleMonitoringRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void SetMonitoringEnabled(bool enabled)
    {
        _toggleMonitoringItem.Text = enabled ? "暂停自动捕获" : "恢复自动捕获";
        _notifyIcon.Text = enabled
            ? "NoticeMemo · 后台通知捕获中"
            : "NoticeMemo · 自动捕获已暂停";
    }

    public void SetPendingCapture(int pendingCount)
    {
        _pendingCaptureItem.Enabled = pendingCount > 0;
        _pendingCaptureItem.Text = pendingCount switch
        {
            <= 0 => "暂无待确认通知",
            1 => "确认待处理通知",
            _ => $"确认待处理通知（{pendingCount}）"
        };
    }

    public void ShowNotification(string title, string message, Action? onClick = null)
    {
        if (_disposed)
        {
            return;
        }

        _balloonClickAction = onClick;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.BalloonTipTitle = Truncate(title, 63);
        _notifyIcon.BalloonTipText = Truncate(message, 255);
        _notifyIcon.ShowBalloonTip(6000);
    }

    public void ShowError(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        _balloonClickAction = () => OpenRequested?.Invoke(this, EventArgs.Empty);
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _notifyIcon.BalloonTipTitle = Truncate(title, 63);
        _notifyIcon.BalloonTipText = Truncate(message, 255);
        _notifyIcon.ShowBalloonTip(8000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(executablePath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch (Exception)
        {
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..Math.Max(0, maximumLength - 1)] + "…";
    }
}
