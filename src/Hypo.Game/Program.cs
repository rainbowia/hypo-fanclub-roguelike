using System.Numerics;
using Hypo.Core.Data;
using Hypo.Core.Effects;
using Hypo.Core.Persistence;
using Hypo.Core.Runtime;
using Hypo.Game.Scenes;
using Hypo.Game.UI;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game;

public abstract class Scene
{
    public virtual void Enter() { }
    public abstract void Update(float dt);
    public abstract void Draw();
}

/// <summary>全局单例：内容库、存档、设置、当前局。</summary>
public static class App
{
    public static ContentDb Db;
    public static Profile Profile;
    public static Settings Settings;
    public static GameRunner Run;
    public static Scene Current { get; private set; }
    public static SettingsOverlay SettingsUi = new();
    public static bool Quit;
    public static string DataDir = "data";

    public static void Go(Scene s) { Current = s; s.Enter(); }

    public static void ReloadContent()
    {
        Db = ContentDb.Load(DataDir, Settings.SafeMode);
        Blocks.LoadPlugins(Path.Combine(DataDir, "..", "mods"));
    }

    public static void NewGame()
    {
        Run = new GameRunner(Db, Profile);
        Run.NewGame();
        Profile.Runs++; Profile.Save();
        Go(new MainScene());
    }
    public static bool LoadGame(int slot)
    {
        var s = SaveSystem.Load(slot);
        if (s == null) return false;
        Run = new GameRunner(Db, Profile);
        Run.Load(s);
        Go(new MainScene());
        return true;
    }
    public static void AutoSave() { if (Run != null && Settings.AutoSave) SaveSystem.Save(0, Run.State); }
}

public static class Program
{
    public static void Main(string[] args)
    {
        // 支持从仓库根目录或输出目录启动
        if (!Directory.Exists(App.DataDir)) App.DataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Assets.Root = App.DataDir;

        App.Settings = Settings.Load();
        App.Profile = Profile.Load();

        SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        InitWindow(App.Settings.Width, App.Settings.Height, "H.Y.P.O. Fanclub");
        SetExitKey(KeyboardKey.Null);   // ESC 留给游戏
        SetTargetFPS(60);
        InitAudioDevice();
        if (App.Settings.Fullscreen) ToggleFullscreen();

        Fonts.Init(Path.Combine(App.DataDir, "assets", "fonts"));
        App.ReloadContent();

        App.Go(new TitleScene());

        while (!WindowShouldClose() && !App.Quit)
        {
            // 虚拟画布缩放（保持 16:9，黑边）
            float scale = Math.Min(GetScreenWidth() / (float)Ui.W, GetScreenHeight() / (float)Ui.H);
            var offset = new Vector2((GetScreenWidth() - Ui.W * scale) / 2, (GetScreenHeight() - Ui.H * scale) / 2);
            Ui.Mouse = (GetMousePosition() - offset) / scale;
            Ui.Wheel = GetMouseWheelMove();
            Ui.Consumed = false;
            float dt = GetFrameTime();

            // 直接按屏幕实际分辨率绘制：坐标仍是 1600x900 虚拟画布，由 Camera2D 缩放（清晰 + MSAA 抗锯齿）
            Ui.Scale = scale; Ui.Offset = offset;
            BeginDrawing();
            ClearBackground(Color.Black);
            BeginMode2D(new Camera2D { Offset = offset, Target = Vector2.Zero, Rotation = 0, Zoom = scale });
            DrawRectangle(0, 0, Ui.W, Ui.H, Th.Bg);
            // 设置面板在最上层，先处理它的输入
            // 即时模式 UI：按钮在 Draw 里画+判定点击。打开模态层时先屏蔽下层点击。
            bool modal = App.SettingsUi.Open;
            if (modal) App.SettingsUi.Update(); else App.Current.Update(dt);
            Ui.Consumed = modal;
            App.Current.Draw();
            if (modal) { Ui.Consumed = false; App.SettingsUi.Draw(); }
            if (!Fonts.HasFontFile) Ui.Text("缺少中文字体：请把 .ttf 放进 data/assets/fonts/ (Chinese font missing)", 10, Ui.H - 24, 18, Th.Red);
            EndMode2D();
            // 黑边（盖住溢出画布的内容）
            int sw = GetScreenWidth(), sh = GetScreenHeight();
            if (offset.X > 0) { DrawRectangle(0, 0, (int)offset.X, sh, Color.Black); DrawRectangle((int)(offset.X + Ui.W * scale), 0, sw, sh, Color.Black); }
            if (offset.Y > 0) { DrawRectangle(0, 0, sw, (int)offset.Y, Color.Black); DrawRectangle(0, (int)(offset.Y + Ui.H * scale), sw, sh, Color.Black); }
            Sfx.Update();
            EndDrawing();
            Fonts.EndFrame();
        }
        App.AutoSave();
        Assets.Clear();
        CloseAudioDevice();
        CloseWindow();
    }
}
