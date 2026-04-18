using System.Threading.RateLimiting;
// using Microsoft.AspNetCore.RateLimiting;
using SecureNotesApi.Config;
using SecureNotesApi.Domain;
using SecureNotesApi.Middlewares;
using SecureNotesApi.Services;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SecureNotes.ConfigAndSecurity.Tests")]

// Настройка маппинга аргументов командной строки
var switchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["--mode"] = "App:Mode",
    ["-m"] = "App:Mode",
    ["--origins"] = "App:AllowedOrigins",
    ["-o"] = "App:AllowedOrigins",
    ["--rate"] = "App:RateLimit:RequestsPerMinute",
    ["-r"] = "App:RateLimit:RequestsPerMinute",
    ["--burst"] = "App:RateLimit:BurstLimit",
    ["-b"] = "App:RateLimit:BurstLimit",
    ["--max-length"] = "App:MaxNoteLength",
    ["-l"] = "App:MaxNoteLength"
};

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables("NOTES_API_")
    .AddCommandLine(args, switchMappings);

var appOptions = new AppOptions();
builder.Configuration.GetSection("App").Bind(appOptions);

var validation = AppOptionsValidator.Validate(appOptions);

// Вывод предупреждений
foreach (var warning in validation.Warnings)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(warning);
    Console.ResetColor();
}

// Остановка при ошибках
if (!validation.IsValid)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("\n🚫 ЗАПУСК ПРИЛОЖЕНИЯ ОСТАНОВЛЕН!");
    Console.Error.WriteLine("Обнаружены критические ошибки в конфигурации:\n");
    
    foreach (var error in validation.Errors)
    {
        Console.Error.WriteLine($"  {error}");
    }
    
    Console.Error.WriteLine("\nИсправьте ошибки и попробуйте снова.\n");
    Console.ResetColor();
    
    Environment.Exit(1);
    return;
}

// Регистрация сервисов
builder.Services.AddSingleton(appOptions);
builder.Services.AddSingleton<INoteRepository, InMemoryNoteRepository>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddCors(options =>
{
    options.AddPolicy("StrictOrigins", policy =>
    {
        policy.WithOrigins(appOptions.AllowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("X-Request-ID", "X-RateLimit-Remaining");
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetTokenBucketLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = appOptions.RateLimit.BurstLimit,
                TokensPerPeriod = appOptions.RateLimit.RequestsPerMinute,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = appOptions.Mode == AppMode.Development ? 10 : 0
            }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        
        var response = new
        {
            error = "rate_limit_exceeded",
            message = appOptions.Mode == AppMode.Development 
                ? "Превышен лимит запросов. Попробуйте через минуту." 
                : "Too Many Requests"
        };
        
        await context.HttpContext.Response.WriteAsJsonAsync(response, token);
    };
});

var app = builder.Build();

// Подключение Middleware в правильном порядке
app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors("StrictOrigins");
app.UseRateLimiter();
app.UseMiddleware<ErrorHandlingMiddleware>();

// Эндпоинты
app.MapGet("/api/health", () => new
{
    status = "healthy",
    mode = appOptions.Mode.ToString(),
    timestamp = DateTime.UtcNow,
    version = "1.0.0"
});

app.MapGet("/api/notes", (INoteRepository repo) =>
{
    return Results.Ok(new
    {
        count = repo.Count,
        items = repo.GetAll()
    });
});

app.MapGet("/api/notes/{id:guid}", (Guid id, INoteRepository repo) =>
{
    var note = repo.GetById(id);
    return note is null 
        ? Results.NotFound(new { error = "note_not_found", message = "Заметка не найдена" })
        : Results.Ok(note);
});

app.MapPost("/api/notes", async (CreateNoteRequest request, INoteRepository repo, AppOptions opts) =>
{
    // Валидация
    if (string.IsNullOrWhiteSpace(request.Title))
        throw new ArgumentException("Заголовок не может быть пустым");
        
    if (string.IsNullOrWhiteSpace(request.Content))
        throw new ArgumentException("Содержание не может быть пустым");
        
    if (request.Title.Length > 100)
        throw new ArgumentException($"Заголовок превышает 100 символов");
        
    if (request.Content.Length > opts.MaxNoteLength)
        throw new ArgumentException($"Содержание превышает {opts.MaxNoteLength} символов");

    var note = repo.Create(request.Title, request.Content);
    
    return Results.Created($"/api/notes/{note.Id}", note);
});

app.MapPut("/api/notes/{id:guid}", async (Guid id, CreateNoteRequest request, INoteRepository repo, AppOptions opts) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
        throw new ArgumentException("Заголовок не может быть пустым");
        
    if (string.IsNullOrWhiteSpace(request.Content))
        throw new ArgumentException("Содержание не может быть пустым");
        
    if (request.Content.Length > opts.MaxNoteLength)
        throw new ArgumentException($"Содержание превышает {opts.MaxNoteLength} символов");

    var success = repo.Update(id, request.Title, request.Content);
    
    return success 
        ? Results.Ok(repo.GetById(id))
        : Results.NotFound(new { error = "note_not_found", message = "Заметка не найдена" });
});

app.MapDelete("/api/notes/{id:guid}", (Guid id, INoteRepository repo) =>
{
    var success = repo.Delete(id);
    return success 
        ? Results.NoContent()
        : Results.NotFound(new { error = "note_not_found", message = "Заметка не найдена" });
});

app.MapGet("/api/config/info", (AppOptions opts) => 
{
    return Results.Ok(new
    {
        mode = opts.Mode.ToString(),
        maxNoteLength = opts.MaxNoteLength,
        rateLimits = new
        {
            perMinute = opts.RateLimit.RequestsPerMinute,
            burst = opts.RateLimit.BurstLimit
        },
        // Не показываем AllowedOrigins в целях безопасности
        features = new
        {
            detailedErrors = opts.Mode == AppMode.Development,
            requestLogging = opts.EnableRequestLogging
        }
    });
}).RequireAuthorization(); 

app.Run();

// Делаем Program видимым для тестов
public partial class Program 
{ 
    // Пустой partial класс нужен для WebApplicationFactory
}