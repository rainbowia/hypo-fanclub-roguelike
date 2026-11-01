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
    public static readonly Color[] Stat = { C(232, 226, 210), C(240, 210, 130), C(230, 90, 130), C(120, 170, 220) };
    public static readonly string[] StatName = { "理智", "欲望", "爱情", "躯体" };
    public static Color A(Color c, float a) => new(c.R, c.G, c.B, (byte)(c.A * Math.Clamp(a, 0, 1)));
    public static Color Lerp(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));
}

public enum TextStyle { Body, Title }

/// <summary>
/// 字体：标题/正文 × 中文/英文 四个槽位。绘制时按字符自动分段：ASCII 用英文字体，其它用中文字体。
/// 中文字体按需加载字形（遇到新字下一帧重建），所以任意文本都能显示，不用预先列字表。
///
/// 放在 data/assets/fonts/，按文件名关键字自动识别：
///   标题中文：*shijinhei*   正文中文：*wangxingyuan*
///   标题英文：*fulbo* / *retro*   正文英文：*krist*
/// 想换字体：改文件名，或写 data/assets/fonts/fonts.json：
///   { "title_cn": "xxx.ttf", "body_cn": "...", "title_en": "...", "body_en": "..." }
/// 缺哪个就回退：英文→同风格中文→另一风格中文→main.ttf→Raylib 默认字体。
/// 注意：Raylib 不支持 CFF 格式的 .otf，请转换成 .ttf。
/// </summary>
public static class Fonts
{
    const int BaseSize = 64;

    class Slot
    {
        public string Path; public bool Cjk; public Font Font; public bool Loaded, Dirty;
        public readonly HashSet<int> Cps = new();
        public void Need(string s)
        {
            if (!Cjk) return;
            foreach (var r in s.EnumerateRunes()) if (r.Value >= 128 && Cps.Add(r.Value)) Dirty = true;
        }
        public void Build()
        {
            if (Loaded) UnloadFont(Font);
            var arr = Cps.ToArray();
            Font = LoadFontEx(Path, BaseSize, arr, arr.Length);
            SetTextureFilter(Font.Texture, TextureFilter.Bilinear);
            Loaded = true; Dirty = false;
        }
    }

    // [style, 0=cn 1=en]
    static readonly Slot[,] _slots = new Slot[2, 2];
    public static bool HasFontFile { get; private set; }

    public static void Init(string dir)
    {
        var files = Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string>();
        string Find(params string[] keys) => files.FirstOrDefault(f => keys.Any(k => System.IO.Path.GetFileName(f).Contains(k, StringComparison.OrdinalIgnoreCase)));

        var map = new Dictionary<string, string>
        {
            ["title_cn"] = Find("shijinhei", "title_cn"), ["body_cn"] = Find("wangxingyuan", "body_cn"),
            ["title_en"] = Find("fulbo", "retro", "title_en"), ["body_en"] = Find("krist", "body_en"),
        };
        var cfg = System.IO.Path.Combine(dir, "fonts.json");
        if (File.Exists(cfg))
            try
            {
                var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(cfg));
                foreach (var kv in d) if (File.Exists(System.IO.Path.Combine(dir, kv.Value))) map[kv.Key] = System.IO.Path.Combine(dir, kv.Value);
            }
            catch { }
        var main = System.IO.Path.Combine(dir, "main.ttf");
        if (File.Exists(main)) { map["title_cn"] ??= main; map["body_cn"] ??= main; }
        map["title_cn"] ??= map["body_cn"]; map["body_cn"] ??= map["title_cn"];

        var common = "欢愉理智欲望爱情躯体周一二三四五六日八开始结算选座完成请离设置画廊存档读取返回联系人催眠道具图鉴";
        Slot Make(string path, bool cjk)
        {
            if (path == null || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) && IsCff(path)) return null;
            var s = new Slot { Path = path, Cjk = cjk };
            for (int c = 32; c < 127; c++) s.Cps.Add(c);
            if (cjk) foreach (var c in common) s.Cps.Add(c);
            s.Build();
            return s.Font.Texture.Id == 0 ? null : s;
        }
        _slots[0, 0] = Make(map["body_cn"], true);
        _slots[1, 0] = map["title_cn"] == map["body_cn"] ? _slots[0, 0] : Make(map["title_cn"], true) ?? _slots[0, 0];
        _slots[0, 0] ??= _slots[1, 0];
        _slots[0, 1] = Make(map["body_en"], false) ?? _slots[0, 0];
        _slots[1, 1] = Make(map["title_en"], false) ?? _slots[1, 0];
        HasFontFile = _slots[0, 0] != null;
    }

    static bool IsCff(string path)
    {
        try
        {
            using var fs = File.OpenRead(path); var b = new byte[4]; fs.Read(b, 0, 4);
            return b[0] == 'O' && b[1] == 'T' && b[2] == 'T' && b[3] == 'O';
        }
        catch { return false; }
    }

    /// <summary>每帧结束调用一次：重建有新字形的中文字体。</summary>
    public static void EndFrame()
    {
        foreach (var s in _slots) if (s != null && s.Dirty) s.Build();
    }

    static Font Pick(TextStyle st, bool ascii, out Slot slot)
    {
        slot = _slots[(int)st, ascii ? 1 : 0];
        return slot != null ? slot.Font : GetFontDefault();
    }

    /// <summary>按 ASCII/非 ASCII 切分成段。</summary>
    static IEnumerable<(string text, bool ascii)> Runs(string s)
    {
        int start = 0; bool? cur = null;
        for (int i = 0; i < s.Length; i++)
        {
            bool a = s[i] < 128;
            if (cur.HasValue && a != cur.Value) { yield return (s[start..i], cur.Value); start = i; }
            cur = a;
        }
        if (s.Length > start) yield return (s[start..], cur ?? true);
    }

    public static Vector2 Measure(string s, float size, TextStyle st = TextStyle.Body)
    {
        float w = 0, h = size;
        foreach (var (t, a) in Runs(s))
        {
            var f = Pick(st, a, out var slot); slot?.Need(t);
            var m = MeasureTextEx(f, t, size, 1); w += m.X + 1; h = Math.Max(h, m.Y);
        }
        return new Vector2(Math.Max(0, w - 1), h);
    }

    public static void Draw(string s, Vector2 pos, float size, Color c, TextStyle st = TextStyle.Body)
    {
        float x = pos.X;
        foreach (var (t, a) in Runs(s))
        {
            var f = Pick(st, a, out var slot); slot?.Need(t);
            DrawTextEx(f, t, new Vector2(MathF.Round(x), MathF.Round(pos.Y)), size, 1, c);
            x += MeasureTextEx(f, t, size, 1).X + 1;
        }
    }
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

    public static void Text(string s, float x, float y, float size, Color c, Align a = Align.Left, TextStyle st = TextStyle.Body)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (a != Align.Left) { var m = Fonts.Measure(s, size, st); x -= a == Align.Center ? m.X / 2 : m.X; }
        Fonts.Draw(s, new Vector2(x, y), size, c, st);
    }
    /// <summary>标题字体 + 阴影。</summary>
    public static void TextShadow(string s, float x, float y, float size, Color c, Align a = Align.Left)
    {
        Text(s, x + 2, y + 2, size, Th.A(Th.Shadow, c.A / 255f), a, TextStyle.Title); Text(s, x, y, size, c, a, TextStyle.Title);
    }
    public static void Title(string s, float x, float y, float size, Color c, Align a = Align.Left) => Text(s, x, y, size, c, a, TextStyle.Title);
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

    /// <summary>图片按钮（普通/悬停/按下三张图），缺图时退回普通按钮。</summary>
    public static bool ArtButton(Rectangle r, string normal, string hover, string pressed, string label, bool enabled = true, float size = 26, Color? textColor = null)
    {
        var n = Art.T(normal);
        if (!n.HasValue) return Button(r, label, enabled, null, size);
        bool h = enabled && Hover(r);
        string img = !enabled ? normal : h && Down && pressed != null ? pressed : h && hover != null ? hover : normal;
        var tint = enabled ? Color.White : Th.C(120, 110, 115);
        if (h && img == normal) tint = Th.C(255, 225, 235);
        Art.Stretch(img, r, tint);
        var tc = textColor ?? Th.White;
        Text(label, r.X + r.Width / 2 + 2, r.Y + r.Height / 2 - size / 2 + 2, size, Th.A(Th.Shadow, enabled ? 1 : 0.4f), Align.Center, TextStyle.Title);
        Text(label, r.X + r.Width / 2, r.Y + r.Height / 2 - size / 2, size, enabled ? tc : Th.Grey, Align.Center, TextStyle.Title);
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
