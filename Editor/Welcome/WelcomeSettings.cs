// Generated from Forge Toolkit 0.5.0 (Editor/Welcome/WelcomeSettings.cs): do not edit.
// This plugin's own copy, so it needs no toolkit. Change the toolkit's file, then decouple again.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace ForgeMCP.Editor.Welcome
{
    /// <summary>
    /// What a Welcome page reads of the project's settings (ProjectSettings/GlitchArtToolkit.json, edited under Project
    /// Settings ▸ Forge Toolkit where the toolkit is installed): how narrow its buttons may get before they go one
    /// a row. Part of the Welcome contract, so a plugin's own copy of the page reads the same file, with the toolkit or
    /// without it (100 points then).
    /// </summary>
    public static class WelcomeSettings
    {
        public const string Path = "ProjectSettings/GlitchArtToolkit.json";
        public const float DefaultButtonWidth = 100f;

        [Serializable] sealed class File_ { public float welcomeButtonMinWidth = DefaultButtonWidth; }

        static float width = DefaultButtonWidth;
        static DateTime readAt = DateTime.MinValue;
        static bool read;

        /// <summary>A Welcome page's buttons go two a row while each is at least this wide (points).</summary>
        public static float ButtonMinWidth
        {
            get
            {
                var at = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : DateTime.MinValue;
                if (read && at == readAt) return width;
                read = true; readAt = at; width = DefaultButtonWidth;
                try
                {
                    if (File.Exists(Path))
                    {
                        var f = new File_();
                        JsonUtility.FromJsonOverwrite(File.ReadAllText(Path), f);
                        if (f.welcomeButtonMinWidth > 0f) width = f.welcomeButtonMinWidth;
                    }
                }
                catch (Exception) { width = DefaultButtonWidth; }
                return width;
            }
        }
    }
}
#endif
