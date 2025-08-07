#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using com.MiAO.MCP.Launcher.UI;

namespace com.MiAO.MCP.Launcher
{
    /// <summary>
    /// Menu items for the MCP Hub Launcher
    /// </summary>
    public static class MenuItems
    {
        private const string MENU_PATH = "Window/MCP Hub Launcher";
        
        /// <summary>
        /// Opens the MCP Hub Launcher window
        /// </summary>
        [MenuItem(MENU_PATH, false, 100)]
        public static void OpenMCPHubLauncher()
        {
            SimpleLauncherWindow.ShowWindow();
        }
        
        /// <summary>
        /// Validates the menu item
        /// </summary>
        [MenuItem(MENU_PATH, true)]
        public static bool ValidateOpenMCPHubLauncher()
        {
            return true; // Always enabled
        }
    }
}
#endif 