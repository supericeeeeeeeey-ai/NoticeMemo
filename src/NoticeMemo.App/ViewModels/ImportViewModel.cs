using NoticeMemo.Core.Models;
using NoticeMemo.Core.Services;

namespace NoticeMemo.App.ViewModels;

public sealed class ImportViewModel : ViewModelBase
{
    private string _rawText;
    private string _sourceGroup = "学院通知群";
    private ParsedNotice? _parsed;

    public ImportViewModel(string initialText = "", string sourceGroup = "学院通知群")
    {
        _rawText = initialText ?? "";
        if (!string.IsNullOrWhiteSpace(sourceGroup))
        {
            _sourceGroup = sourceGroup.Trim();
        }
        Reparse();
    }

    public string RawText
    {
        get => _rawText;
        set
        {
            if (SetProperty(ref _rawText, value))
            {
                Reparse();
            }
        }
    }

    public string SourceGroup
    {
        get => _sourceGroup;
        set
        {
            if (SetProperty(ref _sourceGroup, value))
            {
                Reparse();
            }
        }
    }

    public ParsedNotice? Parsed
    {
        get => _parsed;
        private set
        {
            if (SetProperty(ref _parsed, value))
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(PreviewTitle));
                OnPropertyChanged(nameof(PreviewDue));
                OnPropertyChanged(nameof(PreviewCategory));
                OnPropertyChanged(nameof(PreviewLocation));
                OnPropertyChanged(nameof(PreviewPriority));
                OnPropertyChanged(nameof(PreviewTags));
            }
        }
    }

    public bool HasText => !string.IsNullOrWhiteSpace(RawText);

    public bool HasPreview => HasText && Parsed is not null;

    public bool CanSave => HasText && Parsed is not null;

    public string PreviewTitle => Parsed?.Title ?? "粘贴通知后自动解析";

    public string PreviewDue => Parsed?.DueLabel ?? "未识别到截止时间";

    public string PreviewCategory => Parsed?.Category ?? "未分类";

    public string PreviewLocation => Parsed?.LocationLabel ?? "未识别到地点";

    public string PreviewPriority => Parsed?.PriorityLabel ?? "普通";

    public string PreviewTags => Parsed?.TagsLabel ?? "等待识别标签";

    public Memo BuildMemo()
    {
        return (Parsed ?? NoticeParser.Parse(RawText, SourceGroup)).ToMemo();
    }

    private void Reparse()
    {
        Parsed = string.IsNullOrWhiteSpace(RawText) ? null : NoticeParser.Parse(RawText, SourceGroup);
        OnPropertyChanged(nameof(HasText));
        OnPropertyChanged(nameof(CanSave));
    }
}
