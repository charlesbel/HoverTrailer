using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using MediaBrowser.Model.Providers;
using System.Text.Json.Serialization;
using MediaBrowser.Model.Querying;

namespace Fovty.Plugin.HoverTrailer.Tasks;

/// <summary>
/// Scheduled task for scanning and downloading movie trailers for hover preview functionality.
/// </summary>
public class HoverTrailerScanTask : IScheduledTask
{
    private readonly ILogger<HoverTrailerScanTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="HoverTrailerScanTask"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    public HoverTrailerScanTask(ILogger<HoverTrailerScanTask> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => "Scan for Movie Trailers (Hover Preview)";

    /// <inheritdoc />
    public string Description => "Scans the library for movies without trailers and attempts to download them for hover preview functionality";

    /// <inheritdoc />
    public string Category => "Fovty HoverTrailer";

    /// <inheritdoc />
    public string Key => "HoverTrailerScan";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting HoverTrailer scan task");

        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnableAutoDownload)
            {
                _logger.LogInformation("HoverTrailer auto-download is disabled");
                return;
            }

            if (string.IsNullOrEmpty(config.TMDbApiKey))
            {
                _logger.LogWarning("TMDb API key is not configured");
                return;
            }

            if (string.IsNullOrEmpty(config.PathToYtDlp))
            {
                _logger.LogWarning("yt-dlp path is not configured");
                return;
            }

            // Get all movies from the library
            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().ToList();

            _logger.LogInformation("Found {MovieCount} movies to check for trailers", movies.Count);

            int processedCount = 0;
            int downloadedCount = 0;

            foreach (var movie in movies)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var hasTrailer = HasLocalTrailerAsync(movie);
                    if (!hasTrailer)
                    {
                        _logger.LogDebug("Movie {MovieName} has no trailer, attempting download", movie.Name);
                        var success = await DownloadTrailerForMovieAsync(movie, cancellationToken);
                        if (success)
                        {
                            downloadedCount++;
                            _logger.LogInformation("Successfully downloaded trailer for {MovieName}", movie.Name);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Movie {MovieName} already has a trailer", movie.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing movie {MovieName}", movie.Name);
                }

                processedCount++;
                progress?.Report((double)processedCount / movies.Count * 100);
            }

            _logger.LogInformation("HoverTrailer scan task completed. Processed {ProcessedCount} movies, downloaded {DownloadedCount} trailers",
                processedCount, downloadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during HoverTrailer scan task");
            throw;
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var config = Plugin.Instance?.Configuration;
        var intervalHours = config?.IntervalHours ?? 24;

        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
            }
        };
    }

    /// <summary>
    /// Checks if a movie has a local trailer file.
    /// </summary>
    /// <param name="movie">The movie to check.</param>
    /// <returns>True if the movie has a local trailer, false otherwise.</returns>
    private bool HasLocalTrailerAsync(Movie movie)
    {
        try
        {
            var extras = movie.GetExtras(new[] { ExtraType.Trailer });
            return extras.Any();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for local trailers for movie {MovieName}", movie.Name);
            return false;
        }
    }

    /// <summary>
    /// Downloads a trailer for the specified movie.
    /// </summary>
    /// <param name="movie">The movie to download a trailer for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the download was successful, false otherwise.</returns>
    private async Task<bool> DownloadTrailerForMovieAsync(Movie movie, CancellationToken cancellationToken)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null)
                return false;

            // Get TMDb ID
            var tmdbId = GetTmdbId(movie);
            if (string.IsNullOrEmpty(tmdbId))
            {
                _logger.LogWarning("Could not find TMDb ID for movie {MovieName}", movie.Name);
                return false;
            }

            // Get trailer URL from TMDb
            var trailerUrl = await GetTrailerUrlFromTmdbAsync(tmdbId, cancellationToken);
            if (string.IsNullOrEmpty(trailerUrl))
            {
                _logger.LogWarning("Could not find trailer URL for movie {MovieName}", movie.Name);
                return false;
            }

            // Download trailer using yt-dlp
            var success = await DownloadTrailerAsync(movie, trailerUrl, cancellationToken);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading trailer for movie {MovieName}", movie.Name);
            return false;
        }
    }

    /// <summary>
    /// Gets the TMDb ID from movie metadata.
    /// </summary>
    /// <param name="movie">The movie.</param>
    /// <returns>The TMDb ID or null if not found.</returns>
    private string? GetTmdbId(Movie movie)
    {
        if (movie.ProviderIds?.TryGetValue(MetadataProvider.Tmdb.ToString(), out var tmdbId) == true)
        {
            return tmdbId;
        }

        return null;
    }

    /// <summary>
    /// Gets trailer URL from TMDb API.
    /// </summary>
    /// <param name="tmdbId">The TMDb ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trailer URL or null if not found.</returns>
    private async Task<string?> GetTrailerUrlFromTmdbAsync(string tmdbId, CancellationToken cancellationToken)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrEmpty(config.TMDbApiKey))
                return null;

            using var httpClient = _httpClientFactory.CreateClient();
            var url = $"https://api.themoviedb.org/3/movie/{tmdbId}/videos?api_key={config.TMDbApiKey}";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TMDb API request failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var videoResponse = JsonSerializer.Deserialize<TmdbVideoResponse>(json);

            var trailer = videoResponse?.Results?
                .Where(v => v.Type?.Equals("Trailer", StringComparison.OrdinalIgnoreCase) == true)
                .Where(v => v.Site?.Equals("YouTube", StringComparison.OrdinalIgnoreCase) == true)
                .FirstOrDefault();

            if (trailer?.Key != null)
            {
                return $"https://www.youtube.com/watch?v={trailer.Key}";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trailer URL from TMDb for ID {TmdbId}", tmdbId);
            return null;
        }
    }

    /// <summary>
    /// Downloads a trailer using yt-dlp.
    /// </summary>
    /// <param name="movie">The movie.</param>
    /// <param name="trailerUrl">The trailer URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if download was successful, false otherwise.</returns>
    private async Task<bool> DownloadTrailerAsync(Movie movie, string trailerUrl, CancellationToken cancellationToken)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null)
                return false;

            var movieDirectory = Path.GetDirectoryName(movie.Path);
            if (string.IsNullOrEmpty(movieDirectory))
                return false;

            var movieName = Path.GetFileNameWithoutExtension(movie.Path);
            var outputPath = Path.Combine(movieDirectory, $"{movieName}-trailer.%(ext)s");

            var arguments = new List<string>
            {
                "--format", GetYtDlpFormat(config.TrailerQuality),
                "--output", $"\"{outputPath}\"",
                "--no-playlist",
                "--extract-flat", "false"
            };

            if (config.MaxTrailerDurationSeconds > 0)
            {
                arguments.AddRange(new[] { "--postprocessor-args", $"ffmpeg:-t {config.MaxTrailerDurationSeconds}" });
            }

            arguments.Add($"\"{trailerUrl}\"");

            var processInfo = new ProcessStartInfo
            {
                FileName = config.PathToYtDlp,
                Arguments = string.Join(" ", arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
                return false;

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Successfully downloaded trailer for {MovieName}", movie.Name);
                return true;
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("yt-dlp failed for {MovieName}. Exit code: {ExitCode}, Error: {Error}",
                    movie.Name, process.ExitCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading trailer for movie {MovieName}", movie.Name);
            return false;
        }
    }

    /// <summary>
    /// Gets the yt-dlp format string based on quality preference.
    /// </summary>
    /// <param name="quality">The quality preference.</param>
    /// <returns>The format string.</returns>
    private static string GetYtDlpFormat(string quality)
    {
        return quality.ToLowerInvariant() switch
        {
            "480p" => "best[height<=480]",
            "720p" => "best[height<=720]",
            "1080p" => "best[height<=1080]",
            "1440p" => "best[height<=1440]",
            "2160p" => "best[height<=2160]",
            _ => "best[height<=720]"
        };
    }
}

/// <summary>
/// TMDb video response model.
/// </summary>
public class TmdbVideoResponse
{
    [JsonPropertyName("results")]
    public TmdbVideo[]? Results { get; set; }
}

/// <summary>
/// TMDb video model.
/// </summary>
public class TmdbVideo
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("site")]
    public string? Site { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}