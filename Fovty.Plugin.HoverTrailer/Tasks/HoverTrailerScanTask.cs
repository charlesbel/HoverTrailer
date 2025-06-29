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
using System.Text;
using Fovty.Plugin.HoverTrailer.Helpers;
using Fovty.Plugin.HoverTrailer.Exceptions;
using Fovty.Plugin.HoverTrailer.Configuration;

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
        var requestId = Guid.NewGuid().ToString("N")[..8];
        LoggingHelper.LogInformation(_logger, "Starting HoverTrailer scan task", requestId);

        try
        {
            // Validate configuration first
            await ValidateConfigurationAsync(requestId);

            var config = Plugin.Instance!.Configuration;

            // Get all movies from the library
            var movies = await GetMoviesFromLibraryAsync(requestId);
            LoggingHelper.LogInformation(_logger, "Found {MovieCount} movies to check for trailers", requestId, movies.Count);

            int processedCount = 0;
            int downloadedCount = 0;
            var errors = new List<string>();

            foreach (var movie in movies)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    LoggingHelper.LogWarning(_logger, "Scan task cancelled by user", requestId);
                    break;
                }

                try
                {
                    var result = await ProcessMovieAsync(movie, cancellationToken, requestId);
                    if (result.Downloaded)
                    {
                        downloadedCount++;
                        LoggingHelper.LogInformation(_logger, "Successfully downloaded trailer for {MovieName}", requestId, movie.Name);
                    }
                    else if (result.Error != null)
                    {
                        errors.Add($"{movie.Name}: {result.Error}");
                    }
                }
                catch (YtDlpException ex)
                {
                    var error = $"yt-dlp error for {movie.Name}: {ex.Message}";
                    errors.Add(error);
                    LoggingHelper.LogError(_logger, ex, "yt-dlp error processing movie {MovieName}", requestId, movie.Name);
                }
                catch (TrailerException ex)
                {
                    var error = $"Trailer error for {movie.Name}: {ex.Message}";
                    errors.Add(error);
                    LoggingHelper.LogError(_logger, ex, "Trailer error processing movie {MovieName}", requestId, movie.Name);
                }
                catch (Exception ex)
                {
                    var error = $"Unexpected error for {movie.Name}: {ex.Message}";
                    errors.Add(error);
                    LoggingHelper.LogError(_logger, ex, "Unexpected error processing movie {MovieName}", requestId, movie.Name);
                }

                processedCount++;
                progress?.Report((double)processedCount / movies.Count * 100);
            }

            // Log final results
            LoggingHelper.LogInformation(_logger,
                "HoverTrailer scan task completed. Processed {ProcessedCount} movies, downloaded {DownloadedCount} trailers, {ErrorCount} errors",
                requestId, processedCount, downloadedCount, errors.Count);

            if (errors.Count > 0)
            {
                LoggingHelper.LogWarning(_logger, "Scan completed with errors: {Errors}", requestId, string.Join("; ", errors.Take(5)));
            }
        }
        catch (ConfigurationException ex)
        {
            LoggingHelper.LogError(_logger, ex, "Configuration error during scan task", requestId);
            throw new TaskExecutionException("Configuration validation failed", ex);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Critical error during HoverTrailer scan task", requestId);
            throw new TaskExecutionException("Scan task failed", ex);
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
    /// Validates the plugin configuration for scan task execution.
    /// </summary>
    /// <param name="requestId">Request ID for tracing.</param>
    /// <returns>Task representing the validation operation.</returns>
    /// <exception cref="ConfigurationException">Thrown when configuration is invalid.</exception>
    private async Task ValidateConfigurationAsync(string requestId)
    {
        LoggingHelper.LogDebug(_logger, "Validating configuration for scan task", requestId);

        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            throw new ConfigurationException("Plugin configuration is not available");
        }

        if (!config.EnableAutoDownload)
        {
            throw new ConfigurationException("Auto-download is disabled in configuration");
        }

        var validationErrors = await Task.Run(() => config.GetValidationErrors());
        if (validationErrors.Any())
        {
            var errorMessage = string.Join("; ", validationErrors);
            throw new ConfigurationException($"Configuration validation failed: {errorMessage}");
        }

        LoggingHelper.LogDebug(_logger, "Configuration validation successful", requestId);
    }

    /// <summary>
    /// Gets all movies from the library for trailer processing.
    /// </summary>
    /// <param name="requestId">Request ID for tracing.</param>
    /// <returns>List of movies to process.</returns>
    /// <exception cref="TrailerException">Thrown when library access fails.</exception>
    private async Task<List<Movie>> GetMoviesFromLibraryAsync(string requestId)
    {
        try
        {
            LoggingHelper.LogDebug(_logger, "Retrieving movies from library", requestId);

            var movies = await Task.Run(() => _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().ToList());

            return movies;
        }
        catch (Exception ex)
        {
            throw new TrailerException("Failed to retrieve movies from library", ex);
        }
    }

    /// <summary>
    /// Processes a single movie for trailer download.
    /// </summary>
    /// <param name="movie">The movie to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="requestId">Request ID for tracing.</param>
    /// <returns>Processing result indicating success and any errors.</returns>
    private async Task<MovieProcessingResult> ProcessMovieAsync(Movie movie, CancellationToken cancellationToken, string requestId)
    {
        try
        {
            LoggingHelper.LogDebug(_logger, "Processing movie {MovieName}", requestId, movie.Name);

            var hasTrailer = HasLocalTrailer(movie, requestId);
            if (hasTrailer)
            {
                LoggingHelper.LogDebug(_logger, "Movie {MovieName} already has a trailer", requestId, movie.Name);
                return new MovieProcessingResult { Downloaded = false };
            }

            LoggingHelper.LogDebug(_logger, "Movie {MovieName} has no trailer, attempting download", requestId, movie.Name);
            var success = await DownloadTrailerForMovieAsync(movie, cancellationToken, requestId);

            return new MovieProcessingResult { Downloaded = success };
        }
        catch (Exception ex)
        {
            return new MovieProcessingResult { Downloaded = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Checks if a movie has a local trailer file.
    /// </summary>
    /// <param name="movie">The movie to check.</param>
    /// <param name="requestId">Request ID for tracing.</param>
    /// <returns>True if the movie has a local trailer, false otherwise.</returns>
    private bool HasLocalTrailer(Movie movie, string requestId)
    {
        try
        {
            var extras = movie.GetExtras(new[] { ExtraType.Trailer });
            return extras.Any();
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Error checking for local trailers for movie {MovieName}", requestId, movie.Name);
            return false;
        }
    }

    /// <summary>
    /// Downloads a trailer for the specified movie with retry logic.
    /// </summary>
    /// <param name="movie">The movie to download a trailer for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="requestId">Request ID for tracing.</param>
    /// <returns>True if the download was successful, false otherwise.</returns>
    /// <exception cref="YtDlpException">Thrown when yt-dlp fails.</exception>
    /// <exception cref="TrailerException">Thrown when trailer processing fails.</exception>
    private async Task<bool> DownloadTrailerForMovieAsync(Movie movie, CancellationToken cancellationToken, string requestId)
    {
        const int maxRetries = 3;
        var config = Plugin.Instance!.Configuration;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                LoggingHelper.LogDebug(_logger, "Download attempt {Attempt}/{MaxRetries} for movie {MovieName}",
                    requestId, attempt, maxRetries, movie.Name);

                // Use yt-dlp to search and download trailer directly
                var success = await DownloadTrailerDirectlyAsync(movie, cancellationToken, requestId);
                if (success)
                {
                    LoggingHelper.LogInformation(_logger, "Successfully downloaded trailer for {MovieName} on attempt {Attempt}",
                        requestId, movie.Name, attempt);
                    return true;
                }

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    LoggingHelper.LogWarning(_logger, "Download attempt {Attempt} failed for {MovieName}, retrying in {Delay}s",
                        requestId, attempt, movie.Name, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (YtDlpException)
            {
                if (attempt == maxRetries) throw;
                LoggingHelper.LogWarning(_logger, "yt-dlp error on attempt {Attempt} for {MovieName}", requestId, attempt, movie.Name);
            }
            catch (TrailerException)
            {
                // Don't retry for trailer-specific errors
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    throw new TrailerException($"Unexpected error downloading trailer for {movie.Name}", ex);
                }
                LoggingHelper.LogWarning(_logger, "Unexpected error on attempt {Attempt} for {MovieName}: {Error}",
                    requestId, attempt, movie.Name, ex.Message);
            }
        }

        return false;
    }



    /// <summary>
    /// Downloads trailer directly using yt-dlp search functionality.
    /// </summary>
    /// <param name="movie">The movie to download a trailer for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="requestId">Request ID for tracing.</param>
    /// <returns>True if the download was successful, false otherwise.</returns>
    /// <exception cref="YtDlpException">Thrown when yt-dlp fails.</exception>
    /// <exception cref="TrailerException">Thrown when trailer processing fails.</exception>
    private async Task<bool> DownloadTrailerDirectlyAsync(Movie movie, CancellationToken cancellationToken, string requestId)
    {
        try
        {
            var config = Plugin.Instance!.Configuration;
            LoggingHelper.LogDebug(_logger, "Starting direct yt-dlp search and download for {MovieName}", requestId, movie.Name);

            var movieDirectory = Path.GetDirectoryName(movie.Path);
            if (string.IsNullOrEmpty(movieDirectory))
            {
                throw new TrailerException($"Could not determine directory for movie {movie.Name}");
            }

            // Ensure directory exists
            if (!Directory.Exists(movieDirectory))
            {
                throw new TrailerException($"Movie directory does not exist: {movieDirectory}");
            }

            var movieName = Path.GetFileNameWithoutExtension(movie.Path);
            var outputPath = Path.Combine(movieDirectory, $"{movieName}-trailer.%(ext)s");

            // Create search query for the movie trailer
            var searchQuery = $"{movie.Name} {movie.ProductionYear} trailer";

            var arguments = BuildYtDlpArgumentsForSearch(config, outputPath, searchQuery);
            LoggingHelper.LogDebug(_logger, "yt-dlp search arguments: {Arguments}", requestId, string.Join(" ", arguments));

            var processInfo = new ProcessStartInfo
            {
                FileName = config.PathToYtDlp,
                Arguments = string.Join(" ", arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = movieDirectory
            };

            using var process = new Process { StartInfo = processInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.AppendLine(e.Data);
                    LoggingHelper.LogDebug(_logger, "yt-dlp output: {Output}", requestId, e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    error.AppendLine(e.Data);
                    LoggingHelper.LogDebug(_logger, "yt-dlp error: {Error}", requestId, e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                LoggingHelper.LogInformation(_logger, "Successfully downloaded trailer for {MovieName}", requestId, movie.Name);
                return true;
            }
            else
            {
                var errorOutput = error.ToString();
                LoggingHelper.LogWarning(_logger, "yt-dlp failed for {MovieName} with exit code {ExitCode}. Error: {Error}",
                    requestId, movie.Name, process.ExitCode, errorOutput);
                return false;
            }
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Error downloading trailer directly for movie {MovieName}", requestId, movie.Name);
            return false;
        }
    }



    /// <summary>
    /// Downloads a trailer using yt-dlp with enhanced error handling.
    /// </summary>
    /// <param name="movie">The movie.</param>
    /// <param name="trailerUrl">The trailer URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="requestId">Request ID for tracing.</param>
    /// <returns>True if download was successful, false otherwise.</returns>
    /// <exception cref="YtDlpException">Thrown when yt-dlp fails.</exception>
    /// <exception cref="TrailerException">Thrown when trailer processing fails.</exception>
    private async Task<bool> DownloadTrailerAsync(Movie movie, string trailerUrl, CancellationToken cancellationToken, string requestId)
    {
        try
        {
            var config = Plugin.Instance!.Configuration;
            LoggingHelper.LogDebug(_logger, "Starting yt-dlp download for {MovieName} from {TrailerUrl}", requestId, movie.Name, trailerUrl);

            var movieDirectory = Path.GetDirectoryName(movie.Path);
            if (string.IsNullOrEmpty(movieDirectory))
            {
                throw new TrailerException($"Could not determine directory for movie {movie.Name}");
            }

            // Ensure directory exists
            if (!Directory.Exists(movieDirectory))
            {
                throw new TrailerException($"Movie directory does not exist: {movieDirectory}");
            }

            var movieName = Path.GetFileNameWithoutExtension(movie.Path);
            var outputPath = Path.Combine(movieDirectory, $"{movieName}-trailer.%(ext)s");

            var arguments = BuildYtDlpArguments(config, outputPath, trailerUrl);
            LoggingHelper.LogDebug(_logger, "yt-dlp arguments: {Arguments}", requestId, string.Join(" ", arguments));

            var processInfo = new ProcessStartInfo
            {
                FileName = config.PathToYtDlp,
                Arguments = string.Join(" ", arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Validate yt-dlp executable exists
            if (!File.Exists(config.PathToYtDlp))
            {
                throw new YtDlpException($"yt-dlp executable not found at path: {config.PathToYtDlp}");
            }

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new YtDlpException("Failed to start yt-dlp process");
            }

            // Set a reasonable timeout for the download process
            var timeout = TimeSpan.FromMinutes(10);
            var timeoutCts = new CancellationTokenSource(timeout);
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token).Token;

            try
            {
                await process.WaitForExitAsync(combinedToken);
                var completedInTime = !combinedToken.IsCancellationRequested;

                if (!completedInTime)
                {
                    process.Kill();
                    throw new YtDlpException($"yt-dlp download timed out after {timeout.TotalMinutes} minutes");
                }
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                process.Kill();
                throw new YtDlpException($"yt-dlp download timed out after {timeout.TotalMinutes} minutes");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            if (process.ExitCode == 0)
            {
                LoggingHelper.LogInformation(_logger, "Successfully downloaded trailer for {MovieName}", requestId, movie.Name);
                return true;
            }
            else
            {
                var errorMessage = $"yt-dlp failed with exit code {process.ExitCode}";
                if (!string.IsNullOrEmpty(error))
                {
                    errorMessage += $": {error}";
                }
                throw new YtDlpException(errorMessage, process.ExitCode, error);
            }
        }
        catch (YtDlpException)
        {
            throw; // Re-throw yt-dlp specific exceptions
        }
        catch (IOException ex)
        {
            throw new TrailerException($"File system error downloading trailer for {movie.Name}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new TrailerException($"Access denied downloading trailer for {movie.Name}", ex);
        }
        catch (Exception ex)
        {
            throw new YtDlpException($"Unexpected error downloading trailer for {movie.Name}", ex);
        }
    }

    /// <summary>
    /// Builds yt-dlp command line arguments for searching and downloading a trailer.
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="outputPath">Output file path.</param>
    /// <param name="searchQuery">Search query for trailer.</param>
    /// <returns>List of command line arguments.</returns>
    private static List<string> BuildYtDlpArgumentsForSearch(PluginConfiguration config, string outputPath, string searchQuery)
    {
        var arguments = new List<string>
        {
            "--format", GetYtDlpFormat(config.TrailerQuality),
            "--output", $"\"{outputPath}\"",
            "--no-playlist",
            "--extract-flat", "false",
            "--no-check-certificate", // Help with SSL issues
            "--socket-timeout", "30", // 30 second socket timeout
            "--playlist-end", "1", // Only download the first result
            "--match-filter", "duration < 600" // Limit to videos under 10 minutes
        };

        if (config.MaxTrailerDurationSeconds > 0)
        {
            arguments.AddRange(new[] { "--postprocessor-args", $"ffmpeg:-t {config.MaxTrailerDurationSeconds}" });
        }

        // Use ytsearch: prefix for YouTube search
        arguments.Add($"\"ytsearch:{searchQuery}\"");
        return arguments;
    }

    /// <summary>
    /// Builds the command line arguments for yt-dlp.
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="outputPath">Output file path.</param>
    /// <param name="trailerUrl">Trailer URL.</param>
    /// <returns>List of command line arguments.</returns>
    private static List<string> BuildYtDlpArguments(PluginConfiguration config, string outputPath, string trailerUrl)
    {
        var arguments = new List<string>
        {
            "--format", GetYtDlpFormat(config.TrailerQuality),
            "--output", $"\"{outputPath}\"",
            "--no-playlist",
            "--extract-flat", "false",
            "--no-check-certificate", // Help with SSL issues
            "--socket-timeout", "30" // 30 second socket timeout
        };

        if (config.MaxTrailerDurationSeconds > 0)
        {
            arguments.AddRange(new[] { "--postprocessor-args", $"ffmpeg:-t {config.MaxTrailerDurationSeconds}" });
        }

        arguments.Add($"\"{trailerUrl}\"");
        return arguments;
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
/// Result of processing a single movie for trailers.
/// </summary>
public class MovieProcessingResult
{
    /// <summary>
    /// Gets or sets a value indicating whether a trailer was successfully downloaded.
    /// </summary>
    public bool Downloaded { get; set; }

    /// <summary>
    /// Gets or sets the error message if processing failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Exception thrown when a scheduled task execution fails.
/// </summary>
public class TaskExecutionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskExecutionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TaskExecutionException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskExecutionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TaskExecutionException(string message, Exception innerException) : base(message, innerException) { }
}