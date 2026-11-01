using System.ComponentModel;
using System.Text.RegularExpressions;
using Hypo.Core.Effects;
using Hypo.Core.Runtime;
using Hypo.Core.Util;

namespace Hypo.Core.Data;

// ======================================================================
//  UI 布局数据（Studio 编辑，游戏渲染）。文件：data/ui/{Id}.json
//  节点是扁平列表 + ParentId；列表顺序 = 绘制顺序（后面的在上层）。
// ======================================================================

public enum UiNodeType { Group, Image, Text, Button, Panel, Bar }
public enum UiFont { Body, Title }
public enum UiAlign { Left, Center, Right }
public enum UiImageMode { Stretch, Fit, Cover, NineSlice }
public enum UiEvent { OnClick, OnHoverEnter, OnShow, OnUpdate }

public class UiLayout
{
    [Category("布局"), DisplayName("ID"), Description("文件名")] public string Id { get; set; } = "new_layout";
    [Category("布局"), DisplayName("名字")] public string Name { get; set; } = "新界面";
    [Category("布局"), DisplayName("替换内置界面"), Description("hud = 主场景普通视角 HUD；title = 标题界面；留空 = 不自动使用")]
    public string Replaces { get; set; } = "";
    [Category("布局"), DisplayName("启用")] public bool Enabled { get; set; } = true;
    [Category("编辑器"), DisplayName("参考图"), Description("只在编辑器里显示，例如把草图拖进来对位")] public string EditorReference { get; set; } = "";
    [Category("编辑器"), DisplayName("参考图不透明度")] public float ReferenceOpacity { get; set; } = 0.35f;
    [Browsable(false)] public List<UiNode> Nodes { get; set; } = new();

    public UiNode ById(string id) => string.IsNullOrEmpty(id) ? null : Nodes.FirstOrDefault(n => n.Id == id);
    public UiNode Find(string idOrName) => ById(idOrName) ?? Nodes.FirstOrDefault(n => n.Name == idOrName);
    public IEnumerable<UiNode> Children(string parentId) => Nodes.Where(n => n.ParentId == (parentId ?? ""));
    public bool IsDescendantOf(UiNode n, string ancestorId)
    {
        for (var p = ById(n?.ParentId); p != null; p = ById(p.ParentId)) if (p.Id == ancestorId) return true;
        return false;
    }
    public List<UiNode> Subtree(UiNode root) => Nodes.Where(n => n == root || IsDescendantOf(n, root.Id)).ToList();
}

public class UiNode
{
    [Browsable(false)] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    [Browsable(false)] public string ParentId { get; set; } = "";

    [Category("对象"), DisplayName("名字"), Description("命令 Show/Hide/Toggle 和代码里用名字找对象")] public string Name { get; set; } = "Node";
    [Category("对象"), DisplayName("类型")] public UiNodeType Type { get; set; } = UiNodeType.Image;
    [Category("对象"), DisplayName("可见")] public bool Visible { get; set; } = true;
    [Category("对象"), DisplayName("显示条件"), Description("例：phase == Prepare、moves > 0、inboss。留空 = 总是显示")] public string VisibleIf { get; set; } = "";

    [Category("变换"), DisplayName("X"), Description("相对父对象")] public float X { get; set; }
    [Category("变换"), DisplayName("Y")] public float Y { get; set; }
    [Category("变换"), DisplayName("宽")] public float W { get; set; } = 100;
    [Category("变换"), DisplayName("高")] public float H { get; set; } = 100;

    [Category("图片"), DisplayName("图片"), Description("相对 data/，可从项目面板拖进来")] public string Image { get; set; } = "";
    [Category("图片"), DisplayName("悬停图片")] public string HoverImage { get; set; } = "";
    [Category("图片"), DisplayName("模式")] public UiImageMode ImageMode { get; set; } = UiImageMode.Stretch;
    [Category("图片"), DisplayName("九宫格边距"), Description("NineSlice 模式：四边不拉伸的像素（原图尺寸）")] public int Slice { get; set; } = 16;
    [Category("图片"), DisplayName("颜色")] public string Tint { get; set; } = "#FFFFFFFF";
    [Category("图片"), DisplayName("不透明度")] public float Opacity { get; set; } = 1f;

    [Category("文字"), DisplayName("文字"), Description("可插变量：{pleasure} / {target}")] public string Text { get; set; } = "";
    [Category("文字"), DisplayName("字体")] public UiFont Font { get; set; } = UiFont.Body;
    [Category("文字"), DisplayName("字号")] public float FontSize { get; set; } = 24;
    [Category("文字"), DisplayName("文字颜色")] public string TextColor { get; set; } = "#F5F0F2FF";
    [Category("文字"), DisplayName("对齐")] public UiAlign Align { get; set; } = UiAlign.Center;
    [Category("文字"), DisplayName("阴影")] public bool Shadow { get; set; }
    [Category("文字"), DisplayName("自动换行")] public bool Wrap { get; set; }

    [Category("形状"), DisplayName("填充色"), Description("Panel / 无图按钮 / 进度条底色")] public string FillColor { get; set; } = "#463A40FF";
    [Category("形状"), DisplayName("边框色")] public string BorderColor { get; set; } = "#00000000";
    [Category("形状"), DisplayName("圆角")] public float Round { get; set; } = 0.15f;

    [Category("进度条"), DisplayName("当前值"), Description("公式，例 pleasure")] public string BarValue { get; set; } = "";
    [Category("进度条"), DisplayName("最大值"), Description("公式，例 target")] public string BarMax { get; set; } = "";
    [Category("进度条"), DisplayName("进度颜色")] public string BarColor { get; set; } = "#E25080FF";
    [Category("进度条"), DisplayName("填充图片"), Description("设置后用图片按比例裁切当作进度（例：Icons1_pleasureBarFill），不再画颜色")] public string FillImage { get; set; } = "";

    [Browsable(false)] public List<UiBehaviour> Behaviours { get; set; } = new();
}

/// <summary>对象行为：事件 → 命令（可带参数）→ 可选 C# 代码。</summary>
public class UiBehaviour
{
    public UiEvent Event { get; set; } = UiEvent.OnClick;
    public string Command { get; set; } = "";
    public string Arg { get; set; } = "";
    public string Script { get; set; } = "";
}

/// <summary>游戏/编辑器为 UI 提供数据和命令。</summary>
public interface IUiHost
{
    GameRunner Game { get; }
    /// <summary>取变量：数字放 num，字符串放 str（str 非 null 表示是字符串）。</summary>
    bool TryGet(string name, out double num, out string str);
    void Command(string cmd, string arg);
}

/// <summary>UI 代码块可用的全局对象。</summary>
public class UiScriptGlobals
{
    public IUiHost Host;
    public UiLayout Layout;
    public UiNode Node;
    public GameRunner Game => Host?.Game;
    public void Cmd(string cmd, string arg = "") => Host?.Command(cmd, arg);
    public UiNode Find(string name) => Layout?.Find(name);
    public double Num(string var) => Host != null && Host.TryGet(var, out var n, out _) ? n : 0;
}

/// <summary>绑定求值：文本模板、公式、显示条件。</summary>
public static class UiBind
{
    static readonly Regex VarRx = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}");
    static readonly Regex IdRx = new(@"[A-Za-z_][A-Za-z0-9_]*");
    static readonly HashSet<string> Funcs = new() { "ceil", "floor", "round", "min", "max", "abs" };

    public static string Text(string tpl, IUiHost h)
    {
        if (string.IsNullOrEmpty(tpl) || h == null || tpl.IndexOf('{') < 0) return tpl ?? "";
        return VarRx.Replace(tpl, m => h.TryGet(m.Groups[1].Value, out var n, out var s) ? (s ?? Fmt(n)) : m.Value);
    }
    public static string Fmt(double d) => Math.Abs(d - Math.Round(d)) < 1e-9 ? ((long)Math.Round(d)).ToString() : d.ToString("0.##");

    public static double Num(string expr, IUiHost h)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0;
        if (double.TryParse(expr, out var lit)) return lit;
        var vars = new Dictionary<string, double>();
        foreach (Match m in IdRx.Matches(expr))
            if (!Funcs.Contains(m.Value) && h != null && h.TryGet(m.Value, out var n, out _)) vars[m.Value] = n;
        try { return Formula.Eval(expr, vars); } catch { return 0; }
    }

    public static bool Cond(string expr, IUiHost h)
    {
        if (string.IsNullOrWhiteSpace(expr)) return true;
        expr = expr.Trim();
        bool neg = expr.StartsWith("!") && !expr.StartsWith("!=");
        if (neg) return !Cond(expr[1..], h);
        foreach (var op in new[] { ">=", "<=", "!=", "==", ">", "<" })
        {
            int i = expr.IndexOf(op);
            if (i < 0) continue;
            var l = expr[..i].Trim(); var r = expr[(i + op.Length)..].Trim();
            if (op is "==" or "!=")
            {
                var ls = Str(l, h); var rs = Str(r, h);
                if (ls != null || rs != null || (!IsNum(l, h) && !IsNum(r, h)))
                {
                    bool eq = string.Equals(ls ?? l, rs ?? r, StringComparison.OrdinalIgnoreCase);
                    return op == "==" ? eq : !eq;
                }
            }
            return Compare.Op(Num(l, h), op, Num(r, h));
        }
        if (h != null && h.TryGet(expr, out var n, out var s)) return s != null ? s != "" && s != "false" && s != "0" : n != 0;
        return true;
    }
    static string Str(string tok, IUiHost h) => h != null && h.TryGet(tok, out _, out var s) ? s : null;
    static bool IsNum(string tok, IUiHost h) => double.TryParse(tok, out _) || (h != null && h.TryGet(tok, out _, out var s) && s == null);
}

/// <summary>编辑器里显示的说明：可用变量和命令。</summary>
public static class UiDocs
{
    public static readonly (string name, string desc, string sample)[] Vars =
    {
        ("pleasure", "当前欢愉（Boss 战时为 Boss 计分）", "200"),
        ("target", "当前目标", "300"),
        ("pleasure_normal", "普通累计欢愉", "200"),
        ("stage_target", "本关目标", "300"),
        ("boss_pleasure", "Boss 战计分", "0"),
        ("boss_target", "Boss 目标", "150"),
        ("boss_name", "已选 Boss 名字", "莉莉丝"),
        ("inboss", "是否 Boss 战 (0/1)", "0"),
        ("stage", "第几关", "1"),
        ("day", "第几天 (1-7，8=星期八)", "3"),
        ("dayname", "周一…周日/星期八", "周三"),
        ("dayabbr", "MON…SUN", "WED"),
        ("moves", "剩余换座次数", "2"),
        ("moves_max", "每日换座次数", "3"),
        ("dismiss", "剩余请离次数", "1"),
        ("dismiss_max", "每日请离次数", "1"),
        ("phase", "Prepare / Resolving / Recruit / BossChoice / GameOver", "Prepare"),
        ("can_start", "能否开始结算 (0/1)", "1"),
        ("roster", "成员总数", "6"),
        ("roster_max", "成员上限", "16"),
        ("hypno", "当前催眠数", "1"),
        ("hypno_max", "催眠上限", "3"),
        ("seat_view", "是否选座视角 (0/1)", "0"),
        ("has_save", "（标题）是否有存档 (0/1)", "1"),
        ("runs", "（标题）已玩局数", "3"),
    };

    public static readonly (string cmd, string desc)[] Commands =
    {
        ("", "（无）"),
        ("StartRound", "开始结算"),
        ("EnterSeat", "进入选座视角"),
        ("ExitSeat", "退出选座视角"),
        ("OpenCalendar", "打开日历"),
        ("TogglePhone", "开关手机"),
        ("OpenApp", "打开手机 APP，参数 0-3"),
        ("OpenSettings", "打开设置"),
        ("NewGame", "（标题）新游戏"),
        ("Continue", "（标题）继续"),
        ("LoadMenu", "（标题）读档界面"),
        ("OpenGallery", "（标题）画廊"),
        ("GoTitle", "回到标题"),
        ("Quit", "退出游戏"),
        ("Show", "显示对象，参数=对象名"),
        ("Hide", "隐藏对象，参数=对象名"),
        ("Toggle", "切换对象显示，参数=对象名"),
        ("Log", "输出日志，参数=文本"),
    };
}
