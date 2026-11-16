using ForgeMCP.Editor.Constants;
using ForgeMCP.Editor.Setup;
using ForgeMCP.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace ForgeMCP.Editor.MenuItems
{
    public static class MCPForUnityMenu
    {
        [MenuItem(ProductInfo.MenuRoot + "/Toggle MCP Window %#m", priority = 1)]
        public static void ToggleMCPWindow()
        {
            MCPForUnityEditorWindow.ShowWindow();
        }

        [MenuItem(ProductInfo.MenuRoot + "/Local Setup Window", priority = 2)]
        public static void ShowSetupWindow()
        {
            SetupWindowService.ShowSetupWindow();
        }


        [MenuItem(ProductInfo.MenuRoot + "/Edit EditorPrefs", priority = 3)]
        public static void ShowEditorPrefsWindow()
        {
            EditorPrefsWindow.ShowWindow();
        }
    }
}
