namespace NoticeMemo.Core.Settings;

public sealed class AppSettings
{
    public bool ClipboardMonitoringEnabled { get; set; } = true;

    public bool QqNotificationMonitoringEnabled { get; set; } = true;

    public bool AutoSaveHighConfidence { get; set; } = true;

    public bool ShowTrayNotifications { get; set; } = true;

    public bool RemindersEnabled { get; set; } = true;

    public int ReminderLeadMinutes { get; set; } = 30;

    public bool StartWithWindows { get; set; }

    public bool TrayHintShown { get; set; }

    public string SourceGroup { get; set; } = "学院通知群";

    public AppSettings Clone() => new()
    {
        ClipboardMonitoringEnabled = ClipboardMonitoringEnabled,
        QqNotificationMonitoringEnabled = QqNotificationMonitoringEnabled,
        AutoSaveHighConfidence = AutoSaveHighConfidence,
        ShowTrayNotifications = ShowTrayNotifications,
        RemindersEnabled = RemindersEnabled,
        ReminderLeadMinutes = ReminderLeadMinutes,
        StartWithWindows = StartWithWindows,
        TrayHintShown = TrayHintShown,
        SourceGroup = SourceGroup
    };

    public void Normalize()
    {
        SourceGroup = string.IsNullOrWhiteSpace(SourceGroup)
            ? "学院通知群"
            : SourceGroup.Trim();

        if (SourceGroup.Length > 60)
        {
            SourceGroup = SourceGroup[..60];
        }

        ReminderLeadMinutes = Math.Clamp(ReminderLeadMinutes, 0, 7 * 24 * 60);
    }
}
