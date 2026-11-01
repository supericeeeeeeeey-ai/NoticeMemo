using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NoticeMemo.App.Services;

public sealed class ClipboardTextCapturedEventArgs : EventArgs
{
    public ClipboardTextCapturedEventArgs(string text)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed class ClipboardMonitor : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private HwndSource? _source;
    private bool _disposed;
    private bool _readInProgress;
    private bool _readAgain;
    private DateTimeOffset _suppressUntil;

    public event EventHandler<ClipboardTextCapturedEventArgs>? TextCaptured;

    public bool IsRunning => _source is not null;

    public void Start()
    {
        ThrowIfDisposed();
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("剪贴板监听必须在界面线程启动。");
        }

        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("NoticeMemo.ClipboardMonitor")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = HwndMessage
        };

        var source = new HwndSource(parameters);
        source.AddHook(WndProc);
        if (!AddClipboardFormatListener(source.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            source.RemoveHook(WndProc);
            source.Dispose();
            throw new Win32Exception(error, "无法注册剪贴板监听。");
        }

        _source = source;
    }

    public void Stop()
    {
        if (_source is null)
        {
            return;
        }

        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    public void SuppressNextChange()
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            _suppressUntil = DateTimeOffset.UtcNow.AddSeconds(1);
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                _suppressUntil = DateTimeOffset.UtcNow.AddSeconds(1);
            }));
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmClipboardUpdate)
        {
            return IntPtr.Zero;
        }

        handled = true;
        if (DateTimeOffset.UtcNow < _suppressUntil)
        {
            return IntPtr.Zero;
        }

        QueueRead();
        return IntPtr.Zero;
    }

    private void QueueRead()
    {
        if (_readInProgress)
        {
            _readAgain = true;
            return;
        }

        _readInProgress = true;
        _dispatcher.BeginInvoke(new Action(ReadClipboardAsync), DispatcherPriority.Background);
    }

    private async void ReadClipboardAsync()
    {
        try
        {
            await Task.Delay(60);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (!Clipboard.ContainsText())
                    {
                        return;
                    }

                    var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = Clipboard.GetText();
                    }

                    if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.UtcNow >= _suppressUntil)
                    {
                        TextCaptured?.Invoke(this, new ClipboardTextCapturedEventArgs(text.Trim()));
                    }

                    return;
                }
                catch (Exception exception) when (exception is COMException or ExternalException)
                {
                    if (attempt == 5)
                    {
                        return;
                    }

                    await Task.Delay(100);
                }
            }
        }
        finally
        {
            _readInProgress = false;
            if (_readAgain)
            {
                _readAgain = false;
                QueueRead();
            }
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
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ClipboardMonitor));
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
