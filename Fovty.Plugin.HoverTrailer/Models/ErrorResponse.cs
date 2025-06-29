using System;
using System.Collections.Generic;
using System.Linq;

namespace Fovty.Plugin.HoverTrailer.Models;

/// <summary>
/// Represents a structured error response for API endpoints.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional error details.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the error occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the request ID for tracing purposes.
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Gets or sets validation errors if applicable.
    /// </summary>
    public IEnumerable<ValidationError>? ValidationErrors { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorResponse"/> class.
    /// </summary>
    public ErrorResponse()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorResponse"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    public ErrorResponse(string errorCode, string message)
    {
        ErrorCode = errorCode;
        Message = message;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorResponse"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="details">Additional error details.</param>
    public ErrorResponse(string errorCode, string message, string details)
    {
        ErrorCode = errorCode;
        Message = message;
        Details = details;
    }

    /// <summary>
    /// Creates an error response from an exception.
    /// </summary>
    /// <param name="exception">The exception to create the response from.</param>
    /// <param name="requestId">Optional request ID for tracing.</param>
    /// <returns>An ErrorResponse instance.</returns>
    public static ErrorResponse FromException(Exception exception, string? requestId = null)
    {
        var errorCode = exception switch
        {
            Exceptions.ConfigurationException => "CONFIG_ERROR",
            Exceptions.TMDbApiException => "TMDB_API_ERROR",
            Exceptions.YtDlpException => "YTDLP_ERROR",
            Exceptions.TrailerException => "TRAILER_ERROR",
            Exceptions.HoverTrailerException hte => hte.ErrorCode,
            ArgumentException => "INVALID_ARGUMENT",
            UnauthorizedAccessException => "UNAUTHORIZED",
            System.IO.FileNotFoundException => "FILE_NOT_FOUND",
            System.IO.DirectoryNotFoundException => "DIRECTORY_NOT_FOUND",
            System.Net.Http.HttpRequestException => "HTTP_ERROR",
            TimeoutException => "TIMEOUT_ERROR",
            _ => "INTERNAL_ERROR"
        };

        return new ErrorResponse(errorCode, exception.Message, exception.ToString())
        {
            RequestId = requestId
        };
    }

    /// <summary>
    /// Creates an error response for validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <param name="requestId">Optional request ID for tracing.</param>
    /// <returns>An ErrorResponse instance.</returns>
    public static ErrorResponse FromValidationErrors(IEnumerable<ValidationError> validationErrors, string? requestId = null)
    {
        return new ErrorResponse("VALIDATION_ERROR", "One or more validation errors occurred")
        {
            ValidationErrors = validationErrors,
            RequestId = requestId
        };
    }

    /// <summary>
    /// Creates an error response for configuration validation errors.
    /// </summary>
    /// <param name="configurationErrors">The configuration validation errors.</param>
    /// <param name="requestId">Optional request ID for tracing.</param>
    /// <returns>An ErrorResponse instance.</returns>
    public static ErrorResponse FromConfigurationErrors(IEnumerable<string> configurationErrors, string? requestId = null)
    {
        var validationErrors = configurationErrors.Select(error => new ValidationError("Configuration", error));
        return FromValidationErrors(validationErrors, requestId);
    }
}

/// <summary>
/// Represents a validation error for a specific field or property.
/// </summary>
public class ValidationError
{
    /// <summary>
    /// Gets or sets the name of the field or property that has the error.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the validation error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the attempted value that caused the validation error.
    /// </summary>
    public object? AttemptedValue { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationError"/> class.
    /// </summary>
    public ValidationError()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationError"/> class.
    /// </summary>
    /// <param name="field">The field name.</param>
    /// <param name="message">The error message.</param>
    public ValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationError"/> class.
    /// </summary>
    /// <param name="field">The field name.</param>
    /// <param name="message">The error message.</param>
    /// <param name="attemptedValue">The attempted value.</param>
    public ValidationError(string field, string message, object? attemptedValue)
    {
        Field = field;
        Message = message;
        AttemptedValue = attemptedValue;
    }
}