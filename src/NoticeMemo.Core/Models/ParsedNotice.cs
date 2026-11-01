using System.Globalization;

namespace NoticeMemo.Core.Models;

public sealed class ParsedNotice
{
    public string Title { get; init; } = "未命名通知";

    public string Body { get; init; } = "";

    public string Category { get; init; } = "通知";

    public DateTimeOffset? DueAt { get; init; }

    public string? Location { get; init; }

    public IReadOnlyList<string> Links { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public MemoPriority Priority { get; init; } = MemoPriority.Normal;

    public string SourceGroup { get; init; } = "学院通知群";

    public string SourceText { get; init; } = "";

    public string SourceHash { get; init; } = "";

    public string DueLabel
    {
        get
        {
            if (DueAt is null)
            {
                return "未识别到截止时间";
            }

            var due = DueAt.Value.ToLocalTime();
            var today = DateTimeOffset.Now.Date;
            if (due.Date == today)
            {
                return $"今天 {due:HH:mm}";
            }

            if (due.Date == today.AddDays(1))
            {
                return $"明天 {due:HH:mm}";
            }

            return due.ToString("yyyy年M月d日 HH:mm", CultureInfo.GetCultureInfo("zh-CN"));
        }
    }

    public string PriorityLabel => Priority switch
    {
        MemoPriority.Urgent => "紧急",
        MemoPriority.Important => "重要",
        _ => "普通"
    };

    public string TagsLabel => Tags.Count == 0 ? Category : string.Join("  ·  ", Tags.Take(5));

    public string LocationLabel => string.IsNullOrWhiteSpace(Location) ? "未识别到地点" : Location!;

    public Memo ToMemo()
    {
        var now = DateTimeOffset.Now;
        return new Memo
        {
            Title = Title,
            Body = Body,
            Category = Category,
            DueAt = DueAt,
            Location = Location,
            Links = Links.ToList(),
            Tags = Tags.ToList(),
            Priority = Priority,
            Status = MemoStatus.Inbox,
            SourceGroup = SourceGroup,
            SourceText = SourceText,
            SourceHash = SourceHash,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
