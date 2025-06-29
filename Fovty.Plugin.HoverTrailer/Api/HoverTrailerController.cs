using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Fovty.Plugin.HoverTrailer.Exceptions;
using Fovty.Plugin.HoverTrailer.Helpers;
using Fovty.Plugin.HoverTrailer.Models;

namespace Fovty.Plugin.HoverTrailer.Api;

/// <summary>
/// The hover trailer controller.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("HoverTrailer")]
[Produces(MediaTypeNames.Application.Json)]
public class HoverTrailerController : ControllerBase
{
    private readonly ILogger<HoverTrailerController> _logger;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="HoverTrailerController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{HoverTrailerController}"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    public HoverTrailerController(ILogger<HoverTrailerController> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets the client-side script for hover trailer functionality.
    /// </summary>
    /// <returns>The client script.</returns>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var requestId = GenerateRequestId();

        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                LoggingHelper.LogError(_logger, "Plugin configuration is null");
                var error = new ErrorResponse("CONFIG_ERROR", "Plugin configuration not available")
                {
                    RequestId = requestId
                };
                return StatusCode(500, error);
            }

            if (!config.EnableHoverPreview)
            {
                LoggingHelper.LogDebug(_logger, "Hover preview is disabled in configuration");
                var error = new ErrorResponse("FEATURE_DISABLED", "Hover preview is disabled")
                {
                    RequestId = requestId
                };
                return NotFound(error);
            }

            // Validate configuration before serving script
            var validationErrors = config.GetValidationErrors().ToList();
            if (validationErrors.Any())
            {
                LoggingHelper.LogWarning(_logger, "Configuration validation failed: {Errors}", string.Join("; ", validationErrors));
                var error = ErrorResponse.FromConfigurationErrors(validationErrors, requestId);
                return BadRequest(error);
            }

            var script = GetHoverTrailerScript(config.HoverDelayMs, config.EnableDebugLogging);
            LoggingHelper.LogDebug(_logger, "Successfully served client script");
            return Content(script, "application/javascript");
        }
        catch (ConfigurationException ex)
        {
            LoggingHelper.LogError(_logger, ex, "Configuration error serving client script");
            var error = ErrorResponse.FromException(ex, requestId);
            return BadRequest(error);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Unexpected error serving client script");
            var error = ErrorResponse.FromException(ex, requestId);
            return StatusCode(500, error);
        }
    }

    /// <summary>
    /// Gets trailer information for a specific movie.
    /// </summary>
    /// <param name="movieId">The movie ID.</param>
    /// <returns>The trailer information.</returns>
    [HttpGet("TrailerInfo/{movieId}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<TrailerInfo> GetTrailerInfo([FromRoute] Guid movieId)
    {
        var requestId = GenerateRequestId();

        try
        {
            if (movieId == Guid.Empty)
            {
                LoggingHelper.LogWarning(_logger, "Invalid movie ID provided: {MovieId}", movieId);
                var error = new ErrorResponse("INVALID_ARGUMENT", "Movie ID cannot be empty")
                {
                    RequestId = requestId
                };
                return BadRequest(error);
            }

            var movie = _libraryManager.GetItemById(movieId) as Movie;
            if (movie == null)
            {
                LoggingHelper.LogDebug(_logger, "Movie not found with ID: {MovieId}", movieId);
                var error = new ErrorResponse("MOVIE_NOT_FOUND", "Movie not found", $"No movie found with ID: {movieId}")
                {
                    RequestId = requestId
                };
                return NotFound(error);
            }

            // Multi-source trailer detection with priority: Local → Remote → Downloaded
            LoggingHelper.LogDebug(_logger, "Starting multi-source trailer detection for movie: {MovieName} (ID: {MovieId})", movie.Name, movieId);
            LoggingHelper.LogDebug(_logger, "Movie path: {MoviePath}", movie.Path ?? "null");
            LoggingHelper.LogDebug(_logger, "Movie directory: {MovieDirectory}", movie.Path != null ? System.IO.Path.GetDirectoryName(movie.Path) ?? "null" : "null");

            TrailerInfo? trailerInfo = null;

            // Step 1: Check for local trailers using Jellyfin's GetExtras
            LoggingHelper.LogDebug(_logger, "Step 1: Checking for local trailers...");
            var localTrailers = movie.GetExtras(new[] { ExtraType.Trailer });
            LoggingHelper.LogDebug(_logger, "Found {LocalTrailerCount} local trailers for movie: {MovieName}", localTrailers.Count(), movie.Name);

            // Log detailed information about each local trailer found
            var localTrailerList = localTrailers.ToList();
            for (int i = 0; i < localTrailerList.Count; i++)
            {
                var t = localTrailerList[i];
                LoggingHelper.LogDebug(_logger, "Local Trailer {Index}: ID={TrailerId}, Name={TrailerName}, Path={TrailerPath}",
                    i + 1, t.Id, t.Name, t.Path);
            }

            var localTrailer = localTrailers.FirstOrDefault();

            if (localTrailer != null)
            {
                LoggingHelper.LogDebug(_logger, "No trailer found for movie: {MovieName} (ID: {MovieId})", movie.Name, movieId);

                // Also check if there are any files in the movie directory that might be trailers
                var movieDir = System.IO.Path.GetDirectoryName(movie.Path);
                if (!string.IsNullOrEmpty(movieDir) && System.IO.Directory.Exists(movieDir))
                {
                    var files = System.IO.Directory.GetFiles(movieDir, "*", System.IO.SearchOption.TopDirectoryOnly);
                    LoggingHelper.LogDebug(_logger, "Files in movie directory {MovieDir}: {Files}",
                        movieDir, string.Join(", ", files.Select(System.IO.Path.GetFileName)));

                    // Look for potential trailer files
                    var potentialTrailers = files.Where(f =>
                        f.Contains("trailer", StringComparison.OrdinalIgnoreCase) ||
                        f.Contains("-trailer", StringComparison.OrdinalIgnoreCase) ||
                        f.Contains(".trailer.", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (potentialTrailers.Any())
                    {
                        LoggingHelper.LogDebug(_logger, "Potential trailer files found but not detected by Jellyfin: {PotentialTrailers}",
                            string.Join(", ", potentialTrailers.Select(System.IO.Path.GetFileName)));
                    }
                    else
                    {
                        LoggingHelper.LogDebug(_logger, "No potential trailer files found in directory");
                    }
                }

                var error = new ErrorResponse("TRAILER_NOT_FOUND", "No trailer found for this movie",
                    $"Movie '{movie.Name}' does not have any local or remote trailers available")
                {
                    RequestId = requestId
                };
                return NotFound(error);
            }

            LoggingHelper.LogDebug(_logger, "Successfully retrieved {TrailerType} trailer info for movie: {MovieName} (ID: {MovieId})",
                trailerInfo.TrailerType, movie.Name, movieId);
            return Ok(trailerInfo);
        }
        catch (UnauthorizedAccessException ex)
        {
            LoggingHelper.LogError(_logger, ex, "Unauthorized access getting trailer info for movie {MovieId}", movieId);
            var error = ErrorResponse.FromException(ex, requestId);
            return StatusCode(403, error);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Unexpected error getting trailer info for movie {MovieId}", movieId);
            var error = ErrorResponse.FromException(ex, requestId);
            return StatusCode(500, error);
        }
    }

    /// <summary>
    /// Gets all movies that have trailers available for hover preview.
    /// </summary>
    /// <returns>List of movies with trailers.</returns>
    [HttpGet("MoviesWithTrailers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<IEnumerable<MovieTrailerInfo>> GetMoviesWithTrailers()
    {
        var requestId = GenerateRequestId();

        try
        {
            LoggingHelper.LogDebug(_logger, "Retrieving all movies with trailers");

            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }).OfType<Movie>().ToList();

            var moviesWithTrailers = movies
                .Select(m =>
                {
                    var movieTrailers = m.GetExtras(new[] { ExtraType.Trailer });
                    return new { Movie = m, Trailers = movieTrailers };
                })
                .Where(x => x.Trailers.Any())
                .Select(x => new MovieTrailerInfo
                {
                    Id = x.Movie.Id,
                    Name = x.Movie.Name,
                    HasTrailer = true,
                    TrailerCount = x.Trailers.Count()
                })
                .ToList();

            LoggingHelper.LogDebug(_logger, "Successfully retrieved {Count} movies with trailers", moviesWithTrailers.Count);
            return Ok(moviesWithTrailers);
        }
        catch (UnauthorizedAccessException ex)
        {
            LoggingHelper.LogError(_logger, ex, "Unauthorized access getting movies with trailers");
            var error = ErrorResponse.FromException(ex, requestId);
            return StatusCode(403, error);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Unexpected error getting movies with trailers");
            var error = ErrorResponse.FromException(ex, requestId);
            return StatusCode(500, error);
        }
    }

    /// <summary>
    /// Gets the configuration status for the plugin.
    /// </summary>
    /// <returns>The configuration status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<HoverTrailerStatus> GetStatus()
    {
        var requestId = GenerateRequestId();

        try
        {
            LoggingHelper.LogDebug(_logger, "Retrieving plugin status");

            var config = Plugin.Instance?.Configuration;
            var status = new HoverTrailerStatus
            {
                IsEnabled = config?.EnableHoverPreview ?? false,
                HasYtDlpPath = !string.IsNullOrEmpty(config?.PathToYtDlp),
                AutoDownloadEnabled = config?.EnableAutoDownload ?? false,
                HoverDelayMs = config?.HoverDelayMs ?? 1000,
                TrailerQuality = config?.TrailerQuality ?? "720p"
            };

            LoggingHelper.LogDebug(_logger, "Successfully retrieved plugin status");
            return Ok(status);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Unexpected error getting plugin status");
            var error = ErrorResponse.FromException(ex, requestId);
            return StatusCode(500, error);
        }
    }

    /// <summary>
    /// Gets the hover trailer client script.
    /// </summary>
    /// <param name="hoverDelayMs">The hover delay in milliseconds.</param>
    /// <param name="enableDebugLogging">Whether to enable debug logging.</param>
    /// <returns>The client script.</returns>
    private static string GetHoverTrailerScript(int hoverDelayMs, bool enableDebugLogging)
    {
        return $@"
(function() {{
    'use strict';

    const HOVER_DELAY = {hoverDelayMs};
    const DEBUG_LOGGING = {enableDebugLogging.ToString().ToLower()};

    let hoverTimeout;
    let currentPreview;
    let isPlaying = false;

    function log(message, ...args) {{
        if (DEBUG_LOGGING) {{
            console.log('[HoverTrailer]', message, ...args);
        }}
    }}

    function createVideoPreview(trailerPath) {{
        const video = document.createElement('video');
        video.style.cssText = `
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            max-width: 300px;
            max-height: 200px;
            border-radius: 8px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.5);
            z-index: 1000;
            pointer-events: none;
            opacity: 0;
            transition: opacity 0.3s ease;
        `;

        video.src = trailerPath;
        video.muted = true;
        video.loop = true;
        video.preload = 'metadata';

        return video;
    }}

    function showPreview(element, movieId) {{
        if (currentPreview || isPlaying) return;

        log('Showing preview for movie:', movieId);

        // Get trailer info from API
        fetch(`/HoverTrailer/TrailerInfo/${{movieId}}`)
            .then(response => {{
                if (!response.ok) {{
                    throw new Error('Trailer not found');
                }}
                return response.json();
            }})
            .then(trailerInfo => {{
                const video = createVideoPreview(`/Videos/${{trailerInfo.Id}}/stream`);

                video.addEventListener('loadeddata', () => {{
                    video.style.opacity = '1';
                    video.play().catch(e => log('Error playing video:', e));
                }});

                element.appendChild(video);
                currentPreview = video;

                log('Preview created for:', trailerInfo.Name);
            }})
            .catch(error => {{
                log('Error loading trailer:', error);
            }});
    }}

    function hidePreview() {{
        if (currentPreview) {{
            currentPreview.style.opacity = '0';
            setTimeout(() => {{
                if (currentPreview && currentPreview.parentNode) {{
                    currentPreview.parentNode.removeChild(currentPreview);
                }}
                currentPreview = null;
            }}, 300);
        }}
    }}

    function attachHoverListeners() {{
        const movieCards = document.querySelectorAll('[data-type=""Movie""], .card[data-itemtype=""Movie""]');

        movieCards.forEach(card => {{
            const movieId = card.getAttribute('data-id') || card.getAttribute('data-itemid');
            if (!movieId) return;

            card.addEventListener('mouseenter', (e) => {{
                if (isPlaying) return;

                clearTimeout(hoverTimeout);
                hoverTimeout = setTimeout(() => {{
                    showPreview(card, movieId);
                }}, HOVER_DELAY);
            }});

            card.addEventListener('mouseleave', () => {{
                clearTimeout(hoverTimeout);
                hidePreview();
            }});

            card.addEventListener('click', () => {{
                isPlaying = true;
                hidePreview();
                setTimeout(() => {{ isPlaying = false; }}, 2000);
            }});
        }});
    }}

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {{
        document.addEventListener('DOMContentLoaded', attachHoverListeners);
    }} else {{
        attachHoverListeners();
    }}

    // Re-attach listeners when navigation occurs
    const observer = new MutationObserver(() => {{
        attachHoverListeners();
    }});

    observer.observe(document.body, {{
        childList: true,
        subtree: true
    }});

    log('HoverTrailer script initialized');
}})();
";
    }

    /// <summary>
    /// Determines the source of a remote trailer URL.
    /// </summary>
    /// <param name="url">The trailer URL to analyze.</param>
    /// <returns>A user-friendly source name.</returns>
    private static string GetTrailerSource(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return "Unknown";

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();

            return host switch
            {
                var h when h.Contains("youtube.com") || h.Contains("youtu.be") => "YouTube",
                var h when h.Contains("vimeo.com") => "Vimeo",
                var h when h.Contains("dailymotion.com") => "Dailymotion",
                var h when h.Contains("twitch.tv") => "Twitch",
                var h when h.Contains("facebook.com") => "Facebook",
                var h when h.Contains("instagram.com") => "Instagram",
                var h when h.Contains("tiktok.com") => "TikTok",
                _ => host
            };
        }
        catch (UriFormatException)
        {
            return "External";
        }
    }

    /// <summary>
    /// Generates a unique request ID for tracking purposes.
    /// </summary>
    /// <returns>A unique request identifier.</returns>
    private static string GenerateRequestId()
    {
        return $"REQ_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
    }
}

/// <summary>
/// Trailer types supported by the plugin.
/// </summary>
public enum TrailerType
{
    /// <summary>
    /// Local trailer file stored on the server.
    /// </summary>
    Local,

    /// <summary>
    /// Remote trailer (e.g., YouTube) referenced by Jellyfin.
    /// </summary>
    Remote,

    /// <summary>
    /// Trailer downloaded via yt-dlp.
    /// </summary>
    Downloaded
}

/// <summary>
/// Trailer information model.
/// </summary>
public class TrailerInfo
{
    /// <summary>
    /// Gets or sets the trailer ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the trailer name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the trailer path or URL.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the runtime in ticks.
    /// </summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the duration in seconds (derived from RunTimeTicks).
    /// </summary>
    public int? Duration => RunTimeTicks.HasValue ? (int?)(RunTimeTicks.Value / TimeSpan.TicksPerSecond) : null;

    /// <summary>
    /// Gets or sets a value indicating whether the trailer has subtitles.
    /// </summary>
    public bool HasSubtitles { get; set; }

    /// <summary>
    /// Gets or sets the trailer type.
    /// </summary>
    public TrailerType TrailerType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a remote trailer.
    /// </summary>
    public bool IsRemote { get; set; }

    /// <summary>
    /// Gets or sets the trailer source description.
    /// </summary>
    public string Source { get; set; } = "Unknown";
}

/// <summary>
/// Movie trailer information model.
/// </summary>
public class MovieTrailerInfo
{
    /// <summary>
    /// Gets or sets the movie ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the movie name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the movie has a trailer.
    /// </summary>
    public bool HasTrailer { get; set; }

    /// <summary>
    /// Gets or sets the number of trailers.
    /// </summary>
    public int TrailerCount { get; set; }
}

/// <summary>
/// Hover trailer status model.
/// </summary>
public class HoverTrailerStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether hover preview is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether TMDb API key is configured.
    /// </summary>
    public bool HasTmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether yt-dlp path is configured.
    /// </summary>
    public bool HasYtDlpPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether auto download is enabled.
    /// </summary>
    public bool AutoDownloadEnabled { get; set; }

    /// <summary>
    /// Gets or sets the hover delay in milliseconds.
    /// </summary>
    public int HoverDelayMs { get; set; }

    /// <summary>
    /// Gets or sets the trailer quality.
    /// </summary>
    public string? TrailerQuality { get; set; }
}