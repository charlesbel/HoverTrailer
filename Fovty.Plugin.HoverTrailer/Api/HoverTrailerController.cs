using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using Fovty.Plugin.HoverTrailer.Helpers;
using Fovty.Plugin.HoverTrailer.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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
    /// Gets trailer information for a specific movie.
    /// </summary>
    /// <param name="movieId">The movie ID.</param>
    /// <returns>The trailer information.</returns>
    [HttpGet("TrailerInfo/{movieId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TrailerInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public ActionResult<TrailerInfo> GetTrailerInfo([FromRoute] Guid movieId)
    {
        var requestId = GenerateRequestId();
        try
        {
            if (movieId == Guid.Empty)
            {
                return BadRequest(new ErrorResponse("INVALID_ARGUMENT", "Movie ID cannot be empty") { RequestId = requestId });
            }

            var item = _libraryManager.GetItemById(movieId);
            if (item is not Movie movie)
            {
                return NotFound(new ErrorResponse("MOVIE_NOT_FOUND", "Movie not found") { RequestId = requestId });
            }

            // Priority 1: Local Trailers
            var localTrailer = movie.GetExtras(new[] { ExtraType.Trailer }).FirstOrDefault();
            if (localTrailer != null)
            {
                LoggingHelper.LogDebug(_logger, "Found local trailer for movie: {MovieName}", movie.Name);
                var trailerInfo = new TrailerInfo
                {
                    Id = localTrailer.Id,
                    Name = localTrailer.Name,
                    Path = localTrailer.Path,
                    RunTimeTicks = localTrailer.RunTimeTicks,
                    TrailerType = TrailerType.Local,
                    IsRemote = false,
                    Source = "Local File"
                };
                return Ok(trailerInfo);
            }

            // Priority 2: Remote Trailers
            if (movie.RemoteTrailers?.Any() == true)
            {
                var remoteTrailer = movie.RemoteTrailers.First();
                LoggingHelper.LogDebug(_logger, "Found remote trailer for movie: {MovieName}", movie.Name);
                var trailerInfo = new TrailerInfo
                {
                    Id = movieId, // Use movie ID as a fallback
                    Name = remoteTrailer.Name ?? $"{movie.Name} - Trailer",
                    Path = remoteTrailer.Url,
                    TrailerType = TrailerType.Remote,
                    IsRemote = true,
                    Source = GetTrailerSource(remoteTrailer.Url)
                };
                return Ok(trailerInfo);
            }

            return NotFound(new ErrorResponse("TRAILER_NOT_FOUND", "No trailer found for this movie") { RequestId = requestId });
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, ex, "Unexpected error getting trailer info for movie {MovieId}", movieId);
            return StatusCode(500, ErrorResponse.FromException(ex, requestId));
        }
    }

    private static string GetTrailerSource(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "Unknown";
        try
        {
            var host = new Uri(url).Host.ToLowerInvariant();
            if (host.Contains("youtube.com") || host.Contains("youtu.be")) return "YouTube";
            if (host.Contains("vimeo.com")) return "Vimeo";
            return host;
        }
        catch (UriFormatException)
        {
            return "External";
        }
    }

    private static string GenerateRequestId()
    {
        return $"REQ_{Guid.NewGuid():N}";
    }
}

/// <summary>
/// Trailer types supported by the plugin.
/// </summary>
public enum TrailerType
{
    Local,
    Remote,
    Downloaded
}

/// <summary>
/// Trailer information model.
/// </summary>
public class TrailerInfo
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public long? RunTimeTicks { get; set; }
    public TrailerType TrailerType { get; set; }
    public bool IsRemote { get; set; }
    public string Source { get; set; } = "Unknown";
}
