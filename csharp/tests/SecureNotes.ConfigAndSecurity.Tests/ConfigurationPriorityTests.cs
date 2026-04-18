using Microsoft.Extensions.Configuration;
using SecureNotesApi.Config;
using Xunit;

namespace SecureNotes.ConfigAndSecurity.Tests;

public sealed class ConfigurationPriorityTests
{
    [Fact]
    public void Приоритет_источников_настроек_работает_корректно()
    {
        // Arrange - имитируем три источника с разными значениями
        var jsonSettings = new Dictionary<string, string?>
        {
            ["App:RateLimit:RequestsPerMinute"] = "10",
            ["App:Mode"] = "Development"
        };

        var envSettings = new Dictionary<string, string?>
        {
            ["App:RateLimit:RequestsPerMinute"] = "20"
        };

        var commandLineSettings = new Dictionary<string, string?>
        {
            ["App:RateLimit:RequestsPerMinute"] = "30"
        };

        // Act - строим конфигурацию в порядке приоритета
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonSettings)      // 1. JSON файл
            .AddInMemoryCollection(envSettings)       // 2. Переменные окружения
            .AddInMemoryCollection(commandLineSettings) // 3. Командная строка (высший приоритет)
            .Build();

        var options = new AppOptions();
        configuration.GetSection("App").Bind(options);

        // Assert - должно быть значение из командной строки
        Assert.Equal(30, options.RateLimit.RequestsPerMinute);
        Assert.Equal(AppMode.Development, options.Mode); // Не переопределялось
    }

    [Fact]
    public void Переменная_окружения_переопределяет_JSON_файл()
    {
        // Arrange
        var jsonSettings = new Dictionary<string, string?>
        {
            ["App:Mode"] = "Development",
            ["App:MaxNoteLength"] = "500"
        };

        var envSettings = new Dictionary<string, string?>
        {
            ["App:Mode"] = "Production",
            ["App:MaxNoteLength"] = "2000"
        };

        // Act
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonSettings)
            .AddInMemoryCollection(envSettings)
            .Build();

        var options = new AppOptions();
        configuration.GetSection("App").Bind(options);

        // Assert - значения из env переопределили JSON
        Assert.Equal(AppMode.Production, options.Mode);
        Assert.Equal(2000, options.MaxNoteLength);
    }

    [Fact]
    public void Командная_строка_имеет_высший_приоритет()
    {
        // Arrange
        var jsonSettings = new Dictionary<string, string?>
        {
            ["App:RateLimit:BurstLimit"] = "10"
        };

        var envSettings = new Dictionary<string, string?>
        {
            ["App:RateLimit:BurstLimit"] = "20"
        };

        var cmdSettings = new Dictionary<string, string?>
        {
            ["App:RateLimit:BurstLimit"] = "50"
        };

        // Act
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonSettings)
            .AddInMemoryCollection(envSettings)
            .AddInMemoryCollection(cmdSettings)
            .Build();

        var options = new AppOptions();
        configuration.GetSection("App").Bind(options);

        // Assert
        Assert.Equal(50, options.RateLimit.BurstLimit);
    }

    [Fact]
    public void Некорректные_настройки_вызывают_ошибки_валидации()
    {
        // Arrange
        var invalidOptions = new AppOptions
        {
            AllowedOrigins = Array.Empty<string>(), // Пустой список источников
            RateLimit = new RateLimitOptions 
            { 
                RequestsPerMinute = 0,  // Нулевой лимит
                BurstLimit = 0          // Нулевой burst
            },
            MaxNoteLength = 5 // Слишком маленькая длина
        };

        // Act
        var validationResult = AppOptionsValidator.Validate(invalidOptions);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.True(validationResult.Errors.Count >= 3);
        
        // Проверяем конкретные ошибки
        Assert.Contains(validationResult.Errors, 
            e => e.Contains("AllowedOrigins", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validationResult.Errors, 
            e => e.Contains("RequestsPerMinute", StringComparison.OrdinalIgnoreCase) && 
                 e.Contains("> 0"));
        Assert.Contains(validationResult.Errors, 
            e => e.Contains("MaxNoteLength", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BurstLimit_не_может_превышать_RequestsPerMinute()
    {
        // Arrange
        var invalidOptions = new AppOptions
        {
            AllowedOrigins = new[] { "http://localhost:3000" },
            RateLimit = new RateLimitOptions
            {
                RequestsPerMinute = 50,
                BurstLimit = 100 // Больше чем RequestsPerMinute
            },
            MaxNoteLength = 1000
        };

        // Act
        var validationResult = AppOptionsValidator.Validate(invalidOptions);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.Errors, 
            e => e.Contains("BurstLimit", StringComparison.OrdinalIgnoreCase) && 
                 e.Contains("превышать", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Предупреждения_не_блокируют_запуск()
    {
        // Arrange
        var optionsWithWarnings = new AppOptions
        {
            Mode = AppMode.Production,
            AllowedOrigins = new[] { "http://localhost:3000" }, // HTTP в Production
            RateLimit = new RateLimitOptions
            {
                RequestsPerMinute = 2000, // Высокий лимит
                BurstLimit = 100
            },
            MaxNoteLength = 1000,
            EnableRequestLogging = true // Логирование в Production
        };

        // Act
        var validationResult = AppOptionsValidator.Validate(optionsWithWarnings);

        // Assert
        Assert.True(validationResult.IsValid); // Ошибок нет, запуск разрешен
        Assert.True(validationResult.Warnings.Count >= 2); // Но есть предупреждения
        
        Assert.Contains(validationResult.Warnings, 
            w => w.Contains("HTTP", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validationResult.Warnings, 
            w => w.Contains("логирование", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Режимы_влияют_на_валидацию()
    {
        // Arrange
        var devOptions = new AppOptions
        {
            Mode = AppMode.Development,
            AllowedOrigins = new[] { "http://localhost:3000" },
            RateLimit = new RateLimitOptions { RequestsPerMinute = 60, BurstLimit = 10 },
            MaxNoteLength = 100
        };

        var prodOptions = new AppOptions
        {
            Mode = AppMode.Production,
            AllowedOrigins = new[] { "http://localhost:3000" }, // HTTP в Production
            RateLimit = new RateLimitOptions { RequestsPerMinute = 60, BurstLimit = 10 },
            MaxNoteLength = 100
        };

        // Act
        var devResult = AppOptionsValidator.Validate(devOptions);
        var prodResult = AppOptionsValidator.Validate(prodOptions);

        // Assert
        Assert.True(devResult.IsValid);
        Assert.Empty(devResult.Warnings); // В Dev режиме HTTP - норма
        
        Assert.True(prodResult.IsValid);
        Assert.NotEmpty(prodResult.Warnings); // В Prod режиме HTTP вызывает предупреждение
    }

    [Fact]
    public void Валидация_URL_источников_работает_корректно()
    {
        // Arrange
        var optionsWithInvalidUrl = new AppOptions
        {
            Mode = AppMode.Development,
            AllowedOrigins = new[] 
            { 
                "http://localhost:3000",           // OK
                "ftp://mysite.com",                // Неверная схема
                "not-a-valid-url",                 // Не URL
                "https://valid.com"                // OK
            },
            RateLimit = new RateLimitOptions { RequestsPerMinute = 60, BurstLimit = 10 },
            MaxNoteLength = 1000
        };

        // Act
        var validationResult = AppOptionsValidator.Validate(optionsWithInvalidUrl);
        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.Errors, 
            e => e.Contains("ftp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validationResult.Errors, 
            e => e.Contains("not-a-valid-url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
public void Пустая_конфигурация_вызывает_множество_ошибок()
{
    // Arrange
    var emptyOptions = new AppOptions
    {
        AllowedOrigins = Array.Empty<string>(),
        RateLimit = new RateLimitOptions 
        { 
            RequestsPerMinute = 0,  // ← Явно задаём 0
            BurstLimit = 0          // ← Явно задаём 0
        },
        MaxNoteLength = 0,
        Mode = default
    };

    // Act
    var validationResult = AppOptionsValidator.Validate(emptyOptions);

    // Assert
    Assert.False(validationResult.IsValid);
    Assert.True(validationResult.Errors.Count >= 4, 
        $"Ожидалось минимум 4 ошибки, получено: {validationResult.Errors.Count}");
    
    foreach (var error in validationResult.Errors)
    {
        Console.WriteLine($"Error: {error}");
    }
}

    [Fact]
    public void Корректные_настройки_проходят_валидацию_без_ошибок()
    {
        // Arrange
        var validOptions = new AppOptions
        {
            Mode = AppMode.Development,
            AllowedOrigins = new[] { "http://localhost:3000", "https://myapp.com" },
            RateLimit = new RateLimitOptions
            {
                RequestsPerMinute = 100,
                BurstLimit = 20
            },
            MaxNoteLength = 2000,
            EnableRequestLogging = true
        };

        // Act
        var validationResult = AppOptionsValidator.Validate(validOptions);

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
        
        // Могут быть предупреждения, но это нормально
        if (validationResult.Warnings.Count > 0)
        {
            Console.WriteLine($"Предупреждения ({validationResult.Warnings.Count}):");
            foreach (var warning in validationResult.Warnings)
            {
                Console.WriteLine($"  - {warning}");
            }
        }
    }

    [Fact]
    public void Production_режим_требует_HTTPS_в_предупреждениях()
    {
        // Arrange
        var prodOptionsWithHttp = new AppOptions
        {
            Mode = AppMode.Production,
            AllowedOrigins = new[] { "http://insecure.com", "https://secure.com" },
            RateLimit = new RateLimitOptions { RequestsPerMinute = 100, BurstLimit = 20 },
            MaxNoteLength = 1000
        };

        // Act
        var validationResult = AppOptionsValidator.Validate(prodOptionsWithHttp);

        // Assert
        Assert.True(validationResult.IsValid); // Не блокирует запуск
        Assert.Contains(validationResult.Warnings, 
            w => w.Contains("http://insecure.com", StringComparison.OrdinalIgnoreCase) && 
                 w.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(validationResult.Warnings, 
            w => w.Contains("https://secure.com")); // HTTPS не вызывает предупреждений
    }
}