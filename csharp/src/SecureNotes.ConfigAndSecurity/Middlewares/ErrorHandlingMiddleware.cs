using System.Text.Json;
using SecureNotesApi.Config;
using SecureNotesApi.Domain;

namespace SecureNotesApi.Middlewares;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly AppOptions _options;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        AppOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var requestId = context.Items["RequestId"]?.ToString() ?? "unknown";
        
        var (statusCode, errorCode, message, details) = exception switch
        {
            ArgumentException argEx => (
                400,
                "validation_error",
                GetUserFriendlyMessage("Некорректные данные"),
                _options.Mode == AppMode.Development ? argEx.Message : null
            ),
            
            InvalidOperationException opEx => (
                409,
                "conflict",
                GetUserFriendlyMessage("Конфликт данных"),
                _options.Mode == AppMode.Development ? opEx.Message : null
            ),
            
            _ => (
                500,
                "internal_error",
                GetUserFriendlyMessage("Внутренняя ошибка сервера"),
                _options.Mode == AppMode.Development ? exception.Message : null
            )
        };

        // Логирование с разным уровнем детализации
        if (statusCode == 500)
        {
            _logger.LogError(exception, 
                "Ошибка обработки запроса. RequestId: {RequestId}, Path: {Path}", 
                requestId, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                "Клиентская ошибка {ErrorCode}. RequestId: {RequestId}, Details: {Details}",
                errorCode, requestId, details);
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        
        var response = new ErrorResponse(errorCode, message, details, requestId);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }

    private string GetUserFriendlyMessage(string defaultMessage)
    {
        return _options.Mode == AppMode.Development 
            ? defaultMessage 
            : "Произошла ошибка при обработке запроса";
    }
}