using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SecureNotes.ConfigAndSecurity.Tests;

public sealed class IntegrationSecurityTests
{
    [Fact]
    public async Task Доверенный_источник_получает_разрешающий_заголовок()
    {
        var factory = CreateFactory(trustedOrigin: "http://localhost:5173", readLimit: 100, writeLimit: 100);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/items");
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Contains("http://localhost:5173", values);
    }

    [Fact]
    public async Task Недоверенный_источник_не_получает_разрешающий_заголовок()
    {
        var factory = CreateFactory(trustedOrigin: "http://localhost:5173", readLimit: 100, writeLimit: 100);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/items");
        request.Headers.TryAddWithoutValidation("Origin", "http://evil.local");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Ограничитель_частоты_возвращает_429()
    {
        var factory = CreateFactory(trustedOrigin: "http://localhost:5173", readLimit: 2, writeLimit: 1);
        var client = factory.CreateClient();

        async Task<HttpStatusCode> Call()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/items");
            request.Headers.TryAddWithoutValidation("Origin", "http://localhost:5173");
            var resp = await client.SendAsync(request);
            return resp.StatusCode;
        }

        var a = await Call();
        var b = await Call();
        var c = await Call();

        Assert.Equal(HttpStatusCode.OK, a);
        Assert.Equal(HttpStatusCode.OK, b);
        Assert.Equal((HttpStatusCode)429, c);
    }

    [Fact]
    public async Task Защитные_заголовки_присутствуют()
    {
        var factory = CreateFactory(trustedOrigin: "http://localhost:5173", readLimit: 100, writeLimit: 100);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/items");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Cache-Control"));
    }

    private static WebApplicationFactory<Program> CreateFactory(string trustedOrigin, int readLimit, int writeLimit)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.Sources.Clear();

                    var settings = new Dictionary<string, string?>
                    {
                        ["App:Mode"] = "Учебный",
                        ["App:TrustedOrigins:0"] = trustedOrigin,
                        ["App:RateLimits:ReadPerMinute"] = readLimit.ToString(),
                        ["App:RateLimits:WritePerMinute"] = writeLimit.ToString()
                    };

                    cfg.AddInMemoryCollection(settings);
                });
            });
    }
}
