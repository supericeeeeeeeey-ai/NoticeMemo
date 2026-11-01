using System.Globalization;
using System.Text.RegularExpressions;

namespace NoticeMemo.Core.Models;

public sealed class Memo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public string Category { get; set; } = "通知";

    public DateTimeOffset? DueAt { get; set; }

    public string? Location { get; set; }

    public List<string> Links { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public MemoPriority Priority { get; set; } = MemoPriority.Normal;

    public MemoStatus Status { get; set; } = MemoStatus.Inbox;

    public string SourceGroup { get; set; } = "学院通知群";

    public string SourceText { get; set; } = "";

    public string SourceHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ReminderSentAt { get; set; }

    public bool IsInbox => Status == MemoStatus.Inbox;

    public bool IsActive => Status == MemoStatus.Active;

    public bool IsDone => Status == MemoStatus.Done;

    public bool HasDueDate => DueAt is not null;

    public bool IsOverdue => DueAt is not null && Status != MemoStatus.Done && DueAt.Value < DateTimeOffset.Now;

    public bool IsDueToday => DueAt is not null && DueAt.Value.ToLocalTime().Date == DateTimeOffset.Now.Date;

    public bool HasLinks => Links.Count > 0;

    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);

    public string StatusLabel => Status switch
    {
        MemoStatus.Inbox => "待整理",
        MemoStatus.Active => "进行中",
        MemoStatus.Done => "已完成",
        _ => "待整理"
    };

    public string PriorityLabel => Priority switch
    {
        MemoPriority.Urgent => "紧急",
        MemoPriority.Important => "重要",
        _ => "普通"
    };

    public string PriorityColor => Priority switch
    {
        MemoPriority.Urgent => "#B54545",
        MemoPriority.Important => "#B7791F",
        _ => "#7A776F"
    };

    public string StatusColor => Status switch
    {
        MemoStatus.Inbox => "#C45A3C",
        MemoStatus.Active => "#3E7B5A",
        MemoStatus.Done => "#7A776F",
        _ => "#7A776F"
    };

    public string DueLabel
    {
        get
        {
            if (DueAt is null)
            {
                return "未设置截止时间";
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

            return due.ToString("M月d日 HH:mm", CultureInfo.GetCultureInfo("zh-CN"));
        }
    }

    public string DueShortLabel
    {
        get
        {
            if (DueAt is null)
            {
                return "无截止";
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

            return due.ToString("MM.dd HH:mm", CultureInfo.InvariantCulture);
        }
    }

    public string Summary
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(Body) ? Title : Body;
            var compact = Regex.Replace(source, @"\s+", " ").Trim();
            compact = Regex.Replace(compact, @"^【[^】]{1,16}】\s*", "");
            if (!string.IsNullOrWhiteSpace(Title) && compact.StartsWith(Title, StringComparison.Ordinal))
            {
                compact = compact[Title.Length..].Trim(' ', '：', ':', '。', '，');
            }

            if (compact.Length == 0)
            {
                compact = Title;
            }

            return compact.Length <= 96 ? compact : compact[..96].TrimEnd() + "…";
        }
    }

    public string TagsLabel => Tags.Count == 0 ? Category : string.Join("  ·  ", Tags.Take(4));

    public string CategoryLabel => string.IsNullOrWhiteSpace(Category) ? "通知" : Category;

    public string LocationLabel => string.IsNullOrWhiteSpace(Location) ? "未填写地点" : Location!;

    public string CreatedLabel => CreatedAt.ToLocalTime().ToString("M月d日 HH:mm", CultureInfo.GetCultureInfo("zh-CN"));

    public string CompletedLabel => CompletedAt is null
        ? ""
        : CompletedAt.Value.ToLocalTime().ToString("M月d日 HH:mm", CultureInfo.GetCultureInfo("zh-CN"));

    public string SearchBlob => string.Join(' ', new[]
    {
        Title,
        Body,
        Category,
        SourceGroup,
        Location ?? "",
        string.Join(' ', Tags),
        string.Join(' ', Links)
    }).ToLowerInvariant();
}
