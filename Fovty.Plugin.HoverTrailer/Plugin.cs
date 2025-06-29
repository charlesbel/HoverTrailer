using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fovty.Plugin.HoverTrailer.Configuration;
using Fovty.Plugin.HoverTrailer.Helpers;
using Fovty.Plugin.HoverTrailer.Exceptions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Fovty.Plugin.HoverTrailer;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger,
        IServerConfigurationManager configurationManager)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        try
        {
            LoggingHelper.LogInformation(logger, "Fovty HoverTrailer plugin is initializing...");

            // Validate configuration on startup
            ValidateConfiguration(logger);

            // Initialize client script injection if enabled
            if (Configuration.InjectClientScript)
            {
                InitializeClientScriptInjection(applicationPaths, configurationManager, logger);
            }

            LoggingHelper.LogInformation(logger, "Fovty HoverTrailer plugin initialization completed successfully.");
        }
        catch (ConfigurationException ex)
        {
            LoggingHelper.LogError(logger, "Configuration error during plugin initialization: {Message}", ex.Message);
            LoggingHelper.LogDebug(logger, "Configuration error details: {Exception}", ex.ToString());
            throw; // Re-throw to prevent plugin from loading with invalid config
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(logger, "Unexpected error during plugin initialization: {Message}", ex.Message);
            LoggingHelper.LogDebug(logger, "Initialization error details: {Exception}", ex.ToString());
            throw new PluginInitializationException("Failed to initialize HoverTrailer plugin", ex);
        }
    }

    /// <summary>
    /// Validates the plugin configuration and throws ConfigurationException if invalid.
    /// </summary>
    /// <param name="logger">Logger instance for debug output.</param>
    private void ValidateConfiguration(ILogger<Plugin> logger)
    {
        try
        {
            LoggingHelper.LogDebug(logger, "Validating plugin configuration...");

            if (!Configuration.IsValid())
            {
                var errors = Configuration.GetValidationErrors().ToList();
                var errorMessage = string.Join("; ", errors);

                LoggingHelper.LogError(logger, "Configuration validation failed: {ValidationErrors}", errorMessage);
                throw new ConfigurationException($"Invalid plugin configuration: {errorMessage}");
            }

            LoggingHelper.LogDebug(logger, "Plugin configuration validation completed successfully.");
        }
        catch (ConfigurationException)
        {
            throw; // Re-throw configuration exceptions as-is
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(logger, "Unexpected error during configuration validation: {Message}", ex.Message);
            throw new ConfigurationException("Failed to validate plugin configuration", ex);
        }
    }

    /// <summary>
    /// Initializes client script injection functionality.
    /// </summary>
    /// <param name="applicationPaths">Application paths for locating web files.</param>
    /// <param name="configurationManager">Configuration manager for network settings.</param>
    /// <param name="logger">Logger instance for debug output.</param>
    private void InitializeClientScriptInjection(
        IApplicationPaths applicationPaths,
        IServerConfigurationManager configurationManager,
        ILogger<Plugin> logger)
    {
        try
        {
            LoggingHelper.LogDebug(logger, "Initializing client script injection...");

            if (string.IsNullOrWhiteSpace(applicationPaths.WebPath))
            {
                LoggingHelper.LogWarning(logger, "Web path is not available, skipping client script injection.");
                return;
            }

            var indexFile = Path.Combine(applicationPaths.WebPath, "index.html");
            if (!File.Exists(indexFile))
            {
                LoggingHelper.LogWarning(logger, "Index file not found at {IndexFile}, skipping client script injection.", indexFile);
                return;
            }

            var basePath = GetBasePathFromConfiguration(configurationManager, logger);
            ProcessIndexFileScriptInjection(indexFile, basePath, logger);

            LoggingHelper.LogDebug(logger, "Client script injection initialization completed.");
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(logger, "Error during client script injection initialization: {Message}", ex.Message);
            LoggingHelper.LogDebug(logger, "Script injection error details: {Exception}", ex.ToString());
            throw new PluginInitializationException("Failed to initialize client script injection", ex);
        }
    }

    /// <summary>
    /// Retrieves the base path from Jellyfin's network configuration.
    /// </summary>
    /// <param name="configurationManager">Configuration manager instance.</param>
    /// <param name="logger">Logger instance for debug output.</param>
    /// <returns>The configured base path or empty string if unavailable.</returns>
    private string GetBasePathFromConfiguration(IServerConfigurationManager configurationManager, ILogger<Plugin> logger)
    {
        try
        {
            LoggingHelper.LogDebug(logger, "Retrieving base path from network configuration...");

            var networkConfig = configurationManager.GetConfiguration("network");
            var configType = networkConfig.GetType();
            var basePathField = configType.GetProperty("BaseUrl");
            var confBasePath = basePathField?.GetValue(networkConfig)?.ToString()?.Trim('/');

            var basePath = string.IsNullOrEmpty(confBasePath) ? "" : "/" + confBasePath;

            LoggingHelper.LogDebug(logger, "Retrieved base path: '{BasePath}'", basePath);
            return basePath;
        }
        catch (Exception ex)
        {
            LoggingHelper.LogWarning(logger, "Unable to get base path from network configuration, using default '/': {Message}", ex.Message);
            LoggingHelper.LogDebug(logger, "Base path retrieval error details: {Exception}", ex.ToString());
            return "";
        }
    }

    /// <summary>
    /// Processes the index.html file for script injection.
    /// </summary>
    /// <param name="indexFile">Path to the index.html file.</param>
    /// <param name="basePath">Base path for script URL.</param>
    /// <param name="logger">Logger instance for debug output.</param>
    private void ProcessIndexFileScriptInjection(string indexFile, string basePath, ILogger<Plugin> logger)
    {
        try
        {
            LoggingHelper.LogDebug(logger, "Processing index file for script injection: {IndexFile}", indexFile);

            string indexContents = File.ReadAllText(indexFile);
            string scriptReplace = "<script plugin=\"HoverTrailer\".*?></script>";
            string scriptElement = string.Format("<script plugin=\"HoverTrailer\" version=\"1.0.0.0\" src=\"{0}/HoverTrailer/ClientScript\" defer></script>", basePath);

            if (indexContents.Contains(scriptElement))
            {
                LoggingHelper.LogInformation(logger, "Found HoverTrailer client script already injected in {IndexFile}", indexFile);
                return;
            }

            LoggingHelper.LogInformation(logger, "Attempting to inject HoverTrailer script code in {IndexFile}", indexFile);

            // Replace old HoverTrailer scripts
            indexContents = Regex.Replace(indexContents, scriptReplace, "");

            // Insert script last in body
            int bodyClosing = indexContents.LastIndexOf("</body>");
            if (bodyClosing == -1)
            {
                LoggingHelper.LogWarning(logger, "Could not find closing body tag in {IndexFile}, skipping script injection.", indexFile);
                return;
            }

            indexContents = indexContents.Insert(bodyClosing, scriptElement);
            File.WriteAllText(indexFile, indexContents);

            LoggingHelper.LogInformation(logger, "Successfully injected HoverTrailer script code in {IndexFile}", indexFile);
        }
        catch (IOException ex)
        {
            LoggingHelper.LogError(logger, "File I/O error while processing {IndexFile}: {Message}", indexFile, ex.Message);
            throw new PluginInitializationException($"Failed to modify index file: {indexFile}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LoggingHelper.LogError(logger, "Access denied while modifying {IndexFile}: {Message}", indexFile, ex.Message);
            throw new PluginInitializationException($"Insufficient permissions to modify index file: {indexFile}", ex);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(logger, "Unexpected error while processing {IndexFile}: {Message}", indexFile, ex.Message);
            LoggingHelper.LogDebug(logger, "Index file processing error details: {Exception}", ex.ToString());
            throw new PluginInitializationException($"Failed to process index file: {indexFile}", ex);
        }
    }

    /// <inheritdoc />
    public override string Name => "Fovty HoverTrailer";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("12a5d8c3-4b9e-4f2a-a1c6-8d7e9f0a1b2c");

    /// <inheritdoc />
    public override string Description => "Provides hover trailer preview functionality for movies in your Jellyfin library.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}