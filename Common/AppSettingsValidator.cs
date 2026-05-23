using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Apollarr.Common;

// ValidateDataAnnotations() only validates the top-level AppSettings object and does not
// recurse into nested settings, so the [Required]/[Url]/[Range] attributes on the nested
// settings would otherwise be ignored. This validator runs them explicitly so misconfiguration
// fails at startup.
public sealed class AppSettingsValidator : IValidateOptions<AppSettings>
{
    public ValidateOptionsResult Validate(string? name, AppSettings options)
    {
        var errors = new List<string>();

        ValidateSection(options.Sonarr, nameof(AppSettings.Sonarr), errors);
        ValidateSection(options.Radarr, nameof(AppSettings.Radarr), errors);
        ValidateSection(options.Apollo, nameof(AppSettings.Apollo), errors);
        ValidateSection(options.Strm, nameof(AppSettings.Strm), errors);
        ValidateSection(options.Scheduling, nameof(AppSettings.Scheduling), errors);
        ValidateSection(options.ValidationCache, nameof(AppSettings.ValidationCache), errors);

        if (options.ValidationCache.Enabled
            && options.ValidationCache.Provider.Equals("Redis", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.ValidationCache.RedisConnectionString))
        {
            errors.Add($"{nameof(AppSettings.ValidationCache)}.{nameof(ValidationCacheSettings.RedisConnectionString)}: required when Provider is 'Redis'");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateSection(object section, string prefix, List<string> errors)
    {
        var context = new ValidationContext(section);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(section, context, results, validateAllProperties: true))
            return;

        foreach (var result in results)
        {
            var members = result.MemberNames.Any()
                ? string.Join(", ", result.MemberNames.Select(m => $"{prefix}.{m}"))
                : prefix;
            errors.Add($"{members}: {result.ErrorMessage}");
        }
    }
}
