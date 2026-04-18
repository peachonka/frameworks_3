namespace SecureNotesApi.Domain;

public record ErrorResponse(
    string ErrorCode,
    string Message,
    string? Details = null,
    string? RequestId = null
);