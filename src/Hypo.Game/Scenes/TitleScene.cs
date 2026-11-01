using System.Numerics;
using Hypo.Core.Data;
using Hypo.Core.Persistence;
using Hypo.Core.Runtime;
using Hypo.Game.UI;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.Scenes;

public class TitleScene : Scene, IUiHost
{
    float _t;
    LayoutView _layout;
    public override void Enter() { var l = App.Db.LayoutFor("title"); _layout = l != null ? new LayoutView(l, this) : null; }
    GameRunner IUiHost.Game => null;
    public bool TryGet(string name, out double num, out string str)
    {
        num = 0; str = null;
        switch (name)
        {
            case "has_save": num = SaveSystem.Exists(0) ? 1 : 0; return true;
            case "runs": num = App.Profile.Runs; return true;
            case "cards": num = App.Db.Cards.Count; return true;
            case "bosses": num = App.Db.Bosses.Count; return true;
        }
        return false;
    }
    public void Command(string cmd, string arg)
    {
        switch (cmd)
        {
            case "NewGame": App.NewGame(); break;
            case "Continue": if (SaveSystem.Exists(0)) App.LoadGame(0); break;
            case "LoadMenu": App.SettingsUi.Show(false, 2); break;
            case "OpenGallery": App.Go(new GalleryScene()); break;
            case "OpenSettings": App.SettingsUi.Show(false); break;
            case "Quit": App.Quit = true; break;
            case "Log": Hypo.Core.Data.Log.Info(arg); break;
        }
    }
    public override void Update(float dt)
    {
        _t += dt;
        if (IsKeyPressed(KeyboardKey.Escape)) App.SettingsUi.Show(false);
    }

    public override void Draw()
    {
        if (_layout != null) { _layout.Input(); _layout.Draw(); return; }
        var bg = Assets.UiTex("title_bg");
        if (bg.HasValue) Assets.Draw(bg.Value, Ui.R(0, 0, Ui.W, Ui.H), Color.White, true);
        else
        {
            // 占位：旋转催眠螺旋
            var c = new Vector2(Ui.W * 0.68f, Ui.H * 0.5f);
            for (int i = 0; i < 14; i++)
                DrawRing(c, 30 + i * 32, 44 + i * 32, _t * 40 + i * 25, _t * 40 + i * 25 + 250, 48, Th.A(Th.Pink, 0.12f + 0.02f * (i % 3)));
        }
        var logo = Assets.UiTex("logo");
        if (logo.HasValue) Assets.Draw(logo.Value, Ui.R(80, 90, 600, 220), Color.White);
        else
        {
            Ui.TextShadow("H.Y.P.O.", 100, 110, 110, Th.Pink);
            Ui.TextShadow("同好会", 110, 230, 56, Th.PinkPale);
        }

        float y = 380;
        if (Ui.Button(Ui.R(100, y, 320, 64), "新的开始")) App.NewGame();
        y += 80;
        if (Ui.Button(Ui.R(100, y, 320, 64), "继续", SaveSystem.Exists(0))) App.LoadGame(0);
        y += 80;
        if (Ui.Button(Ui.R(100, y, 320, 64), "读取存档")) App.SettingsUi.Show(false, 2);
        y += 80;
        if (Ui.Button(Ui.R(100, y, 320, 64), "画廊")) App.Go(new GalleryScene());
        y += 80;
        if (Ui.Button(Ui.R(100, y, 150, 56), "设置", true, null, 22)) App.SettingsUi.Show(false);
        if (Ui.Button(Ui.R(270, y, 150, 56), "退出", true, Th.Wine, 22)) App.Quit = true;

        Ui.Text($"卡牌 {App.Db.Cards.Count} · Boss {App.Db.Bosses.Count} · 已游玩 {App.Profile.Runs} 局", 20, Ui.H - 30, 18, Th.Grey);
    }
}

public class GalleryScene : Scene
{
    int _view = -1; float _scroll;
    public override void Update(float dt)
    {
        if (IsKeyPressed(KeyboardKey.Escape)) { if (_view >= 0) _view = -1; else App.Go(new TitleScene()); }
        _scroll = Math.Max(0, _scroll - Ui.Wheel * 60);
    }

    public override void Draw()
    {
        var list = App.Db.Gallery;
        if (_view >= 0) Ui.Consumed = true;
        Ui.TextShadow("画廊", 60, 40, 48, Th.Pink);
        Ui.Text($"{list.Count(g => App.Profile.Unlocked.Contains(g.Id))} / {list.Count}", 200, 58, 24, Th.PinkPale);
        if (Ui.Button(Ui.R(Ui.W - 180, 40, 140, 52), "返回")) App.Go(new TitleScene());

        const int cols = 5; float cw = 270, ch = 180, gap = 22, x0 = 60, y0 = 130 - _scroll;
        BeginScissorMode(0, 120, Ui.W, Ui.H - 120);
        for (int i = 0; i < list.Count; i++)
        {
            var g = list[i];
            var r = Ui.R(x0 + i % cols * (cw + gap), y0 + i / cols * (ch + 60), cw, ch);
            bool open = App.Profile.Unlocked.Contains(g.Id);
            Ui.Panel(r, open ? Th.Panel : Th.Dark, Ui.Hover(r) && open ? Th.Pink : null);
            var tex = open ? Assets.Tex(g.Image) : null;
            if (tex.HasValue) Assets.Draw(tex.Value, new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12), Color.White, true);
            else Ui.Text(open ? "（无图）" : "？", r.X + r.Width / 2, r.Y + r.Height / 2 - 18, 32, Th.Grey, Align.Center);
            Ui.Text(open ? App.Db.T(g.Title) : "？？？", r.X + 4, r.Y + r.Height + 8, 20, open ? Th.PinkPale : Th.Grey);
            if (open && Ui.Clicked(r)) _view = i;
        }
        EndScissorMode();

        if (_view >= 0)
        {
            Ui.Consumed = false;
            Ui.Dim(0.9f);
            var g = list[_view];
            var tex = Assets.Tex(g.Image);
            if (tex.HasValue) Assets.Draw(tex.Value, Ui.R(40, 40, Ui.W - 80, Ui.H - 120), Color.White);
            Ui.Text(App.Db.T(g.Title), Ui.W / 2, Ui.H - 60, 26, Th.White, Align.Center);
            if (Ui.Click) _view = -1;
        }
    }
}
