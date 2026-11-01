using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NoticeMemo.Core.Models;

namespace NoticeMemo.Core.Services;

public static partial class NoticeParser
{
    private static readonly string[] UrgentWords = ["紧急", "务必", "立即", "马上", "速", "今天内", "今天前"];
    private static readonly string[] ImportantWords = ["重要", "注意", "及时", "按时", "截止", "DDL", "ddl"];

    private static readonly Regex AtAllRegex = new(@"^\s*(?:@全体成员|@所有人|全体成员)[\s:：,，]*", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TagRegex = new(@"(?<![\w])#([\p{L}\p{N}_-]{1,24})", RegexOptions.Compiled);
    private static readonly Regex DateYmdRegex = new(@"(?<year>20\d{2})\s*[年./-]\s*(?<month>\d{1,2})\s*[月./-]\s*(?<day>\d{1,2})\s*日?", RegexOptions.Compiled);
    private static readonly Regex DateMdRegex = new(@"(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日", RegexOptions.Compiled);
    private static readonly Regex DateSlashRegex = new(@"(?<![\d/])(?<month>\d{1,2})/(?<day>\d{1,2})(?![\d/])", RegexOptions.Compiled);
    private static readonly Regex RelativeDateRegex = new(@"(?<rel>大后天|后天|明天|今天|本周[一二三四五六日天]|下周[一二三四五六日天]|周[一二三四五六日天]|星期[一二三四五六日天])", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"(?<period>凌晨|早上|上午|中午|下午|傍晚|晚上)?\s*(?<hour>\d{1,2})\s*(?:[:：]\s*(?<minute>\d{1,2})|点\s*(?:(?<minute2>\d{1,2})\s*分?)?)", RegexOptions.Compiled);
    private static readonly Regex LocationRegex = new(@"(?im)^\s*(?:地点|地址|教室|会场|会议地点|活动地点|举办地点)\s*[:：]\s*(?<value>[^\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex LocationInlineRegex = new(@"(?:在|于)(?<value>[\p{L}\p{N}\s·（）()\-]{2,32}?)(?:举行|召开|开展|举办|进行)", RegexOptions.Compiled);
    private static readonly Regex NotificationPrefixRegex = new(@"^(?:\[通知\]|【通知】|通知[:：])\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BracketPrefixRegex = new(@"^【[^】]{1,16}】\s*", RegexOptions.Compiled);

    public static ParsedNotice Parse(string rawText, string sourceGroup = "学院通知群", DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.Now;
        var text = Normalize(rawText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedNotice
            {
                Title = "粘贴一段通知后开始解析",
                SourceGroup = string.IsNullOrWhiteSpace(sourceGroup) ? "学院通知群" : sourceGroup.Trim()
            };
        }

        var cleaned = AtAllRegex.Replace(text, "").Trim();
        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var title = ExtractTitle(lines);
        var category = DetectCategory(cleaned);
        var dueAt = ExtractDueAt(cleaned, clock);
        var location = ExtractLocation(cleaned);
        var links = ExtractLinks(cleaned);
        var priority = DetectPriority(cleaned, dueAt, clock);
        var tags = ExtractTags(cleaned, category, priority);
        var group = string.IsNullOrWhiteSpace(sourceGroup) ? "学院通知群" : sourceGroup.Trim();
        var body = BuildBody(lines, cleaned);

        return new ParsedNotice
        {
            Title = title,
            Body = body,
            Category = category,
            DueAt = dueAt,
            Location = location,
            Links = links,
            Tags = tags,
            Priority = priority,
            SourceGroup = group,
            SourceText = text,
            SourceHash = ComputeSourceHash(group, text)
        };
    }

    public static string ComputeSourceHash(string sourceGroup, string sourceText)
    {
        var normalized = Regex.Replace(Normalize(sourceText), @"[ \t]+", " ");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceGroup.Trim()}\n{normalized}"));
        return Convert.ToHexString(bytes);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string ExtractTitle(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return "未命名通知";
        }

        var originalTitle = lines[0].Trim();
        var title = BracketPrefixRegex.Replace(NotificationPrefixRegex.Replace(originalTitle, ""), "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = originalTitle;
        }
        if (title is "通知" or "学院通知" or "群通知")
        {
            title = lines.Skip(1).FirstOrDefault() ?? title;
        }

        title = Regex.Replace(title, @"\s+", " ");
        if (title.Length > 46)
        {
            title = title[..46].TrimEnd('，', '。', '；', '：', ' ', '、') + "…";
        }

        return string.IsNullOrWhiteSpace(title) ? "未命名通知" : title;
    }

    private static string BuildBody(IReadOnlyList<string> lines, string cleaned)
    {
        if (lines.Count <= 1)
        {
            return cleaned;
        }

        var first = lines[0].Trim();
        var looksLikeTitle = first.Length <= 48
            && !Regex.IsMatch(first, "[。；：!?！？]$")
            && !Regex.IsMatch(first, "^(请|各位|大家|同学|为了|根据|现将|经研究|因)");

        return looksLikeTitle ? string.Join('\n', lines.Skip(1)) : cleaned;
    }

    private static string DetectCategory(string text)
    {
        if (Regex.IsMatch(text, "讲座|报告|宣讲|分享会"))
        {
            return "讲座";
        }

        if (Regex.IsMatch(text, "考试|测验|补考|期末|期中|考场"))
        {
            return "考试";
        }

        if (Regex.IsMatch(text, "报名|申请|征集|填报|登记"))
        {
            return "报名";
        }

        if (Regex.IsMatch(text, "会议|开会|例会|班会|座谈"))
        {
            return "会议";
        }

        if (Regex.IsMatch(text, "比赛|竞赛|挑战赛|大赛"))
        {
            return "竞赛";
        }

        if (Regex.IsMatch(text, "志愿|活动|晚会|演出|社团|实践"))
        {
            return "活动";
        }

        if (Regex.IsMatch(text, "选课|课程|教学|作业|论文|答辩"))
        {
            return "教学";
        }

        if (Regex.IsMatch(text, "招聘|实习|就业|宣讲会|岗位"))
        {
            return "就业";
        }

        return "通知";
    }

    private static DateTimeOffset? ExtractDueAt(string text, DateTimeOffset now)
    {
        var candidates = new List<DateCandidate>();
        var occupied = new HashSet<int>();

        void AddMatches(Regex regex, Func<Match, DateTime?> dateFactory, bool explicitYear)
        {
            foreach (Match match in regex.Matches(text))
            {
                if (!occupied.Add(match.Index))
                {
                    continue;
                }

                var date = dateFactory(match);
                if (date is null)
                {
                    continue;
                }

                candidates.Add(new DateCandidate(date.Value, match.Index, match.Length, explicitYear));
            }
        }

        AddMatches(DateYmdRegex, match =>
        {
            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            return TryCreateDate(year, month, day);
        }, true);

        AddMatches(DateMdRegex, match =>
        {
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var date = TryCreateDate(now.Year, month, day);
            if (date is not null && date.Value < now.Date.AddDays(-1))
            {
                date = TryCreateDate(now.Year + 1, month, day);
            }

            return date;
        }, false);

        AddMatches(DateSlashRegex, match =>
        {
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var date = TryCreateDate(now.Year, month, day);
            if (date is not null && date.Value < now.Date.AddDays(-1))
            {
                date = TryCreateDate(now.Year + 1, month, day);
            }

            return date;
        }, false);

        AddMatches(RelativeDateRegex, match => ResolveRelativeDate(match.Value, now), false);

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(candidate => ScoreCandidate(text, candidate))
            .ThenBy(candidate => candidate.Index)
            .First();

        var time = FindTimeNear(text, best.Index, best.Length);
        var hour = time?.Hour ?? 23;
        var minute = time?.Minute ?? 59;
        var local = new DateTime(best.Date.Year, best.Date.Month, best.Date.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static DateTime? ResolveRelativeDate(string value, DateTimeOffset now)
    {
        var today = now.Date;
        return value switch
        {
            "今天" => today,
            "明天" => today.AddDays(1),
            "后天" => today.AddDays(2),
            "大后天" => today.AddDays(3),
            _ => ResolveWeekday(value, today)
        };
    }

    private static DateTime? ResolveWeekday(string value, DateTime today)
    {
        var dayText = value[^1].ToString();
        var target = dayText switch
        {
            "一" => DayOfWeek.Monday,
            "二" => DayOfWeek.Tuesday,
            "三" => DayOfWeek.Wednesday,
            "四" => DayOfWeek.Thursday,
            "五" => DayOfWeek.Friday,
            "六" => DayOfWeek.Saturday,
            "日" or "天" => DayOfWeek.Sunday,
            _ => DayOfWeek.Monday
        };

        var delta = ((int)target - (int)today.DayOfWeek + 7) % 7;
        if (value.StartsWith("下周", StringComparison.Ordinal))
        {
            delta += 7;
        }
        else if (delta == 0 && !value.StartsWith("本周", StringComparison.Ordinal))
        {
            delta = 7;
        }

        return today.AddDays(delta);
    }

    private static DateTime? TryCreateDate(int year, int month, int day)
    {
        try
        {
            return new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int ScoreCandidate(string text, DateCandidate candidate)
    {
        var start = Math.Max(0, candidate.Index - 18);
        var end = Math.Min(text.Length, candidate.Index + candidate.Length + 12);
        var before = text[start..candidate.Index];
        var after = text[(candidate.Index + candidate.Length)..end];
        var score = candidate.ExplicitYear ? 2 : 1;

        if (Regex.IsMatch(before, "截止|前|DDL|ddl|提交|报名|时间|日期|之前|务必|开会|考试|答辩"))
        {
            score += 5;
        }

        if (Regex.IsMatch(after, "前|截止|之前|举行|召开|开始|提交"))
        {
            score += 2;
        }

        return score;
    }

    private static (int Hour, int Minute)? FindTimeNear(string text, int dateIndex, int dateLength)
    {
        var start = Math.Max(0, dateIndex - 4);
        var end = Math.Min(text.Length, dateIndex + dateLength + 32);
        var window = text[start..end];
        var match = TimeRegex.Match(window);
        if (!match.Success)
        {
            return null;
        }

        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minuteValue = match.Groups["minute"].Success ? match.Groups["minute"].Value : match.Groups["minute2"].Value;
        var minute = int.TryParse(minuteValue, out var parsedMinute) ? parsedMinute : 0;
        var period = match.Groups["period"].Value;

        if ((period is "下午" or "傍晚" or "晚上") && hour < 12)
        {
            hour += 12;
        }
        else if (period == "中午" && hour < 11)
        {
            hour += 12;
        }
        else if (period == "凌晨" && hour == 12)
        {
            hour = 0;
        }

        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59 ? (hour, minute) : null;
    }

    private static string? ExtractLocation(string text)
    {
        if (Regex.IsMatch(text, "腾讯会议|线上会议|线上|钉钉会议|Zoom|zoom"))
        {
            return "线上";
        }

        var match = LocationRegex.Match(text);
        if (match.Success)
        {
            return CleanLocation(match.Groups["value"].Value);
        }

        match = LocationInlineRegex.Match(text);
        return match.Success ? CleanLocation(match.Groups["value"].Value) : null;
    }

    private static string CleanLocation(string value) => value.Trim().TrimEnd('。', '，', '；', '：', '.', ',');

    private static List<string> ExtractLinks(string text)
    {
        return UrlRegex.Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '，', '。', '；', '：', '！', '？', ')', '）', ']', '】', '》', '」', '』'))
            .Where(link => link.Length > 8)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static List<string> ExtractTags(string text, string category, MemoPriority priority)
    {
        var tags = TagRegex.Matches(text)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (!tags.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            tags.Insert(0, category);
        }

        if (priority == MemoPriority.Urgent && !tags.Contains("紧急", StringComparer.OrdinalIgnoreCase))
        {
            tags.Add("紧急");
        }

        return tags.Take(6).ToList();
    }

    private static MemoPriority DetectPriority(string text, DateTimeOffset? dueAt, DateTimeOffset now)
    {
        if (dueAt is not null)
        {
            var hours = (dueAt.Value - now).TotalHours;
            if (hours is >= 0 and <= 24)
            {
                return MemoPriority.Urgent;
            }

            if (hours is >= 0 and <= 72)
            {
                return MemoPriority.Important;
            }
        }

        if (UrgentWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return MemoPriority.Urgent;
        }

        if (ImportantWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return MemoPriority.Important;
        }

        return MemoPriority.Normal;
    }

    private sealed record DateCandidate(DateTime Date, int Index, int Length, bool ExplicitYear);
}
