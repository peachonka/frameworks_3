using System.Collections.Concurrent;
using SecureNotesApi.Domain;

namespace SecureNotesApi.Services;

public class InMemoryNoteRepository : INoteRepository
{
    private readonly ConcurrentDictionary<Guid, Note> _notes = new();
    
    public int Count => _notes.Count;

    public IEnumerable<Note> GetAll()
    {
        return _notes.Values
            .OrderByDescending(n => n.CreatedAt)
            .ToList();
    }

    public Note? GetById(Guid id)
    {
        return _notes.TryGetValue(id, out var note) ? note : null;
    }

    public Note Create(string title, string content)
    {
        var note = new Note(
            Guid.NewGuid(),
            title.Trim(),
            content.Trim(),
            DateTime.UtcNow
        );
        
        _notes[note.Id] = note;
        return note;
    }

    public bool Update(Guid id, string title, string content)
    {
        if (!_notes.TryGetValue(id, out var existingNote))
            return false;

        var updatedNote = existingNote with
        {
            Title = title.Trim(),
            Content = content.Trim(),
            UpdatedAt = DateTime.UtcNow
        };

        return _notes.TryUpdate(id, updatedNote, existingNote);
    }

    public bool Delete(Guid id)
    {
        return _notes.TryRemove(id, out _);
    }
}