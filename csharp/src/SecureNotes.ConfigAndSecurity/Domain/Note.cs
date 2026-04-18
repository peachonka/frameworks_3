namespace SecureNotesApi.Domain;

public record Note(
    Guid Id,
    string Title,
    string Content,
    DateTime CreatedAt,
    DateTime? UpdatedAt = null
);