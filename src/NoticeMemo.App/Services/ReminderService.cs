using System.Windows.Threading;
using NoticeMemo.App.ViewModels;
using NoticeMemo.Core.Models;
using NoticeMemo.Core.Services;
using NoticeMemo.Core.Settings;

namespace NoticeMemo.App.Services;

public sealed class ReminderDueEventArgs : EventArgs
{
    public ReminderDueEventArgs(IReadOnlyList<Memo> memos)
    {
        Memos = memos;
    }

    public IReadOnlyList<Memo> Memos { get; }
}

public sealed class ReminderService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Func<AppSettings> _settingsAccessor;
    private readonly DispatcherTimer _timer;
    private bool _checking;
    private bool _disposed;

    public ReminderService(MainViewModel viewModel, Func<AppSettings> settingsAccessor)
    {
        _viewModel = viewModel;
        _settingsAccessor = settingsAccessor;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += Timer_Tick;
    }

    public event EventHandler<ReminderDueEventArgs>? ReminderDue;

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Start();
        CheckNow();
    }

    public void CheckNow()
    {
        if (_disposed || _checking)
        {
            return;
        }

        var settings = _settingsAccessor();
        if (!settings.RemindersEnabled)
        {
            return;
        }

        try
        {
            _checking = true;
            var now = DateTimeOffset.Now;
            var dueMemos = _viewModel.GetMemosSnapshot()
                .Where(memo => ReminderPolicy.Evaluate(memo, settings.ReminderLeadMinutes, now).ShouldRemind)
                .OrderBy(memo => memo.DueAt)
                .Take(3)
                .ToList();
            if (dueMemos.Count > 0)
            {
                ReminderDue?.Invoke(this, new ReminderDueEventArgs(dueMemos));
            }
        }
        finally
        {
            _checking = false;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e) => CheckNow();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }
}
