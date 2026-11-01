using NoticeMemo.Core.Models;

namespace NoticeMemo.Core.Storage;

public interface IMemoStore : IDisposable
{
    string Location { get; }

    string BackendName { get; }

    IReadOnlyList<Memo> GetAll();

    Memo? Get(string id);

    void Upsert(Memo memo);

    void Delete(string id);
}
