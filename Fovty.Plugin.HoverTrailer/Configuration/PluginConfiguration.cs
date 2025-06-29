using MediaBrowser.Model.Plugins;

namespace Fovty.Plugin.HoverTrailer.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the TMDb API key.
    /// </summary>
    public string? TMDbApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include adult content in searches.
    /// </summary>
    public bool IncludeAdultContent { get; set; } = false;

    /// <summary>
    /// Gets or sets the preferred language for trailer metadata.
    /// </summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>
    /// Gets or sets a value indicating whether trailer downloading is enabled.
    /// </summary>
    public bool EnableTrailerDownload { get; set; } = true;

    /// <summary>
    /// Gets or sets the path to the yt-dlp executable.
    /// </summary>
    public string PathToYtDlp { get; set; } = "yt-dlp";

    /// <summary>
    /// Gets or sets the output directory for downloaded trailers.
    /// </summary>
    public string OutputPath { get; set; } = "";

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
    public bool EnableAutoDownload { get; set; } = true;

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
}