// Generated from Forge Toolkit 0.5.0 (Editor/Welcome/McpLink.cs): do not edit.
// This plugin's own copy, so it needs no toolkit. Change the toolkit's file, then decouple again.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ForgeMCP.Editor.Welcome
{
    /// <summary>
    /// The toolkit's view of Forge MCP (io.glitchr.mcp, the MCP for Unity fork; com.coplaydev.unity-mcp in the
    /// projects still on the tagged fork), found by reflection so the toolkit works with it or without it: whether it
    /// is installed, whether this editor is connected, its address; this editor's connection started and stopped (the
    /// shared server that fronts every open editor is never stopped from here); its window; and, when it is missing,
    /// the link to the local clone or, without one, the package from GitHub.
    /// </summary>
    public static class McpLink
    {
        public static readonly string[] Packages = { "io.glitchr.mcp", "com.coplaydev.unity-mcp" };
        /// <summary>
        /// A copy of the package on this machine, when a project beside this one hosts it (a real folder
        /// Packages/io.glitchr.mcp with its package.json, in a sibling of this project's folder): several projects then
        /// share one copy by a link. Null when there is none: the package then comes from GitHub.
        /// </summary>
        public static string LocalClone
        {
            get
            {
                if (cloneSought) return localClone;
                cloneSought = true;
                try
                {
                    string project = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                    string beside = Path.GetDirectoryName(project);
                    if (beside == null) return null;
                    foreach (string sibling in Directory.GetDirectories(beside))
                    {
                        if (sibling == project) continue;
                        string candidate = Path.Combine(sibling, "Packages", "io.glitchr.mcp");
                        if (!File.Exists(Path.Combine(candidate, "package.json"))) continue;
                        // The host's own folder, not another project's link to it.
                        if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) continue;
                        localClone = candidate;
                        break;
                    }
                }
                catch (Exception) { }
                return localClone;
            }
        }
        static string localClone;
        static bool cloneSought;

        public const string GitUrl = "https://github.com/meiyasan/unity-mcp.git#glitchr";

        static Type locator, endpoints;
        static bool sought;

        static void Seek()
        {
            if (sought) return;
            sought = true;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = a.GetName().Name;
                if (n != "MCP.Editor" && n != "MCPForUnity.Editor") continue;
                // Forge MCP's namespace first (ForgeMCP), then the one before it (MCPForUnity, upstream's and older forks').
                locator = a.GetType("ForgeMCP.Editor.Services.MCPServiceLocator") ?? a.GetType("MCPForUnity.Editor.Services.MCPServiceLocator");
                endpoints = a.GetType("ForgeMCP.Editor.Helpers.HttpEndpointUtility") ?? a.GetType("MCPForUnity.Editor.Helpers.HttpEndpointUtility");
            }
        }

        /// <summary>The installed package, null when Forge MCP is not in this project.</summary>
        public static PackageInfo Package => PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => Packages.Contains(p.name));
        public static bool Installed { get { Seek(); return locator != null; } }

        static object Bridge
        {
            get
            {
                Seek();
                try { return locator?.GetProperty("Bridge", BindingFlags.Public | BindingFlags.Static)?.GetValue(null); }
                catch (Exception) { return null; }
            }
        }

        static T Read<T>(object o, string name, T fallback)
        {
            try { return o != null && o.GetType().GetProperty(name)?.GetValue(o) is T v ? v : fallback; }
            catch (Exception) { return fallback; }
        }

        /// <summary>Whether this editor is connected to the MCP server.</summary>
        public static bool Running => Read(Bridge, "IsRunning", false);
        /// <summary>The port AI tools reach this editor on: the address's (the bridge's own port is the old stdio one, 6400).</summary>
        public static int Port => Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u.Port : Read(Bridge, "CurrentPort", 0);

        public static string Url
        {
            get
            {
                Seek();
                try { return endpoints?.GetMethod("GetMcpRpcUrl", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null) as string ?? ""; }
                catch (Exception) { return ""; }
            }
        }

        /// <summary>Connects this editor, or disconnects it once the user confirms (AI tools lose this editor until it connects again).</summary>
        public static void Toggle()
        {
            var bridge = Bridge;
            if (bridge == null) return;
            if (Running)
            {
                if (!EditorUtility.DisplayDialog("Disconnect this editor from MCP?",
                        "AI tools (Claude Code and the like) lose this editor until it connects again. The shared MCP server and the other editors keep running.",
                        "Disconnect", "Keep it connected"))
                    return;
                Call(bridge, "StopAsync");
            }
            else Call(bridge, "StartAsync");
        }

        static void Call(object o, string method)
        {
            try { o.GetType().GetMethod(method, Type.EmptyTypes)?.Invoke(o, null); }
            catch (Exception e) { Debug.LogWarning($"Forge Toolkit: MCP {method} failed: {e.InnerException?.Message ?? e.Message}"); }
        }

        /// <summary>Forge MCP's own window.</summary>
        public static void OpenWindow()
        {
            // Its menu under this fork's name, or under upstream's (an older copy, or MCP for Unity itself).
            if (!EditorApplication.ExecuteMenuItem("Window/Forge MCP/Toggle MCP Window") && !EditorApplication.ExecuteMenuItem("Window/Forge MCP/Toggle MCP Window") && !EditorApplication.ExecuteMenuItem("Window/MCP for Unity/Toggle MCP Window")) Debug.LogWarning("Forge Toolkit: Forge MCP's window was not found.");
        }

        /// <summary>What Get does: link the local copy when there is one, else add the package from GitHub.</summary>
        public static string GetLabel => LocalClone != null ? "Link the local copy" : "Add from GitHub";

        /// <summary>
        /// Gets Forge MCP into this project: Packages/io.glitchr.mcp linked to a copy a project beside this one
        /// hosts (and git-ignored), else the package added from GitHub.
        /// </summary>
        public static void Get()
        {
            if (LocalClone != null)
            {
                string link = Path.GetFullPath("Packages/io.glitchr.mcp");
                if (!Directory.Exists(link) && !File.Exists(link))
                {
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/ln", $"-s \"{LocalClone}\" \"{link}\"") { UseShellExecute = false });
                    p?.WaitForExit();
                }
                const string ignore = "/Packages/io.glitchr.mcp";
                try
                {
                    var gi = Path.GetFullPath(".gitignore");
                    if (File.Exists(gi) && !File.ReadAllText(gi).Contains(ignore)) File.AppendAllText(gi, $"\n# Forge MCP, linked from the project that hosts it\n{ignore}\n");
                }
                catch (Exception) { }
                UnityEditor.PackageManager.Client.Resolve();
                AssetDatabase.Refresh();
            }
            else UnityEditor.PackageManager.Client.Add(GitUrl);
        }
    }
}
#endif
