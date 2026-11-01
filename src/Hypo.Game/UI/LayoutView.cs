using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Hypo.Core.Data;
using Hypo.Core.Scripting;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.UI;

/// <summary>
/// 渲染并运行一个 UiLayout。游戏和 Studio 共用。
/// 用法：每帧先 Input()（在场景其它交互之前，抢点击），再 Draw()。
/// </summary>
public class LayoutView
{
    public UiLayout Layout { get; }
    public IUiHost Host;
    /// <summary>编辑器模式：忽略显示条件、空图片画虚线框、不响应交互。</summary>
    public bool EditorMode;
    readonly Dictionary<string, Rectangle> _rects = new();
    readonly HashSet<string> _hover = new(), _shown = new();
    Dictionary<string, UiNode> _byId = new();

    public LayoutView(UiLayout layout, IUiHost host, bool clone = true)
    {
        Layout = clone ? JsonSerializer.Deserialize<UiLayout>(JsonSerializer.Serialize(layout, Hypo.Core.Data.ContentDb.Json), Hypo.Core.Data.ContentDb.Json) : layout;
        Host = host;
    }

    // ---------------- 几何 ----------------
    public void Compute()
    {
        _byId = Layout.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
        _rects.Clear();
        foreach (var n in Layout.Nodes) Abs(n, 0);
    }
    Rectangle Abs(UiNode n, int depth)
    {
        if (_rects.TryGetValue(n.Id, out var r)) return r;
        float px = 0, py = 0;
        if (depth < 32 && !string.IsNullOrEmpty(n.ParentId) && _byId.TryGetValue(n.ParentId, out var p))
        { var pr = Abs(p, depth + 1); px = pr.X; py = pr.Y; }
        r = new Rectangle(px + n.X, py + n.Y, n.W, n.H);
        _rects[n.Id] = r;
        return r;
    }
    public Rectangle RectOf(UiNode n) => n != null && _rects.TryGetValue(n.Id, out var r) ? r : default;

    public bool IsVisible(UiNode n)
    {
        for (int i = 0; n != null && i < 32; i++)
        {
            if (!n.Visible) return false;
            if (!EditorMode && !UiBind.Cond(n.VisibleIf, Host)) return false;
            n = string.IsNullOrEmpty(n.ParentId) ? null : _byId.GetValueOrDefault(n.ParentId);
        }
        return true;
    }

    static bool Clickable(UiNode n) => n.Type == UiNodeType.Button || n.Behaviours.Any(b => b.Event == UiEvent.OnClick);

    /// <summary>最上层的、在点 p 下的对象（clickableOnly=只找可点击的）。</summary>
    public UiNode Pick(Vector2 p, bool clickableOnly)
    {
        for (int i = Layout.Nodes.Count - 1; i >= 0; i--)
        {
            var n = Layout.Nodes[i];
            if (clickableOnly && !Clickable(n)) continue;
            if (!IsVisible(n)) continue;
            if (CheckCollisionPointRec(p, RectOf(n))) return n;
        }
        return null;
    }

    // ---------------- 交互 ----------------
    /// <summary>处理悬停/点击/OnShow/OnUpdate。返回鼠标是否在可点击对象上。</summary>
    public bool Input()
    {
        Compute();
        if (EditorMode) return false;
        foreach (var n in Layout.Nodes.ToList())
        {
            bool vis = IsVisible(n);
            if (vis && _shown.Add(n.Id)) Fire(n, UiEvent.OnShow);
            else if (!vis) _shown.Remove(n.Id);
            if (vis) Fire(n, UiEvent.OnUpdate);
        }
        var hit = Ui.Consumed ? null : Pick(Ui.Mouse, true);
        var over = hit?.Id;
        if (over != null && !_hover.Contains(over)) Fire(hit, UiEvent.OnHoverEnter);
        _hover.Clear(); if (over != null) _hover.Add(over);
        if (hit != null && Ui.Click) { Ui.Consumed = true; Fire(hit, UiEvent.OnClick); }
        return hit != null;
    }

    public void Fire(UiNode n, UiEvent e)
    {
        foreach (var b in n.Behaviours.Where(b => b.Event == e).ToList())
        {
            if (!string.IsNullOrWhiteSpace(b.Command)) Run(b.Command, UiBind.Text(b.Arg, Host));
            if (!string.IsNullOrWhiteSpace(b.Script)) ScriptHost.RunUi(b.Script, new UiScriptGlobals { Host = Host, Layout = Layout, Node = n });
        }
    }

    public void Run(string cmd, string arg)
    {
        switch (cmd)
        {
            case "Show": SetVis(arg, v => true); break;
            case "Hide": SetVis(arg, v => false); break;
            case "Toggle": SetVis(arg, v => !v); break;
            default: Host?.Command(cmd, arg); break;
        }
    }
    void SetVis(string name, Func<bool, bool> f) { var n = Layout.Find(name); if (n != null) n.Visible = f(n.Visible); }

    // ---------------- 绘制 ----------------
    public void Draw()
    {
        if (_rects.Count == 0 || _byId.Count != Layout.Nodes.Count) Compute();
        foreach (var n in Layout.Nodes)
            if (IsVisible(n)) DrawNode(n, RectOf(n));
    }

    void DrawNode(UiNode n, Rectangle r)
    {
        float op = Math.Clamp(n.Opacity, 0, 1);
        bool hover = !EditorMode && _hover.Contains(n.Id);
        switch (n.Type)
        {
            case UiNodeType.Panel:
                DrawRectangleRounded(r, n.Round, 8, Col(n.FillColor, op));
                Border(n, r, op);
                DrawImage(n, r, op, hover);
                break;
            case UiNodeType.Image:
                if (!DrawImage(n, r, op, hover) && EditorMode) Dashed(r);
                break;
            case UiNodeType.Button:
                if (!DrawImage(n, r, op, hover))
                {
                    var fill = Col(n.FillColor, op);
                    if (hover) fill = Th.Lerp(fill, Th.Pink, 0.35f);
                    DrawRectangleRounded(r, n.Round, 8, fill);
                    Border(n, r, op);
                }
                break;
            case UiNodeType.Bar:
                float t = (float)(UiBind.Num(n.BarValue, Host) / Math.Max(1e-6, UiBind.Num(n.BarMax, Host)));
                DrawRectangleRounded(r, n.Round, 8, Col(n.FillColor, op));
                t = Math.Clamp(t, 0, 1);
                if (t > 0) DrawRectangleRounded(new Rectangle(r.X, r.Y, Math.Max(r.Height * n.Round, r.Width * t), r.Height), n.Round, 8, Col(n.BarColor, op));
                DrawImage(n, r, op, hover);
                Border(n, r, op);
                break;
            case UiNodeType.Group:
                if (EditorMode) Dashed(r, 0.25f);
                break;
        }
        if (!string.IsNullOrEmpty(n.Text)) DrawLabel(n, r, op);
    }

    bool DrawImage(UiNode n, Rectangle r, float op, bool hover)
    {
        var path = hover && !string.IsNullOrEmpty(n.HoverImage) ? n.HoverImage : n.Image;
        var tex = Assets.Tex(path);
        if (!tex.HasValue) return false;
        var t = tex.Value;
        var tint = Col(n.Tint, op);
        if (hover && string.IsNullOrEmpty(n.HoverImage) && n.Type == UiNodeType.Button) tint = Th.Lerp(tint, Th.White, 0.25f);
        switch (n.ImageMode)
        {
            case UiImageMode.Fit: Assets.Draw(t, r, tint); break;
            case UiImageMode.Cover: Assets.Draw(t, r, tint, true); break;
            case UiImageMode.NineSlice:
                int s = Math.Clamp(n.Slice, 0, Math.Min(t.Width, t.Height) / 2);
                var info = new NPatchInfo { Source = new Rectangle(0, 0, t.Width, t.Height), Left = s, Top = s, Right = s, Bottom = s, Layout = NPatchLayout.NinePatch };
                DrawTextureNPatch(t, info, r, Vector2.Zero, 0, tint);
                break;
            default: DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), r, Vector2.Zero, 0, tint); break;
        }
        return true;
    }

    void Border(UiNode n, Rectangle r, float op)
    {
        var c = Col(n.BorderColor, op);
        if (c.A > 0) DrawRectangleRoundedLinesEx(r, n.Round, 8, 2, c);
    }

    void DrawLabel(UiNode n, Rectangle r, float op)
    {
        var text = UiBind.Text(n.Text, Host);
        var c = Col(n.TextColor, op);
        var st = n.Font == UiFont.Title ? TextStyle.Title : TextStyle.Body;
        if (n.Wrap) { Ui.Wrap(text, new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), n.FontSize, c); return; }
        float y = r.Y + (r.Height - n.FontSize) / 2;
        float x = n.Align switch { UiAlign.Left => r.X + 6, UiAlign.Right => r.X + r.Width - 6, _ => r.X + r.Width / 2 };
        var al = n.Align switch { UiAlign.Left => Align.Left, UiAlign.Right => Align.Right, _ => Align.Center };
        if (n.Shadow) Ui.Text(text, x + 2, y + 2, n.FontSize, Th.A(Th.Shadow, op), al, st);
        Ui.Text(text, x, y, n.FontSize, c, al, st);
    }

    static void Dashed(Rectangle r, float a = 0.6f)
    {
        var c = Th.A(Th.PinkPale, a);
        for (float x = r.X; x < r.X + r.Width; x += 12) { DrawLineV(new(x, r.Y), new(Math.Min(x + 6, r.X + r.Width), r.Y), c); DrawLineV(new(x, r.Y + r.Height), new(Math.Min(x + 6, r.X + r.Width), r.Y + r.Height), c); }
        for (float y = r.Y; y < r.Y + r.Height; y += 12) { DrawLineV(new(r.X, y), new(r.X, Math.Min(y + 6, r.Y + r.Height)), c); DrawLineV(new(r.X + r.Width, y), new(r.X + r.Width, Math.Min(y + 6, r.Y + r.Height)), c); }
    }

    /// <summary>"#RRGGBB" 或 "#RRGGBBAA"。</summary>
    public static Color Col(string hex, float opacity = 1)
    {
        var c = ParseHex(hex);
        return new Color(c.R, c.G, c.B, (byte)(c.A * Math.Clamp(opacity, 0, 1)));
    }
    public static Color ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.White;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 6) h += "FF";
        if (h.Length != 8 || !uint.TryParse(h, NumberStyles.HexNumber, null, out var v)) return Color.White;
        return new Color((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
