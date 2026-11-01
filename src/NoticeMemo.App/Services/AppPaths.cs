namespace NoticeMemo.App.Services;

public static class AppPaths
{
    private static string? _dataDirectoryOverride;

    public static string DataDirectory => _dataDirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NoticeMemo");

    public static void SetDataDirectoryOverride(string? path)
    {
        _dataDirectoryOverride = string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path);
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string DemoDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NoticeMemo",
        "demo");

    public static string ExportDirectory
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents)
                ? Path.Combine(DataDirectory, "Exports")
                : Path.Combine(documents, "NoticeMemo", "Exports");
        }
    }
}
