using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoticeMemo.Core.Models;

namespace NoticeMemo.Core.Storage;

public sealed class JsonMemoStore : IMemoStore
{
    private readonly object _gate = new();
    private readonly List<Memo> _memos;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonMemoStore(string filePath)
    {
        Location = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _memos = Load(filePath);
    }

    public string Location { get; }

    public string BackendName => "JSON";

    public IReadOnlyList<Memo> GetAll()
    {
        lock (_gate)
        {
            return _memos
                .OrderBy(memo => memo.Status == MemoStatus.Done ? 1 : 0)
                .ThenBy(memo => memo.DueAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(memo => memo.CreatedAt)
                .ToList();
        }
    }

    public Memo? Get(string id)
    {
        lock (_gate)
        {
            return _memos.FirstOrDefault(memo => memo.Id == id);
        }
    }

    public void Upsert(Memo memo)
    {
        lock (_gate)
        {
            var index = _memos.FindIndex(existing => existing.Id == memo.Id);
            if (index >= 0)
            {
                _memos[index] = memo;
            }
            else
            {
                _memos.Add(memo);
            }

            Save();
        }
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            _memos.RemoveAll(memo => memo.Id == id);
            Save();
        }
    }

    public void Dispose()
    {
    }

    private static List<Memo> Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<Memo>>(json, JsonOptions) ?? [];
        }
        catch (Exception)
        {
            var backupPath = $"{filePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(filePath, backupPath, true);
            return [];
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_memos, JsonOptions);
        var temporaryPath = $"{Location}.tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, Location, true);
    }
}
