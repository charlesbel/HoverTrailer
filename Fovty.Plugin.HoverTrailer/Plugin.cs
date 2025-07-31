using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fovty.Plugin.HoverTrailer.Configuration;
using Fovty.Plugin.HoverTrailer.Exceptions;
using Fovty.Plugin.HoverTrailer.Helpers;
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
    private readonly ILogger<Plugin> _logger;

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
        _logger = logger;

        try
        {
            LoggingHelper.LogInformation(_logger, "HoverTrailer plugin is initializing...");

            // Validate configuration on startup
            ValidateConfiguration();

            // Handle client script injection if enabled
            if (Configuration.InjectClientScript)
            {
                InitializeClientScriptInjection(applicationPaths);
            }

            LoggingHelper.LogInformation(_logger, "HoverTrailer plugin initialization completed successfully.");
        }
        catch (ConfigurationException ex)
        {
            LoggingHelper.LogError(_logger, "Configuration error during plugin initialization: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(logger, "Unexpected error during plugin initialization: {Message}", ex.Message);
            throw new PluginInitializationException("An unexpected error occurred during plugin initialization.", ex);
        }
    }

    /// <inheritdoc />
    public override string Name => "HoverTrailer";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("82c71cde-a52b-44f1-a18e-d93eb6a35ed0");

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

    private void ValidateConfiguration()
    {
        try
        {
            LoggingHelper.LogDebug(_logger, "Validating plugin configuration...");
            Configuration.Validate();
            LoggingHelper.LogDebug(_logger, "Plugin configuration validation completed successfully.");
        }
        catch (ConfigurationException ex)
        {
            LoggingHelper.LogError(_logger, "Configuration validation failed: {ValidationErrors}", ex.Message);
            // Re-throw to be caught in the constructor for logging
            throw;
        }
    }

    private void InitializeClientScriptInjection(IApplicationPaths applicationPaths)
    {
        try
        {
            LoggingHelper.LogDebug(_logger, "Initializing client script injection...");
            var webPath = applicationPaths.WebPath;
            if (string.IsNullOrWhiteSpace(webPath))
            {
                LoggingHelper.LogWarning(_logger, "Web path is not available, skipping client script injection.");
                return;
            }

            var indexFile = Path.Combine(webPath, "index.html");
            if (!File.Exists(indexFile))
            {
                LoggingHelper.LogWarning(_logger, "index.html not found at {IndexFile}, skipping script injection.", indexFile);
                return;
            }

            ProcessIndexFileScriptInjection(indexFile);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, "Error during client script injection: {Message}", ex.Message);
            // Don't rethrow, allow server to start
        }
    }

    private void ProcessIndexFileScriptInjection(string indexFile)
    {
        try
        {
            LoggingHelper.LogDebug(_logger, "Processing index file for script injection: {IndexFile}", indexFile);
            var indexContents = File.ReadAllText(indexFile);

            // Using a unique attribute to identify our injected block
            const string injectionIdentifier = "data-plugin-hovertrailer";
            if (indexContents.Contains(injectionIdentifier))
            {
                LoggingHelper.LogInformation(_logger, "HoverTrailer scripts already found in {IndexFile}. No changes made.", indexFile);
                return;
            }

            // Remove any old script tags from previous versions to ensure a clean slate
            indexContents = Regex.Replace(indexContents, @".*\s*", "", RegexOptions.Singleline);
            indexContents = Regex.Replace(indexContents, @"<script plugin=""HoverTrailer"".*?></script>\s*", "", RegexOptions.Singleline);


            var tagsToInject = @$"
    <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/gh/charlesbel/HoverTrailer/web/slideshowpure.css"" {injectionIdentifier} />
    <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/gh/charlesbel/HoverTrailer/web/hover_trailer.css"" />
    <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/gh/charlesbel/HoverTrailer/web/zombie_revived.css"" />
    <script async src=""https://cdn.jsdelivr.net/gh/charlesbel/HoverTrailer/web/slideshowpure.js""></script>
    <script async src=""https://cdn.jsdelivr.net/gh/charlesbel/HoverTrailer/web/hover_trailer.js""></script>
    ";

            var headEndIndex = indexContents.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headEndIndex == -1)
            {
                LoggingHelper.LogWarning(_logger, "Could not find closing </head> tag in {IndexFile}. Skipping injection.", indexFile);
                return;
            }

            indexContents = indexContents.Insert(headEndIndex, tagsToInject);
            File.WriteAllText(indexFile, indexContents);

            LoggingHelper.LogInformation(_logger, "Successfully injected HoverTrailer scripts into {IndexFile}.", indexFile);
        }
        catch (IOException ex)
        {
            LoggingHelper.LogError(_logger, "File I/O error while processing {IndexFile}: {Message}", indexFile, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            LoggingHelper.LogError(_logger, "Access denied while modifying {IndexFile}: {Message}", indexFile, ex.Message);
        }
        catch (Exception ex)
        {
            LoggingHelper.LogError(_logger, "An unexpected error occurred while processing {IndexFile}: {Message}", indexFile, ex.Message);
        }
    }
}
