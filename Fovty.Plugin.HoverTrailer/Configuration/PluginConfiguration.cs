using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Plugins;
using Fovty.Plugin.HoverTrailer.Exceptions;

namespace Fovty.Plugin.HoverTrailer.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether trailer downloading is enabled.
    /// </summary>
    public bool EnableTrailerDownload { get; set; } = true;

    /// <summary>
    /// Gets or sets the path to the yt-dlp executable.
    /// </summary>
    public string PathToYtDlp { get; set; } = "yt-dlp";

    /// <summary>
    /// Gets or sets the trailer quality preference.
    /// </summary>
    public string TrailerQuality { get; set; } = "720p";

    /// <summary>
    /// Gets or sets the maximum trailer duration in seconds.
    /// </summary>
    public int MaxTrailerDurationSeconds { get; set; } = 180;

    /// <summary>
    /// Gets or sets the maximum number of concurrent downloads.
    /// </summary>
    public int MaxConcurrentDownloads { get; set; } = 2;

    /// <summary>
    /// Gets or sets the interval in hours for automatic scanning.
    /// </summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets a value indicating whether automatic trailer downloading is enabled.
    /// </summary>
    public bool EnableAutoDownload { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether hover preview is enabled.
    /// </summary>
    public bool EnableHoverPreview { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the plugin should inject the client-side script tag into jellyfin-web.
    /// </summary>
    public bool InjectClientScript { get; set; } = true;

    /// <summary>
    /// Gets or sets the hover delay in milliseconds before starting preview.
    /// </summary>
    public int HoverDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether to enable debug logging.
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// Gets or sets the trailer source URL pattern for manual trailer downloads.
    /// Use {movie_name} placeholder for the movie name.
    /// Example: "https://www.youtube.com/results?search_query={movie_name}+trailer"
    /// </summary>
    public string TrailerSourcePattern { get; set; } = "";

    /// <summary>
    /// Validates the current configuration and throws ConfigurationException if invalid.
    /// </summary>
    /// <exception cref="ConfigurationException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        var errors = GetValidationErrors().ToList();
        if (errors.Any())
        {
            throw new ConfigurationException($"Configuration validation failed: {string.Join("; ", errors)}");
        }
    }

    /// <summary>
    /// Gets all validation errors for the current configuration.
    /// </summary>
    /// <returns>An enumerable of validation error messages.</returns>
    public IEnumerable<string> GetValidationErrors()
    {
        // yt-dlp path validation (only if trailer download is enabled)
        if (EnableTrailerDownload && string.IsNullOrWhiteSpace(PathToYtDlp))
        {
            yield return "Path to yt-dlp cannot be empty when trailer download is enabled";
        }

        // Trailer quality validation
        if (string.IsNullOrWhiteSpace(TrailerQuality))
        {
            yield return "Trailer Quality cannot be empty";
        }
        else if (!IsValidTrailerQuality(TrailerQuality))
        {
            yield return $"Trailer Quality '{TrailerQuality}' is not supported";
        }

        // Numeric range validations
        if (MaxTrailerDurationSeconds <= 0)
        {
            yield return "Max Trailer Duration must be greater than 0";
        }
        else if (MaxTrailerDurationSeconds > 3600)
        {
            yield return "Max Trailer Duration cannot exceed 3600 seconds (1 hour)";
        }

        if (MaxConcurrentDownloads <= 0)
        {
            yield return "Max Concurrent Downloads must be greater than 0";
        }
        else if (MaxConcurrentDownloads > 10)
        {
            yield return "Max Concurrent Downloads cannot exceed 10";
        }

        if (IntervalHours <= 0)
        {
            yield return "Interval Hours must be greater than 0";
        }
        else if (IntervalHours > 8760) // 1 year
        {
            yield return "Interval Hours cannot exceed 8760 (1 year)";
        }

        if (HoverDelayMs < 0)
        {
            yield return "Hover Delay cannot be negative";
        }
        else if (HoverDelayMs > 10000)
        {
            yield return "Hover Delay cannot exceed 10000ms (10 seconds)";
        }
    }

    /// <summary>
    /// Checks if the current configuration is valid.
    /// </summary>
    /// <returns>True if configuration is valid, false otherwise.</returns>
    public bool IsValid()
    {
        return !GetValidationErrors().Any();
    }

    /// <summary>
    /// Validates a specific configuration property.
    /// </summary>
    /// <param name="propertyName">The name of the property to validate.</param>
    /// <returns>Validation error message if invalid, null if valid.</returns>
    public string? ValidateProperty(string propertyName)
    {
        return propertyName switch
        {

            nameof(PathToYtDlp) => ValidatePathToYtDlp(),

            nameof(TrailerQuality) => ValidateTrailerQuality(),
            nameof(MaxTrailerDurationSeconds) => ValidateMaxTrailerDurationSeconds(),
            nameof(MaxConcurrentDownloads) => ValidateMaxConcurrentDownloads(),
            nameof(IntervalHours) => ValidateIntervalHours(),
            nameof(HoverDelayMs) => ValidateHoverDelayMs(),
            _ => null
        };
    }

    private string? ValidatePathToYtDlp()
    {
        if (EnableTrailerDownload && string.IsNullOrWhiteSpace(PathToYtDlp))
            return "Path to yt-dlp cannot be empty when trailer download is enabled";
        return null;
    }

    private string? ValidateTrailerQuality()
    {
        if (string.IsNullOrWhiteSpace(TrailerQuality))
            return "Trailer Quality cannot be empty";
        if (!IsValidTrailerQuality(TrailerQuality))
            return $"Trailer Quality '{TrailerQuality}' is not supported";
        return null;
    }

    private string? ValidateMaxTrailerDurationSeconds()
    {
        if (MaxTrailerDurationSeconds <= 0)
            return "Max Trailer Duration must be greater than 0";
        if (MaxTrailerDurationSeconds > 3600)
            return "Max Trailer Duration cannot exceed 3600 seconds (1 hour)";
        return null;
    }

    private string? ValidateMaxConcurrentDownloads()
    {
        if (MaxConcurrentDownloads <= 0)
            return "Max Concurrent Downloads must be greater than 0";
        if (MaxConcurrentDownloads > 10)
            return "Max Concurrent Downloads cannot exceed 10";
        return null;
    }

    private string? ValidateIntervalHours()
    {
        if (IntervalHours <= 0)
            return "Interval Hours must be greater than 0";
        if (IntervalHours > 8760)
            return "Interval Hours cannot exceed 8760 (1 year)";
        return null;
    }

    private string? ValidateHoverDelayMs()
    {
        if (HoverDelayMs < 0)
            return "Hover Delay cannot be negative";
        if (HoverDelayMs > 10000)
            return "Hover Delay cannot exceed 10000ms (10 seconds)";
        return null;
    }

    private static bool IsValidLanguageCode(string languageCode)
    {
        // Common language codes supported by TMDb
        var validLanguageCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "en", "es", "fr", "de", "it", "pt", "ru", "ja", "ko", "zh", "ar", "hi", "th", "tr", "pl", "nl", "sv", "da", "no", "fi", "he", "cs", "hu", "ro", "bg", "hr", "sk", "sl", "et", "lv", "lt", "mt", "ga", "cy", "eu", "ca", "gl", "ast", "br", "co", "eo", "ia", "ie", "io", "kl", "la", "lb", "mk", "sq", "is", "fo", "gd", "gv", "kw", "mi", "ms", "ml", "mr", "ne", "or", "pa", "sa", "sd", "si", "ta", "te", "ur", "bn", "gu", "kn", "as", "ks", "kok", "mai", "mni", "sat", "bho", "mag", "awa", "raj", "bgc", "hne", "gom", "new"
        };

        return validLanguageCodes.Contains(languageCode);
    }

    private static bool IsValidTrailerQuality(string quality)
    {
        // Common video quality options supported by yt-dlp
        var validQualities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "144p", "240p", "360p", "480p", "720p", "1080p", "1440p", "2160p", "4320p",
            "worst", "best", "bestvideo", "worstvideo", "bestaudio", "worstaudio"
        };

        return validQualities.Contains(quality);
    }
}