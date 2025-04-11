// <copyright file="PluginConfiguration.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MediaUploader.Configuration;

using MediaBrowser.Model.Plugins;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the target directory path for uploads.
    /// </summary>
    public string UploadPath { get; set; } = string.Empty;
}
