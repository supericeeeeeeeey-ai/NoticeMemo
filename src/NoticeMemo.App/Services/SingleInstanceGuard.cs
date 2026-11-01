using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace NoticeMemo.App.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly EventWaitHandle _exitEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private RegisteredWaitHandle? _exitRegistration;
    private bool _disposed;

    private SingleInstanceGuard(
        Mutex mutex,
        EventWaitHandle activationEvent,
        EventWaitHandle exitEvent,
        bool isPrimary)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _exitEvent = exitEvent;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public event EventHandler? ActivationRequested;

    public event EventHandler? ExitRequested;

    public static SingleInstanceGuard Create()
    {
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        var mutexName = $"Local\\NoticeMemo.Instance.{userHash}";
        var eventName = $"Local\\NoticeMemo.Activate.{userHash}";
        var exitEventName = $"Local\\NoticeMemo.Exit.{userHash}";

        var isPrimary = true;
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
            isPrimary = createdNew;
        }
        catch (AbandonedMutexException)
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out _);
        }

        var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, exitEventName);
        var guard = new SingleInstanceGuard(mutex, activationEvent, exitEvent, isPrimary);
        if (isPrimary)
        {
            guard._activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, _) => guard.DispatchActivation(),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
            guard._exitRegistration = ThreadPool.RegisterWaitForSingleObject(
                exitEvent,
                (_, _) => guard.DispatchExit(),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }

        return guard;
    }

    public void SignalPrimary()
    {
        try
        {
            _activationEvent.Set();
        }
        catch (Exception)
        {
        }
    }

    public void SignalExit()
    {
        try
        {
            _exitEvent.Set();
        }
        catch (Exception)
        {
        }
    }

    private void DispatchActivation()
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            }));
        }
        catch (Exception)
        {
        }
    }

    private void DispatchExit()
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }));
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationRegistration?.Unregister(null);
        _exitRegistration?.Unregister(null);
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _activationEvent.Dispose();
        _exitEvent.Dispose();
        _mutex.Dispose();
    }
}
