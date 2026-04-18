using System.Security.Cryptography;

namespace SecureNotesApi.Middlewares;

public class RequestIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string RequestIdHeader = "X-Request-ID";

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Генерируем короткий ID (8 символов) вместо длинного GUID
        var requestId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        
        context.Items["RequestId"] = requestId;
        context.Response.Headers[RequestIdHeader] = requestId;

        await _next(context);
    }
}