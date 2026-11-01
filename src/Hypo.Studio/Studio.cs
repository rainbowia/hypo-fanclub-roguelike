using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Hypo.Core.Data;
using Hypo.Core.Effects;
using Hypo.Game.UI;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Hypo.Studio;

/// <summary>工作区：左（层级/列表）· 中（场景/主编辑区）· 右（检查器）。</summary>
public interface IWorkspace
{
    string Name { get; }
    void Left();
    void Center();
    void Right();
    void Save();
    void PreRender() { }
}

public static class Studio
{
    public static string Root, DataDir;
    public static bool Dirty;
    public static string DragAsset;     // 项目面板拖拽中的资源路径（相对 data/）
    public static string DragNode;      // 层级面板拖拽中的节点 Id
    public static readonly List<IWorkspace> Workspaces = new();
    public static int Current;
    static readonly List<string> _console = new();
    static bool _consoleScroll;
    static bool _quitAsked, _quit;
    static int _bottomTab;

    public static void MarkDirty() => Dirty = true;

    [STAThread]
    public static void Main(string[] args)
    {
        Root = FindRoot();
        DataDir = Path.Combine(Root, "data");
        Log.OnLog += s => { lock (_console) { _console.Add($"[{DateTime.Now:HH:mm:ss}] {s}"); if (_console.Count > 800) _console.RemoveAt(0); _consoleScroll = true; } };

        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1600, 950, "H.Y.P.O. Studio");
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(60);
        Raylib.MaximizeWindow();

        Assets.Root = DataDir;
        Fonts.Init(Path.Combine(DataDir, "assets", "fonts"));
        rlImGui.Setup(true);
        Theme.Apply();
        Theme.LoadFont(DataDir);
        Blocks.LoadPlugins(Path.Combine(Root, "mods"));
        ReloadGameDb();

        Workspaces.Add(new LayoutWorkspace());
        Workspaces.Add(DefWorkspace<CardDef>.Cards());
        Workspaces.Add(DefWorkspace<BossDef>.Simple("Boss", "bosses", b => b.Id, b => b.Name, b => b.Effects));
        var items = DefWorkspace<ItemDef>.Simple("道具", "items", b => b.Id, b => b.Name, b => b.Effects);
        EffectsEditor.ItemIds = () => items.Store.Items.Select(i => i.Id);
        Workspaces.Add(items);
        Workspaces.Add(DefWorkspace<HypnoDef>.Simple("催眠", "hypno", b => b.Id, b => b.Name, b => b.Effects));
        Workspaces.Add(DefWorkspace<DayEventDef>.Simple("每日事件", "events", b => b.Id, b => b.Name, b => b.Effects));
        Workspaces.Add(DefWorkspace<GalleryDef>.Gallery());
        Workspaces.Add(new RulesWorkspace());
        Workspaces.Add(new TextWorkspace());
        Workspaces.Add(new SandboxWorkspace());
        Log.Info("数据目录：" + DataDir);

        while (!_quit)
        {
            if (Raylib.WindowShouldClose()) { if (Dirty) _quitAsked = true; else _quit = true; }
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(22, 18, 22, 255));
            Workspaces[Current].PreRender();
            rlImGui.Begin();
            Frame();
            rlImGui.End();
            Raylib.EndDrawing();
            Fonts.EndFrame();
        }
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }

    /// <summary>向上找 HypoFanclub.sln 所在目录（避免改到 bin 里的副本）。</summary>
    static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "HypoFanclub.sln"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }

    public static void ReloadGameDb()
    {
        Hypo.Game.App.Db = ContentDb.Load(DataDir);
    }

    public static void SaveAll()
    {
        try
        {
            foreach (var w in Workspaces) w.Save();
            Dirty = false;
            ReloadGameDb();
            Log.Info("已保存全部 ✓");
        }
        catch (Exception e) { Log.Warn("保存失败：" + e.Message); }
    }

    // =====================================================================
    const ImGuiWindowFlags Pane = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus;

    static void Frame()
    {
        var io = ImGui.GetIO();
        // ---------- 快捷键 ----------
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S)) SaveAll();
        if (ImGui.IsKeyPressed(ImGuiKey.F5) && !io.WantTextInput) RunGame();

        // ---------- 菜单 ----------
        float menuH = 0;
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("文件"))
            {
                if (ImGui.MenuItem("保存全部", "Ctrl+S")) SaveAll();
                if (ImGui.MenuItem("刷新图片缓存")) { Assets.Clear(); Log.Info("图片缓存已清空"); }
                if (ImGui.MenuItem("打开数据目录")) Process.Start(new ProcessStartInfo { FileName = DataDir, UseShellExecute = true });
                ImGui.Separator();
                if (ImGui.MenuItem("退出")) { if (Dirty) _quitAsked = true; else _quit = true; }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("运行"))
            {
                if (ImGui.MenuItem("保存并运行游戏", "F5")) RunGame();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("帮助"))
            {
                Help.Menu();
                ImGui.EndMenu();
            }
            ImGui.SameLine(ImGui.GetWindowWidth() - 260);
            ImGui.TextDisabled(Dirty ? "● 有未保存的修改" : "已保存");
            menuH = ImGui.GetWindowHeight();
            ImGui.EndMainMenuBar();
        }

        float W = Raylib.GetScreenWidth(), H = Raylib.GetScreenHeight();
        float toolH = 46, leftW = MathF.Max(260, W * 0.17f), rightW = MathF.Max(340, W * 0.24f), bottomH = MathF.Max(180, H * 0.24f);
        float top = menuH + toolH, midH = H - top - bottomH;

        // ---------- 工具栏：工作区切换 ----------
        ImGui.SetNextWindowPos(new Vector2(0, menuH));
        ImGui.SetNextWindowSize(new Vector2(W, toolH));
        ImGui.Begin("##toolbar", Pane | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        for (int i = 0; i < Workspaces.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool on = i == Current;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
            if (ImGui.Button(Workspaces[i].Name, new Vector2(0, 30))) Current = i;
            if (on) ImGui.PopStyleColor();
        }
        ImGui.SameLine(W - 150);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.55f, 0.35f, 1));
        if (ImGui.Button("▶ 运行游戏", new Vector2(130, 30))) RunGame();
        ImGui.PopStyleColor();
        ImGui.End();

        var ws = Workspaces[Current];
        if (Begin("层级##L", 0, top, leftW, midH)) ws.Left();
        ImGui.End();
        if (Begin(ws.Name + "##C", leftW, top, W - leftW - rightW, midH)) ws.Center();
        ImGui.End();
        if (Begin("检查器##R", W - rightW, top, rightW, midH)) ws.Right();
        ImGui.End();
        if (Begin("##B", 0, top + midH, W, bottomH, ImGuiWindowFlags.NoTitleBar)) Bottom();
        ImGui.End();

        // ---------- 退出确认 ----------
        if (_quitAsked) { ImGui.OpenPopup("退出"); _quitAsked = false; }
        Help.Popup();
        bool keep = true;
        if (ImGui.BeginPopupModal("退出", ref keep, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("有未保存的修改。");
            if (ImGui.Button("保存并退出", new Vector2(140, 0))) { SaveAll(); _quit = true; }
            ImGui.SameLine();
            if (ImGui.Button("不保存退出", new Vector2(140, 0))) _quit = true;
            ImGui.SameLine();
            if (ImGui.Button("取消", new Vector2(80, 0))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    static bool Begin(string title, float x, float y, float w, float h, ImGuiWindowFlags extra = 0)
    {
        ImGui.SetNextWindowPos(new Vector2(x, y));
        ImGui.SetNextWindowSize(new Vector2(w, h));
        return ImGui.Begin(title, Pane | extra);
    }

    static void RunGame()
    {
        SaveAll();
        try
        {
            Process.Start(new ProcessStartInfo("dotnet", "run --project src/Hypo.Game") { WorkingDirectory = Root, UseShellExecute = true });
            Log.Info("正在编译并启动游戏…");
        }
        catch (Exception e) { Log.Warn("启动失败：" + e.Message); }
    }

    // =====================================================================
    static void Bottom()
    {
        if (ImGui.BeginTabBar("bottom"))
        {
            if (ImGui.BeginTabItem("项目 Project")) { _bottomTab = 0; ProjectPanel.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("控制台 Console")) { _bottomTab = 1; ConsolePanel(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    static void ConsolePanel()
    {
        if (ImGui.SmallButton("清空")) lock (_console) _console.Clear();
        ImGui.Separator();
        ImGui.BeginChild("log", new Vector2(0, 0));
        lock (_console)
            foreach (var l in _console)
            {
                bool warn = l.Contains("[警告]");
                if (warn) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.5f, 1));
                ImGui.TextUnformatted(l);
                if (warn) ImGui.PopStyleColor();
            }
        if (_consoleScroll) { ImGui.SetScrollHereY(1); _consoleScroll = false; }
        ImGui.EndChild();
    }

    // =====================================================================
    //  拖放工具
    // =====================================================================
    public static unsafe bool AcceptDrop(string type)
    {
        if (!ImGui.BeginDragDropTarget()) return false;
        var p = ImGui.AcceptDragDropPayload(type);
        bool ok = p.NativePtr != null;
        ImGui.EndDragDropTarget();
        return ok;
    }
    public static void DragSource(string type, string label)
    {
        if (ImGui.BeginDragDropSource())
        {
            ImGui.SetDragDropPayload(type, IntPtr.Zero, 0);
            ImGui.TextUnformatted(label);
            ImGui.EndDragDropSource();
        }
    }
}

/// <summary>主题：深色 + 美术稿的玫粉。</summary>
public static class Theme
{
    public static readonly Vector4 Accent = new(0.80f, 0.27f, 0.47f, 1f);
    public static readonly Vector4 AccentHi = new(0.92f, 0.36f, 0.56f, 1f);
    public static uint U(Vector4 v) => ImGui.GetColorU32(v);

    static readonly ushort[] Ranges =
    {
        0x0020, 0x00FF, 0x2000, 0x206F, 0x2190, 0x21FF, 0x2460, 0x24FF, 0x2500, 0x27BF,
        0x3000, 0x30FF, 0x31F0, 0x31FF, 0x4E00, 0x9FAF, 0xFF00, 0xFFEF, 0
    };
    static GCHandle _rangesHandle;

    public static void Apply()
    {
        var st = ImGui.GetStyle();
        st.WindowRounding = 4; st.FrameRounding = 4; st.TabRounding = 4; st.GrabRounding = 4; st.PopupRounding = 4; st.ChildRounding = 4;
        st.WindowPadding = new Vector2(10, 8); st.FramePadding = new Vector2(8, 4); st.ItemSpacing = new Vector2(8, 6);
        st.WindowBorderSize = 1; st.ScrollbarSize = 12;
        var c = st.Colors;
        Vector4 bg = new(0.105f, 0.090f, 0.105f, 1), panel = new(0.145f, 0.125f, 0.140f, 1), frame = new(0.20f, 0.17f, 0.19f, 1),
                frameHi = new(0.27f, 0.22f, 0.25f, 1), text = new(0.95f, 0.92f, 0.93f, 1), dim = new(0.58f, 0.52f, 0.55f, 1);
        c[(int)ImGuiCol.Text] = text;
        c[(int)ImGuiCol.TextDisabled] = dim;
        c[(int)ImGuiCol.WindowBg] = panel;
        c[(int)ImGuiCol.ChildBg] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.PopupBg] = new Vector4(0.12f, 0.10f, 0.12f, 0.98f);
        c[(int)ImGuiCol.MenuBarBg] = bg;
        c[(int)ImGuiCol.Border] = new Vector4(0.30f, 0.24f, 0.28f, 1);
        c[(int)ImGuiCol.FrameBg] = frame;
        c[(int)ImGuiCol.FrameBgHovered] = frameHi;
        c[(int)ImGuiCol.FrameBgActive] = new Vector4(0.33f, 0.24f, 0.29f, 1);
        c[(int)ImGuiCol.TitleBg] = bg;
        c[(int)ImGuiCol.TitleBgActive] = new Vector4(0.24f, 0.13f, 0.19f, 1);
        c[(int)ImGuiCol.TitleBgCollapsed] = bg;
        c[(int)ImGuiCol.Button] = frame;
        c[(int)ImGuiCol.ButtonHovered] = new Vector4(0.55f, 0.22f, 0.38f, 1);
        c[(int)ImGuiCol.ButtonActive] = Accent;
        c[(int)ImGuiCol.Header] = new Vector4(0.42f, 0.17f, 0.30f, 1);
        c[(int)ImGuiCol.HeaderHovered] = new Vector4(0.55f, 0.22f, 0.38f, 1);
        c[(int)ImGuiCol.HeaderActive] = Accent;
        c[(int)ImGuiCol.CheckMark] = AccentHi;
        c[(int)ImGuiCol.SliderGrab] = Accent;
        c[(int)ImGuiCol.SliderGrabActive] = AccentHi;
        c[(int)ImGuiCol.Separator] = new Vector4(0.30f, 0.24f, 0.28f, 1);
        c[(int)ImGuiCol.Tab] = frame;
        c[(int)ImGuiCol.TabHovered] = new Vector4(0.55f, 0.22f, 0.38f, 1);
        c[(int)ImGuiCol.TabActive] = new Vector4(0.42f, 0.17f, 0.30f, 1);
        c[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.80f, 0.27f, 0.47f, 0.45f);
        c[(int)ImGuiCol.DragDropTarget] = AccentHi;
        c[(int)ImGuiCol.ScrollbarBg] = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.ScrollbarGrab] = frameHi;
        c[(int)ImGuiCol.ResizeGrip] = new Vector4(0, 0, 0, 0);
    }

    /// <summary>编辑器字体：优先微软雅黑（清楚），否则用游戏正文字体。</summary>
    public static void LoadFont(string dataDir)
    {
        var fontsDir = Path.Combine(dataDir, "assets", "fonts");
        var candidates = new List<string> { @"C:\Windows\Fonts\msyh.ttc", @"C:\Windows\Fonts\msyh.ttf", @"C:\Windows\Fonts\simhei.ttf", @"C:\Windows\Fonts\Deng.ttf" };
        if (Directory.Exists(fontsDir)) candidates.AddRange(Directory.GetFiles(fontsDir, "*.ttf"));
        var f = candidates.FirstOrDefault(File.Exists);
        var io = ImGui.GetIO();
        io.Fonts.Clear();
        if (f != null)
        {
            _rangesHandle = GCHandle.Alloc(Ranges, GCHandleType.Pinned);
            io.Fonts.AddFontFromFileTTF(f, 18f, new ImFontConfigPtr(IntPtr.Zero), _rangesHandle.AddrOfPinnedObject());
        }
        else io.Fonts.AddFontDefault();
        rlImGui.ReloadFonts();
    }
}
