using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Fovty.Plugin.HoverTrailer.Configuration;
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

        logger.LogInformation("Fovty HoverTrailer plugin is initializing...");

        if (Configuration.InjectClientScript)
        {
            if (!string.IsNullOrWhiteSpace(applicationPaths.WebPath))
            {
                var indexFile = Path.Combine(applicationPaths.WebPath, "index.html");
                if (File.Exists(indexFile))
                {
                    string indexContents = File.ReadAllText(indexFile);
                    string basePath = "";

                    // Get base path from network config
                    try
                    {
                        var networkConfig = configurationManager.GetConfiguration("network");
                        var configType = networkConfig.GetType();
                        var basePathField = configType.GetProperty("BaseUrl");
                        var confBasePath = basePathField?.GetValue(networkConfig)?.ToString()?.Trim('/');

                        if (!string.IsNullOrEmpty(confBasePath)) basePath = "/" + confBasePath.ToString();
                    }
                    catch (Exception e)
                    {
                        logger.LogError("Unable to get base path from config, using '/': {0}", e);
                    }

                    // Don't run if script already exists
                    string scriptReplace = "<script plugin=\"HoverTrailer\".*?></script>";
                    string scriptElement = string.Format("<script plugin=\"HoverTrailer\" version=\"1.0.0.0\" src=\"{0}/HoverTrailer/ClientScript\" defer></script>", basePath);

                    if (!indexContents.Contains(scriptElement))
                    {
                        logger.LogInformation("Attempting to inject HoverTrailer script code in {0}", indexFile);

                        // Replace old HoverTrailer scripts
                        indexContents = Regex.Replace(indexContents, scriptReplace, "");

                        // Insert script last in body
                        int bodyClosing = indexContents.LastIndexOf("</body>");
                        if (bodyClosing != -1)
                        {
                            indexContents = indexContents.Insert(bodyClosing, scriptElement);

                            try
                            {
                                File.WriteAllText(indexFile, indexContents);
                                logger.LogInformation("Finished injecting HoverTrailer script code in {0}", indexFile);
                            }
                            catch (Exception e)
                            {
                                logger.LogError("Encountered exception while writing to {0}: {1}", indexFile, e);
                            }
                        }
                        else
                        {
                            logger.LogInformation("Could not find closing body tag in {0}", indexFile);
                        }
                    }
                    else
                    {
                        logger.LogInformation("Found HoverTrailer client script injected in {0}", indexFile);
                    }
                }
            }
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