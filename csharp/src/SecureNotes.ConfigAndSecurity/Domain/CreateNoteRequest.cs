namespace SecureNotesApi.Domain;

public record CreateNoteRequest(
    string Title,
    string Content
);