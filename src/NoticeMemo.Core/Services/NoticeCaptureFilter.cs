using System.Text.RegularExpressions;
using NoticeMemo.Core.Models;

namespace NoticeMemo.Core.Services;

public enum CaptureConfidence
{
    Low,
    Medium,
    High
}

public sealed record CaptureAssessment(
    bool IsNotice,
    CaptureConfidence Confidence,
    int Score,
    IReadOnlyList<string> Signals)
{
    public static CaptureAssessment Ignore(string reason) => new(false, CaptureConfidence.Low, 0, [reason]);
}

public static class NoticeCaptureFilter
{
    public const int MinimumNoticeScore = 4;
    public const int HighConfidenceScore = 7;

    private const int MinimumLength = 20;
    private const int MaximumLength = 20_000;

    private static readonly string[] ActionWords =
    [
        "通知", "公告", "报名", "截止", "请于", "务必", "安排", "会议",
        "讲座", "考试", "提交", "地点", "签到", "调课", "选课", "领取"
    ];

    public static CaptureAssessment Evaluate(string? text, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return CaptureAssessment.Ignore("没有文本");
        }

        var normalized = text.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.Length < MinimumLength)
        {
            return CaptureAssessment.Ignore("内容过短");
        }

        if (normalized.Length > MaximumLength)
        {
            return CaptureAssessment.Ignore("内容过长");
        }

        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var signals = new List<string>();
        var score = 0;

        if (Regex.IsMatch(normalized, @"(^|\n)\s*【[^】]{1,24}】"))
        {
            score += 2;
            signals.Add("标题");
        }

        if (normalized.Contains("@全体成员", StringComparison.Ordinal))
        {
            score += 2;
            signals.Add("群公告");
        }

        ParsedNotice? parsed = null;
        try
        {
            parsed = NoticeParser.Parse(normalized, now: now);
        }
        catch (Exception)
        {
        }

        if (parsed?.DueAt is not null)
        {
            score += 3;
            signals.Add("时间");
        }

        var keywordHits = ActionWords
            .Where(word => normalized.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();
        score += keywordHits.Count;
        if (keywordHits.Count > 0)
        {
            signals.Add("行动词");
        }

        if (Regex.IsMatch(normalized, @"https?://", RegexOptions.IgnoreCase))
        {
            score += 1;
            signals.Add("链接");
        }

        if (lines.Length >= 3)
        {
            score += 1;
            signals.Add("多行");
        }

        var firstLine = lines.FirstOrDefault() ?? "";
        if (Regex.IsMatch(firstLine, "通知|公告|安排|提醒") ||
            firstLine.StartsWith("关于", StringComparison.Ordinal) ||
            firstLine.StartsWith("【", StringComparison.Ordinal))
        {
            score += 2;
            signals.Add("通知标题");
        }

        if (Regex.IsMatch(firstLine, "^(各位|同学们|大家|全体)"))
        {
            score += 1;
            signals.Add("称呼");
        }

        if (normalized.Length >= 80)
        {
            score += 1;
            signals.Add("正文");
        }

        if (LooksLikeCode(normalized))
        {
            score -= 4;
            signals.Add("代码");
        }

        var confidence = score >= HighConfidenceScore
            ? CaptureConfidence.High
            : score >= MinimumNoticeScore
                ? CaptureConfidence.Medium
                : CaptureConfidence.Low;

        return new CaptureAssessment(
            score >= MinimumNoticeScore,
            confidence,
            Math.Max(0, score),
            signals.Distinct(StringComparer.Ordinal).ToList());
    }

    private static bool LooksLikeCode(string text)
    {
        var hasBraces = text.Contains('{') && text.Contains('}');
        var hasCodeMarker = text.Contains("=>", StringComparison.Ordinal) ||
                            text.Contains("public ", StringComparison.Ordinal) ||
                            text.Contains("private ", StringComparison.Ordinal) ||
                            text.Contains("function ", StringComparison.Ordinal) ||
                            text.Contains("const ", StringComparison.Ordinal) ||
                            text.Contains("class ", StringComparison.Ordinal);
        return hasBraces && (hasCodeMarker || text.Count(character => character == ';') >= 3);
    }
}
