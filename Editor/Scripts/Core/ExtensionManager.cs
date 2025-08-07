#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using com.MiAO.MCP.Launcher.Config;
using Debug = UnityEngine.Debug;

namespace com.MiAO.MCP.Launcher.Extensions
{
    /// <summary>
    /// Manages MCP extension packages - installation, removal, updates, and discovery
    /// Integrates with Unity Package Manager for actual package operations
    /// </summary>
    public static class ExtensionManager
    {
        private static readonly Dictionary<string, ExtensionPackageInfo> s_KnownExtensions = 
            new Dictionary<string, ExtensionPackageInfo>();
        
        private static readonly Dictionary<string, UnityEditor.PackageManager.PackageInfo> s_InstalledPackages = 
            new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();

        // Known MCP extension packages registry
        private static readonly Dictionary<string, ExtensionRegistryEntry> s_ExtensionRegistry = 
            new Dictionary<string, ExtensionRegistryEntry>
            {
                {
                    "com.miao.mcp",
                    new ExtensionRegistryEntry(
                        "com.miao.mcp",
                        "MiAO MCP Core Framework(required)",
                        "Core framework for Unity MCP",
                        "MiAO",
                        ExtensionCategory.Essential,
                        "https://github.com/MiAO-AI-Lab/MiAO-MCP-for-Unity.git"
                    )
                },
                {
                    "com.miao.mcp.essential",
                    new ExtensionRegistryEntry(
                        "com.miao.mcp.essential",
                        "MiAO MCP Essential Tools",
                        "Essential tools for basic Unity MCP operations including GameObject, Scene, Assets, Component, and Editor manipulation.",
                        "MiAO",
                        ExtensionCategory.Essential,
                        "https://github.com/MiAO-AI-Lab/Unity-MCP-Tools-Essential.git"
                    )
                },
                {
                    "com.miao.mcp.behavior-designer-tools",
                    new ExtensionRegistryEntry(
                        "com.miao.mcp.behavior-designer-tools",
                        "MiAO MCP Behavior Designer Tools",
                        "Behavior Designer Tools for Unity MCP",
                        "MiAO",
                        ExtensionCategory.Essential,
                        "https://github.com/MiAO-AI-Lab/Unity-MCP-Tools-Behavior-Designer.git"
                    )
                }
            };

        /// <summary>
        /// Event triggered when extension list is updated
        /// </summary>
        public static event Action OnExtensionsUpdated;

        /// <summary>
        /// Event triggered when an extension installation completes
        /// </summary>
        public static event Action<ExtensionPackageInfo, bool> OnExtensionInstalled;

        /// <summary>
        /// Gets list of all available extensions (both installed and available)
        /// </summary>
        public static List<ExtensionPackageInfo> GetAvailableExtensions()
        {
            RefreshExtensionCache();
            
            // If no extensions found after refresh, create sample data for testing
            if (s_KnownExtensions.Count == 0)
            {
                Debug.Log("[MCP Hub] No extensions found in registry, creating sample data for testing");
                CreateSampleData();
            }
            
            var result = s_KnownExtensions.Values.ToList();
            return result;
        }

        /// <summary>
        /// Gets list of installed extensions only
        /// </summary>
        public static List<ExtensionPackageInfo> GetInstalledExtensions()
        {
            RefreshExtensionCache();
            return s_KnownExtensions.Values.Where(ext => ext.IsInstalled).ToList();
        }

        /// <summary>
        /// Gets list of extensions that have updates available
        /// </summary>
        public static List<ExtensionPackageInfo> GetExtensionsWithUpdates()
        {
            RefreshExtensionCache();
            return s_KnownExtensions.Values.Where(ext => ext.HasUpdate).ToList();
        }

        /// <summary>
        /// Gets extensions by category
        /// </summary>
        public static List<ExtensionPackageInfo> GetExtensionsByCategory(ExtensionCategory category)
        {
            RefreshExtensionCache();
            return s_KnownExtensions.Values.Where(ext => ext.ExtensionCategory == category).ToList();
        }

        /// <summary>
        /// Installs an extension package asynchronously
        /// </summary>
        public static async Task<bool> InstallExtensionAsync(ExtensionPackageInfo extension)
        {
            try
            {
                Debug.Log($"[MCP Hub] Installing extension: {extension.DisplayName}");

                // Check if extension is in registry
                if (!s_ExtensionRegistry.TryGetValue(extension.Id, out var registryEntry))
                {
                    throw new InvalidOperationException($"Extension {extension.Id} not found in registry");
                }

                // Check if this is a local package installation (Assets structure)
                if (IsLocalPackageInstallation(registryEntry))
                {
                    Debug.Log($"[MCP Hub] Installing as local package: {extension.DisplayName}");
                    
                    // Check if package already exists (with compatibility check)
                    var existingDirectoryName = FindActualPackageDirectory(extension.Id);
                    if (!string.IsNullOrEmpty(existingDirectoryName))
                    {
                        Debug.Log($"[MCP Hub] Package already exists locally: {extension.DisplayName} at {existingDirectoryName}");
                        extension.UpdateInstallationStatus(true, "local");
                        OnExtensionInstalled?.Invoke(extension, true);
                        OnExtensionsUpdated?.Invoke();
                        return true;
                    }
                    
                    // Get the target directory name for new installation (use standard package ID)
                    var targetDirectoryName = GetPackageDirectoryName(extension.Id);
                    var packagePath = Path.Combine("Packages", targetDirectoryName);
                    
                    // Clone the package from Git repository
                    Debug.Log($"[MCP Hub] Cloning package from Git: {registryEntry.PackageUrl}");
                    var cloneResult = await ClonePackageFromGit(registryEntry.PackageUrl, packagePath, targetDirectoryName, extension.LatestVersion);
                    
                    if (cloneResult)
                    {
                        Debug.Log($"[MCP Hub] Successfully cloned package: {extension.DisplayName}");
                        extension.UpdateInstallationStatus(true, "local");
                        OnExtensionInstalled?.Invoke(extension, true);
                        OnExtensionsUpdated?.Invoke();
                        return true;
                    }
                    else
                    {
                        throw new Exception($"Failed to clone package from Git: {registryEntry.PackageUrl}");
                    }
                }
                else
                {
                // Use Unity Package Manager to add the package
                var addRequest = Client.Add(registryEntry.PackageUrl);
                
                while (!addRequest.IsCompleted)
                {
                    await Task.Delay(100);
                }

                if (addRequest.Status == StatusCode.Success)
                {
                    Debug.Log($"[MCP Hub] Successfully installed {extension.DisplayName}");
                    
                    // Update extension status
                    extension.UpdateInstallationStatus(true, addRequest.Result.version);
                    
                    // Trigger events
                    OnExtensionInstalled?.Invoke(extension, true);
                    OnExtensionsUpdated?.Invoke();
                    
                    return true;
                }
                else
                {
                    throw new Exception($"Package Manager error: {addRequest.Error?.message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Failed to install {extension.DisplayName}: {ex.Message}");
                OnExtensionInstalled?.Invoke(extension, false);
                throw;
            }
        }

        /// <summary>
        /// Uninstalls an extension package asynchronously
        /// </summary>
        public static async Task<bool> UninstallExtensionAsync(ExtensionPackageInfo extension)
        {
            try
            {
                Debug.Log($"[MCP Hub] Uninstalling extension: {extension.DisplayName}");

                // Check if this is a local package (installed in Assets directory)
                if (IsLocalPackage(extension.Id))
                {
                    Debug.Log($"[MCP Hub] Detected local package: {extension.Id}, removing from Assets directory");
                    
                    // Find the actual directory name for this package (could be legacy name)
                    var actualDirectoryName = FindActualPackageDirectory(extension.Id);
                    if (string.IsNullOrEmpty(actualDirectoryName))
                    {
                        throw new Exception($"Could not find directory for package {extension.Id}");
                    }
                    
                    var packagePath = Path.Combine("Packages", actualDirectoryName);
                    
                    if (Directory.Exists(packagePath))
                    {
                        try
                        {
                            // Check if this is a Git submodule
                            if (IsGitSubmodule(packagePath))
                            {
                                Debug.Log($"[MCP Hub] Detected Git submodule: {actualDirectoryName}");
                                
                                // For Git submodules, we need to use Git commands
                                // First, try to remove the submodule using Git
                                var gitRemoveResult = RemoveGitSubmodule(packagePath, actualDirectoryName);
                                if (gitRemoveResult)
                                {
                                    Debug.Log($"[MCP Hub] Successfully removed Git submodule: {extension.DisplayName}");
                                    
                                    // Update extension status
                                    extension.UpdateInstallationStatus(false);
                                    
                                    // Trigger events
                                    OnExtensionsUpdated?.Invoke();
                                    
                                    return true;
                                }
                                else
                                {
                                    throw new Exception("Failed to remove Git submodule");
                                }
                            }
                            else
                            {
                                // Use Unity's AssetDatabase to delete the package directory
                                // This is safer than direct file system operations
                                if (AssetDatabase.DeleteAsset($"Packages/{actualDirectoryName}"))
                                {
                                    Debug.Log($"[MCP Hub] Successfully removed local package: {extension.DisplayName}");
                                    
                                    // Update extension status
                                    extension.UpdateInstallationStatus(false);
                                    
                                    // Trigger events
                                    OnExtensionsUpdated?.Invoke();
                                    
                                    return true;
                                }
                                else
                                {
                                    throw new Exception("AssetDatabase.DeleteAsset failed");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // If AssetDatabase.DeleteAsset fails, try direct file system deletion
                            try
                            {
                                Debug.Log($"[MCP Hub] AssetDatabase deletion failed, trying direct file system deletion: {ex.Message}");
                                
                                // Remove the package directory
                                Directory.Delete(packagePath, true);
                                
                                // Also remove the .meta file if it exists
                                var metaPath = packagePath + ".meta";
                                if (File.Exists(metaPath))
                                {
                                    File.Delete(metaPath);
                                }
                                
                                Debug.Log($"[MCP Hub] Successfully removed local package via file system: {extension.DisplayName}");
                                
                                // Update extension status
                                extension.UpdateInstallationStatus(false);
                                
                                // Trigger events
                                OnExtensionsUpdated?.Invoke();
                                
                                return true;
                            }
                            catch (Exception fsEx)
                            {
                                throw new Exception($"Failed to remove local package directory: {fsEx.Message}. Original error: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        throw new Exception($"Local package directory not found: {packagePath}");
                    }
                }
                else
                {
                // Use Unity Package Manager to remove the package
                var removeRequest = Client.Remove(extension.Id);
                
                while (!removeRequest.IsCompleted)
                {
                    await Task.Delay(100);
                }

                if (removeRequest.Status == StatusCode.Success)
                {
                    Debug.Log($"[MCP Hub] Successfully uninstalled {extension.DisplayName}");
                    
                    // Update extension status
                    extension.UpdateInstallationStatus(false);
                    
                    // Trigger events
                    OnExtensionsUpdated?.Invoke();
                    
                    return true;
                }
                else
                {
                    throw new Exception($"Package Manager error: {removeRequest.Error?.message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Failed to uninstall {extension.DisplayName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates an extension package asynchronously
        /// </summary>
        public static async Task<bool> UpdateExtensionAsync(ExtensionPackageInfo extension)
        {
            try
            {
                Debug.Log($"[MCP Hub] Updating extension: {extension.DisplayName}");

                // Check if extension is in registry
                if (!s_ExtensionRegistry.TryGetValue(extension.Id, out var registryEntry))
                {
                    throw new InvalidOperationException($"Extension {extension.Id} not found in registry");
                }

                // Use Unity Package Manager to add the latest version
                var addRequest = Client.Add(registryEntry.PackageUrl);
                
                while (!addRequest.IsCompleted)
                {
                    await Task.Delay(100);
                }

                if (addRequest.Status == StatusCode.Success)
                {
                    Debug.Log($"[MCP Hub] Successfully updated {extension.DisplayName} to {addRequest.Result.version}");
                    
                    // Update extension status
                    extension.UpdateInstallationStatus(true, addRequest.Result.version);
                    
                    // Trigger events
                    OnExtensionsUpdated?.Invoke();
                    
                    return true;
                }
                else
                {
                    throw new Exception($"Package Manager error: {addRequest.Error?.message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Failed to update {extension.DisplayName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Checks if a specific extension is installed
        /// </summary>
        public static bool IsExtensionInstalled(string extensionId)
        {
            RefreshExtensionCache();
            return s_KnownExtensions.TryGetValue(extensionId, out var extension) && extension.IsInstalled;
        }

        /// <summary>
        /// Gets information about a specific extension
        /// </summary>
        public static ExtensionPackageInfo GetExtensionInfo(string extensionId)
        {
            RefreshExtensionCache();
            s_KnownExtensions.TryGetValue(extensionId, out var extension);
            return extension;
        }

        /// <summary>
        /// Refreshes the extension cache by querying Unity Package Manager
        /// </summary>
        public static void RefreshExtensionCache()
        {
            try
            {
                // Get list of installed packages synchronously
                var listRequest = Client.List(true); // Include built-in packages
                
                // Wait for completion
                while (!listRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }

                if (listRequest.Status == StatusCode.Success)
                {
                    
                    // Update installed packages cache
                    s_InstalledPackages.Clear();
                    foreach (var package in listRequest.Result)
                    {
                        s_InstalledPackages[package.name] = package;
                        if (package.name.StartsWith("com.miao.mcp"))
                        {
                        }
                    }

                    // Update known extensions
                    UpdateKnownExtensions();
                }
                else
                {
                    Debug.LogWarning($"[MCP Hub] Failed to refresh package list: {listRequest.Error?.message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Error refreshing extension cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the known extensions list based on registry and installed packages
        /// </summary>
        private static void UpdateKnownExtensions()
        {
            s_KnownExtensions.Clear();

            // Add all registered extensions
            foreach (var registryEntry in s_ExtensionRegistry.Values)
            {
                var extension = new ExtensionPackageInfo(
                    registryEntry.Id,
                    registryEntry.DisplayName,
                    registryEntry.Description,
                    registryEntry.Author,
                    registryEntry.LatestVersion,
                    registryEntry.Category
                );

                extension.SetUrls(registryEntry.PackageUrl, registryEntry.DocumentationUrl);
                extension.SetKeywords(registryEntry.Keywords);
                extension.SetDependencies(registryEntry.Dependencies);

                // Check if installed
                if (s_InstalledPackages.TryGetValue(registryEntry.Id, out var installedPackage))
                {
                    extension.UpdateInstallationStatus(true, installedPackage.version);
                }
                else if (IsLocalPackage(registryEntry.Id))
                {
                    // Check if it's installed as a local package in Assets
                    extension.UpdateInstallationStatus(true, "local");
                }

                s_KnownExtensions[extension.Id] = extension;
            }

            // Also add any installed MCP packages that might not be in our registry
            foreach (var installedPackage in s_InstalledPackages.Values)
            {
                if (installedPackage.name.StartsWith("com.miao.mcp") && 
                    !s_KnownExtensions.ContainsKey(installedPackage.name))
                {
                    var extension = new ExtensionPackageInfo(
                        installedPackage.name,
                        installedPackage.displayName ?? installedPackage.name,
                        installedPackage.description ?? "MCP Extension Package",
                        installedPackage.author?.name ?? "Unknown",
                        installedPackage.version,
                        ExtensionCategory.Community
                    );

                    extension.UpdateInstallationStatus(true, installedPackage.version);
                    s_KnownExtensions[extension.Id] = extension;
                }
            }
        }

        /// <summary>
        /// Registers a new extension in the registry (for development/testing)
        /// </summary>
        public static void RegisterExtension(ExtensionRegistryEntry registryEntry)
        {
            s_ExtensionRegistry[registryEntry.Id] = registryEntry;
            RefreshExtensionCache();
        }

        /// <summary>
        /// Forces a refresh of remote configuration
        /// </summary>
        public static async Task<bool> RefreshRemoteConfigurationAsync()
        {
            Debug.Log("[MCP Hub] Refreshing remote configuration...");
            return await RemoteConfigManager.RefreshCacheAsync();
        }

        /// <summary>
        /// Gets cache information for remote configuration
        /// </summary>
        public static CacheInfo GetRemoteConfigCacheInfo()
        {
            return RemoteConfigManager.GetCacheInfo();
        }

        /// <summary>
        /// Creates sample data for testing the Hub interface
        /// </summary>
        public static void CreateSampleData()
        {
            Debug.Log("[MCP Hub] Creating sample data for testing...");
            
            // Don't clear existing registry extensions, only add samples if needed
            var samplesToAdd = new[]
            {
                ("com.miao.mcp.vision", "Vision Pack", ExtensionCategory.Vision, false),
                ("com.miao.mcp.programmer", "Programmer Pack", ExtensionCategory.Programmer, false),
                ("com.miao.mcp.animation", "Animation Tools", ExtensionCategory.Essential, true),
                ("com.miao.mcp.physics", "Physics Tools", ExtensionCategory.Essential, false),
            };

            foreach (var (id, name, category, installed) in samplesToAdd)
            {
                if (!s_KnownExtensions.ContainsKey(id))
                {
                    var sample = ExtensionPackageInfo.CreateSampleExtension(id, name, category, installed);
                    s_KnownExtensions[sample.Id] = sample;
                    Debug.Log($"[MCP Hub] Added sample extension: {sample.DisplayName}");
                }
            }

            Debug.Log($"[MCP Hub] Sample data creation complete. Total extensions: {s_KnownExtensions.Count}");
            OnExtensionsUpdated?.Invoke();
        }

        /// <summary>
        /// Checks if a package is installed as a local package in the Assets directory
        /// Provides compatibility with different naming conventions and legacy installations
        /// </summary>
        private static bool IsLocalPackage(string packageId)
        {
            // Standard check: package ID as directory name
            var standardPath = Path.Combine("Packages", packageId);
            if (Directory.Exists(standardPath))
            {
                Debug.Log($"[MCP Hub] Found local package: {packageId} at {standardPath}");
                return true;
            }
            
            // Legacy compatibility: check old directory name mappings
            var legacyDirectoryNames = GetLegacyDirectoryNames(packageId);
            foreach (var legacyName in legacyDirectoryNames)
            {
                var legacyPath = Path.Combine("Packages", legacyName);
                if (Directory.Exists(legacyPath))
                {
                    Debug.Log($"[MCP Hub] Found legacy local package: {packageId} at {legacyPath}");
                    return true;
                }
            }
            
            // Check all subdirectories in Packages for package.json with matching ID
            if (CheckPackagesDirectoryForId(packageId))
            {
                return true;
            }
            
            // Check if it's listed in packages-lock.json as a local package
            return IsPackageInLockFile(packageId);
        }

        /// <summary>
        /// Gets legacy directory names for backward compatibility
        /// </summary>
        private static string[] GetLegacyDirectoryNames(string packageId)
        {
            var legacyMappings = new Dictionary<string, string[]>
            {
                { 
                    "com.miao.mcp", 
                    new[] { "MiAO-MCP-for-Unity", "miao-mcp-core", "mcp-core" }
                },
                { 
                    "com.miao.mcp.essential", 
                    new[] { "Unity-MCP-Essential", "Unity-MCP-Tools-Essential", "mcp-essential", "mcp-tools-essential" }
                },
                { 
                    "com.miao.mcp.behavior-designer-tools", 
                    new[] { "Unity-MCP-Tools-Behavior-Designer", "mcp-behavior-designer", "behavior-designer-tools" }
                }
            };

            return legacyMappings.TryGetValue(packageId, out var names) ? names : new string[0];
        }

        /// <summary>
        /// Scans Packages directory for package.json files with matching ID
        /// </summary>
        private static bool CheckPackagesDirectoryForId(string packageId)
        {
            try
            {
                var packagesDir = "Packages";
                if (!Directory.Exists(packagesDir))
                    return false;

                var subdirectories = Directory.GetDirectories(packagesDir);
                foreach (var dir in subdirectories)
                {
                    var packageJsonPath = Path.Combine(dir, "package.json");
                    if (File.Exists(packageJsonPath))
                    {
                        try
                        {
                            var packageJsonContent = File.ReadAllText(packageJsonPath);
                            // Simple JSON parsing to extract "name" field
                            if (ExtractPackageNameFromJson(packageJsonContent) == packageId)
                            {
                                Debug.Log($"[MCP Hub] Found package {packageId} in directory: {Path.GetFileName(dir)}");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[MCP Hub] Failed to read package.json in {dir}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Error scanning Packages directory: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Extracts package name from package.json content
        /// Simple implementation without full JSON parser
        /// </summary>
        private static string ExtractPackageNameFromJson(string jsonContent)
        {
            try
            {
                // Use Unity's JsonUtility or simple regex to extract name field
                var lines = jsonContent.Split('\n');
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("\"name\""))
                    {
                        // Extract value between quotes: "name": "com.miao.mcp"
                        var colonIndex = trimmedLine.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            var valuesPart = trimmedLine.Substring(colonIndex + 1).Trim();
                            valuesPart = valuesPart.TrimEnd(','); // Remove trailing comma
                            if (valuesPart.StartsWith("\"") && valuesPart.EndsWith("\""))
                            {
                                return valuesPart.Substring(1, valuesPart.Length - 2);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Error parsing package.json: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Checks if a package is listed in packages-lock.json as a local package
        /// </summary>
        private static bool IsPackageInLockFile(string packageId)
        {
            try
            {
                var lockFilePath = Path.Combine("Packages", "packages-lock.json");
                if (!File.Exists(lockFilePath))
                {
                    return false;
                }
                
                var lockFileContent = File.ReadAllText(lockFilePath);
                // Simple check - if the package is mentioned with "file:" source, it's a local package
                return lockFileContent.Contains($"\"{packageId}\"") && 
                       lockFileContent.Contains($"\"source\": \"embedded\"");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Error checking packages-lock.json: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a package directory is a Git submodule
        /// </summary>
        private static bool IsGitSubmodule(string packagePath)
        {
            var gitPath = Path.Combine(packagePath, ".git");
            return Directory.Exists(gitPath);
        }

        /// <summary>
        /// Gets the directory name for a package ID
        /// Uses package ID as directory name for consistency
        /// </summary>
        private static string GetPackageDirectoryName(string packageId)
        {
            // Use package ID directly as directory name for consistency
            // This makes the directory structure more predictable and standardized
            return packageId;
        }

        /// <summary>
        /// Finds the actual directory name for a package, considering legacy names
        /// </summary>
        private static string FindActualPackageDirectory(string packageId)
        {
            // First check standard package ID directory
            var standardPath = Path.Combine("Packages", packageId);
            if (Directory.Exists(standardPath))
            {
                return packageId;
            }
            
            // Check legacy directory names
            var legacyNames = GetLegacyDirectoryNames(packageId);
            foreach (var legacyName in legacyNames)
            {
                var legacyPath = Path.Combine("Packages", legacyName);
                if (Directory.Exists(legacyPath))
                {
                    return legacyName;
                }
            }
            
            // Scan all directories for package.json with matching ID
            try
            {
                var packagesDir = "Packages";
                if (Directory.Exists(packagesDir))
                {
                    var subdirectories = Directory.GetDirectories(packagesDir);
                    foreach (var dir in subdirectories)
                    {
                        var packageJsonPath = Path.Combine(dir, "package.json");
                        if (File.Exists(packageJsonPath))
                        {
                            try
                            {
                                var packageJsonContent = File.ReadAllText(packageJsonPath);
                                if (ExtractPackageNameFromJson(packageJsonContent) == packageId)
                                {
                                    return Path.GetFileName(dir);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[MCP Hub] Failed to read package.json in {dir}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Error scanning for package directory: {ex.Message}");
            }
            
            return null; // Not found
        }

        /// <summary>
        /// Checks if a registry entry represents a local package installation
        /// </summary>
        private static bool IsLocalPackageInstallation(ExtensionRegistryEntry registryEntry)
        {
            // Check if the package URL points to a local path or if it's a known local package
            return registryEntry.PackageUrl.Contains("github.com/MiAO-AI-LAB") || 
                   registryEntry.PackageUrl.Contains("github.com/MiAO-AI-Lab");
        }

        /// <summary>
        /// Removes a Git repository from the project
        /// </summary>
        private static bool RemoveGitSubmodule(string packagePath, string directoryName)
        {
            try
            {
                Debug.Log($"[MCP Hub] Removing Git repository: {directoryName}");
                
                // Check if the directory exists
                if (!Directory.Exists(packagePath))
                {
                    Debug.LogWarning($"[MCP Hub] Package directory does not exist: {packagePath}");
                    return true; // Consider it already removed
                }
                
                // First, try to remove read-only attributes to avoid permission issues
                try
                {
                    RemoveReadOnlyAttributes(packagePath);
                    Debug.Log($"[MCP Hub] Removed read-only attributes from: {directoryName}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCP Hub] Could not remove read-only attributes: {ex.Message}");
                }
                
                // Try to remove the directory directly
                try
                {
                    Directory.Delete(packagePath, true);
                    Debug.Log($"[MCP Hub] Successfully removed directory: {directoryName}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCP Hub] Could not remove directory directly: {ex.Message}");
                    
                    // Try alternative approach: remove files one by one
                    try
                    {
                        RemoveDirectoryRecursively(packagePath);
                        Debug.Log($"[MCP Hub] Successfully removed directory recursively: {directoryName}");
                        return true;
                    }
                    catch (Exception recursiveEx)
                    {
                        Debug.LogError($"[MCP Hub] Failed to remove directory recursively: {recursiveEx.Message}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Failed to remove Git repository {directoryName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes a directory recursively by deleting files one by one
        /// </summary>
        private static void RemoveDirectoryRecursively(string directoryPath)
        {
            try
            {
                // First, remove read-only attributes from all files and directories
                RemoveReadOnlyAttributes(directoryPath);
                
                // Delete all files first
                var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        Debug.Log($"[MCP Hub] Deleted file: {file}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP Hub] Could not delete file {file}: {ex.Message}");
                    }
                }
                
                // Delete all directories (in reverse order to handle nested directories)
                var directories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length) // Delete deepest directories first
                    .ToList();
                
                foreach (var dir in directories)
                {
                    try
                    {
                        Directory.Delete(dir);
                        Debug.Log($"[MCP Hub] Deleted directory: {dir}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP Hub] Could not delete directory {dir}: {ex.Message}");
                    }
                }
                
                // Finally, delete the main directory
                try
                {
                    Directory.Delete(directoryPath);
                    Debug.Log($"[MCP Hub] Deleted main directory: {directoryPath}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCP Hub] Could not delete main directory {directoryPath}: {ex.Message}");
                    throw; // Re-throw to indicate failure
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Error removing directory recursively: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Removes read-only attributes from all files in a directory recursively
        /// </summary>
        private static void RemoveReadOnlyAttributes(string directoryPath)
        {
            try
            {
                // Remove read-only attribute from all files in the directory
                var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP Hub] Could not remove read-only attribute from {file}: {ex.Message}");
                    }
                }
                
                // Remove read-only attribute from all directories
                var directories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories);
                foreach (var dir in directories)
                {
                    try
                    {
                        var attributes = File.GetAttributes(dir);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            File.SetAttributes(dir, attributes & ~FileAttributes.ReadOnly);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP Hub] Could not remove read-only attribute from directory {dir}: {ex.Message}");
                    }
                }
                
                // Remove read-only attribute from the main directory
                try
                {
                    var attributes = File.GetAttributes(directoryPath);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(directoryPath, attributes & ~FileAttributes.ReadOnly);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCP Hub] Could not remove read-only attribute from main directory {directoryPath}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Error removing read-only attributes: {ex.Message}");
            }
        }

        /// <summary>
        /// Clones a package from Git repository to the Assets directory
        /// First tries to clone the specific version tag, falls back to main branch
        /// </summary>
        private static async Task<bool> ClonePackageFromGit(string gitUrl, string targetPath, string directoryName, string targetVersion = null)
        {
            try
            {
                Debug.Log($"[MCP Hub] Starting Git clone: {gitUrl} -> {targetPath}");
                
                // Ensure the Packages directory exists
                var packagesDir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(packagesDir))
                {
                    Directory.CreateDirectory(packagesDir);
                }
                
                // Remove target directory if it exists
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }
                
                // First try to clone specific version tag if specified
                if (!string.IsNullOrEmpty(targetVersion))
                {
                    Debug.Log($"[MCP Hub] Attempting to clone tag version: {targetVersion}");
                    var tagResult = await TryCloneSpecificTag(gitUrl, directoryName, targetVersion);
                    if (tagResult)
                    {
                        Debug.Log($"[MCP Hub] Successfully cloned tag version {targetVersion}");
                        AssetDatabase.Refresh();
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[MCP Hub] Tag {targetVersion} not found, falling back to main branch");
                    }
                }
                
                // Fall back to cloning main branch
                Debug.Log($"[MCP Hub] Cloning main branch from: {gitUrl}");
                var mainResult = await TryCloneMainBranch(gitUrl, directoryName);
                if (mainResult)
                {
                    Debug.Log($"[MCP Hub] Successfully cloned main branch");
                    AssetDatabase.Refresh();
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Failed to clone package from Git: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tries to clone a specific tag version
        /// </summary>
        private static async Task<bool> TryCloneSpecificTag(string gitUrl, string directoryName, string tagVersion)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"clone --branch v{tagVersion} --single-branch {gitUrl} \"{directoryName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath("Packages")
                };
                
                Debug.Log($"[MCP Hub] Executing: git clone --branch v{tagVersion} --single-branch {gitUrl} \"{directoryName}\"");
                
                using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
                {
                    process.Start();
                    
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    
                    await Task.Run(() => process.WaitForExit());
                    
                    var output = await outputTask;
                    var error = await errorTask;
                    
                    if (process.ExitCode == 0)
                    {
                        Debug.Log($"[MCP Hub] Tag clone successful: {output}");
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[MCP Hub] Tag clone failed: {error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Hub] Exception during tag clone: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tries to clone the main branch
        /// </summary>
        private static async Task<bool> TryCloneMainBranch(string gitUrl, string directoryName)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"clone {gitUrl} \"{directoryName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath("Packages")
                };
                
                Debug.Log($"[MCP Hub] Executing: git clone {gitUrl} \"{directoryName}\"");
                
                using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
                {
                    process.Start();
                    
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    
                    await Task.Run(() => process.WaitForExit());
                    
                    var output = await outputTask;
                    var error = await errorTask;
                    
                    if (process.ExitCode == 0)
                    {
                        Debug.Log($"[MCP Hub] Main branch clone successful: {output}");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[MCP Hub] Main branch clone failed: {error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP Hub] Exception during main branch clone: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Registry entry for an extension package
    /// </summary>
    [Serializable]
    public class ExtensionRegistryEntry
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string Author { get; }
        public string LatestVersion { get; }
        public ExtensionCategory Category { get; }
        public string PackageUrl { get; }
        public string DocumentationUrl { get; }
        public string[] Keywords { get; }
        public string[] Dependencies { get; }

        public ExtensionRegistryEntry(string id, string displayName, string description,
            string author, ExtensionCategory category, string packageUrl,
            string latestVersion = "1.0.0", string documentationUrl = null,
            string[] keywords = null, string[] dependencies = null)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Author = author;
            LatestVersion = latestVersion;
            Category = category;
            PackageUrl = packageUrl;
            DocumentationUrl = documentationUrl;
            Keywords = keywords ?? new string[0];
            Dependencies = dependencies ?? new string[0];
        }
    }
}
#endif 