using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ForgeMCP.Editor.Constants;
using ForgeMCP.Editor.Helpers;
using ForgeMCP.Editor.Services;
using ForgeMCP.Editor.Windows.Components.Advanced;
using ForgeMCP.Editor.Windows.Components.AssetGen;
using ForgeMCP.Editor.Windows.Components.Branding;
using ForgeMCP.Editor.Windows.Components.ClientConfig;
using ForgeMCP.Editor.Windows.Components.Connection;
using ForgeMCP.Editor.Windows.Components.Resources;
using ForgeMCP.Editor.Windows.Components.Tools;
using ForgeMCP.Editor.Setup;
using ForgeMCP.Editor.Windows.Components.Validation;
using ForgeMCP.Editor.Welcome;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgeMCP.Editor.Windows
{
    /// <summary>
    /// Forge MCP's page, on the Forge Welcome contract (Editor/Welcome, copied from the Forge Toolkit by
    /// embed_features.py, so it needs no toolkit): the logo card with the version and this editor's connection, a row
    /// of buttons, then parts that fold - Connect (the server and the AI clients), Tools, Resources, Generative,
    /// Dependencies, Advanced - and the contract's own Build, Optionals and Links. Each MCP part is its UI Toolkit
    /// panel, made once with the page and brought up to date when it is unfolded.
    /// </summary>
    public class MCPForUnityEditorWindow : WelcomeWindow
    {
        // Section controllers
        private McpConnectionSection connectionSection;
        private McpClientConfigSection clientConfigSection;
        private McpAdvancedSection advancedSection;
        private McpToolsSection toolsSection;
        private McpResourcesSection resourcesSection;
        private McpAssetGenSection assetGenSection;

        // The update notice, at the top of Connect.
        private VisualElement updateNotification;
        private Label updateNotificationText;

        private static readonly HashSet<MCPForUnityEditorWindow> OpenWindows = new();
        private bool toolsLoaded = false;
        private bool resourcesLoaded = false;
        private double lastRefreshTime = 0;
        private const double RefreshDebounceSeconds = 0.5;
        private bool updateCheckQueued = false;
        private bool updateCheckInFlight = false;

        internal static void CloseAllWindows()
        {
            var windows = OpenWindows.Where(window => window != null).ToArray();
            foreach (var window in windows)
            {
                window.Close();
            }
        }

        public static void ShowWindow()
        {
            var existingWindows = UnityEngine.Resources.FindObjectsOfTypeAll<MCPForUnityEditorWindow>();
            MCPForUnityEditorWindow window = null;

            if (existingWindows.Length > 0)
            {
                window = existingWindows[0];

                // If multiple instances exist, keep one and close the extras to avoid stale hidden tabs.
                for (int i = 1; i < existingWindows.Length; i++)
                {
                    try
                    {
                        existingWindows[i].Close();
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"Error closing duplicate MCP window: {ex.Message}");
                    }
                }
            }
            else
            {
                window = GetWindow<MCPForUnityEditorWindow>(ProductInfo.ProductName);
            }

            window.titleContent = new GUIContent(ProductInfo.ProductName);
            window.minSize = new Vector2(500, 340);

            if (window.position.width < 100 || window.position.height < 100)
            {
                window.position = new Rect(120, 120, 900, 700);
            }

            window.Show();
            window.ShowTab();
            window.Focus();
        }

        // Helper to check and manage open windows from other classes
        public static bool HasAnyOpenWindow()
        {
            return OpenWindows.Count > 0;
        }

        public static void CloseAllOpenWindows()
        {
            if (OpenWindows.Count == 0)
                return;

            // Copy to array to avoid modifying the collection while iterating
            var arr = new MCPForUnityEditorWindow[OpenWindows.Count];
            OpenWindows.CopyTo(arr);
            foreach (var window in arr)
            {
                try
                {
                    window?.Close();
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Error closing MCP window: {ex.Message}");
                }
            }
        }

        // ---- The page ----------------------------------------------------------------------------------------------

        protected override string PackageName => "io.glitchr.mcp";
        protected override string ProductName => ProductInfo.ProductName;
        protected override string LogoPath => AssetPathUtility.GetMcpPackageRootPath() + "/Editor/Images/ForgeMCPLogo.png";
        protected override string About =>
            "Connects this Unity editor to AI assistants: they read the project, run its tools and see what changed.";

        [InitializeOnLoadMethod]
        private static void AtStartup() => ShowAtStartup<MCPForUnityEditorWindow>("io.glitchr.mcp");

        protected override IEnumerable<WelcomeButton> Buttons => new[]
        {
            new WelcomeButton("Set up", "Python and uv for the server", () => MCPSetupWindow.ShowWindow()),
            new WelcomeButton("Connect clients", "Claude Code, Cursor, VS Code...", ConfigureAllClients),
            new WelcomeButton("Server logs", "What the server said", OpenServerLogs),
            new WelcomeButton("Preferences", "Every Forge MCP setting", EditorPrefsWindow.ShowWindow),
        };

        // The MCP parts, made once with the page; the contract keeps whether each is open.
        private VisualElement clientsPart, toolsPart, resourcesPart, assetGenPart, depsPart, advancedPart;

        protected override IEnumerable<WelcomeSection> Sections
        {
            get
            {
                BuildParts();
                return new[]
                {
                    new WelcomeSection("Connect", ConnectSummary, () => clientsPart, openByDefault: true,
                        opened: () => clientConfigSection?.RefreshSelectedClient(forceImmediate: true)),
                    new WelcomeSection("Tools", () => "what an assistant may do here", () => toolsPart, opened: EnsureToolsLoaded),
                    new WelcomeSection("Resources", () => "what an assistant may read", () => resourcesPart, opened: EnsureResourcesLoaded),
                    new WelcomeSection("Generative", () => "images, models and sounds", () => assetGenPart, opened: () => assetGenSection?.Refresh()),
                    new WelcomeSection("Dependencies", () => "Python and uv for the server", () => depsPart),
                    new WelcomeSection("Advanced", () => "the server's source, logs, checks", () => advancedPart),
                    BuildSection(),
                    LinksSection(),
                };
            }
        }

        /// <summary>What Connect's bar says folded: this editor's connection and the transport.</summary>
        private static string ConnectSummary()
        {
            bool on = MCPServiceLocator.Bridge.IsRunning;
            string transport = EditorConfigurationCache.Instance.UseHttpTransport ? "HTTP" : "stdio";
            return on ? $"connected · {transport}" : $"not connected · {transport}";
        }

        private static void ConfigureAllClients()
        {
            var windows = UnityEngine.Resources.FindObjectsOfTypeAll<MCPForUnityEditorWindow>();
            var section = windows.Length > 0 ? windows[0].clientConfigSection : null;
            if (section != null) section.ConfigureAllDetectedClients();
            else ShowWindow();
        }

        private static void OpenServerLogs()
        {
            // The server's launch logs (Library/MCPForUnity/Logs, one a port), else the folder they go in.
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Library", "MCPForUnity");
            string logs = System.IO.Path.Combine(folder, "Logs");
            if (System.IO.Directory.Exists(logs)) EditorUtility.RevealInFinder(System.IO.Directory.GetFiles(logs).OrderByDescending(System.IO.File.GetLastWriteTime).FirstOrDefault() ?? logs);
            else if (System.IO.Directory.Exists(folder)) EditorUtility.RevealInFinder(folder);
            else EditorUtility.DisplayDialog(ProductInfo.ProductName, "No server log in this project yet: start the server first.", "OK");
        }

        protected override void PageBuilt()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();
            foreach (var sheet in new[] { "/Editor/Windows/MCPForUnityEditorWindow.uss", "/Editor/Windows/Components/Common.uss" })
            {
                var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(basePath + sheet);
                if (style != null) rootVisualElement.styleSheets.Add(style);
            }
            rootVisualElement.AddToClassList("forge-mcp");
            ApplySectionLastClasses();
            RefreshAllData();
            QueueUpdateCheck();
        }

        private static VisualElement Part(string name)
        {
            var part = new VisualElement { name = name };
            part.AddToClassList("section-stack");
            part.AddToClassList("forge-part");
            return part;
        }

        private VisualElement Instantiate(string basePath, string uxml, VisualElement into)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{basePath}/Editor/Windows/Components/{uxml}");
            if (tree == null)
            {
                McpLog.Warn($"Forge MCP: {uxml} is missing; that part of the window is empty.");
                return null;
            }
            var root = tree.Instantiate();
            into.Add(root);
            return root;
        }

        private void BuildParts()
        {
            string basePath = AssetPathUtility.GetMcpPackageRootPath();
            clientsPart = Part("clients-container");
            toolsPart = Part("tools-container");
            resourcesPart = Part("resources-container");
            assetGenPart = Part("assetgen-container");
            depsPart = Part("deps-container");
            advancedPart = Part("advanced-container");

            // The update notice heads Connect, shown only when there is one.
            updateNotification = new VisualElement { name = "update-notification" };
            updateNotification.AddToClassList("update-notification");
            updateNotificationText = new Label { name = "update-notification-text" };
            updateNotificationText.AddToClassList("update-notification-text");
            updateNotification.Add(updateNotificationText);
            clientsPart.Add(updateNotification);

            var connectionRoot = Instantiate(basePath, "Connection/McpConnectionSection.uxml", clientsPart);
            if (connectionRoot != null)
            {
                connectionSection = new McpConnectionSection(connectionRoot);
                connectionSection.OnManualConfigUpdateRequested += () =>
                    clientConfigSection?.UpdateManualConfiguration();
                connectionSection.OnTransportChanged += () =>
                    clientConfigSection?.RefreshSelectedClient(forceImmediate: true);
            }

            var clientConfigRoot = Instantiate(basePath, "ClientConfig/McpClientConfigSection.uxml", clientsPart);
            if (clientConfigRoot != null)
            {
                clientConfigSection = new McpClientConfigSection(clientConfigRoot);
                // A client's transport or version that does not match this editor's: Connect's warning says so.
                clientConfigSection.OnClientTransportDetected += (clientName, transport) =>
                    connectionSection?.UpdateTransportMismatchWarning(clientName, transport);
                clientConfigSection.OnClientConfigMismatch += (clientName, mismatchMessage) =>
                    connectionSection?.UpdateVersionMismatchWarning(clientName, mismatchMessage);
            }

            BuildDependenciesSection(depsPart);

            var advancedRoot = Instantiate(basePath, "Advanced/McpAdvancedSection.uxml", advancedPart);
            if (advancedRoot != null)
            {
                advancedSection = new McpAdvancedSection(advancedRoot);
                advancedSection.OnGitUrlChanged += () =>
                    clientConfigSection?.UpdateManualConfiguration();
                advancedSection.OnHttpServerCommandUpdateRequested += () =>
                {
                    connectionSection?.UpdateHttpServerCommandDisplay();
                    connectionSection?.UpdateConnectionStatus();
                };
                advancedSection.OnTestConnectionRequested += async () =>
                {
                    if (connectionSection != null)
                        await connectionSection.VerifyBridgeConnectionAsync();
                };
                advancedSection.OnPackageDeployed += () =>
                {
                    UpdateVersionLabel();
                    QueueUpdateCheck();
                };
                connectionSection?.SetHealthStatusUpdateCallback((isHealthy, statusText) =>
                    advancedSection?.UpdateHealthStatus(isHealthy, statusText));
            }

            var validationRoot = Instantiate(basePath, "Validation/McpValidationSection.uxml", advancedPart);
            if (validationRoot != null) new McpValidationSection(validationRoot);

            toolsLoaded = resourcesLoaded = false;
            var toolsRoot = Instantiate(basePath, "Tools/McpToolsSection.uxml", toolsPart);
            if (toolsRoot != null) toolsSection = new McpToolsSection(toolsRoot);
            var resourcesRoot = Instantiate(basePath, "Resources/McpResourcesSection.uxml", resourcesPart);
            if (resourcesRoot != null) resourcesSection = new McpResourcesSection(resourcesRoot);
            var assetGenRoot = Instantiate(basePath, "AssetGen/McpAssetGenSection.uxml", assetGenPart);
            if (assetGenRoot != null) assetGenSection = new McpAssetGenSection(assetGenRoot);
        }

        // The version is the page's pill: a deploy shows by repainting it.
        private void UpdateVersionLabel() => Repaint();

        private void QueueUpdateCheck()
        {
            if (updateCheckQueued || updateCheckInFlight)
            {
                return;
            }

            updateCheckQueued = true;
            EditorApplication.delayCall += CheckForPackageUpdates;
        }

        private void CheckForPackageUpdates()
        {
            updateCheckQueued = false;

            if (updateNotification == null || updateNotificationText == null)
            {
                return;
            }

            string currentVersion = AssetPathUtility.GetPackageVersion();
            if (string.IsNullOrEmpty(currentVersion) || currentVersion == "unknown")
            {
                updateNotification.RemoveFromClassList("visible");
                return;
            }

            // Main thread: resolve service + read EditorPrefs cache (both require main thread)
            var updateService = MCPServiceLocator.Updates;
            var cachedResult = updateService.TryGetCachedResult(currentVersion);
            if (cachedResult != null)
            {
                ApplyUpdateCheckResult(cachedResult, currentVersion);
                return;
            }

            // Main thread: pre-compute installation info (uses main-thread-only Unity APIs)
            bool isGitInstallation = updateService.IsGitInstallation();
            string gitBranch = isGitInstallation ? updateService.GetGitUpdateBranch(currentVersion) : "main";

            // Background thread: network I/O only (no EditorPrefs or Unity API access)
            updateCheckInFlight = true;
            Task.Run(() =>
            {
                try
                {
                    return updateService.FetchAndCompare(currentVersion, isGitInstallation, gitBranch);
                }
                catch (Exception ex)
                {
                    McpLog.Info($"Package update check skipped: {ex.Message}");
                    return null;
                }
            }).ContinueWith(t =>
            {
                EditorApplication.delayCall += () =>
                {
                    updateCheckInFlight = false;

                    // Main thread: cache the result in EditorPrefs
                    var result = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                    if (result != null && result.CheckSucceeded && !string.IsNullOrEmpty(result.LatestVersion))
                    {
                        updateService.CacheFetchResult(currentVersion, result.LatestVersion);
                    }

                    if (this == null || updateNotification == null || updateNotificationText == null)
                    {
                        return;
                    }

                    ApplyUpdateCheckResult(result, currentVersion);
                };
            }, TaskScheduler.Default);
        }

        private void ApplyUpdateCheckResult(UpdateCheckResult result, string currentVersion)
        {
            if (result != null && result.CheckSucceeded && result.UpdateAvailable && !string.IsNullOrEmpty(result.LatestVersion))
            {
                updateNotificationText.text = $"Update available: v{result.LatestVersion}  (current: v{currentVersion})";
                updateNotificationText.tooltip = $"Latest version: v{result.LatestVersion}\nCurrent version: v{currentVersion}";
                updateNotification.AddToClassList("visible");
            }
            else
            {
                updateNotification.RemoveFromClassList("visible");
            }
        }

        private void EnsureToolsLoaded()
        {
            if (toolsLoaded)
            {
                return;
            }

            if (toolsSection == null)
            {
                return;
            }

            toolsLoaded = true;
            toolsSection.Refresh();
        }

        private void EnsureResourcesLoaded()
        {
            if (resourcesLoaded)
            {
                return;
            }

            if (resourcesSection == null)
            {
                return;
            }

            resourcesLoaded = true;
            resourcesSection.Refresh();
        }

        /// <summary>
        /// Applies the .section-last class to the last .section element in each .section-stack container.
        /// This is a workaround for Unity UI Toolkit not supporting the :last-child pseudo-class.
        /// </summary>
        private void ApplySectionLastClasses()
        {
            var sectionStacks = rootVisualElement.Query<VisualElement>(className: "section-stack").ToList();
            foreach (var stack in sectionStacks)
            {
                var sections = stack.Children().Where(c => c.ClassListContains("section")).ToList();
                if (sections.Count > 0)
                {
                    // Remove class from all sections first (in case of refresh)
                    foreach (var section in sections)
                    {
                        section.RemoveFromClassList("section-last");
                    }
                    // Add class to the last section
                    sections[sections.Count - 1].AddToClassList("section-last");
                }
            }
        }

        // Throttle OnEditorUpdate to avoid per-frame overhead (GitHub issue #577).
        // Connection status polling every frame caused expensive network checks 60+ times/sec.
        private double _lastEditorUpdateTime;
        private const double EditorUpdateIntervalSeconds = 2.0;

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += OnEditorUpdate;
            OpenWindows.Add(this);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            OpenWindows.Remove(this);
            toolsLoaded = false;
            resourcesLoaded = false;
        }

        private void OnFocus()
        {
            // Only refresh data if UI is built
            if (rootVisualElement == null || rootVisualElement.childCount == 0)
                return;

            RefreshAllData();
        }

        private void OnEditorUpdate()
        {
            // Throttle to 2-second intervals instead of every frame.
            // This prevents the expensive IsLocalHttpServerReachable() socket checks from running
            // 60+ times per second, which caused main thread blocking and GC pressure.
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastEditorUpdateTime < EditorUpdateIntervalSeconds)
            {
                return;
            }
            _lastEditorUpdateTime = now;

            if (rootVisualElement == null || rootVisualElement.childCount == 0)
                return;

            connectionSection?.UpdateConnectionStatus();
            Repaint();   // the connection pill and the bars' summaries
        }

        private void RefreshAllData()
        {
            // Debounce rapid successive calls (e.g., from OnFocus being called multiple times)
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - lastRefreshTime < RefreshDebounceSeconds)
            {
                return;
            }
            lastRefreshTime = currentTime;

            connectionSection?.UpdateConnectionStatus();

            if (MCPServiceLocator.Bridge.IsRunning)
            {
                _ = connectionSection?.VerifyBridgeConnectionAsync();
            }

            advancedSection?.UpdatePathOverrides();
            clientConfigSection?.RefreshSelectedClient();
        }

        internal static void RequestHealthVerification()
        {
            foreach (var window in OpenWindows)
            {
                window?.ScheduleHealthCheck();
            }
        }

        private void ScheduleHealthCheck()
        {
            EditorApplication.delayCall += async () =>
            {
                // Ensure window and components are still valid before execution
                if (this == null || connectionSection == null)
                {
                    return;
                }

                try
                {
                    await connectionSection.VerifyBridgeConnectionAsync();
                }
                catch (Exception ex)
                {
                    // Log but don't crash if verification fails during cleanup
                    McpLog.Warn($"Health check verification failed: {ex.Message}");
                }
            };
        }

        private static void BuildDependenciesSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section");

            var title = new Label("Optional Dependencies");
            title.AddToClassList("section-title");
            section.Add(title);

            var content = new VisualElement();
            content.AddToClassList("section-content");

            var desc = new Label("Some tool groups require optional packages. Install them to unlock additional capabilities.");
            desc.AddToClassList("validation-description");
            desc.style.marginBottom = 4;
            content.Add(desc);

            // Install All / Uninstall All buttons
            var bulkRow = new VisualElement();
            bulkRow.style.flexDirection = FlexDirection.Row;
            bulkRow.style.marginBottom = 8;

            var upmPackages = new[] { "com.unity.probuilder", "com.unity.cinemachine", "com.unity.visualeffectgraph", "com.unity.cloud.gltfast" };

            Button installAllButton = null;
            installAllButton = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Install All Dependencies",
                    "This will install Roslyn DLLs, ProBuilder, Cinemachine, VFX Graph, and glTFast. Continue?",
                    "Install All", "Cancel")) return;
                installAllButton.SetEnabled(false);
                installAllButton.text = "Installing...";
                if (!RoslynInstaller.IsInstalled()) RoslynInstaller.Install(interactive: false);
                BatchUpmAdd(upmPackages, () =>
                {
                    installAllButton.SetEnabled(true);
                    installAllButton.text = "Install All";
                });
            });
            installAllButton.text = "Install All";
            installAllButton.AddToClassList("action-button");
            installAllButton.style.marginRight = 4;
            bulkRow.Add(installAllButton);

            Button uninstallAllButton = null;
            uninstallAllButton = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Uninstall All Dependencies",
                    "This will remove Roslyn DLLs, ProBuilder, Cinemachine, VFX Graph, and glTFast. Continue?",
                    "Uninstall All", "Cancel")) return;
                uninstallAllButton.SetEnabled(false);
                uninstallAllButton.text = "Removing...";
                UninstallRoslyn();
                BatchUpmRemove(upmPackages, () =>
                {
                    uninstallAllButton.SetEnabled(true);
                    uninstallAllButton.text = "Uninstall All";
                });
            });
            uninstallAllButton.text = "Uninstall All";
            uninstallAllButton.AddToClassList("action-button");
            bulkRow.Add(uninstallAllButton);

            content.Add(bulkRow);

            // Roslyn — for execute_code modern C# support
            // Check if Roslyn types are actually loaded (covers NuGet, Plugins folder, etc.)
            bool roslynLoaded = Type.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation, Microsoft.CodeAnalysis.CSharp") != null;
            bool roslynInstalledLocally = RoslynInstaller.IsInstalled();
            AddDependencyRow(content,
                "Roslyn (C# 12+ Compiler)",
                "Enables modern C# syntax in execute_code tool (scripting_ext group).",
                roslynLoaded,
                roslynInstalledLocally
                    ? "Installed via Plugins/Roslyn \u2014 execute_code uses Roslyn"
                    : "Available (loaded from NuGet/external) \u2014 execute_code uses Roslyn",
                "Not installed \u2014 execute_code falls back to C# 6 (CodeDom)",
                done => { RoslynInstaller.Install(interactive: true); done?.Invoke(); },
                roslynInstalledLocally
                    ? (Action<Action>)(done => { UninstallRoslyn(); done?.Invoke(); })
                    : null);

            // ProBuilder
            bool hasProBuilder = Type.GetType("UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder") != null;
            AddDependencyRow(content,
                "ProBuilder",
                "Required for the manage_probuilder tool (probuilder group).",
                hasProBuilder,
                "Installed",
                "Not installed",
                done => InstallUpmPackage("com.unity.probuilder", done),
                done => RemoveUpmPackage("com.unity.probuilder", done));

            // Cinemachine
            bool hasCinemachine = Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine") != null
                || Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine") != null;
            AddDependencyRow(content,
                "Cinemachine",
                "Enhances manage_camera with virtual camera support (core group).",
                hasCinemachine,
                "Installed",
                "Not installed \u2014 camera tool works without it",
                done => InstallUpmPackage("com.unity.cinemachine", done),
                done => RemoveUpmPackage("com.unity.cinemachine", done));

            // VFX Graph — uses preprocessor symbol, so check via UPM package list
            bool hasVfxGraph = IsUpmPackageInstalled("com.unity.visualeffectgraph");
            AddDependencyRow(content,
                "VFX Graph",
                "Enables VisualEffect support in manage_vfx tool (vfx group).",
                hasVfxGraph,
                "Installed",
                "Not installed \u2014 VFX tool falls back to ParticleSystem/LineRenderer",
                done => InstallUpmPackage("com.unity.visualeffectgraph", done),
                done => RemoveUpmPackage("com.unity.visualeffectgraph", done));

            // glTFast — uses an assembly type, but also check via UPM package list
            bool hasGltfast = IsUpmPackageInstalled("com.unity.cloud.gltfast") || Type.GetType("GLTFast.GltfImport, glTFast") != null;
            AddDependencyRow(content,
                "glTFast (glTF/GLB import)",
                "Enables .glb/.gltf model import for the AI Asset Generation tools (asset_gen group).",
                hasGltfast,
                "Installed — GLB generation/import works",
                "Not installed — GLB import is unavailable; FBX still works, or install to enable GLB",
                done => InstallUpmPackage("com.unity.cloud.gltfast", done),
                done => RemoveUpmPackage("com.unity.cloud.gltfast", done));

            section.Add(content);
            container.Add(section);
        }

        private static void AddDependencyRow(VisualElement parent, string name, string description,
            bool isInstalled, string installedText, string missingText,
            Action<Action> installAction, Action<Action> uninstallAction)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;
            row.style.paddingBottom = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            var nameLabel = new Label(name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexGrow = 1;
            header.Add(nameLabel);

            var statusIcon = new Label(isInstalled ? "\u2713" : "\u2717");
            statusIcon.style.color = isInstalled ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.8f, 0.4f, 0.4f);
            statusIcon.style.fontSize = 14;
            header.Add(statusIcon);

            row.Add(header);

            var descLabel = new Label(description);
            descLabel.AddToClassList("validation-description");
            descLabel.style.marginBottom = 2;
            row.Add(descLabel);

            var statusText = new Label(isInstalled ? installedText : missingText);
            statusText.style.fontSize = 11;
            statusText.style.color = isInstalled ? new Color(0.6f, 0.8f, 0.6f) : new Color(0.8f, 0.7f, 0.5f);
            row.Add(statusText);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 4;

            if (!isInstalled && installAction != null)
            {
                Button btn = null;
                btn = new Button(() =>
                {
                    btn.SetEnabled(false);
                    btn.text = "Installing...";
                    Action restore = () =>
                    {
                        btn.SetEnabled(true);
                        btn.text = "Install";
                    };
                    try { installAction(restore); }
                    catch (Exception e)
                    {
                        Debug.LogError($"[MCP] Install failed: {e.Message}");
                        restore();
                    }
                });
                btn.text = "Install";
                btn.AddToClassList("action-button");
                buttonRow.Add(btn);
            }

            if (isInstalled && uninstallAction != null)
            {
                Button btn = null;
                btn = new Button(() =>
                {
                    if (!EditorUtility.DisplayDialog("Remove " + name,
                        $"Are you sure you want to remove {name}?", "Remove", "Cancel")) return;
                    btn.SetEnabled(false);
                    btn.text = "Removing...";
                    Action restore = () =>
                    {
                        btn.SetEnabled(true);
                        btn.text = "Uninstall";
                    };
                    try { uninstallAction(restore); }
                    catch (Exception e)
                    {
                        Debug.LogError($"[MCP] Uninstall failed: {e.Message}");
                        restore();
                    }
                });
                btn.text = "Uninstall";
                btn.AddToClassList("action-button");
                buttonRow.Add(btn);
            }

            if (buttonRow.childCount > 0)
                row.Add(buttonRow);

            parent.Add(row);
        }

        private static void InstallUpmPackage(string packageId, Action onComplete = null)
        {
            BatchUpmAdd(new[] { packageId }, onComplete);
        }

        private static void RemoveUpmPackage(string packageId, Action onComplete = null)
        {
            BatchUpmRemove(new[] { packageId }, onComplete);
        }

        private static void BatchUpmAdd(string[] packageIds, Action onComplete = null)
        {
            var request = UnityEditor.PackageManager.Client.AddAndRemove(packageIds, null);
            EditorUtility.DisplayProgressBar("Installing Packages", $"Installing {packageIds.Length} package(s)...", 0.5f);
            PollUpmRequest(request, "install", onComplete);
        }

        private static void BatchUpmRemove(string[] packageIds, Action onComplete = null)
        {
            var request = UnityEditor.PackageManager.Client.AddAndRemove(null, packageIds);
            EditorUtility.DisplayProgressBar("Removing Packages", $"Removing {packageIds.Length} package(s)...", 0.5f);
            PollUpmRequest(request, "remove", onComplete);
        }

        private static void PollUpmRequest(UnityEditor.PackageManager.Requests.AddAndRemoveRequest request, string verb, Action onComplete)
        {
            EditorApplication.CallbackFunction pollCallback = null;
            pollCallback = () =>
            {
                if (!request.IsCompleted) return;
                EditorApplication.update -= pollCallback;
                EditorUtility.ClearProgressBar();
                if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
                    Debug.Log($"[MCP] Package {verb} succeeded.");
                else
                    Debug.LogError($"[MCP] Package {verb} failed: {request.Error?.message}");
                onComplete?.Invoke();
            };
            EditorApplication.update += pollCallback;
        }

        private static void UninstallRoslyn()
        {
            string folder = System.IO.Path.Combine(Application.dataPath, "Plugins/Roslyn");
            if (System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.Delete(folder, true);
                string metaPath = folder + ".meta";
                if (System.IO.File.Exists(metaPath))
                    System.IO.File.Delete(metaPath);
                AssetDatabase.Refresh();
                Debug.Log("[MCP] Roslyn DLLs removed from Assets/Plugins/Roslyn/");
            }
        }

        private static bool IsUpmPackageInstalled(string packageId)
        {
            // Check manifest.json directly — faster than async UPM API
            string manifestPath = System.IO.Path.Combine(Application.dataPath, "../Packages/manifest.json");
            if (!System.IO.File.Exists(manifestPath)) return false;
            string manifest = System.IO.File.ReadAllText(manifestPath);
            return manifest.Contains($"\"{packageId}\"");
        }
    }
}
