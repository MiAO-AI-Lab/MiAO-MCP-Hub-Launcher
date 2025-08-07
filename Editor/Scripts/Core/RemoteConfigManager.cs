#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using com.MiAO.MCP.Launcher.Extensions;

namespace com.MiAO.MCP.Launcher.Config
{
    /// <summary>
    /// Manages remote configuration for MCP Hub extension registry
    /// Handles fetching, caching, and updating extension information from remote sources
    /// </summary>
    public static class RemoteConfigManager
    {
        // Configuration URLs - primary and fallback sources
        private static readonly string[] CONFIG_URLS = new string[]
        {
            "https://raw.githubusercontent.com/MiAO-AI-Lab/MiAO-MCP-Hub-Launcher/main/Assets/MiAO-MCP-Hub-Launcher/Editor/Config/extensions.json",
            "https://cdn.jsdelivr.net/gh/MiAO-AI-Lab/MiAO-MCP-Hub-Launcher@main/Assets/MiAO-MCP-Hub-Launcher/Editor/Config/extensions.json"
        };

        // Local cache settings
        private const string CACHE_DIRECTORY = "Library/MCP-Hub-Cache";
        private const string CACHE_FILE_NAME = "remote-config.json";
        private const string CACHE_METADATA_FILE = "cache-metadata.json";
        private const int DEFAULT_CACHE_EXPIRY_HOURS = 24;

        // EditorPrefs keys for persistent settings
        private const string PREF_LAST_FETCH_TIME = "mcp-hub:last-fetch-time";
        private const string PREF_CONFIG_VERSION = "mcp-hub:config-version";
        private const string PREF_CACHE_EXPIRY_HOURS = "mcp-hub:cache-expiry-hours";
        private const string PREF_AUTO_UPDATE_ENABLED = "mcp-hub:auto-update-enabled";

        // Events
        public static event Action<RemoteConfigModel> OnConfigurationUpdated;
        public static event Action<string> OnConfigurationError;
        public static event Action<float> OnDownloadProgress;

        // Cache
        private static RemoteConfigModel s_CachedConfig;
        private static DateTime s_LastFetchTime;
        private static bool s_IsInitialized = false;

        /// <summary>
        /// Gets the current cached configuration, initializing if needed
        /// </summary>
        public static RemoteConfigModel CurrentConfig
        {
            get
            {
                if (!s_IsInitialized)
                {
                    Initialize();
                }
                return s_CachedConfig;
            }
        }

        /// <summary>
        /// Gets whether auto-update is enabled
        /// </summary>
        public static bool AutoUpdateEnabled
        {
            get => EditorPrefs.GetBool(PREF_AUTO_UPDATE_ENABLED, true);
            set => EditorPrefs.SetBool(PREF_AUTO_UPDATE_ENABLED, value);
        }

        /// <summary>
        /// Gets the cache expiry time in hours
        /// </summary>
        public static int CacheExpiryHours
        {
            get => EditorPrefs.GetInt(PREF_CACHE_EXPIRY_HOURS, DEFAULT_CACHE_EXPIRY_HOURS);
            set => EditorPrefs.SetInt(PREF_CACHE_EXPIRY_HOURS, Mathf.Max(1, value));
        }

        /// <summary>
        /// Initializes the configuration manager
        /// </summary>
        public static void Initialize()
        {
            if (s_IsInitialized) return;

            Debug.Log("[MCP Hub] Initializing Remote Configuration Manager");

            // Create cache directory if it doesn't exist
            EnsureCacheDirectoryExists();

            // Load cached configuration
            LoadCachedConfiguration();

            // Check if we need to fetch fresh configuration
            if (ShouldFetchFreshConfiguration())
            {
                _ = FetchConfigurationAsync(); // Fire and forget for initialization
            }

            s_IsInitialized = true;
        }

        /// <summary>
        /// Fetches the latest configuration from remote sources
        /// </summary>
        public static async Task<bool> FetchConfigurationAsync(bool forceRefresh = false)
        {
            try
            {
                Debug.Log("[MCP Hub] Fetching remote configuration...");

                if (!forceRefresh && !ShouldFetchFreshConfiguration())
                {
                    Debug.Log("[MCP Hub] Configuration is up to date, skipping fetch");
                    return true;
                }

                string configJson = null;
                
                // Try each configuration URL until one succeeds
                for (int i = 0; i < CONFIG_URLS.Length; i++)
                {
                    var url = CONFIG_URLS[i];
                    Debug.Log($"[MCP Hub] Trying config source {i + 1}/{CONFIG_URLS.Length}: {url}");
                    
                    configJson = await DownloadConfigurationAsync(url);
                    if (!string.IsNullOrEmpty(configJson))
                    {
                        Debug.Log($"[MCP Hub] Successfully downloaded configuration from source {i + 1}");
                        break;
                    }
                    
                    Debug.LogWarning($"[MCP Hub] Failed to download from source {i + 1}, trying next...");
                }

                if (string.IsNullOrEmpty(configJson))
                {
                    throw new Exception("Failed to download configuration from all sources");
                }

                // Parse and validate configuration
                var config = ParseConfiguration(configJson);
                if (config == null)
                {
                    throw new Exception("Failed to parse remote configuration");
                }

                // Cache the configuration
                await CacheConfigurationAsync(configJson, config);

                // Update in-memory cache
                s_CachedConfig = config;
                s_LastFetchTime = DateTime.Now;

                // Update preferences
                EditorPrefs.SetString(PREF_LAST_FETCH_TIME, s_LastFetchTime.ToBinary().ToString());
                EditorPrefs.SetString(PREF_CONFIG_VERSION, config.version ?? "unknown");

                Debug.Log($"[MCP Hub] Successfully updated configuration to version {config.version}");

                // Notify listeners
                OnConfigurationUpdated?.Invoke(config);

                return true;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to fetch remote configuration: {ex.Message}";
                Debug.LogError($"[MCP Hub] {errorMessage}");
                OnConfigurationError?.Invoke(errorMessage);
                return false;
            }
        }

        /// <summary>
        /// Downloads configuration JSON from a URL
        /// </summary>
        private static async Task<string> DownloadConfigurationAsync(string url)
        {
            try
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    // Set timeout and headers
                    request.timeout = 30;
                    request.SetRequestHeader("User-Agent", "MCP-Hub-Launcher/1.0");
                    request.SetRequestHeader("Cache-Control", "no-cache");

                    // Start the request
                    var operation = request.SendWebRequest();

                    // Wait for completion with progress updates
                    while (!operation.isDone)
                    {
                        OnDownloadProgress?.Invoke(operation.progress);
                        await Task.Delay(100);
                    }

                    OnDownloadProgress?.Invoke(1.0f);

                    // Check for errors
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        return request.downloadHandler.text;
                    }
                    else
                    {
                        Debug.LogWarning($"[MCP Hub] Request failed: {request.error}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Download exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses configuration JSON into RemoteConfigModel
        /// </summary>
        private static RemoteConfigModel ParseConfiguration(string json)
        {
            try
            {
                var config = JsonUtility.FromJson<RemoteConfigModel>(json);
                
                // Validate required fields
                if (config?.extensions?.packages == null)
                {
                    Debug.LogError("[MCP Hub] Invalid configuration: missing extensions data");
                    return null;
                }

                Debug.Log($"[MCP Hub] Parsed configuration with {config.extensions.packages.Length} packages");
                return config;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Failed to parse configuration JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Caches configuration to local storage
        /// </summary>
        private static async Task CacheConfigurationAsync(string configJson, RemoteConfigModel config)
        {
            try
            {
                var cacheFilePath = GetCacheFilePath();
                var metadataFilePath = GetCacheMetadataFilePath();

                // Write configuration file
                await File.WriteAllTextAsync(cacheFilePath, configJson);

                // Write metadata
                var metadata = new CacheMetadata
                {
                    cachedAt = DateTime.Now.ToBinary(),
                    configVersion = config.version,
                    originalUrl = CONFIG_URLS[0], // Primary URL
                    expiryHours = CacheExpiryHours
                };

                var metadataJson = JsonUtility.ToJson(metadata, true);
                await File.WriteAllTextAsync(metadataFilePath, metadataJson);

                Debug.Log("[MCP Hub] Successfully cached configuration");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Failed to cache configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads cached configuration from local storage
        /// </summary>
        private static void LoadCachedConfiguration()
        {
            try
            {
                var cacheFilePath = GetCacheFilePath();
                if (!File.Exists(cacheFilePath))
                {
                    Debug.Log("[MCP Hub] No cached configuration found");
                    return;
                }

                var configJson = File.ReadAllText(cacheFilePath);
                var config = ParseConfiguration(configJson);
                
                if (config != null)
                {
                    s_CachedConfig = config;
                    
                    // Load cache metadata
                    LoadCacheMetadata();
                    
                    Debug.Log($"[MCP Hub] Loaded cached configuration version {config.version}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Failed to load cached configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads cache metadata
        /// </summary>
        private static void LoadCacheMetadata()
        {
            try
            {
                var metadataFilePath = GetCacheMetadataFilePath();
                if (File.Exists(metadataFilePath))
                {
                    var metadataJson = File.ReadAllText(metadataFilePath);
                    var metadata = JsonUtility.FromJson<CacheMetadata>(metadataJson);
                    
                    if (metadata != null)
                    {
                        s_LastFetchTime = DateTime.FromBinary(metadata.cachedAt);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Failed to load cache metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if fresh configuration should be fetched
        /// </summary>
        private static bool ShouldFetchFreshConfiguration()
        {
            if (s_CachedConfig == null) return true;
            if (!AutoUpdateEnabled) return false;
            
            var expiry = TimeSpan.FromHours(CacheExpiryHours);
            return DateTime.Now - s_LastFetchTime > expiry;
        }

        /// <summary>
        /// Gets all extension registry entries from remote configuration
        /// </summary>
        public static Dictionary<string, ExtensionRegistryEntry> GetRemoteExtensionRegistry()
        {
            var registry = new Dictionary<string, ExtensionRegistryEntry>();
            
            if (CurrentConfig?.extensions?.packages != null)
            {
                foreach (var package in CurrentConfig.extensions.packages)
                {
                    try
                    {
                        var registryEntry = package.ToRegistryEntry();
                        registry[package.id] = registryEntry;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP Hub] Failed to convert package {package.id}: {ex.Message}");
                    }
                }
            }
            
            Debug.Log($"[MCP Hub] Generated registry with {registry.Count} entries from remote config");
            return registry;
        }

        /// <summary>
        /// Forces a cache refresh
        /// </summary>
        public static async Task<bool> RefreshCacheAsync()
        {
            Debug.Log("[MCP Hub] Forcing cache refresh...");
            return await FetchConfigurationAsync(true);
        }

        /// <summary>
        /// Clears the local cache
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                var cacheFilePath = GetCacheFilePath();
                var metadataFilePath = GetCacheMetadataFilePath();
                
                if (File.Exists(cacheFilePath))
                    File.Delete(cacheFilePath);
                    
                if (File.Exists(metadataFilePath))
                    File.Delete(metadataFilePath);
                    
                s_CachedConfig = null;
                s_LastFetchTime = DateTime.MinValue;
                
                Debug.Log("[MCP Hub] Cache cleared successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Failed to clear cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets information about the current cache state
        /// </summary>
        public static CacheInfo GetCacheInfo()
        {
            return new CacheInfo
            {
                HasCache = s_CachedConfig != null,
                LastFetchTime = s_LastFetchTime,
                ConfigVersion = s_CachedConfig?.version ?? "unknown",
                IsExpired = ShouldFetchFreshConfiguration(),
                ExpiryHours = CacheExpiryHours,
                AutoUpdateEnabled = AutoUpdateEnabled
            };
        }

        // Helper methods
        private static void EnsureCacheDirectoryExists()
        {
            var cacheDir = Path.Combine(Application.dataPath, "..", CACHE_DIRECTORY);
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
        }

        private static string GetCacheFilePath()
        {
            return Path.Combine(Application.dataPath, "..", CACHE_DIRECTORY, CACHE_FILE_NAME);
        }

        private static string GetCacheMetadataFilePath()
        {
            return Path.Combine(Application.dataPath, "..", CACHE_DIRECTORY, CACHE_METADATA_FILE);
        }
    }

    /// <summary>
    /// Cache metadata for tracking cached configuration
    /// </summary>
    [Serializable]
    public class CacheMetadata
    {
        public long cachedAt;
        public string configVersion;
        public string originalUrl;
        public int expiryHours;
    }

    /// <summary>
    /// Information about the current cache state
    /// </summary>
    [Serializable]
    public class CacheInfo
    {
        public bool HasCache;
        public DateTime LastFetchTime;
        public string ConfigVersion;
        public bool IsExpired;
        public int ExpiryHours;
        public bool AutoUpdateEnabled;
    }
}
#endif 