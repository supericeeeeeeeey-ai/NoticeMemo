using System.Windows.Threading;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NoticeMemo.App.Services;

public sealed class QqNotificationCapturedEventArgs : EventArgs
{
    public QqNotificationCapturedEventArgs(string chatName, string text, DateTimeOffset createdAt, uint notificationId)
    {
        ChatName = chatName;
        Text = text;
        CreatedAt = createdAt;
        NotificationId = notificationId;
    }

    public string ChatName { get; }

    public string Text { get; }

    public DateTimeOffset CreatedAt { get; }

    public uint NotificationId { get; }
}

public sealed class NotificationMonitorStatusEventArgs : EventArgs
{
    public NotificationMonitorStatusEventArgs(bool isActive, string message)
    {
        IsActive = isActive;
        Message = message;
    }

    public bool IsActive { get; }

    public string Message { get; }
}

public sealed class WindowsNotificationMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InitialLookback = TimeSpan.FromHours(24);

    private readonly DispatcherTimer _pollTimer;
    private readonly HashSet<uint> _seenNotificationIds = [];
    private UserNotificationListener? _listener;
    private bool _polling;
    private bool _initialPollCompleted;
    private bool _disposed;

    public WindowsNotificationMonitor()
    {
        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += PollTimer_Tick;
    }

    public event EventHandler<QqNotificationCapturedEventArgs>? NotificationCaptured;

    public event EventHandler<NotificationMonitorStatusEventArgs>? StatusChanged;

    public bool IsRunning { get; private set; }

    public async Task<bool> StartAsync()
    {
        if (_disposed || IsRunning)
        {
            return IsRunning;
        }

        try
        {
            _listener = UserNotificationListener.Current;
            var access = _listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified)
            {
                access = await _listener.RequestAccessAsync();
            }

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                StatusChanged?.Invoke(
                    this,
                    new NotificationMonitorStatusEventArgs(
                        false,
                        "Windows 通知读取权限未开启，QQ 自动监听暂不可用。可在 Windows 通知设置中允许后重试。"));
                return false;
            }

            IsRunning = true;
            _initialPollCompleted = false;
            _seenNotificationIds.Clear();
            await PollAsync();
            if (!IsRunning)
            {
                return false;
            }

            _pollTimer.Start();
            StatusChanged?.Invoke(
                this,
                new NotificationMonitorStatusEventArgs(true, "QQ 系统通知监听已启动。"));
            return true;
        }
        catch (Exception exception)
        {
            IsRunning = false;
            _listener = null;
            StatusChanged?.Invoke(
                this,
                new NotificationMonitorStatusEventArgs(
                    false,
                    $"QQ 系统通知监听启动失败：{exception.Message}"));
            return false;
        }
    }

    public void Stop()
    {
        _pollTimer.Stop();
        IsRunning = false;
        _listener = null;
        _polling = false;
    }

    private async void PollTimer_Tick(object? sender, EventArgs e)
    {
        await PollAsync();
    }

    private async Task PollAsync()
    {
        if (_disposed || !IsRunning || _listener is null || _polling)
        {
            return;
        }

        _polling = true;
        try
        {
            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var oldestAccepted = _initialPollCompleted
                ? DateTimeOffset.MinValue
                : DateTimeOffset.Now.Subtract(InitialLookback);

            foreach (var item in notifications.OrderBy(notification => notification.CreationTime))
            {
                try
                {
                    if (!IsQqNotification(item.AppInfo) || item.CreationTime < oldestAccepted)
                    {
                        continue;
                    }

                    if (!_seenNotificationIds.Add(item.Id))
                    {
                        continue;
                    }

                    if (TryExtract(item, out var capture))
                    {
                        NotificationCaptured?.Invoke(this, capture);
                    }
                }
                catch (Exception)
                {
                    // Some third-party toasts expose incomplete WinRT projections.
                }
            }

            _initialPollCompleted = true;
        }
        catch (Exception exception)
        {
            IsRunning = false;
            _pollTimer.Stop();
            StatusChanged?.Invoke(
                this,
                new NotificationMonitorStatusEventArgs(
                    false,
                    $"QQ 系统通知监听已停止：{exception.Message}"));
        }
        finally
        {
            _polling = false;
        }
    }

    private static bool IsQqNotification(AppInfo appInfo)
    {
        var appUserModelId = appInfo.AppUserModelId?.Trim() ?? "";
        var displayName = appInfo.DisplayInfo?.DisplayName?.Trim() ?? "";
        return displayName.Equals("QQ", StringComparison.OrdinalIgnoreCase) ||
               appUserModelId.Equals("QQ", StringComparison.OrdinalIgnoreCase) ||
               appUserModelId.StartsWith("Tencent.QQ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtract(UserNotification notification, out QqNotificationCapturedEventArgs capture)
    {
        capture = null!;
        try
        {
            var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null)
            {
                return false;
            }

            var texts = binding.GetTextElements()
                .Select(element => element.Text?.Trim() ?? "")
                .Where(text => text.Length > 0)
                .ToList();
            if (texts.Count == 0)
            {
                return false;
            }

            var chatName = texts.Count >= 2 ? texts[0] : "";
            var message = texts.Count >= 2
                ? string.Join('\n', texts.Skip(1))
                : texts[0];
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            capture = new QqNotificationCapturedEventArgs(
                chatName,
                message.Trim(),
                notification.CreationTime,
                notification.Id);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _pollTimer.Tick -= PollTimer_Tick;
    }
}
