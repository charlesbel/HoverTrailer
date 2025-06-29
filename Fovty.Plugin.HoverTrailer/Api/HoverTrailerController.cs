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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.EnableHoverPreview)
            {
                return NotFound("Hover preview is disabled");
            }

            var script = GetHoverTrailerScript(config.HoverDelayMs, config.EnableDebugLogging);
            return Content(script, "application/javascript");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving client script");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Gets trailer information for a specific movie.
    /// </summary>
    /// <param name="movieId">The movie ID.</param>
    /// <returns>The trailer information.</returns>
    [HttpGet("TrailerInfo/{movieId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TrailerInfo> GetTrailerInfo([FromRoute] Guid movieId)
    {
        try
        {
            var movie = _libraryManager.GetItemById(movieId) as Movie;
            if (movie == null)
            {
                return NotFound("Movie not found");
            }

            // Get trailers using extras like AutoTrailer
            var trailers = movie.GetExtras(new[] { ExtraType.Trailer });
            var trailer = trailers.FirstOrDefault();

            if (trailer == null)
            {
                return NotFound("No trailer found for this movie");
            }

            var trailerInfo = new TrailerInfo
            {
                Id = trailer.Id,
                Name = trailer.Name,
                Path = trailer.Path,
                RunTimeTicks = trailer.RunTimeTicks,
                HasSubtitles = trailer.GetMediaSources(false).Any(s => s.MediaStreams.Any(ms => ms.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle))
            };

            return Ok(trailerInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trailer info for movie {MovieId}", movieId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Gets all movies that have trailers available for hover preview.
    /// </summary>
    /// <returns>List of movies with trailers.</returns>
    [HttpGet("MoviesWithTrailers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<MovieTrailerInfo>> GetMoviesWithTrailers()
    {
        try
        {
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

            return Ok(moviesWithTrailers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting movies with trailers");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Gets the configuration status for the plugin.
    /// </summary>
    /// <returns>The configuration status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<HoverTrailerStatus> GetStatus()
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            var status = new HoverTrailerStatus
            {
                IsEnabled = config?.EnableHoverPreview ?? false,
                HasTmdbApiKey = !string.IsNullOrEmpty(config?.TMDbApiKey),
                HasYtDlpPath = !string.IsNullOrEmpty(config?.PathToYtDlp),
                AutoDownloadEnabled = config?.EnableAutoDownload ?? false,
                HoverDelayMs = config?.HoverDelayMs ?? 1000,
                TrailerQuality = config?.TrailerQuality ?? "720p"
            };

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting status");
            return StatusCode(500, "Internal server error");
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
    /// Gets or sets the trailer path.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the runtime in ticks.
    /// </summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the trailer has subtitles.
    /// </summary>
    public bool HasSubtitles { get; set; }
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