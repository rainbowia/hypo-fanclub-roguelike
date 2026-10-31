using Hypo.Core.Persistence;
using Hypo.Game.UI;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.Scenes;

/// <summary>设置 / 存档 / 读档 / 回标题。ESC 或右上角齿轮打开，ESC 关闭。</summary>
public class SettingsOverlay
{
    public bool Open;
    int _tab;           // 0 设置 1 存档 2 读档
    bool _inGame;
    string _toast = ""; float _toastT;

    public void Show(bool inGame, int tab = 0) { Open = true; _inGame = inGame; _tab = tab; }
    public void Close() { Open = false; App.Settings.Save(); }

    public void Update()
    {
        if (IsKeyPressed(KeyboardKey.Escape)) Close();
        _toastT -= GetFrameTime();
    }

    public void Draw()
    {
        Ui.Dim(0.75f);
        var p = Ui.R(400, 120, 800, 660);
        Ui.Panel(p, Th.Dark, Th.Pink, 0.04f);
        string[] tabs = _inGame ? new[] { "设置", "存档", "读档" } : new[] { "设置", "", "读档" };
        for (int i = 0; i < 3; i++)
        {
            if (tabs[i] == "") continue;
            if (Ui.Button(Ui.R(p.X + 30 + i * 150, p.Y + 24, 136, 48), tabs[i], true, _tab == i ? Th.Pink : Th.Panel, 24)) _tab = i;
        }
        if (Ui.Button(Ui.R(p.X + p.Width - 70, p.Y + 24, 48, 48), "×", true, Th.Panel, 30)) Close();

        var body = Ui.R(p.X + 50, p.Y + 120, p.Width - 100, p.Height - 200);
        if (_tab == 0) DrawSettings(body);
        else DrawSlots(body, _tab == 1);

        if (_inGame)
        {
            if (Ui.Button(Ui.R(p.X + 30, p.Y + p.Height - 74, 200, 50), "回到标题", true, Th.Wine, 22))
            { App.AutoSave(); Close(); App.Go(new TitleScene()); }
        }
        else if (Ui.Button(Ui.R(p.X + 30, p.Y + p.Height - 74, 200, 50), "退出游戏", true, Th.Wine, 22)) App.Quit = true;

        Ui.Eat(p);
        if (_toastT > 0) Ui.Text(_toast, p.X + p.Width / 2, p.Y + p.Height - 60, 24, Th.Gold, Align.Center);
    }

    void DrawSettings(Rectangle b)
    {
        var s = App.Settings;
        s.MasterVolume = Ui.Slider(Ui.R(b.X, b.Y + 30, 420, 14), s.MasterVolume, "主音量");
        SetMasterVolume(s.MasterVolume);
        s.MusicVolume = Ui.Slider(Ui.R(b.X, b.Y + 110, 420, 14), s.MusicVolume, "音乐");
        s.SfxVolume = Ui.Slider(Ui.R(b.X, b.Y + 190, 420, 14), s.SfxVolume, "音效");
        s.ResolveSpeed = 0.5f + 2.5f * Ui.Slider(Ui.R(b.X, b.Y + 270, 420, 14), (s.ResolveSpeed - 0.5f) / 2.5f, $"结算速度 x{s.ResolveSpeed:0.0}");
        bool fs = Ui.Toggle(Ui.R(b.X, b.Y + 330, 300, 30), s.Fullscreen, "全屏");
        if (fs != s.Fullscreen) { s.Fullscreen = fs; ToggleFullscreen(); }
        s.AutoSave = Ui.Toggle(Ui.R(b.X, b.Y + 380, 300, 30), s.AutoSave, "每日自动存档");
        bool safe = Ui.Toggle(Ui.R(b.X, b.Y + 430, 400, 30), s.SafeMode, "全年龄文本（绿色版）");
        if (safe != s.SafeMode) { s.SafeMode = safe; App.ReloadContent(); Toast("已切换文本表"); }
    }

    void DrawSlots(Rectangle b, bool saving)
    {
        for (int i = 0; i < SaveSystem.SlotCount; i++)
        {
            var r = Ui.R(b.X, b.Y + i * 100, b.Width, 84);
            bool exists = SaveSystem.Exists(i);
            string label = (i == 0 ? "自动存档  " : $"存档 {i}  ") + SaveSystem.Describe(i);
            bool enabled = saving ? i != 0 : exists;
            if (Ui.Button(r, label, enabled, Th.Panel, 22))
            {
                if (saving) { SaveSystem.Save(i, App.Run.State); Toast("已保存"); }
                else { Close(); App.LoadGame(i); }
            }
        }
    }

    void Toast(string s) { _toast = s; _toastT = 1.5f; }
}
