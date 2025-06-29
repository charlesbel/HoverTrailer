using System;
using Microsoft.Extensions.Logging;

namespace Fovty.Plugin.HoverTrailer.Helpers;

/// <summary>
/// Provides centralized logging functionality that respects plugin configuration settings.
/// </summary>
public static class LoggingHelper
{
    /// <summary>
    /// Logs a debug message if debug logging is enabled in the plugin configuration.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogDebugIfEnabled<T>(ILogger<T> logger, string message, params object[] args)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableDebugLogging == true)
        {
            logger.LogDebug(message, args);
        }
    }

    /// <summary>
    /// Logs a debug message if debug logging is enabled in the plugin configuration.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogDebug<T>(ILogger<T> logger, string message, params object[] args)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableDebugLogging == true)
        {
            logger.LogDebug(message, args);
        }
    }

    /// <summary>
    /// Logs an information message if debug logging is enabled in the plugin configuration.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogInformationIfEnabled<T>(ILogger<T> logger, string message, params object[] args)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableDebugLogging == true)
        {
            logger.LogInformation(message, args);
        }
    }

    /// <summary>
    /// Logs a warning message if debug logging is enabled in the plugin configuration.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogWarningIfEnabled<T>(ILogger<T> logger, string message, params object[] args)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.EnableDebugLogging == true)
        {
            logger.LogWarning(message, args);
        }
    }

    /// <summary>
    /// Always logs an error message regardless of debug logging settings.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogError<T>(ILogger<T> logger, Exception exception, string message, params object[] args)
    {
        logger.LogError(exception, message, args);
    }

    /// <summary>
    /// Always logs an error message regardless of debug logging settings.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogError<T>(ILogger<T> logger, string message, params object[] args)
    {
        logger.LogError(message, args);
    }

    /// <summary>
    /// Always logs an information message regardless of debug logging settings.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogInformation<T>(ILogger<T> logger, string message, params object[] args)
    {
        logger.LogInformation(message, args);
    }

    /// <summary>
    /// Always logs a warning message regardless of debug logging settings.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The message to log.</param>
    /// <param name="args">Optional message arguments.</param>
    public static void LogWarning<T>(ILogger<T> logger, string message, params object[] args)
    {
        logger.LogWarning(message, args);
    }
}