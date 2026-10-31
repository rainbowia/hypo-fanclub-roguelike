using System.Numerics;
using Hypo.Core.Data;
using Hypo.Core.Runtime;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.UI;

/// <summary>卡面绘制。美术到位后替换 assets/ui/card_frame.png 与卡牌 Portrait 即可。</summary>
public static class CardView
{
    public const float W = 150, H = 210;

    public static void Draw(CardInstance c, Rectangle r, float alpha = 1, bool selected = false, bool active = false)
    {
        var def = c?.Def;
        var frame = Assets.UiTex("card_frame");
        var shadow = new Rectangle(r.X + 6, r.Y + 8, r.Width, r.Height);
        DrawRectangleRounded(shadow, 0.06f, 6, Th.A(Th.Black, 0.5f * alpha));
        if (frame.HasValue) Assets.Draw(frame.Value, r, Th.A(Color.White, alpha));
        else DrawRectangleRounded(r, 0.06f, 6, Th.A(Th.PinkPale, alpha));

        var art = new Rectangle(r.X + r.Width * 0.06f, r.Y + r.Width * 0.06f, r.Width * 0.88f, r.Height * 0.66f);
        var tex = Assets.Tex(def?.Portrait);
        if (tex.HasValue) Assets.Draw(tex.Value, art, Th.A(Color.White, alpha), true);
        else
        {
            DrawRectangleRec(art, Th.A(Th.Dark, alpha));
            // 占位剪影
            var cx = art.X + art.Width / 2;
            DrawCircleV(new Vector2(cx, art.Y + art.Height * 0.35f), art.Width * 0.18f, Th.A(Th.Panel, alpha));
            DrawRectangleRounded(new Rectangle(cx - art.Width * 0.3f, art.Y + art.Height * 0.55f, art.Width * 0.6f, art.Height * 0.45f), 0.5f, 6, Th.A(Th.Panel, alpha));
        }
        float s = r.Width / W;
        // 名字条
        var nameBar = new Rectangle(r.X + 8 * s, art.Y + art.Height + 6 * s, r.Width - 16 * s, 26 * s);
        DrawRectangleRounded(nameBar, 0.5f, 6, Th.A(Th.Wine, alpha));
        if (c != null) Ui.Text(App.Db.T(def?.Name ?? c.DefId), nameBar.X + nameBar.Width / 2, nameBar.Y + 3 * s, 19 * s, Th.A(Th.White, alpha), Align.Center);
        // 四维小条
        if (c != null)
            for (int i = 0; i < 4; i++)
            {
                var b = new Rectangle(r.X + 10 * s + i * (r.Width - 20 * s) / 4, nameBar.Y + nameBar.Height + 6 * s, (r.Width - 20 * s) / 4 - 4 * s, 22 * s);
                DrawRectangleRounded(b, 0.4f, 4, Th.A(Th.Stat[i], alpha));
                Ui.Text(c.Stats[i].ToString(), b.X + b.Width / 2, b.Y + 1 * s, 19 * s, Th.A(Th.Dark, alpha), Align.Center);
            }
        // 产出角标
        if (c != null)
        {
            var o = new Vector2(r.X + r.Width - 8 * s, r.Y + 8 * s);
            Ui.Sparkle(o, 26 * s, Th.A(Th.Gold, alpha));
            Ui.Text(c.FixedOutput.ToString(), o.X, o.Y - 11 * s, 22 * s, Th.A(Th.Dark, alpha), Align.Center);
            if (c.Hypnos.Count > 0) Ui.Text("◎", r.X + 4 * s, r.Y + 2 * s, 24 * s, Th.A(Th.PinkHot, alpha));
            if (c.Items.Count > 0) Ui.Text("◆", r.X + 4 * s, r.Y + 26 * s, 20 * s, Th.A(Th.Gold, alpha));
        }
        if (selected) DrawRectangleRoundedLinesEx(r, 0.06f, 6, 4, Th.PinkHot);
        if (active) DrawRectangleRoundedLinesEx(new Rectangle(r.X - 6, r.Y - 6, r.Width + 12, r.Height + 12), 0.08f, 6, 4, Th.Gold);
    }

    public static void Empty(Rectangle r, float alpha, bool hover)
    {
        var t = Assets.UiTex("slot_empty");
        if (t.HasValue) { Assets.Draw(t.Value, r, Th.A(Color.White, alpha)); return; }
        DrawRectangleRoundedLinesEx(r, 0.08f, 6, 3, Th.A(hover ? Th.Pink : Th.Grey, alpha * 0.8f));
        Ui.Text("+", r.X + r.Width / 2, r.Y + r.Height / 2 - r.Height * 0.15f, r.Height * 0.3f, Th.A(hover ? Th.Pink : Th.Grey, alpha), Align.Center);
    }

    public static string TriggerName(Trigger t) => t switch
    {
        Trigger.BeforeResolve => "结算前", Trigger.OnResolve => "结算时", Trigger.AfterResolve => "结算后",
        Trigger.OnDestroyed => "亡语", Trigger.OnDismissed => "请离时", Trigger.OnEnterBoard => "上场", Trigger.OnRoundStart => "每圈开始",
        Trigger.OnRoundEnd => "每圈结束", Trigger.OnMoved => "被移动", Trigger.BattleStart => "开战",
        Trigger.AnyCardResolving => "任意卡结算", Trigger.AnyCardDestroyed => "任意卡撕毁", _ => t.ToString()
    };

    /// <summary>效果描述：优先用 Description，否则自动由积木生成。</summary>
    public static string Describe(EffectDef e)
    {
        var head = $"【{TriggerName(e.Trigger)}{(e.OncePerRound ? "·每轮限一次" : "")}】";
        if (!string.IsNullOrWhiteSpace(e.Description)) return head + App.Db.T(e.Description);
        var parts = new List<string>();
        if (e.Conditions.Count > 0) parts.Add("若 " + string.Join(" 且 ", e.Conditions.Select(c => c.ToString())));
        parts.AddRange(e.Actions.Select(a => a.ToString()));
        if (!string.IsNullOrWhiteSpace(e.Script)) parts.Add("（代码块）");
        return head + string.Join("；", parts);
    }
}
