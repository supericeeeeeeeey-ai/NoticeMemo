using NoticeMemo.Core.Models;
using NoticeMemo.Core.Services;
using NoticeMemo.Core.Settings;
using NoticeMemo.Core.Storage;

var tests = new (string Name, Action Body)[]
{
    ("解析完整通知", ParseFullNotice),
    ("解析相对日期", ParseRelativeDate),
    ("无日期通知", ParseNoticeWithoutDate),
    ("来源哈希稳定", SourceHashIsStable),
    ("捕获过滤器识别高置信度通知", CaptureFilterAcceptsNotice),
    ("捕获过滤器忽略普通文本", CaptureFilterRejectsOrdinaryText),
    ("来源群聊名称匹配", SourceGroupMatching),
    ("提醒触发窗口与去重", ReminderPolicyWindow),
    ("设置持久化与规范化", SettingsRoundTrip),
    ("SQLite 增删改查", SqliteRoundTrip),
    ("JSON 存储回退", JsonRoundTrip),
    ("Markdown 导出", MarkdownExport)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL  {test.Name}");
        Console.WriteLine($"      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine(failed == 0 ? $"全部通过：{tests.Length} 项" : $"失败：{failed} / {tests.Length} 项");
return failed == 0 ? 0 : 1;

static void ParseFullNotice()
{
    const string text = """
        @全体成员
        【教务通知】关于2026年秋季期中考试安排的通知
        各位同学：
        本学期期中考试定于2026年11月5日（周四）下午2:30在教学楼A302举行。
        请务必于11月3日17:00前完成报名确认，逾期不再受理。
        详情：https://example.edu.cn/notice/123
        #考试 #报名
        """;

    var now = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8));
    var parsed = NoticeParser.Parse(text, "学院通知群", now);

    AssertEqual("关于2026年秋季期中考试安排的通知", parsed.Title);
    AssertEqual("考试", parsed.Category);
    AssertEqual(new DateTime(2026, 11, 3, 17, 0, 0), parsed.DueAt?.LocalDateTime);
    AssertEqual("教学楼A302", parsed.Location);
    AssertTrue(parsed.Links.Single() == "https://example.edu.cn/notice/123");
    AssertTrue(parsed.Tags.Contains("考试"));
    AssertTrue(parsed.Tags.Contains("报名"));
    AssertEqual(MemoPriority.Important, parsed.Priority);
}

static void ParseRelativeDate()
{
    var now = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8));
    var parsed = NoticeParser.Parse("请于明天下午5点前提交材料\n联系老师：张老师", "学院通知群", now);

    AssertEqual(new DateTime(2026, 11, 2, 17, 0, 0), parsed.DueAt?.LocalDateTime);
    AssertEqual("请于明天下午5点前提交材料", parsed.Title);
}

static void ParseNoticeWithoutDate()
{
    var parsed = NoticeParser.Parse("请大家查看群文件中的课程安排。", "学院通知群");
    AssertTrue(parsed.DueAt is null);
    AssertEqual("教学", parsed.Category);
}

static void SourceHashIsStable()
{
    const string first = "关于报名通知\r\n截止：11月3日";
    const string second = "关于报名通知\n截止：11月3日";
    AssertEqual(
        NoticeParser.ComputeSourceHash("学院通知群", first),
        NoticeParser.ComputeSourceHash("学院通知群", second));
}

static void CaptureFilterAcceptsNotice()
{
    const string text = """
        @全体成员
        【教务通知】关于2026年秋季期中考试安排的通知
        各位同学：
        本学期期中考试定于2026年11月5日（周四）下午2:30在教学楼A302举行。
        请务必于11月3日17:00前完成报名确认，逾期不再受理。
        详情：https://example.edu.cn/notice/123
        """;
    var now = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8));
    var assessment = NoticeCaptureFilter.Evaluate(text, now);

    AssertTrue(assessment.IsNotice);
    AssertEqual(CaptureConfidence.High, assessment.Confidence);
    AssertTrue(assessment.Signals.Contains("时间"));
}

static void CaptureFilterRejectsOrdinaryText()
{
    const string chat = "今晚一起吃饭吗？我大概六点半到学校门口等你。";
    var chatAssessment = NoticeCaptureFilter.Evaluate(chat);
    AssertTrue(!chatAssessment.IsNotice);

    const string code = """
        public class Example
        {
            private const string Message = "通知";
            public void Run() { Console.WriteLine(Message); }
        }
        """;
    var codeAssessment = NoticeCaptureFilter.Evaluate(code);
    AssertTrue(!codeAssessment.IsNotice);
}

static void SourceGroupMatching()
{
    AssertTrue(SourceGroupMatcher.IsMatch("XX学院 2026 级通知群", "学院通知群"));
    AssertTrue(SourceGroupMatcher.IsMatch("学院通知群", "学院通知群"));
    AssertTrue(!SourceGroupMatcher.IsMatch("网安2603聊天群", "学院通知群"));
    AssertTrue(!SourceGroupMatcher.IsMatch("", "学院通知群"));
}

static void ReminderPolicyWindow()
{
    var now = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(8));
    var memo = new Memo
    {
        Status = MemoStatus.Inbox,
        DueAt = now.AddMinutes(20)
    };

    var upcoming = ReminderPolicy.Evaluate(memo, 30, now);
    AssertTrue(upcoming.ShouldRemind);
    AssertTrue(!upcoming.IsOverdue);

    AssertTrue(!ReminderPolicy.Evaluate(memo, 10, now).ShouldRemind);

    memo.DueAt = now.AddMinutes(-5);
    var overdue = ReminderPolicy.Evaluate(memo, 30, now);
    AssertTrue(overdue.ShouldRemind);
    AssertTrue(overdue.IsOverdue);

    memo.ReminderSentAt = now;
    AssertTrue(!ReminderPolicy.Evaluate(memo, 30, now).ShouldRemind);

    memo.ReminderSentAt = null;
    memo.Status = MemoStatus.Done;
    AssertTrue(!ReminderPolicy.Evaluate(memo, 30, now).ShouldRemind);
}

static void SettingsRoundTrip()
{
    var directory = CreateTempDirectory();
    try
    {
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings
        {
            ClipboardMonitoringEnabled = false,
            QqNotificationMonitoringEnabled = false,
            AutoSaveHighConfidence = false,
            ShowTrayNotifications = false,
            RemindersEnabled = false,
            ReminderLeadMinutes = 45,
            StartWithWindows = true,
            TrayHintShown = true,
            SourceGroup = "  学院通知群  "
        });

        var loaded = store.Load();
        AssertEqual("学院通知群", loaded.SourceGroup);
        AssertTrue(!loaded.ClipboardMonitoringEnabled);
        AssertTrue(!loaded.QqNotificationMonitoringEnabled);
        AssertTrue(!loaded.AutoSaveHighConfidence);
        AssertTrue(!loaded.ShowTrayNotifications);
        AssertTrue(!loaded.RemindersEnabled);
        AssertEqual(45, loaded.ReminderLeadMinutes);
        AssertTrue(loaded.StartWithWindows);
        AssertTrue(loaded.TrayHintShown);
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void SqliteRoundTrip()
{
    var directory = CreateTempDirectory();
    try
    {
        var databasePath = Path.Combine(directory, "test.db");
        using var store = new SqliteMemoStore(databasePath);
        var memo = CreateMemo();
        store.Upsert(memo);

        var loaded = store.Get(memo.Id) ?? throw new InvalidOperationException("未读回备忘录");
        AssertEqual(memo.Title, loaded.Title);
        AssertEqual(memo.SourceHash, loaded.SourceHash);
        AssertEqual(2, loaded.Links.Count);
        AssertEqual(2, loaded.Tags.Count);
        AssertEqual(memo.DueAt?.ToUnixTimeSeconds(), loaded.DueAt?.ToUnixTimeSeconds());
        loaded.ReminderSentAt = new DateTimeOffset(2026, 11, 3, 16, 30, 0, TimeSpan.FromHours(8));
        store.Upsert(loaded);
        AssertEqual(loaded.ReminderSentAt?.ToUnixTimeSeconds(), store.Get(memo.Id)?.ReminderSentAt?.ToUnixTimeSeconds());

        loaded.Status = MemoStatus.Done;
        loaded.CompletedAt = DateTimeOffset.Now;
        store.Upsert(loaded);
        AssertEqual(MemoStatus.Done, store.Get(memo.Id)?.Status);

        store.Delete(memo.Id);
        AssertTrue(store.Get(memo.Id) is null);
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void JsonRoundTrip()
{
    var directory = CreateTempDirectory();
    try
    {
        var path = Path.Combine(directory, "test.json");
        using (var store = new JsonMemoStore(path))
        {
            store.Upsert(CreateMemo());
            AssertEqual(1, store.GetAll().Count);
        }

        using (var reopened = new JsonMemoStore(path))
        {
            AssertEqual("关于期中考试安排的通知", reopened.GetAll().Single().Title);
        }
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void MarkdownExport()
{
    var directory = CreateTempDirectory();
    try
    {
        var result = MarkdownExporter.ExportAll([CreateMemo()], directory);
        AssertEqual(1, result.Count);
        AssertTrue(File.Exists(Path.Combine(directory, "index.md")));
        AssertEqual(2, Directory.GetFiles(directory, "*.md").Length);

        var exported = File.ReadAllText(result.DirectoryPath == directory
            ? Directory.GetFiles(directory, "2026-*.md").Single()
            : throw new InvalidOperationException("导出目录不正确"));
        AssertTrue(exported.Contains("# 关于期中考试安排的通知", StringComparison.Ordinal));
        AssertTrue(exported.Contains("https://example.edu.cn/notice/123", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static Memo CreateMemo() => new()
{
    Title = "关于期中考试安排的通知",
    Body = "期中考试定于11月5日举行，请于11月3日17:00前完成报名。",
    Category = "考试",
    DueAt = new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.FromHours(8)),
    Location = "教学楼A302",
    Links = ["https://example.edu.cn/notice/123", "https://example.edu.cn/file.pdf"],
    Tags = ["考试", "报名"],
    Priority = MemoPriority.Important,
    Status = MemoStatus.Inbox,
    SourceGroup = "学院通知群",
    SourceText = "测试通知原文",
    SourceHash = "ABC123",
    CreatedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8)),
    UpdatedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8))
};

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "NoticeMemoTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void AssertTrue(bool condition, string? message = null)
{
    if (!condition)
    {
        throw new InvalidOperationException(message ?? "断言失败");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"期望：{expected}；实际：{actual}");
    }
}
