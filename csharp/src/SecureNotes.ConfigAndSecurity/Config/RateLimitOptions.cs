namespace SecureNotesApi.Config;

public class RateLimitOptions
{
    public int RequestsPerMinute { get; set; } = 60;
    public int BurstLimit { get; set; } = 10;
}