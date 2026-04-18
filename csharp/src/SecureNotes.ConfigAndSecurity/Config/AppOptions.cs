namespace SecureNotesApi.Config;

public class AppOptions
{
    public AppMode Mode { get; set; } = AppMode.Development;
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public RateLimitOptions RateLimit { get; set; } = new();
    public bool EnableRequestLogging { get; set; } = true;
    public int MaxNoteLength { get; set; } = 1000;
}