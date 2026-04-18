using System.Text.RegularExpressions;

namespace SecureNotesApi.Config;

public static class AppOptionsValidator
{
    private static readonly Regex OriginPattern = new(
        @"^https?:\/\/(localhost|[\w\-]+(\.[\w\-]+)+)(:\d+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static ValidationResult Validate(AppOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. Проверка AllowedOrigins
        if (options.AllowedOrigins.Length == 0)
        {
            errors.Add("❌ Список разрешенных источников (AllowedOrigins) пуст. API будет недоступен из браузера.");
        }
        else
        {
            foreach (var origin in options.AllowedOrigins)
            {
                if (!OriginPattern.IsMatch(origin))
                {
                    errors.Add($"❌ Некорректный формат источника: '{origin}'. Ожидается: http(s)://domain[:port]");
                }
                
                if (origin.StartsWith("http://") && options.Mode == AppMode.Production)
                {
                    warnings.Add($"⚠️ В Production режиме используется HTTP источник: '{origin}'. Рекомендуется HTTPS.");
                }
            }
        }

        // 2. Проверка RateLimit
        if (options.RateLimit.RequestsPerMinute <= 0)
            errors.Add("❌ RequestsPerMinute должен быть > 0");
            
        if (options.RateLimit.BurstLimit <= 0)
            errors.Add("❌ BurstLimit должен быть > 0");
            
        if (options.RateLimit.BurstLimit > options.RateLimit.RequestsPerMinute)
            errors.Add("❌ BurstLimit не может превышать RequestsPerMinute");

        // 3. Проверка MaxNoteLength
        if (options.MaxNoteLength < 10)
            errors.Add("❌ MaxNoteLength должен быть не менее 10 символов");
            
        if (options.MaxNoteLength > 10000)
            warnings.Add($"⚠️ MaxNoteLength = {options.MaxNoteLength} может привести к проблемам с памятью");

        // 4. Проверки специфичные для режимов
        if (options.Mode == AppMode.Production)
        {
            if (options.EnableRequestLogging)
                warnings.Add("⚠️ В Production режиме включено детальное логирование. Это может снизить производительность.");
                
            if (options.RateLimit.RequestsPerMinute > 1000)
                warnings.Add($"⚠️ Высокий лимит запросов ({options.RateLimit.RequestsPerMinute}/мин) в Production режиме.");
        }

        return new ValidationResult(errors, warnings);
    }
}

public record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}