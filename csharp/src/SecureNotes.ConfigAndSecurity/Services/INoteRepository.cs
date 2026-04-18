using SecureNotesApi.Domain;

namespace SecureNotesApi.Services;

public interface INoteRepository
{
    IEnumerable<Note> GetAll();
    Note? GetById(Guid id);
    Note Create(string title, string content);
    bool Update(Guid id, string title, string content);
    bool Delete(Guid id);
    int Count { get; }
}