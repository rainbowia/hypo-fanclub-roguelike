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
    static readonly string[] DayAbbr = { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN", "???" };
    Rectangle RosterStrip => Ui.R(30, 770, 540, 116);

    // ======================= 普通视角 HUD =======================
    void DrawHud()
    {
        DrawPleasure();

        // 右上：设置
        if (Ui.IconButton(Ui.R(1500, 24, 76, 76), Art.P(Art.Settings), "⚙")) App.SettingsUi.Show(true);

        // 左下：日历 + 日期进度
        if (Ui.IconButton(Ui.R(36, 784, 96, 96), Art.P(Art.Calendar), "▦")) _calendar = true;
        Ui.TextShadow(DayAbbr[Math.Min(S.Day, 7)], 150, 790, 34, S.InBoss ? Th.PinkHot : Th.White);
        Ui.Text($"第{S.Stage}关", 260, 798, 22, Th.PinkPale);
        for (int d = 0; d < R.DaysPerStage; d++)
        {
            var p = new Vector2(162 + d * 34, 856);
            if (d < S.Day) Ui.Text("✓", p.X, p.Y - 16, 30, Th.Pink, Align.Center);
            else if (d == S.Day) DrawRing(p, 9, 13, 0, 360, 24, Th.Gold);
            else DrawCircleV(p, 5, Th.Grey);
        }
        if (!string.IsNullOrEmpty(S.BossId)) Ui.Text("☠", 162 + R.DaysPerStage * 34, 840, 28, Th.PinkHot, Align.Center);

        // 底部中：选座次数 + 开始结算 + 选座视角
        bool canAct = S.Phase == Phase.Prepare && _resolver == null;
        Ui.Text($"自由换座  {S.MovesLeft} / {R.MovesPerDay}     请离  {S.DismissLeft} / {R.DismissPerDay}", 800, 752, 22, Th.PinkPale, Align.Center);
        string mainLabel = _resolver != null ? "结算中…" : S.InBoss ? "迎战" : "开始结算";
        if (Ui.ArtButton(Ui.R(590, 780, 380, 84), Art.BtnMain, Art.BtnMainHover, Art.BtnMainPressed, mainLabel, canAct, 30)) StartRound();
        if (Ui.IconButton(Ui.R(980, 782, 76, 80), Art.P(Art.SeatView), "◎", canAct)) EnterSeat();
        if (Ui.Hover(Ui.R(980, 782, 76, 80))) Ui.Text("选座", 1018, 864, 16, Th.PinkPale, Align.Center);
        if (G.OverCapacity) Ui.Text($"联系人超出上限 {S.Cards.Count}/{R.RosterCapacity}，请在手机中请离", 800, 870, 18, Th.Red, Align.Center);

        // 右下：手机
        if (Ui.IconButton(Ui.R(1470, 766, 110, 116), Art.P(Art.Phone), "▯")) { _phone = !_phone; _app = -1; }

        DrawResolveLog();
    }

    /// <summary>结算日志（右侧小字）</summary>
    void DrawResolveLog()
    {
        if (_resolver == null && S.Phase == Phase.Prepare) return;
        int n = 0;
        foreach (var line in G.ResolveLog.TakeLast(10))
            Ui.Text(line, 1180, 160 + n++ * 24, 18, Th.A(Th.PinkPale, 0.8f));
    }

    void DrawPleasure()
    {
        long cur = S.InBoss ? S.BossPleasure : S.Pleasure, max = S.InBoss ? S.BossTarget : S.StageTarget;
        float frac = max > 0 ? Math.Clamp(cur / (float)max, 0, 1) : 0;
        var barTex = Art.T(Art.PleasureBar);
        if (barTex.HasValue)
        {
            // 素材版：底图 + 裁切的填充条（槽位位置按 Icons1_pleasureBar 原图测得）
            var br = Ui.R(96, 6, 470, 470f * barTex.Value.Height / barTex.Value.Width);
            var track = Ui.R(br.X + br.Width * 0.150f, br.Y + br.Height * 0.712f, br.Width * 0.796f, br.Height * 0.205f);
            DrawTexturePro(barTex.Value, new Rectangle(0, 0, barTex.Value.Width, barTex.Value.Height), br, Vector2.Zero, 0, Color.White);
            if (Art.T(Art.PleasureFill) is { } fill && frac > 0)
                Art.Part(fill, 0, 0, frac, 1, Ui.R(track.X, track.Y, track.Width * frac, track.Height), S.InBoss ? Th.C(255, 150, 190) : Color.White);
            Art.Draw(Art.Pleasure, Ui.R(10, 8, 120, 120), Color.White);
            Ui.Text($"{cur} / {max}", track.X + track.Width / 2 + 2, track.Y - 32 + 2, 26, Th.Shadow, Align.Center, TextStyle.Title);
            Ui.Text($"{cur} / {max}", track.X + track.Width / 2, track.Y - 32, 26, Th.White, Align.Center, TextStyle.Title);
            if (S.InBoss) Ui.Text($"Boss 欢愉 · 普通累计 {S.Pleasure}", track.X, track.Y + track.Height + 6, 18, Th.PinkPale);
            return;
        }
        if (!Art.Draw(Art.Pleasure, Ui.R(24, 20, 100, 100), Color.White))
            Ui.Sparkle(new Vector2(74, 70), 46, S.InBoss ? Th.PinkHot : Th.Gold);
        var bar = Ui.R(130, 50, 380, 40);
        Ui.Bar(bar, max > 0 ? cur / (float)max : 0, S.InBoss ? Th.PinkHot : Th.Pink, Th.Dark);
        DrawRectangleRoundedLinesEx(bar, 0.5f, 8, 3, Th.PinkPale);
        Ui.Text($"{cur} / {max}", bar.X + bar.Width / 2, bar.Y + 7, 26, Th.White, Align.Center);
        Ui.Text(S.InBoss ? "Boss 欢愉" : "欢愉", bar.X + 6, bar.Y + 44, 18, Th.PinkPale);
        if (S.InBoss) Ui.Text($"普通累计 {S.Pleasure}", bar.X + 120, bar.Y + 44, 18, Th.Grey);
    }

    // ======================= 选座视角 =======================
    void DrawSeatUi()
    {
        // 顶部：请离
        if (_sel != null)
        {
            bool can = S.DismissLeft > 0 || G.OverCapacity;
            if (Ui.ArtButton(Ui.R(690, 18, 220, 72), Art.Cancel, null, null, "请离", can, 26)) { G.Dismiss(_sel); _sel = null; }
            Ui.Text($"请离 {S.DismissLeft} / {R.DismissPerDay}", 800, 86, 20, Th.PinkPale, Align.Center);
        }
        // 右上：返回
        if (Ui.Button(Ui.R(1440, 24, 136, 56), "返回", true, Th.Panel, 24)) ExitSeat();

        // 底部：完成选座
        Ui.Text($"移动次数  {S.MovesLeft} / {R.MovesPerDay}", 800, 770, 24, Th.PinkPale, Align.Center);
        if (Ui.ArtButton(Ui.R(610, 796, 380, 90), Art.Confirm, null, null, "完成选座", true, 28)) ExitSeat();

        // 环内：名字 + 四维条
        if (_sel != null)
        {
            var c = new Vector2(800, 430);
            if (Art.T(Art.CircleMenu).HasValue) DrawStatRing(_sel, c);
            else
            {
            Ui.TextShadow(_sel.Name(App.Db), c.X, c.Y - 110, 40, Th.White, Align.Center);
            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0; float y = c.Y - 40 + (i / 2) * 70;
                var r = left ? Ui.R(c.X - 250, y, 200, 26) : Ui.R(c.X + 50, y, 200, 26);
                float t = (_sel.Stats[i] - R.StatMin) / (float)(R.StatMax - R.StatMin);
                Ui.Bar(r, t, Th.Stat[i], Th.Dark);
                Ui.Text($"{Th.StatName[i]} {_sel.Stats[i]}", left ? r.X + r.Width + 10 : r.X - 10, y + 1, 22, Th.Stat[i], left ? Align.Left : Align.Right);
                // 危险提示：靠近 0 / 9
                if (_sel.Stats[i] <= R.StatMin + 1 || _sel.Stats[i] >= R.StatMax - 1)
                    DrawRectangleRoundedLinesEx(r, 0.5f, 6, 2, Th.Red);
            }
            }
            DrawDetailPanel(_sel, Ui.R(24, 100, 400, 640));
            if (_phoneX > 0.9f) DrawItemPanel(_sel, Ui.R(1176, 100, 400, 560));
        }
        DrawRosterStrip();
    }

    void DrawDetailPanel(CardInstance c, Rectangle p)
    {
        Board(p);
        Ui.Eat(p);
        var art = Ui.R(p.X + 16, p.Y + 16, p.Width - 32, 300);
        var tex = Assets.Tex(string.IsNullOrEmpty(c.Def?.FullArt) ? c.Def?.Portrait : c.Def.FullArt);
        DrawRectangleRec(art, Th.Dark);
        if (tex.HasValue) Assets.Draw(tex.Value, art, Color.White, true);
        float y = art.Y + art.Height + 12;
        Ui.Text($"{c.Name(App.Db)}  {GenderMark(c.Gender)}", p.X + 20, y, 28, Th.White, Align.Left, TextStyle.Title);
        int rar = Math.Clamp(c.Def?.Rarity ?? 1, 1, 5);
        Art.Draw(Art.Level[rar - 1], Ui.R(p.X + p.Width - 130, y - 4, 110, 40), Color.White);
        y += 36;
        var tags = (c.Def?.Tags ?? new()).Concat(c.ExtraTags).ToList();
        Ui.Text($"产出 {c.FixedOutput}   {string.Join(" ", tags.Select(t => "#" + t))}", p.X + 20, y, 20, Th.Gold); y += 30;
        foreach (var h in c.Hypnos)
            if (App.Db.Hypnos.TryGetValue(h.DefId, out var hd)) { Ui.Text("◎ " + App.Db.T(hd.Name) + (h.DaysLeft > 0 ? $"（{h.DaysLeft}天）" : ""), p.X + 20, y, 20, Th.PinkHot); y += 26; }
        foreach (var e in c.Def?.Effects ?? new())
            y += Ui.Wrap(CardView.Describe(e), Ui.R(p.X + 20, y, p.Width - 40, 200), 19, Th.PinkPale) + 4;
        if (!string.IsNullOrEmpty(c.Def?.Flavor)) Ui.Wrap(App.Db.T(c.Def.Flavor), Ui.R(p.X + 20, y + 6, p.Width - 40, 100), 17, Th.Grey);
    }
    static string GenderMark(Gender g) => g switch { Gender.Female => "♀", Gender.Male => "♂", _ => "" };

    void DrawItemPanel(CardInstance c, Rectangle p)
    {
        Board(p);
        Ui.Text("道具", p.X + 20, p.Y + 16, 26, Th.White);
        float y = p.Y + 60;
        Ui.Text($"已装备 {c.Items.Count}/{R.ItemSlotsPerCard}", p.X + 20, y, 20, Th.PinkPale); y += 30;
        foreach (var it in c.Items.ToList())
        {
            var d = App.Db.Items.GetValueOrDefault(it);
            if (Ui.Button(Ui.R(p.X + 20, y, p.Width - 40, 44), (d != null ? App.Db.T(d.Name) : it) + "  （卸下）", S.Phase == Phase.Prepare, Th.Wine, 20)) G.Unequip(c, it);
            y += 52;
        }
        y += 10; Ui.Text("库存（点击装备）", p.X + 20, y, 20, Th.PinkPale); y += 30;
        foreach (var grp in S.ItemStock.GroupBy(x => x).Take(8))
        {
            var d = App.Db.Items.GetValueOrDefault(grp.Key);
            bool can = c.OnBoard && c.Items.Count < R.ItemSlotsPerCard && S.Phase == Phase.Prepare;
            if (Ui.Button(Ui.R(p.X + 20, y, p.Width - 40, 44), $"{(d != null ? App.Db.T(d.Name) : grp.Key)} ×{grp.Count()}", can, Th.Panel, 20)) G.Equip(c, grp.Key);
            y += 52;
        }
        Ui.Eat(p);
    }

    void DrawRosterStrip()
    {
        var r = RosterStrip;
        Ui.Panel(r, Th.A(Th.Dark, 0.9f), Ui.Hover(r) && _dragging ? Th.Pink : Th.A(Th.PinkPale, 0.3f), 0.1f);
        Ui.Text("联系人（拖上场 / 拖回来取回）", r.X + 12, r.Y + 6, 16, Th.PinkPale);
        float x = r.X + 12;
        foreach (var c in S.Roster.Take(8))
        {
            var cr = Ui.R(x, r.Y + 28, 58, 81);
            if (c != _drag || !_dragging) CardView.Draw(c, cr, 1, c == _sel);
            if (Ui.Hover(cr) && Ui.Click) { Ui.Consumed = true; _sel = c; _drag = c; _dragStart = Ui.Mouse; _dragging = false; }
            x += 66;
        }
        if (S.Roster.Count() > 8) Ui.Text($"+{S.Roster.Count() - 8}", x, r.Y + 60, 20, Th.Grey);
    }

    /// <summary>面板底图：有 DetailBoard 素材用素材，否则画圆角面板。</summary>
    static void Board(Rectangle p)
    {
        if (!Art.Stretch(Art.DetailBoard, Ui.R(p.X - 14, p.Y - 10, p.Width + 28, p.Height + 24), Color.White))
            Ui.Panel(p, Th.A(Th.Panel, 0.95f), Th.A(Th.PinkPale, 0.4f), 0.04f);
    }

    /// <summary>选座视角中央：圆环 + 上右下左四段（理智 欲望 爱情 躯体），段的亮度表示数值，靠近死亡极端变红闪烁。</summary>
    void DrawStatRing(CardInstance card, Vector2 c)
    {
        // 名字牌
        var plate = Ui.R(c.X - 170, c.Y - 250, 340, 340f * 154 / 534);
        if (!Art.Stretch(Art.NamePlate, plate, Color.White)) Ui.Panel(plate, Th.Dark);
        Ui.Text(card.Name(App.Db), c.X, plate.Y + plate.Height / 2 - 18, 34, Th.White, Align.Center, TextStyle.Title);

        Art.Draw(Art.CircleMenu, Ui.R(c.X - 80, c.Y - 80, 160, 160), Color.White);
        // 段的位置（上 右 下 左）
        Rectangle[] seg =
        {
            Ui.R(c.X - 110, c.Y - 175, 220, 105), Ui.R(c.X + 72, c.Y - 105, 105, 210),
            Ui.R(c.X - 110, c.Y + 72, 220, 105), Ui.R(c.X - 177, c.Y - 105, 105, 210),
        };
        float blink = (MathF.Sin((float)GetTime() * 8) + 1) / 2;
        for (int i = 0; i < 4; i++)
        {
            int v = card.Stats[i];
            float t = Math.Clamp((v - R.StatMin) / (float)(R.StatMax - R.StatMin), 0, 1);
            bool danger = v <= R.StatMin + 1 || v >= R.StatMax - 1;
            var tint = Th.Lerp(Th.C(110, 100, 105), Th.Stat[i], 0.35f + 0.65f * t);
            if (danger) tint = Th.Lerp(tint, Th.Red, blink * 0.8f);
            Art.Draw(Art.Segment[i], seg[i], tint);
            var ic = new Vector2(seg[i].X + seg[i].Width / 2, seg[i].Y + seg[i].Height / 2);
            Art.Draw(Art.StatIcon[i], Ui.R(ic.X - 22, ic.Y - 34, 44, 44), Color.White);
            Ui.Text(v.ToString(), ic.X + 2, ic.Y + 8 + 2, 30, Th.Shadow, Align.Center, TextStyle.Title);
            Ui.Text(v.ToString(), ic.X, ic.Y + 8, 30, danger ? Th.Red : Th.White, Align.Center, TextStyle.Title);
            if (Ui.Hover(seg[i])) Ui.Text($"{Th.StatName[i]}  {R.StatMin}<x<{R.StatMax}", ic.X, seg[i].Y - 24, 18, Th.PinkPale, Align.Center);
        }
        Ui.Text($"产出 {card.FixedOutput}", c.X, c.Y - 14, 24, Th.Gold, Align.Center, TextStyle.Title);
    }
}
