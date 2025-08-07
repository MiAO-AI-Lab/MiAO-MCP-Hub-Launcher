#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using com.MiAO.MCP.Launcher.Extensions;

namespace com.MiAO.MCP.Launcher.Config
{
    /// <summary>
    /// Root configuration model for remote MCP Hub configuration
    /// Contains extension registry information
    /// </summary>
    [Serializable]
    public class RemoteConfigModel
    {
        [SerializeField] public string version;
        [SerializeField] public ExtensionRegistry extensions;
    }

    /// <summary>
    /// Extension registry containing all available MCP extensions
    /// </summary>
    [Serializable]
    public class ExtensionRegistry
    {
        [SerializeField] public RemoteExtensionInfo[] packages;
    }

    /// <summary>
    /// Remote extension information that can be deserialized from JSON
    /// Core fields for extension packages
    /// </summary>
    [Serializable]
    public class RemoteExtensionInfo
    {
        [SerializeField] public string id;
        [SerializeField] public string displayName;
        [SerializeField] public string description;
        [SerializeField] public string author;
        [SerializeField] public string latestVersion;
        [SerializeField] public string category;
        [SerializeField] public string packageUrl;
        [SerializeField] public string[] dependencies;

        /// <summary>
        /// Converts RemoteExtensionInfo to ExtensionRegistryEntry for internal use
        /// </summary>
        public ExtensionRegistryEntry ToRegistryEntry()
        {
            // Parse category enum
            ExtensionCategory parsedCategory = ExtensionCategory.Community;
            if (Enum.TryParse<ExtensionCategory>(category, true, out var categoryResult))
            {
                parsedCategory = categoryResult;
            }

            return new ExtensionRegistryEntry(
                id,
                displayName,
                description,
                author,
                parsedCategory,
                packageUrl,
                latestVersion,
                null, // documentationUrl
                null, // keywords
                dependencies
            );
        }
    }
}
#endif 