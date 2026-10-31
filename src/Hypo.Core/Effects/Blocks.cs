using System.Reflection;
using Hypo.Core.Data;
using Hypo.Core.Runtime;

namespace Hypo.Core.Effects;

// ======================================================================
//  模块化积木系统
//  新增一种积木 = 写一个类，打上 [Block] 和 [Param]，实现 ICondition 或 IAction。
//  编辑器通过反射自动列出所有积木和参数，不需要改编辑器代码。
//  也可以把积木类编译成 dll 丢进 mods/ 目录（Blocks.LoadPlugins）。
// ======================================================================

[AttributeUsage(AttributeTargets.Class)]
public class BlockAttribute : Attribute
{
    public string Type, Category, Desc;
    public BlockAttribute(string type, string category, string desc) { Type = type; Category = category; Desc = desc; }
}

/// <summary>参数声明。Kind: int / expr / stat / target / text / bool / card / item / tag / gender / op / gallery / code</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ParamAttribute : Attribute
{
    public string Name, Kind, Default, Desc;
    public ParamAttribute(string name, string kind, string def = "", string desc = "") { Name = name; Kind = kind; Default = def; Desc = desc; }
}

public interface ICondition { bool Check(EffectContext ctx, BlockDef b); }
public interface IAction { void Run(EffectContext ctx, BlockDef b); }

public class BlockInfo
{
    public string Type, Category, Desc; public bool IsCondition; public Type Impl;
    public List<ParamAttribute> Params = new();
    public BlockDef CreateDefault()
    {
        var b = new BlockDef { Type = Type };
        foreach (var p in Params) b.Args[p.Name] = p.Default;
        return b;
    }
}

public static class Blocks
{
    static readonly Dictionary<string, BlockInfo> _all = new();
    static readonly Dictionary<string, object> _inst = new();
    public static IEnumerable<BlockInfo> All { get { Ensure(); return _all.Values; } }
    public static IEnumerable<BlockInfo> Conditions => All.Where(b => b.IsCondition);
    public static IEnumerable<BlockInfo> Actions => All.Where(b => !b.IsCondition);
    public static BlockInfo Info(string t) { Ensure(); return _all.GetValueOrDefault(t); }

    static bool _ready;
    static void Ensure() { if (_ready) return; _ready = true; Register(typeof(Blocks).Assembly); }

    public static void Register(Assembly asm)
    {
        foreach (var t in asm.GetTypes())
        {
            var a = t.GetCustomAttribute<BlockAttribute>();
            if (a == null || t.IsAbstract) continue;
            var info = new BlockInfo
            {
                Type = a.Type, Category = a.Category, Desc = a.Desc, Impl = t,
                IsCondition = typeof(ICondition).IsAssignableFrom(t),
                Params = t.GetCustomAttributes<ParamAttribute>().ToList()
            };
            _all[a.Type] = info;
            _inst[a.Type] = Activator.CreateInstance(t);
        }
    }

    /// <summary>加载 mods/*.dll 里的自定义积木。</summary>
    public static void LoadPlugins(string dir)
    {
        Ensure();
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*.dll"))
            try { Register(Assembly.LoadFrom(f)); Log.Info("已加载积木插件 " + Path.GetFileName(f)); }
            catch (Exception e) { Log.Warn("插件加载失败 " + f + ": " + e.Message); }
    }

    public static bool Check(EffectContext ctx, BlockDef b)
    {
        Ensure();
        if (_inst.GetValueOrDefault(b.Type) is ICondition c) return c.Check(ctx, b);
        Log.Warn("未知条件积木 " + b.Type); return false;
    }
    public static void Run(EffectContext ctx, BlockDef b)
    {
        Ensure();
        if (_inst.GetValueOrDefault(b.Type) is IAction a) a.Run(ctx, b);
        else Log.Warn("未知动作积木 " + b.Type);
    }
}

/// <summary>
/// 目标选择器语法：  基础[:数量] | 过滤 | 过滤 ...
///   基础：self target left right adjacent all others board roster slot:N random:N empty（空槽无卡，返回空）
///   过滤：gender=Female  tag=xxx  !tag=xxx  def=cardId  stat:love>7  stat:sanity<=2  notself
///   例：adjacent|gender=Male     all|!tag=cultist|notself     random:1|others
/// </summary>
public static class Targets
{
    public static IEnumerable<CardInstance> Select(EffectContext ctx, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) selector = "self";
        var parts = selector.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var head = parts[0].Split(':');
        var g = ctx.Game; var self = ctx.Self;
        IEnumerable<CardInstance> set = head[0] switch
        {
            "self" => self != null ? new[] { self } : Array.Empty<CardInstance>(),
            "target" => ctx.Target != null ? new[] { ctx.Target } : Array.Empty<CardInstance>(),
            "left" => Single(g.Neighbor(self, -1)),
            "right" => Single(g.Neighbor(self, +1)),
            "adjacent" => g.Adjacent(self),
            "all" or "board" => ctx.S.Board,
            "others" => ctx.S.Board.Where(c => c != self),
            "roster" => ctx.S.Roster,
            "slot" => Single(ctx.S.AtSlot(int.Parse(head[1]) - 1)),
            "random" => ctx.S.Board,
            _ => Array.Empty<CardInstance>()
        };
        var list = set.Where(c => c != null).ToList();
        foreach (var f in parts.Skip(1)) list = list.Where(c => Pass(c, f, self)).ToList();
        if (head[0] == "random")
        {
            int n = head.Length > 1 ? int.Parse(head[1]) : 1;
            ctx.Rng.Shuffle(list);
            list = list.Take(n).ToList();
        }
        return list;
    }

    static IEnumerable<CardInstance> Single(CardInstance c) => c == null ? Array.Empty<CardInstance>() : new[] { c };

    static bool Pass(CardInstance c, string f, CardInstance self)
    {
        if (f == "notself") return c != self;
        bool neg = f.StartsWith("!"); if (neg) f = f[1..];
        bool r;
        if (f.StartsWith("gender=")) r = c.Gender.ToString().Equals(f[7..], StringComparison.OrdinalIgnoreCase);
        else if (f.StartsWith("tag=")) r = c.HasTag(f[4..]);
        else if (f.StartsWith("def=")) r = c.DefId == f[4..];
        else if (f.StartsWith("stat:")) r = StatFilter(c, f[5..]);
        else r = true;
        return neg ? !r : r;
    }
    static bool StatFilter(CardInstance c, string e)
    {
        foreach (var op in new[] { ">=", "<=", "!=", "==", ">", "<", "=" })
        {
            int i = e.IndexOf(op); if (i < 0) continue;
            var stat = Enum.Parse<Stat>(e[..i], true); int v = int.Parse(e[(i + op.Length)..]);
            return Compare.Op(c.GetStat(stat), op, v);
        }
        return true;
    }
}

public static class Compare
{
    public static bool Op(double a, string op, double b) => op switch
    {
        ">" => a > b, ">=" => a >= b, "<" => a < b, "<=" => a <= b,
        "==" or "=" => Math.Abs(a - b) < 1e-9, "!=" => Math.Abs(a - b) >= 1e-9, _ => false
    };
    public static Stat ParseStat(string s) => s.ToLowerInvariant() switch
    {
        "理智" or "sanity" => Stat.Sanity, "欲望" or "desire" => Stat.Desire,
        "爱情" or "love" => Stat.Love, "躯体" or "body" => Stat.Body, _ => Enum.Parse<Stat>(s, true)
    };
}
