// Generated from Forge Toolkit 0.5.0 (Editor/Welcome/WelcomeWindow.cs): do not edit.
// This plugin's own copy, so it needs no toolkit. Change the toolkit's file, then decouple again.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ForgeMCP.Editor.Welcome
{
    /// <summary>A button of a Welcome page's "Open a window" grid: a name, a line under it, what it does.</summary>
    public readonly struct WelcomeButton
    {
        public readonly string Title, About;
        public readonly Action Action;
        public WelcomeButton(string title, string about, Action action) { Title = title; About = about; Action = action; }
        /// <summary>A button that runs a menu item.</summary>
        public WelcomeButton(string title, string about, string menuPath) : this(title, about, () => EditorApplication.ExecuteMenuItem(menuPath)) { }
    }

    /// <summary>
    /// A folding part of a Welcome page: a bar with its name and, on the right, what it holds (read at a glance when
    /// folded), and under it when open what it draws (IMGUI) or the element it gives (UI Toolkit, made once, when the
    /// page is). Opened: called each time the part is unfolded (bring what it shows up to date).
    /// </summary>
    public sealed class WelcomeSection
    {
        public readonly string Title;
        public readonly Func<string> Summary;
        public readonly Action Draw;
        public readonly Func<VisualElement> Element;
        public readonly Action Opened;
        public readonly bool OpenByDefault;
        public WelcomeSection(string title, Func<string> summary, Action draw, bool openByDefault = false)
        {
            Title = title; Summary = summary; Draw = draw; OpenByDefault = openByDefault;
        }
        public WelcomeSection(string title, Func<string> summary, Func<VisualElement> element, bool openByDefault = false, Action opened = null)
        {
            Title = title; Summary = summary; Element = element; OpenByDefault = openByDefault; Opened = opened;
        }
    }

    /// <summary>
    /// The contract of a Forge plugin's Welcome page, so that every plugin's looks and behaves the same: the logo
    /// in a card with a line about the plugin and its version, a grid of buttons ("Open a window"), then parts that
    /// fold, and whether the page shows when the editor starts. A plugin derives one class and gives only what is its
    /// own: its package's name (the version, the links and where it is installed from are read from the package), its
    /// logo, its buttons and its parts.
    /// <code>
    /// public sealed class MyWelcome : WelcomeWindow
    /// {
    ///     protected override string PackageName => "io.glitchr.myplugin";
    ///     protected override string LogoPath => "Packages/io.glitchr.myplugin/Editor/Images/MyPluginLogo.png";
    ///     protected override IEnumerable&lt;WelcomeButton&gt; Buttons => new[] { new WelcomeButton("Import", "Drop your files", MyImport.Open) };
    ///     protected override IEnumerable&lt;WelcomeSection&gt; Sections => new[] { BuildSection(), MySection(), LinksSection() };
    ///     [MenuItem("Tools/My Plugin/Welcome")] static void Open() => Open&lt;MyWelcome&gt;();
    ///     [InitializeOnLoadMethod] static void AtStartup() => ShowAtStartup&lt;MyWelcome&gt;("io.glitchr.myplugin");
    /// }
    /// </code>
    /// The logo: a picture about 970 points wide on a transparent background, the mark over the name; a file of the
    /// same name ending in "-White" beside it is used on the dark skin.
    /// </summary>
    public abstract class WelcomeWindow : EditorWindow
    {
        // ---- What a plugin gives ------------------------------------------------------------------------------------

        /// <summary>The plugin's package (io.glitchr.…): its version, links and source are read from it.</summary>
        protected abstract string PackageName { get; }
        /// <summary>The tab's title; the package's display name unless given.</summary>
        protected virtual string ProductName => Package != null && !string.IsNullOrEmpty(Package.displayName) ? Package.displayName : PackageName;
        /// <summary>The logo's asset path (see the class summary), or null for the name in letters.</summary>
        protected virtual string LogoPath => null;
        /// <summary>A line or two about the plugin, under the logo.</summary>
        protected virtual string About => null;
        /// <summary>The "Open a window" grid's buttons, two a row.</summary>
        protected virtual IEnumerable<WelcomeButton> Buttons => Enumerable.Empty<WelcomeButton>();
        /// <summary>The page's folding parts, in order. <see cref="BuildSection"/>, <see cref="PackagesSection"/> and
        /// <see cref="LinksSection"/> are ready made.</summary>
        protected virtual IEnumerable<WelcomeSection> Sections => new[] { BuildSection(), LinksSection() };
        /// <summary>The widest the logo is drawn, in points.</summary>
        protected virtual float LogoWidth => 300f;

        // ---- Opening ------------------------------------------------------------------------------------------------

        /// <summary>Opens a plugin's Welcome page.</summary>
        public static T Open<T>() where T : WelcomeWindow
        {
            var window = GetWindow<T>();
            window.minSize = new Vector2(300f, 380f);
            window.Show();
            return window;
        }

        /// <summary>
        /// To call from a plugin's [InitializeOnLoadMethod]: opens its page the first time the plugin runs in a
        /// project, and afterwards at each start of the editor only if the page's tick asks for it. Never at a mere
        /// script reload, in batch mode, or in an import worker.
        /// </summary>
        public static void ShowAtStartup<T>(string packageName) where T : WelcomeWindow
        {
            if (Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess()) return;
            EditorApplication.delayCall += () =>
            {
                string prefix = Prefix(packageName);
                if (SessionState.GetBool(prefix + "Session", false)) return;
                SessionState.SetBool(prefix + "Session", true);
                bool first = EditorUserSettings.GetConfigValue(prefix + "Seen") != "1";
                if (!first && !EditorPrefs.GetBool(prefix + "AtStartup", false)) return;
                EditorUserSettings.SetConfigValue(prefix + "Seen", "1");
                Open<T>();
            };
        }

        static string Prefix(string packageName) => "GlitchArt.Welcome." + packageName + ".";

        // ---- The package --------------------------------------------------------------------------------------------

        PackageInfo package;
        bool packageSought;

        /// <summary>The plugin's package, as the Package Manager knows it (null when it is not installed as one).</summary>
        protected PackageInfo Package
        {
            get
            {
                if (packageSought) return package;
                packageSought = true;
                package = PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == PackageName);
                return package;
            }
        }

        /// <summary>The plugin's version, as its package says.</summary>
        protected virtual string Version => Package != null ? Package.version : "unknown";

        /// <summary>Where a package comes from, in a word or two: "source" for a folder, a Git commit, a registry.</summary>
        public static string Source(PackageInfo p)
        {
            if (p == null) return "unknown";
            if (p.git != null && !string.IsNullOrEmpty(p.git.hash)) return "Git " + p.git.hash.Substring(0, Mathf.Min(8, p.git.hash.Length));
            return p.source switch
            {
                UnityEditor.PackageManager.PackageSource.Embedded => "source",
                UnityEditor.PackageManager.PackageSource.Local => "source",
                UnityEditor.PackageManager.PackageSource.LocalTarball => "tarball",
                UnityEditor.PackageManager.PackageSource.Registry => "registry",
                UnityEditor.PackageManager.PackageSource.BuiltIn => "built in",
                _ => p.source.ToString(),
            };
        }

        // ---- Ready-made parts ---------------------------------------------------------------------------------------

        /// <summary>
        /// Which build this is, in full: the plugin's version, its package and where it comes from (a commit, a folder),
        /// when its code was last compiled here, Unity, the build target, the render pipeline, the project; Copy all puts
        /// it on the clipboard for a bug report. The version pill opens this part.
        /// </summary>
        protected WelcomeSection BuildSection() => new(BuildTitle, () => $"{Version} · Unity {Application.unityVersion}", () =>
        {
            foreach (var (label, value) in BuildRows()) Row(label, value);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy all", EditorStyles.miniButton, GUILayout.Width(70f)))
                    EditorGUIUtility.systemCopyBuffer = string.Join("\n", BuildRows().Select(r => $"{r.label}: {r.value}"));
                GUILayout.Space(6f);
            }
        });

        const string BuildTitle = "Build";

        System.Collections.Generic.IEnumerable<(string label, string value)> BuildRows()
        {
            yield return (ProductName, $"{Version} ({Source(Package)})");
            if (Package != null)
            {
                yield return ("Package", Package.name);
                if (Package.git != null && !string.IsNullOrEmpty(Package.git.hash)) yield return ("Commit", Package.git.hash);
                if (!string.IsNullOrEmpty(Package.resolvedPath)) yield return ("Folder", Package.resolvedPath);
            }
            yield return ("Compiled", Compiled());
            yield return ("Unity", Application.unityVersion);
            yield return ("Build target", EditorUserBuildSettings.activeBuildTarget.ToString());
            yield return ("Render pipeline", GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name.Replace("Asset", "") : "Built-in");
            yield return ("Project", $"{Application.productName} ({PlayerSettings.companyName})");
        }

        /// <summary>When the plugin's code was last compiled in this project: its assembly's time on disk.</summary>
        string Compiled()
        {
            try
            {
                var file = GetType().Assembly.Location;
                if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file)) return System.IO.File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception) { }
            return "unknown";
        }

        /// <summary>The package a Welcome page is for (the plugin's own page is found by it).</summary>
        public string PackageId => PackageName;

        /// <summary>
        /// The files a report of a problem with this plugin needs, asked for one by one by the toolkit's Report a
        /// Problem (read by name, so a plugin's copy of this contract needs no toolkit). Each is
        /// "Label|extensions, comma separated|why it is needed|path"; a label ending in * is one the report should not go
        /// without, and the path (optional) is the file itself when the plugin knows it, attached without asking. A
        /// scene by default.
        /// </summary>
        public virtual string[] SupportFiles => new[] { "Scene*|unity|The scene where it happens: the problem can then be seen on our side." };

        static System.Reflection.MethodInfo supportPage;
        static bool supportSought;

        /// <summary>
        /// The Report a Problem window, found by convention (this contract is copied into plugins and names no type
        /// of the toolkit): a window named SupportWindow with a public static Open(string package). The one in this
        /// contract's own namespace first (a plugin's own copy), else any (the toolkit's). Null when there is none.
        /// </summary>
        static System.Reflection.MethodInfo SupportPage()
        {
            if (supportSought) return supportPage;
            supportSought = true;
            foreach (var t in TypeCache.GetTypesDerivedFrom<EditorWindow>())
            {
                if (t.Name != "SupportWindow") continue;
                var open = t.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (open == null) continue;
                if (t.Namespace == typeof(WelcomeWindow).Namespace) { supportPage = open; break; }
                supportPage ??= open;
            }
            return supportPage;
        }

        /// <summary>The package's links: documentation, changelog, licence, the author's site; Support with the toolkit.</summary>
        protected WelcomeSection LinksSection() => new("Links", () => "", () =>
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(Indent);
                // Each goes to its address, else to the package's own file; one with neither is not shown.
                PageLink("Documentation", Package?.documentationUrl, "README.md");
                PageLink("Changelog", Package?.changelogUrl, "CHANGELOG.md");
                PageLink("Licence", Package?.licensesUrl, "LICENSE.md");
                var support = SupportPage();
                if (support != null && GUILayout.Button(new GUIContent("Support", "Report a problem: a zip with the versions, your files and the log, and a mail already written"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    support.Invoke(null, new object[] { PackageName });
                GUILayout.FlexibleSpace();
            }
        }, false);

        // ---- Pieces a part is drawn with ----------------------------------------------------------------------------

        /// <summary>A part's left margin, in points.</summary>
        protected const float Indent = 14f;

        /// <summary>A name and its value on a line (the value can be selected and copied).</summary>
        protected static void Row(string label, string value)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(Indent);
                GUILayout.Label(label, GUILayout.Width(120f));
                EditorGUILayout.SelectableLabel(value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        /// <summary>A wrapped line of text (rich text: b, i).</summary>
        protected void Note(string text)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(Indent);
                GUILayout.Label(text, wrapped);
                GUILayout.Space(6f);
            }
        }

        /// <summary>
        /// A feature and what it does, as a row of a table: its name in bold in a first column, a short line beside it
        /// (dimmer, wrapped), a hairline under it. Keep the line short: one phrase reads at a glance, a paragraph does not.
        /// </summary>
        protected void Item(string name, string about)
        {
            GUILayout.Space(3f);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(Indent);
                GUILayout.Label(name, bold, GUILayout.Width(ItemNameWidth));
                // As wide as the page leaves it, and no wider: a wrapped label asks for its text's whole length on one
                // line, and the page would scroll sideways instead of wrapping it.
                GUILayout.Label(about, dim, GUILayout.Width(Mathf.Max(60f, EditorGUIUtility.currentViewWidth - Indent - ItemNameWidth - 36f)));
                GUILayout.Space(6f);
            }
            GUILayout.Space(3f);
            var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            line.xMin += Indent; line.xMax -= 6f;
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.22f));
        }

        /// <summary>The width of the names' column in a list of <see cref="Item"/> rows.</summary>
        protected virtual float ItemNameWidth => 92f;

        /// <summary>A small button to a page of the package: its address when the package gives one, else its own file (shown in the Project window and opened); nothing with neither.</summary>
        void PageLink(string label, string url, string file)
        {
            if (!string.IsNullOrEmpty(url)) { Link(label, url); return; }
            string path = Package != null ? $"{Package.assetPath}/{file}" : null;
            var asset = path != null ? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) : null;
            if (asset == null) return;
            if (GUILayout.Button(new GUIContent(label, path), EditorStyles.miniButton, GUILayout.ExpandWidth(false))) { EditorGUIUtility.PingObject(asset); AssetDatabase.OpenAsset(asset); }
        }

        /// <summary>A small button that opens a page; greyed without one.</summary>
        protected static void Link(string label, string url)
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(url)))
                if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.ExpandWidth(false))) Application.OpenURL(url);
        }

        // ---- The page -----------------------------------------------------------------------------------------------

        Vector2 scroll;
        Texture2D logo;
        bool logoSought, logoForPro;
        float logoAspect = 1f;
        // Not serialized: Unity keeps a window's fields over a script reload, and a style added since came back as an
        // empty GUIStyle (not null, no wrapping), so the page never made it.
        [NonSerialized] GUIStyle wrapped, dim, bold, centred, pill, sectionTitle, summaryStyle, buttonTitle, buttonAbout, bigName;
        WelcomeSection[] sections;
        WelcomeButton[] buttons;

        protected virtual void OnEnable()
        {
            packageSought = false; logoSought = false; sections = null; buttons = null;
            titleContent = new GUIContent(ProductName);
            wantsMouseMove = true;   // the pills light up under the pointer
        }

        Texture2D Logo()
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (logoSought && logoForPro == pro) return logo;
            logoSought = true; logoForPro = pro;
            logo = null;
            string path = LogoPath;
            if (string.IsNullOrEmpty(path)) return null;
            if (pro)
            {
                int dot = path.LastIndexOf('.');
                if (dot > 0) logo = AssetDatabase.LoadAssetAtPath<Texture2D>(path.Substring(0, dot) + "-White" + path.Substring(dot));
            }
            if (logo == null) logo = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            // The picture's own shape, not the imported texture's: an import left at Unity's defaults rounds each
            // side to a power of two (554 x 731 became 512 x 512), and the logo was drawn square.
            logoAspect = logo != null ? (float)logo.height / logo.width : 1f;
            if (logo != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(logo)) is TextureImporter importer)
            {
                importer.GetSourceTextureWidthAndHeight(out int sw, out int sh);
                if (sw > 0 && sh > 0) logoAspect = (float)sh / sw;
            }
            return logo;
        }

        void Styles()
        {
            if (wrapped != null) return;
            wrapped = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
            dim = new GUIStyle(wrapped);
            var ink = dim.normal.textColor; ink.a = 0.72f; dim.normal.textColor = ink;
            bold = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
            centred = new GUIStyle(EditorStyles.label) { wordWrap = true, alignment = TextAnchor.UpperCenter, fontSize = 13 };
            // White text in every state: it stays readable on the pills' bright hover colours.
            pill = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
            pill.normal.textColor = pill.hover.textColor = pill.active.textColor = pill.focused.textColor = Color.white;
            sectionTitle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
            summaryStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            buttonTitle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.LowerCenter, fontSize = 13, clipping = TextClipping.Clip };
            buttonAbout = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, fontSize = 11, clipping = TextClipping.Clip };
            bigName = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 24 };
        }

        // The page is built once, in UI Toolkit: one scroll, and in it the card and the buttons, then each part's bar
        // and what it holds - IMGUI drawn in its own container, or the element a part gives - then the start-up box.
        // So a part may be either, and the IMGUI pages look as they did when the page was one OnGUI.
        ScrollView page;
        readonly List<(WelcomeSection part, VisualElement bar, VisualElement body)> parts = new();

        /// <summary>Called once the page is built: a page adds its style sheets, or starts what it shows, here.</summary>
        protected virtual void PageBuilt() { }

        /// <summary>Builds the page (Unity calls it when the window opens and after a reload).</summary>
        protected void CreateGUI()
        {
            sections = Sections.Where(s => s != null).ToArray();
            buttons = Buttons.ToArray();
            parts.Clear();
            rootVisualElement.Clear();
            page = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            page.style.flexGrow = 1f;
            rootVisualElement.Add(page);
            var head = Imgui(() => { Header(); ButtonGrid(); });
            // The pills light up under the pointer.
            head.RegisterCallback<PointerMoveEvent>(_ => head.MarkDirtyRepaint());
            head.RegisterCallback<PointerLeaveEvent>(_ => head.MarkDirtyRepaint());
            page.Add(head);
            foreach (var s in sections)
            {
                VisualElement body = null;
                if (s.Element != null)
                {
                    try { body = s.Element(); }
                    catch (Exception ex) { Debug.LogException(ex); }
                    if (body != null) { body.style.paddingTop = 4f; body.style.paddingBottom = 6f; }
                }
                else if (s.Draw != null) body = Imgui(() => { s.Draw(); GUILayout.Space(4f); });
                IMGUIContainer bar = null;
                bar = Imgui(() => Bar(s, bar));
                page.Add(bar);
                if (body != null) page.Add(body);
                parts.Add((s, bar, body));
                Show(s, IsOpen(s), opening: IsOpen(s));
            }
            page.Add(Imgui(() =>
            {
                GUILayout.Space(8f);
                string key = Prefix(PackageName) + "AtStartup";
                bool atStart = EditorPrefs.GetBool(key, false);
                bool now = EditorGUILayout.ToggleLeft("Show this page when the editor starts", atStart);
                if (now != atStart) EditorPrefs.SetBool(key, now);
                GUILayout.Space(6f);
            }));
            // Where the page was scrolled to, kept over a reload.
            var at = scroll;
            page.schedule.Execute(() => page.scrollOffset = at);
            page.verticalScroller.valueChanged += v => scroll.y = v;
            PageBuilt();
        }

        IMGUIContainer Imgui(Action draw) => new IMGUIContainer(() =>
        {
            var e = Event.current;
            if (e.rawType == EventType.MouseDown && e.button == 0) pressed = true;
            else if (e.rawType == EventType.MouseUp) { pressed = false; Repaint(); }
            Styles();
            draw();
        });

        bool IsOpen(WelcomeSection s) => EditorPrefs.GetBool(Prefix(PackageName) + "Open." + s.Title, s.OpenByDefault);

        void Show(WelcomeSection s, bool open, bool opening)
        {
            foreach (var p in parts)
            {
                if (p.part != s) continue;
                if (p.body != null) p.body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open && opening)
                {
                    try { s.Opened?.Invoke(); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
        }

        // A pill: a rectangle with round ends (or only some corners round: top-left, top-right, bottom-right, bottom-left).
        /// <summary>
        /// A pill's colour under the pointer: the same hue made dark and solid (dark green, dark blue), darker still while
        /// pressed, so the white text stands out more, never less (a lighter hover washed it out); <see cref="Pill"/>
        /// rings it with a thin light line.
        /// </summary>
        static Color Lit(Rect r, Color c)
        {
            if (!r.Contains(Event.current.mousePosition)) return c;
            float k = pressed ? 0.42f : 0.6f;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        static bool pressed;
        bool revealBuild;

        static void Pill(Rect r, Color colour, Vector4? corners = null)
        {
            float round = r.height * 0.5f;
            var radii = corners ?? new Vector4(round, round, round, round);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, Vector4.zero, radii);
            // Under the pointer: a thin light ring, and the hand cursor (the pills are buttons).
            if (r.Contains(Event.current.mousePosition))
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.55f), new Vector4(1f, 1f, 1f, 1f), radii);
        }

        // The card: the logo (never wider than the window lets it be), the line about the plugin, the version.
        void Header()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(10f);
                var picture = Logo();
                float room = Mathf.Max(60f, EditorGUIUtility.currentViewWidth - 44f);
                if (picture != null)
                {
                    float w = Mathf.Min(LogoWidth, room), h = w * logoAspect;
                    var rect = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(true));
                    GUI.DrawTexture(new Rect(rect.x + (rect.width - w) * 0.5f, rect.y, w, h), picture, ScaleMode.StretchToFill, true);
                }
                else GUILayout.Label(ProductName, bigName, GUILayout.Height(40f));
                if (!string.IsNullOrEmpty(About))
                {
                    GUILayout.Space(8f);
                    GUILayout.Label(About, centred);
                }
                GUILayout.Space(8f);
                // The version, in a pill: which build this is, at a glance. Beside it the editor's MCP server: a click
                // starts or stops it, the arrow opens its page.
                var text = new GUIContent($"Version {Version} · {Source(Package)}");
                // Forge MCP (io.glitchr.mcp), when installed: this editor's connection, on or off; when not, a way to get it.
                bool installed = McpLink.Installed, serving = installed && McpLink.Running;
                var mcp = !installed
                    ? new GUIContent("MCP · Get", $"Forge MCP is not in this project: {McpLink.GetLabel.ToLowerInvariant()}")
                    : new GUIContent(serving ? $"MCP ● :{McpLink.Port}" : "MCP ○ off",
                        serving ? $"This editor is connected to AI tools at {McpLink.Url}. Click to disconnect it (you are asked first)." : "This editor is not connected to the MCP server. Click to connect it.");
                const float gap = 6f, arrow = 22f;
                float pw = pill.CalcSize(text).x + 28f, mw = pill.CalcSize(mcp).x + 20f;
                float total = Mathf.Min(room, pw + gap + mw + 2f + arrow);
                var line = GUILayoutUtility.GetRect(total, 22f, GUILayout.ExpandWidth(true));
                float x = line.x + (line.width - total) * 0.5f;
                var box = new Rect(x, line.y, pw, 22f);
                var chip = new Rect(box.xMax + gap, line.y, mw, 22f);
                var open = new Rect(chip.xMax + 2f, line.y, arrow, 22f);
                if (Event.current.type == EventType.Repaint)
                {
                    Pill(box, Lit(box, EditorGUIUtility.isProSkin ? new Color(0.36f, 0.5f, 0.38f, 0.75f) : new Color(0.55f, 0.75f, 0.58f, 0.75f)));
                    var on = EditorGUIUtility.isProSkin ? new Color(0.25f, 0.42f, 0.62f, 0.85f) : new Color(0.55f, 0.7f, 0.9f, 0.85f);
                    var off = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.1f) : new Color(0f, 0f, 0f, 0.1f);
                    // The chip and its arrow read as one pill cut in two: round on the outside, square where they meet.
                    float round = chip.height * 0.5f;
                    Pill(chip, Lit(chip, serving ? on : off), new Vector4(round, 0f, 0f, round));
                    Pill(open, Lit(open, serving ? on : off), new Vector4(0f, round, round, 0f));
                }
                EditorGUIUtility.AddCursorRect(box, MouseCursor.Link);
                EditorGUIUtility.AddCursorRect(chip, MouseCursor.Link);
                EditorGUIUtility.AddCursorRect(open, MouseCursor.Link);
                if (GUI.Button(box, new GUIContent(text.text, "The build's details: where it comes from, when it was compiled, Unity, the pipeline"), pill))
                {
                    EditorPrefs.SetBool(Prefix(PackageName) + "Open." + BuildTitle, true);
                    revealBuild = true;
                    Repaint();
                }
                if (GUI.Button(chip, mcp, pill)) { if (installed) McpLink.Toggle(); else McpLink.Get(); }
                if (GUI.Button(open, new GUIContent("›", installed ? "Forge MCP's window: its server, the clients, the tools" : "Forge MCP: what it is"), pill))
                {
                    if (installed) McpLink.OpenWindow(); else Application.OpenURL("https://github.com/meiyasan/unity-mcp");
                }
                GUILayout.Space(10f);
            }
        }

        // The buttons, two a row; one a row only when half the card is narrower than the project's setting (Project
        // Settings > Forge Toolkit, 100 points by default). A title or a line under it too
        // long for its button goes on two lines, every button of the grid then as tall as the tallest needs.
        void ButtonGrid()
        {
            if (buttons.Length == 0) return;
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Open a window", EditorStyles.boldLabel);
                float card = EditorGUIUtility.currentViewWidth - 34f;
                int perRow = (card - 4f) / 2f >= WelcomeSettings.ButtonMinWidth ? 2 : 1;
                float inner = (card - (perRow - 1) * 4f) / perRow - 8f;
                float widestTitle = 0f, widestAbout = 0f;
                foreach (var b in buttons)
                {
                    widestTitle = Mathf.Max(widestTitle, buttonTitle.CalcSize(new GUIContent(b.Title)).x);
                    widestAbout = Mathf.Max(widestAbout, buttonAbout.CalcSize(new GUIContent(b.About)).x);
                }
                bool titleTwo = widestTitle > inner, aboutTwo = widestAbout > inner;
                buttonTitle.wordWrap = titleTwo; buttonAbout.wordWrap = aboutTwo;
                float titleHeight = titleTwo ? 36f : 22f, aboutHeight = aboutTwo ? 30f : 16f;
                float height = titleHeight + aboutHeight + 4f;
                for (int i = 0; i < buttons.Length; i += perRow)
                {
                    // A row takes the card's whole width, shared between its buttons.
                    var row = GUILayoutUtility.GetRect(0f, height + 2f, GUILayout.ExpandWidth(true));
                    float w = (row.width - (perRow - 1) * 4f) / perRow;
                    for (int j = i; j < Mathf.Min(i + perRow, buttons.Length); j++)
                    {
                        var b = buttons[j];
                        var rect = new Rect(row.x + (j - i) * (w + 4f), row.y, w, height);
                        if (GUI.Button(rect, GUIContent.none)) b.Action?.Invoke();
                        GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, rect.width - 8f, titleHeight), b.Title, buttonTitle);
                        GUI.Label(new Rect(rect.x + 4f, rect.y + 1f + titleHeight, rect.width - 8f, aboutHeight), b.About, buttonAbout);
                    }
                }
                GUILayout.Space(2f);
            }
        }

        // A bar the width of the window: an arrow, the part's name, and on the right what it holds. A click anywhere
        // on it folds or unfolds; the state is kept.
        void Bar(WelcomeSection s, VisualElement host)
        {
            string key = Prefix(PackageName) + "Open." + s.Title;
            bool open = EditorPrefs.GetBool(key, s.OpenByDefault);
            GUILayout.Space(2f);
            var bar = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(bar, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.08f));
                EditorStyles.foldout.Draw(new Rect(bar.x + 6f, bar.y + 4f, 14f, 16f), false, false, open, false);
            }
            GUI.Label(new Rect(bar.x + 22f, bar.y, bar.width * 0.6f, bar.height), s.Title, sectionTitle);
            string summary = null;
            try { summary = s.Summary?.Invoke(); } catch (Exception) { /* a summary that fails says nothing */ }
            if (!string.IsNullOrEmpty(summary)) GUI.Label(new Rect(bar.x + bar.width * 0.4f, bar.y, bar.width * 0.6f - 8f, bar.height), summary, summaryStyle);
            if (GUI.Button(bar, GUIContent.none, GUIStyle.none))
            {
                open = !open;
                EditorPrefs.SetBool(key, open);
                Show(s, open, opening: open);
            }
            if (revealBuild && s.Title == BuildTitle && Event.current.type == EventType.Repaint)
            {
                revealBuild = false;
                Show(s, true, opening: true);
                page?.schedule.Execute(() => page.ScrollTo(host));
            }
            if (open) GUILayout.Space(4f);
        }
    }
}
#endif
