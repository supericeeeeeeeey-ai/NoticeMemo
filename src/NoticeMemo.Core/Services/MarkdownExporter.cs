using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NoticeMemo.Core.Models;

namespace NoticeMemo.Core.Services;

public static partial class MarkdownExporter
{
    public static string Render(Memo memo)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"id: {memo.Id}");
        builder.AppendLine($"title: {Quote(memo.Title)}");
        builder.AppendLine($"category: {Quote(memo.Category)}");
        builder.AppendLine($"status: {memo.Status.ToString().ToLowerInvariant()}");
        builder.AppendLine($"priority: {memo.Priority.ToString().ToLowerInvariant()}");
        builder.AppendLine($"due: {memo.DueAt?.ToString("O", CultureInfo.InvariantCulture) ?? "null"}");
        builder.AppendLine($"source: {Quote(memo.SourceGroup)}");
        builder.AppendLine($"created: {memo.CreatedAt.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"tags: [{string.Join(", ", memo.Tags.Select(Quote))}]");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {memo.Title}");
        builder.AppendLine();
        builder.AppendLine($"> 截止：{memo.DueLabel}");
        if (!string.IsNullOrWhiteSpace(memo.Location))
        {
            builder.AppendLine($"> 地点：{memo.Location}");
        }

        builder.AppendLine($"> 来源：{memo.SourceGroup}");
        if (memo.Links.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## 链接");
            foreach (var link in memo.Links)
            {
                builder.AppendLine($"- {link}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 通知原文");
        builder.AppendLine();
        builder.AppendLine(memo.Body);
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine($"由 NoticeMemo 整理 · {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        return builder.ToString();
    }

    public static ExportResult ExportAll(IEnumerable<Memo> memos, string directory)
    {
        Directory.CreateDirectory(directory);
        var list = memos.ToList();
        var index = new StringBuilder();
        index.AppendLine("# NoticeMemo 导出索引");
        index.AppendLine();
        index.AppendLine($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        index.AppendLine();

        foreach (var memo in list)
        {
            var fileName = BuildFileName(memo);
            File.WriteAllText(Path.Combine(directory, fileName), Render(memo), new UTF8Encoding(false));
            index.AppendLine($"- [{memo.Title}]({fileName}) · {memo.DueLabel} · {memo.StatusLabel}");
        }

        File.WriteAllText(Path.Combine(directory, "index.md"), index.ToString(), new UTF8Encoding(false));
        return new ExportResult(directory, list.Count);
    }

    private static string BuildFileName(Memo memo)
    {
        var slug = Regex.Replace(memo.Title.Trim(), @"[^\p{L}\p{N}\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "notice";
        }

        if (slug.Length > 36)
        {
            slug = slug[..36].TrimEnd('-');
        }

        return $"{memo.CreatedAt.ToLocalTime():yyyy-MM-dd}_{slug}_{memo.Id[..6]}.md";
    }

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}

public sealed record ExportResult(string DirectoryPath, int Count);
