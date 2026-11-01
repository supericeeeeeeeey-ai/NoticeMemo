using System.Text.RegularExpressions;

namespace NoticeMemo.Core.Services;

public static class SourceGroupMatcher
{
    private static readonly Regex GradeYearRegex = new(@"\d{2,4}\s*级", RegexOptions.Compiled);

    public static bool IsMatch(string? detectedGroup, string? configuredGroup)
    {
        var detected = Normalize(detectedGroup);
        var configured = Normalize(configuredGroup);
        if (detected.Length == 0 || configured.Length == 0)
        {
            return false;
        }

        return detected.Contains(configured, StringComparison.OrdinalIgnoreCase) ||
               configured.Contains(detected, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var compact = Regex.Replace(value, @"\s+", "").Trim();
        return GradeYearRegex.Replace(compact, "");
    }
}
