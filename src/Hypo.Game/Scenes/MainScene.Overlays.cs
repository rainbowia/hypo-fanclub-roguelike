using System.Numerics;
using Hypo.Core.Data;
using Hypo.Core.Persistence;
using Hypo.Core.Runtime;
using Hypo.Game.UI;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.Scenes;

public partial class MainScene
{
    int _app = -1;               // 手机内打开的 APP：0 联系人 1 催眠 2 道具 3 图鉴
    int _dayDetail = -1;         // 日历中点开的那天（History 索引）
    float _calScroll;            // 日历向上翻历史（行）
    float _phoneScroll;
    string _phoneMsg = "";

    // ======================= 手机 =======================
    static readonly (string name, string sub, string icon)[] Apps =
    {
        ("Member", "联系人", Art.P(Art.AppMember)),
        ("H.Y.P.O.", "催眠", Art.P(Art.AppHypo)),
        ("Keeper", "道具", Art.P(Art.AppKeeper)),
        ("Guest", "图鉴", Art.P(Art.AppGuest)),
    };

    void DrawPhone()
    {
        if (_phoneX > 0.99f) return;
        var p = Ui.R(1130 + _phoneX * 480, 60, 450, 700);
        var frame = Assets.UiTex("phone_frame");
        if (frame.HasValue) Assets.Draw(frame.Value, p, Color.White);
        else { Ui.Panel(p, Th.C(30, 28, 30), Th.Grey, 0.06f); }
        var screen = Ui.R(p.X + 26, p.Y + 30, p.Width - 52, p.Height - 120);
        DrawRectangleRec(screen, Th.C(18, 14, 18));
        // Home 键 = 返回
        if (Ui.Button(Ui.R(p.X + p.Width / 2 - 30, p.Y + p.Height - 76, 60, 60), "□", true, Th.Dark, 26)) { if (_app >= 0) _app = -1; else _phone = false; }

        if (_app < 0)
        {
            // 2×2 APP 宫格（对应草图）
            for (int i = 0; i < 4; i++)
            {
                var r = Ui.R(screen.X + 20 + i % 2 * (screen.Width / 2 - 10), screen.Y + 30 + i / 2 * 230, screen.Width / 2 - 30, 200);
                bool h = Ui.Hover(r);
                Ui.Panel(r, h ? Th.PanelHi : Th.Panel, null, 0.12f);
                var t = Assets.Tex(Apps[i].icon);
                if (t.HasValue) Assets.Draw(t.Value, Ui.R(r.X + 20, r.Y + 14, r.Width - 40, 120), Color.White);
                else Ui.Text(i == 1 ? "◎" : i == 0 ? "☺" : i == 2 ? "◆" : "?", r.X + r.Width / 2, r.Y + 40, 64, Th.Pink, Align.Center);
                Ui.Text(Apps[i].name, r.X + r.Width / 2, r.Y + 140, 26, Th.White, Align.Center);
                Ui.Text(Apps[i].sub, r.X + r.Width / 2, r.Y + 170, 18, Th.PinkPale, Align.Center);
                if (Ui.Clicked(r)) { _app = i; _phoneScroll = 0; _phoneMsg = ""; }
            }
        }
        else
        {
            Ui.Text(Apps[_app].name + "  " + Apps[_app].sub, screen.X + 16, screen.Y + 12, 24, Th.Pink);
            var body = Ui.R(screen.X + 10, screen.Y + 50, screen.Width - 20, screen.Height - 60);
            if (Ui.Hover(body)) _phoneScroll = Math.Max(0, _phoneScroll - Ui.Wheel * 50);
            Ui.Clip(body.X, body.Y, body.Width, body.Height);
            switch (_app)
            {
                case 0: AppMembers(body); break;
                case 1: AppHypno(body); break;
                case 2: AppItems(body); break;
                case 3: AppDex(body); break;
            }
            Ui.EndClip();
        }
        Ui.Eat(p);
    }

    void AppMembers(Rectangle b)
    {
        float y = b.Y - _phoneScroll;
        Ui.Text($"{S.Cards.Count} / {R.RosterCapacity}", b.X + b.Width - 10, b.Y - 38, 20, G.OverCapacity ? Th.Red : Th.PinkPale, Align.Right);
        foreach (var c in S.Cards.OrderBy(c => c.Slot < 0).ThenBy(c => c.Slot).ToList())
        {
            var r = Ui.R(b.X, y, b.Width, 64);
            Ui.Panel(r, c == _sel ? Th.Wine : Th.Panel, null, 0.2f);
            CardView.Draw(c, Ui.R(r.X + 6, r.Y + 4, 40, 56));
            Ui.Text(c.Name(App.Db), r.X + 56, r.Y + 8, 22, Th.White);
            Ui.Text((c.OnBoard ? $"座位 {c.Slot + 1}" : "待命") + $"  产出{c.FixedOutput}  {string.Join("/", c.Stats)}", r.X + 56, r.Y + 36, 16, Th.PinkPale);
            if (G.OverCapacity && Ui.Button(Ui.R(r.X + r.Width - 78, r.Y + 14, 70, 36), "请离", true, Th.Red, 18)) G.Dismiss(c);
            else if (Ui.Clicked(r)) { _sel = c; if (S.Phase == Phase.Prepare) { _phone = false; EnterSeat(); _sel = c; } }
            y += 72;
        }
    }

    void AppHypno(Rectangle b)
    {
        float y = b.Y;
        Ui.Wrap($"给一名在座成员施加随机催眠强化。\n同时最多 {R.HypnoMax} 个（当前 {G.ActiveHypnoCount}）\n消耗 {R.HypnoCostPleasure} 欢愉", Ui.R(b.X + 6, y, b.Width - 12, 100), 18, Th.PinkPale);
        y += 96;
        if (!string.IsNullOrEmpty(_phoneMsg)) { Ui.Text(_phoneMsg, b.X + 6, y, 20, Th.Gold); y += 30; }
        y -= _phoneScroll;
        foreach (var c in S.Board.ToList())
        {
            bool can = G.CanHypno(c) && S.Phase == Phase.Prepare;
            if (Ui.Button(Ui.R(b.X, y, b.Width, 50), $"{c.Slot + 1}. {c.Name(App.Db)}  {(c.Hypnos.Count > 0 ? "◎" + c.Hypnos.Count : "")}", can, Th.Panel, 20))
            {
                var h = G.ApplyHypno(c);
                _phoneMsg = h != null ? $"{c.Name(App.Db)} ← {App.Db.T(h.Name)}" : "失败";
            }
            y += 58;
        }
    }

    void AppItems(Rectangle b)
    {
        float y = b.Y - _phoneScroll;
        Ui.Text("在选座视角中选中成员后，右侧面板可装备道具。", b.X + 6, y, 16, Th.Grey); y += 30;
        foreach (var g in S.ItemStock.GroupBy(x => x))
        {
            var d = App.Db.Items.GetValueOrDefault(g.Key);
            Ui.Text($"{(d != null ? App.Db.T(d.Name) : g.Key)} ×{g.Count()}", b.X + 6, y, 22, Th.White); y += 28;
            if (d != null) y += Ui.Wrap(App.Db.T(d.Description), Ui.R(b.X + 6, y, b.Width - 12, 80), 16, Th.PinkPale) + 10;
        }
        foreach (var c in S.Board.Where(c => c.Items.Count > 0))
        { Ui.Text($"{c.Name(App.Db)}：{string.Join("、", c.Items.Select(i => App.Db.T(App.Db.Items.GetValueOrDefault(i)?.Name ?? i)))}", b.X + 6, y, 18, Th.Gold); y += 26; }
    }

    void AppDex(Rectangle b)
    {
        foreach (var c in S.Cards) App.Profile.SeenCards.Add(c.DefId);
        float y = b.Y - _phoneScroll;
        Ui.Text("成员", b.X + 6, y, 22, Th.Pink); y += 32;
        foreach (var d in App.Db.Cards.Values)
        {
            bool seen = App.Profile.SeenCards.Contains(d.Id);
            Ui.Text(seen ? App.Db.T(d.Name) : "？？？", b.X + 12, y, 20, seen ? Th.White : Th.Grey); y += 28;
        }
        y += 10; Ui.Text("来访者", b.X + 6, y, 22, Th.Pink); y += 32;
        foreach (var d in App.Db.Bosses.Values) { Ui.Text(App.Db.T(d.Name), b.X + 12, y, 20, Th.White); y += 28; }
    }

    // ======================= 日历 =======================
    void DrawCalendar()
    {
        Ui.Dim(0.6f);
        var p = Ui.R(60, 110, 1480, 600);
        Ui.Panel(p, Th.A(Th.Dark, 0.97f), Th.Pink, 0.03f);
        Ui.TextShadow("DATE", 800, 130, 54, Th.White, Align.Center);
        if (Ui.Button(Ui.R(p.X + p.Width - 150, p.Y + 20, 130, 52), "返回", true, Th.Panel, 22)) _calendar = false;

        // 行 = 一个大关。最下面是本关，滚轮向上看历史
        int stages = S.Stage;
        _calScroll = Math.Clamp(_calScroll + Ui.Wheel, 0, Math.Max(0, stages - 1));
        float rowH = 380, colW = 160;
        int curRowStage = S.Stage - (int)MathF.Round(_calScroll);
        for (int k = 0; k < 2; k++)          // 当前行 + 上一行（预览）
        {
            int stage = curRowStage - k;
            if (stage < 1) break;
            float y0 = p.Y + 190 - k * (rowH + 20);
            if (y0 < p.Y + 80) { Ui.Text($"▲ 第{stage}关（滚轮向上查看）", p.X + 40, p.Y + 90, 18, Th.Grey); continue; }
            Ui.Text($"第{stage}关", p.X + 30, y0 - 40, 22, Th.PinkPale);
            for (int d = 0; d <= 7; d++)
            {
                var r = Ui.R(p.X + 40 + d * (colW + 18), y0, colW, rowH - 30);
                var rec = S.History.LastOrDefault(h => h.Stage == stage && h.Day == d);
                bool isNow = stage == S.Stage && d == S.Day;
                bool future = stage == S.Stage && d > S.Day;
                if (d == 7)
                {
                    // 星期八：Boss
                    Ui.Panel(r, Th.Wine, Th.PinkHot, 0.06f);
                    var bossId = stage == S.Stage ? S.BossId : "";
                    if (!string.IsNullOrEmpty(bossId) && App.Db.Bosses.TryGetValue(bossId, out var boss))
                    {
                        var t = Assets.Tex(boss.Icon);
                        if (t.HasValue) Assets.Draw(t.Value, Ui.R(r.X + 10, r.Y + 60, r.Width - 20, r.Width - 20), Color.White);
                        else if (!Art.Draw(Art.Boss, Ui.R(r.X + 15, r.Y + 50, r.Width - 30, r.Width - 30), Color.White)) Ui.Text("☠", r.X + r.Width / 2, r.Y + 80, 90, Th.PinkHot, Align.Center);
                        Ui.Text(App.Db.T(boss.Name), r.X + r.Width / 2, r.Y + r.Height - 50, 22, Th.White, Align.Center);
                    }
                    else Ui.Text("？", r.X + r.Width / 2, r.Y + 120, 80, Th.Grey, Align.Center);
                    Ui.Text("星期八", r.X + r.Width / 2, r.Y + 12, 22, Th.PinkPale, Align.Center);
                    continue;
                }
                Ui.Panel(r, isNow ? Th.PanelHi : Th.Panel, isNow ? Th.Gold : null, 0.06f);
                Ui.Text(SaveSystem.DayName(d), r.X + 12, r.Y + 10, 22, Th.White);
                if (future) Ui.Hatch(Ui.R(r.X + 6, r.Y + 44, r.Width - 12, r.Height - 52), Th.A(Th.Grey, 0.4f));
                else if (rec != null && rec.Done) Ui.Text("✓", r.X + r.Width / 2, r.Y + 50, 72, Th.Pink, Align.Center);
                else if (isNow) DrawRing(new Vector2(r.X + r.Width / 2, r.Y + 92), 30, 40, 0, 360, 36, Th.Gold);
                // 特殊事件
                if (stage == S.Stage && S.DayEvents.TryGetValue(d, out var ev) && App.Db.Events.TryGetValue(ev, out var evd))
                {
                    if (Art.Draw(Art.Event, Ui.R(r.X + 8, r.Y + 142, 28, 30), Color.White)) Ui.Text(App.Db.T(evd.Name), r.X + 40, r.Y + 148, 18, Th.Gold);
                    else Ui.Text("! " + App.Db.T(evd.Name), r.X + 10, r.Y + 150, 18, Th.Gold);
                }
                // 获得/失去小 icon
                if (rec != null)
                {
                    float iy = r.Y + 190, ix = r.X + 10;
                    foreach (var g in rec.Gained) { MiniIcon(ix, iy, true); ix += 30; if (ix > r.X + r.Width - 30) { ix = r.X + 10; iy += 34; } }
                    foreach (var l in rec.Lost) { MiniIcon(ix, iy, false); ix += 30; if (ix > r.X + r.Width - 30) { ix = r.X + 10; iy += 34; } }
                    if (rec.Pleasure != 0) Ui.Text($"+{rec.Pleasure}", r.X + r.Width - 10, r.Y + 14, 18, Th.Gold, Align.Right);
                    if (Ui.Clicked(r)) _dayDetail = S.History.IndexOf(rec);
                }
            }
        }
        Ui.Eat(p);
    }

    static void MiniIcon(float x, float y, bool gained)
    {
        var r = Ui.R(x, y, 26, 30);
        if (!Art.Draw(Art.Person, r, gained ? Color.White : Th.C(150, 130, 140)))
        {
            DrawRectangleRounded(r, 0.2f, 4, Th.PinkPale);
            DrawCircle((int)(x + 13), (int)(y + 11), 6, Th.Panel);
            DrawRectangle((int)x + 6, (int)y + 18, 14, 10, Th.Panel);
        }
        Ui.Text(gained ? "✓" : "×", x + 24, y - 8, 18, gained ? Th.Pink : Th.Red);
    }

    void DrawDayDetail()
    {
        var rec = S.History[_dayDetail];
        Ui.Dim(0.5f);
        var p = Ui.R(450, 60, 700, 780);
        Ui.Panel(p, Th.Dark, Th.Pink, 0.04f);
        Ui.TextShadow($"第{rec.Stage}关 · {SaveSystem.DayName(rec.Day)}", p.X + 30, p.Y + 30, 34, Th.White);
        Ui.Text($"产生欢愉 {rec.Pleasure}", p.X + 30, p.Y + 90, 24, Th.Gold);
        float y = p.Y + 140;
        void Section(string title, List<string> ids, Color c)
        {
            Ui.Text(title, p.X + 30, y, 24, c); y += 36;
            if (ids.Count == 0) { Ui.Text("—", p.X + 50, y, 20, Th.Grey); y += 30; }
            foreach (var id in ids) { Ui.Text(App.Db.T(App.Db.Cards.GetValueOrDefault(id)?.Name ?? id), p.X + 50, y, 22, Th.White); y += 30; }
            y += 16;
        }
        Section("获得成员", rec.Gained, Th.Pink);
        Section("失去成员（撕毁）", rec.Lost, Th.Red);
        Section("请离", rec.Dismissed, Th.Grey);
        if (Ui.Button(Ui.R(p.X + p.Width - 160, p.Y + p.Height - 76, 130, 52), "返回") || (Ui.Click && !Ui.Hover(p))) _dayDetail = -1;
        Ui.Eat(p);
    }

    // ======================= 招募 / Boss / 战败 =======================
    void DrawRecruit()
    {
        Ui.Dim(0.65f);
        Ui.TextShadow("今晚的新成员", 800, 150, 46, Th.White, Align.Center);
        Ui.Text("选择一名加入同好会（或放弃）", 800, 210, 22, Th.PinkPale, Align.Center);
        var offer = S.RecruitOffer;
        float w = 200, h = 280, gap = 30, x = 800 - (offer.Count * (w + gap) - gap) / 2;
        for (int i = 0; i < offer.Count; i++)
        {
            var def = App.Db.Cards.GetValueOrDefault(offer[i]);
            var tmp = new CardInstance { DefId = offer[i], Def = def, Stats = (int[])(def?.Stats ?? new int[4]).Clone(), FixedOutput = def?.BaseOutput ?? 0 };
            var r = Ui.R(x + i * (w + gap), 280 - (Ui.Hover(Ui.R(x + i * (w + gap), 280, w, h)) ? 16 : 0), w, h);
            CardView.Draw(tmp, r, 1, Ui.Hover(r));
            if (def != null)
            {
                float yy = r.Y + h + 10;
                foreach (var e in def.Effects.Take(3)) yy += Ui.Wrap(CardView.Describe(e), Ui.R(r.X, yy, w, 80), 15, Th.PinkPale);
            }
            if (Ui.Clicked(r)) { G.Recruit(offer[i]); return; }
        }
        if (Ui.ArtButton(Ui.R(680, 790, 240, 84), Art.Cancel, null, null, "放弃", true, 24)) G.Recruit(null);
    }

    void DrawBossChoice()
    {
        Ui.Dim(0.75f);
        Ui.TextShadow("星期八的来访者", 800, 120, 48, Th.PinkHot, Align.Center);
        Ui.Text("选择一位，她将在本周结束后到访", 800, 186, 22, Th.PinkPale, Align.Center);
        for (int i = 0; i < S.BossOffer.Count; i++)
        {
            var b = App.Db.Bosses[S.BossOffer[i]];
            var r = Ui.R(800 - (S.BossOffer.Count * 440 - 40) / 2f + i * 440, 250, 400, 520);
            bool h = Ui.Hover(r);
            Ui.Panel(r, h ? Th.Wine : Th.Panel, h ? Th.PinkHot : Th.PinkPale, 0.05f);
            var t = Assets.Tex(string.IsNullOrEmpty(b.Portrait) ? b.Icon : b.Portrait);
            if (t.HasValue) Assets.Draw(t.Value, Ui.R(r.X + 20, r.Y + 20, r.Width - 40, 300), Color.White);
            else Ui.Text("☠", r.X + r.Width / 2, r.Y + 100, 140, Th.PinkHot, Align.Center);
            Ui.Text(App.Db.T(b.Name), r.X + r.Width / 2, r.Y + 340, 34, Th.White, Align.Center);
            Ui.Wrap(App.Db.T(b.Intro), Ui.R(r.X + 24, r.Y + 390, r.Width - 48, 120), 18, Th.PinkPale);
            if (Ui.Clicked(r)) { G.ChooseBoss(b.Id); return; }
        }
    }

    void DrawGameOver()
    {
        Ui.Dim(0.85f);
        var g = App.Db.Gallery.FirstOrDefault(x => x.UnlockEvent == S.LastDefeatKey);
        var t = g != null ? Assets.Tex(g.Image) : null;
        if (t.HasValue) Assets.Draw(t.Value, Ui.R(100, 40, Ui.W - 200, 640), Color.White);
        else Ui.TextShadow("未能达成目标……", 800, 300, 60, Th.PinkHot, Align.Center);
        Ui.Text($"到达第 {S.Stage} 关 · 欢愉 {S.Pleasure}", 800, 700, 26, Th.PinkPale, Align.Center);
        if (Ui.Button(Ui.R(560, 760, 220, 64), "回到标题")) { SaveSystem.Delete(0); App.Go(new TitleScene()); }
        if (Ui.Button(Ui.R(820, 760, 220, 64), "再来一局", true, Th.Pink)) App.NewGame();
    }
}
