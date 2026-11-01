using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using NoticeMemo.App.Services;
using NoticeMemo.Core.Models;
using NoticeMemo.Core.Services;
using NoticeMemo.Core.Storage;

namespace NoticeMemo.App.ViewModels;

public enum MemoFilter
{
    All,
    Inbox,
    Today,
    Done
}

public sealed record OperationResult(bool Success, string Message);

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IMemoStore _store;
    private List<Memo> _allMemos = [];
    private Memo? _selectedMemo;
    private string _searchText = "";
    private MemoFilter _currentFilter = MemoFilter.All;
    private string _statusMessage = "";
    private bool _initialized;
    private bool _captureEnabled;
    private bool _autoSaveHighConfidence;

    public MainViewModel(IMemoStore store)
    {
        _store = store;
        SetFilterCommand = new RelayCommand(parameter =>
        {
            if (parameter is string name && Enum.TryParse<MemoFilter>(name, out var filter))
            {
                CurrentFilter = filter;
            }
        });
        ToggleDoneCommand = new RelayCommand(_ => ToggleDone());
        ActivateCommand = new RelayCommand(_ => ActivateSelected());
    }

    public ObservableCollection<Memo> VisibleMemos { get; } = [];

    public ICommand SetFilterCommand { get; }

    public ICommand ToggleDoneCommand { get; }

    public ICommand ActivateCommand { get; }

    public Memo? SelectedMemo
    {
        get => _selectedMemo;
        set
        {
            if (SetProperty(ref _selectedMemo, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public MemoFilter CurrentFilter
    {
        get => _currentFilter;
        set
        {
            if (SetProperty(ref _currentFilter, value))
            {
                OnPropertyChanged(nameof(HeaderTitle));
                OnPropertyChanged(nameof(HeaderSubtitle));
                ApplyFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int TotalCount => _allMemos.Count;

    public int InboxCount => _allMemos.Count(memo => memo.Status == MemoStatus.Inbox);

    public int TodayCount => _allMemos.Count(memo => memo.Status != MemoStatus.Done && memo.DueAt is not null && memo.DueAt.Value.ToLocalTime().Date <= DateTimeOffset.Now.Date);

    public int DoneCount => _allMemos.Count(memo => memo.Status == MemoStatus.Done);

    public int VisibleCount => VisibleMemos.Count;

    public string VisibleCountLabel => $"共 {VisibleCount} 条";

    public bool IsListEmpty => VisibleMemos.Count == 0;

    public bool HasSelection => SelectedMemo is not null;

    public bool HasNoSelection => SelectedMemo is null;

    public string HeaderTitle => CurrentFilter switch
    {
        MemoFilter.Inbox => "待整理",
        MemoFilter.Today => "今天截止",
        MemoFilter.Done => "已完成",
        _ => "全部备忘录"
    };

    public string HeaderSubtitle => CurrentFilter switch
    {
        MemoFilter.Inbox => "先确认通知内容，再把它变成一条真正会执行的备忘录。",
        MemoFilter.Today => "今天需要处理，或者已经逾期的事项。",
        MemoFilter.Done => "已经处理完的通知，留作记录。",
        _ => "把学院群里的通知，收进一张张可以行动的卡片。"
    };

    public string TodayLabel => DateTime.Now.ToString("yyyy年M月d日 · dddd", CultureInfo.GetCultureInfo("zh-CN"));

    public string StorageLabel => $"本地存储 · {_store.BackendName}";

    public string StoragePath => _store.Location;

    public string ExportDirectory => AppPaths.ExportDirectory;

    public string CaptureStatusLabel => _captureEnabled ? "后台自动捕获中" : "自动捕获已暂停";

    public string CaptureStatusDetail => _captureEnabled
        ? _autoSaveHighConfidence
            ? "高置信度通知会自动收录"
            : "捕获后通过托盘确认"
        : "可从托盘或设置中恢复";

    public string CaptureIndicatorColor => _captureEnabled ? "#7CB58B" : "#B7791F";

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Reload();
        StatusMessage = "数据只保存在这台电脑上。";
    }

    public void UpdateCaptureStatus(bool enabled, bool autoSaveHighConfidence)
    {
        _captureEnabled = enabled;
        _autoSaveHighConfidence = autoSaveHighConfidence;
        OnPropertyChanged(nameof(CaptureStatusLabel));
        OnPropertyChanged(nameof(CaptureStatusDetail));
        OnPropertyChanged(nameof(CaptureIndicatorColor));
    }

    public bool ContainsSourceHash(string sourceHash)
    {
        return _allMemos.Any(existing =>
            string.Equals(existing.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase));
    }

    public OperationResult AddImportedMemo(Memo memo, bool focus = true, string? successMessage = null)
    {
        if (string.IsNullOrWhiteSpace(memo.SourceHash))
        {
            memo.SourceHash = NoticeParser.ComputeSourceHash(memo.SourceGroup, memo.SourceText);
        }

        if (ContainsSourceHash(memo.SourceHash))
        {
            return new OperationResult(false, "这条通知已经导入过了。");
        }

        var selectedId = SelectedMemo?.Id;
        _store.Upsert(memo);
        if (focus)
        {
            if (CurrentFilter is MemoFilter.Done or MemoFilter.Today)
            {
                CurrentFilter = MemoFilter.Inbox;
            }

            Reload(memo.Id);
        }
        else
        {
            Reload(selectedId);
        }

        if (focus)
        {
            SelectedMemo = VisibleMemos.FirstOrDefault(item => item.Id == memo.Id) ?? VisibleMemos.FirstOrDefault();
        }

        StatusMessage = successMessage ?? "已收进待整理，确认后即可变成正式备忘录。";
        return new OperationResult(true, StatusMessage);
    }

    public void ToggleDone()
    {
        if (SelectedMemo is null)
        {
            return;
        }

        SetStatus(SelectedMemo.IsDone ? MemoStatus.Active : MemoStatus.Done);
    }

    public void ActivateSelected()
    {
        if (SelectedMemo is not null)
        {
            SetStatus(MemoStatus.Active);
        }
    }

    public void SetStatus(MemoStatus status)
    {
        if (SelectedMemo is null)
        {
            return;
        }

        var memo = SelectedMemo;
        memo.Status = status;
        memo.UpdatedAt = DateTimeOffset.Now;
        memo.CompletedAt = status == MemoStatus.Done ? DateTimeOffset.Now : null;
        _store.Upsert(memo);
        Reload(memo.Id);
        StatusMessage = status switch
        {
            MemoStatus.Done => "已标记完成。",
            MemoStatus.Active => "已转为正式备忘录。",
            _ => "已移回待整理。"
        };
    }

    public void DeleteSelected()
    {
        if (SelectedMemo is null)
        {
            return;
        }

        var id = SelectedMemo.Id;
        _store.Delete(id);
        Reload();
        StatusMessage = "已删除这条备忘录。";
    }

    public string? GetSelectedMarkdown()
    {
        return SelectedMemo is null ? null : MarkdownExporter.Render(SelectedMemo);
    }

    public ExportResult ExportAll()
    {
        var result = MarkdownExporter.ExportAll(_allMemos, ExportDirectory);
        StatusMessage = result.Count == 0 ? "还没有可以导出的备忘录。" : $"已导出 {result.Count} 条 Markdown。";
        return result;
    }

    public void Reload(string? preferredId = null)
    {
        var selectedId = preferredId ?? SelectedMemo?.Id;
        _allMemos = _store.GetAll().ToList();
        ApplyFilter(selectedId);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    private void ApplyFilter(string? preferredId = null)
    {
        var selectedId = preferredId ?? SelectedMemo?.Id;
        IEnumerable<Memo> query = _allMemos;
        query = CurrentFilter switch
        {
            MemoFilter.Inbox => query.Where(memo => memo.Status == MemoStatus.Inbox),
            MemoFilter.Today => query.Where(memo => memo.Status != MemoStatus.Done && memo.DueAt is not null && memo.DueAt.Value.ToLocalTime().Date <= DateTimeOffset.Now.Date),
            MemoFilter.Done => query.Where(memo => memo.Status == MemoStatus.Done),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            query = query.Where(memo => memo.SearchBlob.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query
            .OrderBy(memo => memo.Status == MemoStatus.Done ? 1 : 0)
            .ThenBy(memo => memo.DueAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(memo => memo.CreatedAt)
            .ToList();

        VisibleMemos.Clear();
        foreach (var memo in filtered)
        {
            VisibleMemos.Add(memo);
        }

        SelectedMemo = filtered.FirstOrDefault(memo => memo.Id == selectedId) ?? filtered.FirstOrDefault();
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(VisibleCountLabel));
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(InboxCount));
        OnPropertyChanged(nameof(TodayCount));
        OnPropertyChanged(nameof(DoneCount));
    }
}
