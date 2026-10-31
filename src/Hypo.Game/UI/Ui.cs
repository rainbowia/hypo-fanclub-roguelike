using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.UI;

/// <summary>配色取自美术稿：深底、玫粉、灰紫、浅粉。</summary>
public static class Th
{
    public static Color C(int r, int g, int b, int a = 255) => new((byte)r, (byte)g, (byte)b, (byte)a);
    public static readonly Color Bg = C(24, 18, 22), Panel = C(70, 58, 64), PanelHi = C(98, 80, 88),
        Pink = C(226, 80, 128), PinkHot = C(255, 16, 120), PinkPale = C(216, 188, 196), Wine = C(94, 0, 44),
        White = C(245, 240, 242), Grey = C(150, 140, 145), Dark = C(40, 32, 36), Black = C(0, 0, 0),
        Red = C(230, 50, 70), Gold = C(240, 210, 130), Blue = C(120, 170, 220), Shadow = C(0, 0, 0, 140);
    // 四维颜色：理智◇灰 / 欲望💧蓝 / 爱情♡粉 / 躯体✦黄
    public static readonly Color[] Stat = { C(170, 160, 165), C(120, 170, 220), C(230, 90, 130), C(240, 210, 130) };
    public static readonly string[] StatName = { "理智", "欲望", "爱情", "躯体" };
    public static Color A(Color c, float a) => new(c.R, c.G, c.B, (byte)(c.A * Math.Clamp(a, 0, 1)));
    public static Color Lerp(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
}

/// <summary>
/// 字体：按需加载字形（遇到新汉字时下一帧重建字体），所以任意文本都能显示，不用预先列字表。
/// 字体文件：data/assets/fonts/main.ttf （推荐 思源黑体/站酷系列 等可商用字体）
/// </summary>
public static class Fonts
{
    static Font _font; static bool _loaded, _dirty;
    static readonly HashSet<int> _cps = new();
    static string _path;
    const int BaseSize = 48;

    public static void Init(string path)
    {
        _path = path;
        for (int c = 32; c < 127; c++) _cps.Add(c);
        foreach (var c in "欢愉理智欲望爱情躯体周一二三四五六日八开始结算选座完成请离设置画廊存档读取返回联系人催眠道具图鉴") _cps.Add(c);
        Rebuild();
    }
    static void Rebuild()
    {
        if (_loaded && _font.Texture.Id != GetFontDefault().Texture.Id) UnloadFont(_font);
        if (File.Exists(_path))
        {
            var arr = _cps.ToArray();
            _font = LoadFontEx(_path, BaseSize, arr, arr.Length);
            SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
        }
        else _font = GetFontDefault();
        _loaded = true; _dirty = false;
    }
    /// <summary>每帧结束调用一次。</summary>
    public static void EndFrame() { if (_dirty) Rebuild(); }
    public static bool HasFontFile => File.Exists(_path);

    static void Need(string s)
    {
        foreach (var r in s.EnumerateRunes()) if (_cps.Add(r.Value)) _dirty = true;
    }
    public static Font F(string s) { Need(s); return _font; }
    public static Vector2 Measure(string s, float size) => MeasureTextEx(F(s), s, size, 1);
}

public enum Align { Left, Center, Right }

/// <summary>立即模式 UI 小工具。所有坐标基于 1600x900 虚拟画布。</summary>
public static class Ui
{
    public const int W = 1600, H = 900;
    public static Vector2 Mouse;
    public static bool Click => IsMouseButtonPressed(MouseButton.Left) && !Consumed;
    public static bool Released => IsMouseButtonReleased(MouseButton.Left);
    public static bool Down => IsMouseButtonDown(MouseButton.Left);
    /// <summary>上层面板吃掉点击后设为 true，阻止下层响应。</summary>
    public static bool Consumed;
    public static float Wheel;
    public static bool Hover(Rectangle r) => CheckCollisionPointRec(Mouse, r);
    public static bool Clicked(Rectangle r) { if (Click && Hover(r)) { Consumed = true; return true; } return false; }
    public static void Eat(Rectangle r) { if (IsMouseButtonPressed(MouseButton.Left) && Hover(r)) Consumed = true; }

    public static Rectangle R(float x, float y, float w, float h) => new(x, y, w, h);

    public static void Text(string s, float x, float y, float size, Color c, Align a = Align.Left)
    {
        if (string.IsNullOrEmpty(s)) return;
        var f = Fonts.F(s);
        if (a != Align.Left) { var m = Fonts.Measure(s, size); x -= a == Align.Center ? m.X / 2 : m.X; }
        DrawTextEx(f, s, new Vector2(MathF.Round(x), MathF.Round(y)), size, 1, c);
    }
    public static void TextShadow(string s, float x, float y, float size, Color c, Align a = Align.Left)
    {
        Text(s, x + 2, y + 2, size, Th.Shadow, a); Text(s, x, y, size, c, a);
    }
    /// <summary>自动换行文本，返回占用高度。</summary>
    public static float Wrap(string s, Rectangle box, float size, Color c)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        float y = box.Y; string line = "";
        foreach (var para in s.Split('\n'))
        {
            line = "";
            foreach (var r in para.EnumerateRunes())
            {
                var t = line + r;
                if (Fonts.Measure(t, size).X > box.Width && line.Length > 0) { Text(line, box.X, y, size, c); y += size * 1.3f; line = r.ToString(); }
                else line = t;
            }
            Text(line, box.X, y, size, c); y += size * 1.3f;
        }
        return y - box.Y;
    }

    public static void Panel(Rectangle r, Color fill, Color? border = null, float round = 0.08f)
    {
        DrawRectangleRounded(new Rectangle(r.X + 4, r.Y + 6, r.Width, r.Height), round, 8, Th.Shadow);
        DrawRectangleRounded(r, round, 8, fill);
        if (border.HasValue) DrawRectangleRoundedLinesEx(r, round, 8, 2, border.Value);
    }

    /// <summary>按钮。返回是否被点击。</summary>
    public static bool Button(Rectangle r, string label, bool enabled = true, Color? color = null, float size = 26)
    {
        bool h = enabled && Hover(r);
        var c = enabled ? (color ?? Th.Panel) : Th.Dark;
        if (h) c = Th.Lerp(c, Th.Pink, 0.35f);
        Panel(r, c, h ? Th.PinkPale : Th.A(Th.PinkPale, 0.25f), 0.3f);
        Text(label, r.X + r.Width / 2, r.Y + r.Height / 2 - size / 2, size, enabled ? Th.White : Th.Grey, Align.Center);
        return enabled && Clicked(r);
    }

    public static bool IconButton(Rectangle r, string icon, string fallbackGlyph, bool enabled = true)
    {
        bool h = enabled && Hover(r);
        var tex = Assets.Tex(icon);
        float s = h ? 1.08f : 1f;
        var rr = new Rectangle(r.X + r.Width * (1 - s) / 2, r.Y + r.Height * (1 - s) / 2, r.Width * s, r.Height * s);
        if (tex.HasValue) Assets.Draw(tex.Value, rr, enabled ? Th.White : Th.Grey);
        else
        {
            DrawCircle((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2), r.Width / 2, h ? Th.PanelHi : Th.Panel);
            Text(fallbackGlyph, r.X + r.Width / 2, r.Y + r.Height / 2 - r.Height * 0.25f, r.Height * 0.5f, Th.PinkPale, Align.Center);
        }
        return enabled && Clicked(r);
    }

    /// <summary>进度条。</summary>
    public static void Bar(Rectangle r, float t, Color fill, Color back)
    {
        DrawRectangleRounded(r, 0.5f, 8, back);
        t = Math.Clamp(t, 0, 1);
        if (t > 0) DrawRectangleRounded(new Rectangle(r.X, r.Y, Math.Max(r.Height, r.Width * t), r.Height), 0.5f, 8, fill);
    }

    public static float Slider(Rectangle r, float v, string label)
    {
        Text(label, r.X, r.Y - 30, 22, Th.PinkPale);
        Bar(r, v, Th.Pink, Th.Dark);
        if (Down && Hover(new Rectangle(r.X - 10, r.Y - 10, r.Width + 20, r.Height + 20)))
        { v = Math.Clamp((Mouse.X - r.X) / r.Width, 0, 1); Consumed = true; }
        DrawCircleV(new Vector2(r.X + r.Width * v, r.Y + r.Height / 2), r.Height, Th.White);
        Text($"{(int)(v * 100)}%", r.X + r.Width + 20, r.Y - 6, 22, Th.White);
        return v;
    }

    public static bool Toggle(Rectangle r, bool v, string label)
    {
        Text(label, r.X + 60, r.Y + 2, 24, Th.PinkPale);
        var box = new Rectangle(r.X, r.Y, 46, 28);
        Bar(box, v ? 1 : 0, Th.Pink, Th.Dark);
        DrawCircleV(new Vector2(box.X + (v ? 32 : 14), box.Y + 14), 11, Th.White);
        if (Clicked(new Rectangle(r.X, r.Y, r.Width, 28))) v = !v;
        return v;
    }

    /// <summary>全屏半透明遮罩，吃掉所有点击。</summary>
    public static void Dim(float a = 0.7f)
    {
        DrawRectangle(0, 0, W, H, Th.A(Th.Black, a));
    }

    // 绘制小图形（缺图时的占位）
    public static void Sparkle(Vector2 c, float r, Color col)
    {
        DrawTriangle(c + new Vector2(0, -r), c + new Vector2(-r * 0.22f, 0), c + new Vector2(r * 0.22f, 0), col);
        DrawTriangle(c + new Vector2(0, r), c + new Vector2(r * 0.22f, 0), c + new Vector2(-r * 0.22f, 0), col);
        DrawTriangle(c + new Vector2(-r, 0), c + new Vector2(0, r * 0.22f), c + new Vector2(0, -r * 0.22f), col);
        DrawTriangle(c + new Vector2(r, 0), c + new Vector2(0, -r * 0.22f), c + new Vector2(0, r * 0.22f), col);
    }
    public static void Hatch(Rectangle r, Color c)
    {
        BeginScissorMode((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        for (float x = r.X - r.Height; x < r.X + r.Width; x += 14)
            DrawLineEx(new Vector2(x, r.Y + r.Height), new Vector2(x + r.Height, r.Y), 2, c);
        EndScissorMode();
    }
}

/// <summary>
/// 贴图缓存。路径相对 data/ 。缺图返回 null，界面自动用占位图形。
/// UI 图标放在 data/assets/ui/ （文件名见 README）。
/// </summary>
public static class Assets
{
    static readonly Dictionary<string, Texture2D?> _cache = new();
    public static string Root = "data";

    public static Texture2D? Tex(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (_cache.TryGetValue(rel, out var t)) return t;
        var p = Path.Combine(Root, rel);
        if (!File.Exists(p) && File.Exists(p + ".png")) p += ".png";
        Texture2D? res = null;
        if (File.Exists(p))
        {
            var tex = LoadTexture(p);
            SetTextureFilter(tex, TextureFilter.Bilinear);
            res = tex;
        }
        _cache[rel] = res;
        return res;
    }
    public static Texture2D? UiTex(string name) => Tex("assets/ui/" + name + ".png");

    /// <summary>保持比例填充（cover=true 裁切填满，否则完整放入）。</summary>
    public static void Draw(Texture2D t, Rectangle dst, Color tint, bool cover = false)
    {
        float sx = dst.Width / t.Width, sy = dst.Height / t.Height;
        float s = cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
        if (cover)
        {
            float sw = dst.Width / s, sh = dst.Height / s;
            var src = new Rectangle((t.Width - sw) / 2, (t.Height - sh) / 2, sw, sh);
            DrawTexturePro(t, src, dst, Vector2.Zero, 0, tint);
        }
        else
        {
            float w = t.Width * s, h = t.Height * s;
            DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height), new Rectangle(dst.X + (dst.Width - w) / 2, dst.Y + (dst.Height - h) / 2, w, h), Vector2.Zero, 0, tint);
        }
    }
    public static void Clear()
    {
        foreach (var t in _cache.Values) if (t.HasValue) UnloadTexture(t.Value);
        _cache.Clear();
    }
}
