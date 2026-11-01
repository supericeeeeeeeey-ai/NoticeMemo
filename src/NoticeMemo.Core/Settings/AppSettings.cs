namespace NoticeMemo.Core.Settings;

public sealed class AppSettings
{
    public bool ClipboardMonitoringEnabled { get; set; } = true;

    public bool AutoSaveHighConfidence { get; set; } = true;

    public bool ShowTrayNotifications { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool TrayHintShown { get; set; }

    public string SourceGroup { get; set; } = "学院通知群";

    public AppSettings Clone() => new()
    {
        ClipboardMonitoringEnabled = ClipboardMonitoringEnabled,
        AutoSaveHighConfidence = AutoSaveHighConfidence,
        ShowTrayNotifications = ShowTrayNotifications,
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
    }
}
