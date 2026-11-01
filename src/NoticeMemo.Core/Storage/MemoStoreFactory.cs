namespace NoticeMemo.Core.Storage;

public static class MemoStoreFactory
{
    public static IMemoStore Create(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "noticememo.db");

        try
        {
            return new SqliteMemoStore(databasePath);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new JsonMemoStore(Path.Combine(dataDirectory, "noticememo.json"));
        }
    }
}
