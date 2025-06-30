using System;

namespace Fovty.Plugin.HoverTrailer.Exceptions;

/// <summary>
/// Base exception class for all HoverTrailer plugin specific exceptions.
/// </summary>
public abstract class HoverTrailerException : Exception
{
    /// <summary>
    /// Gets the error code associated with this exception.
    /// </summary>
    public abstract string ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HoverTrailerException"/> class.
    /// </summary>
    protected HoverTrailerException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HoverTrailerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    protected HoverTrailerException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HoverTrailerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    protected HoverTrailerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when plugin configuration is invalid or missing.
/// </summary>
public class ConfigurationException : HoverTrailerException
{
    /// <inheritdoc />
    public override string ErrorCode => "CONFIG_ERROR";

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
    /// </summary>
    public ConfigurationException()
        : base("Configuration error occurred")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}


/// <summary>
/// Exception thrown when plugin initialization fails.
/// </summary>
public class PluginInitializationException : HoverTrailerException
{
    /// <inheritdoc />
    public override string ErrorCode => "PLUGIN_INIT_ERROR";

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginInitializationException"/> class.
    /// </summary>
    public PluginInitializationException()
        : base("Plugin initialization error occurred")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginInitializationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public PluginInitializationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginInitializationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PluginInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}


/// <summary>
/// Exception thrown when trailer operations fail.
/// </summary>
public class TrailerException : HoverTrailerException
{
    /// <inheritdoc />
    public override string ErrorCode => "TRAILER_ERROR";

    /// <summary>
    /// Initializes a new instance of the <see cref="TrailerException"/> class.
    /// </summary>
    public TrailerException()
        : base("Trailer operation error occurred")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrailerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public TrailerException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrailerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public TrailerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}