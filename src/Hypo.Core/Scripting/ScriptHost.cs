using System.Collections.Concurrent;
using Hypo.Core.Data;
using Hypo.Core.Effects;
using Hypo.Core.Runtime;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Hypo.Core.Scripting;

/// <summary>代码块可直接访问的全局变量。</summary>
public class ScriptGlobals
{
    public EffectContext ctx;
    public CardInstance Self => ctx.Self;
    public CardInstance Target => ctx.Target;
    public GameRunner Game => ctx.Game;
    public GameState S => ctx.S;
    public ContentDb Db => ctx.Db;
    public RuleRng Rng => ctx.Rng;
    // 四维快捷写法：Stat.Love 也可
    public const Stat Sanity = Stat.Sanity, Desire = Stat.Desire, Love = Stat.Love, Body = Stat.Body;
}

/// <summary>
/// Roslyn 脚本宿主。同一段代码只编译一次（缓存）。
/// 首次编译约几百毫秒；发布前可调用 Precompile 预热全部卡牌脚本。
/// 注意：脚本是完整 C#，能做任何事 —— 只加载你信任的卡包。
/// </summary>
public static class ScriptHost
{
    static readonly ConcurrentDictionary<string, ScriptRunner<object>> _cache = new();
    static readonly ScriptOptions _opt = ScriptOptions.Default
        .AddReferences(typeof(ScriptHost).Assembly)
        .AddImports("System", "System.Linq", "System.Collections.Generic",
                    "Hypo.Core.Data", "Hypo.Core.Runtime", "Hypo.Core.Effects");

    public static ScriptRunner<object> Compile(string code)
    {
        return _cache.GetOrAdd(code, c =>
        {
            var s = CSharpScript.Create<object>(c, _opt, typeof(ScriptGlobals));
            var diags = s.Compile().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
            if (diags.Count > 0) throw new CompilationErrorException(string.Join("\n", diags), diags.ToImmutableArray());
            return s.CreateDelegate();
        });
    }

    /// <summary>编辑器用：返回错误文本（null = 编译通过）。</summary>
    public static string Validate(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try { _cache.TryRemove(code, out _); Compile(code); return null; }
        catch (CompilationErrorException e) { return e.Message; }
        catch (Exception e) { return e.Message; }
    }

    public static object Run(string code, EffectContext ctx)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try { return Compile(code)(new ScriptGlobals { ctx = ctx }).GetAwaiter().GetResult(); }
        catch (Exception e) { Log.Warn($"[脚本 {ctx.SourceId}] {e.Message}"); return null; }
    }
    public static bool RunBool(string code, EffectContext ctx) => Run(code, ctx) is bool b && b;

    public static void Precompile(ContentDb db)
    {
        IEnumerable<EffectDef> all = db.Cards.Values.SelectMany(c => c.Effects)
            .Concat(db.Items.Values.SelectMany(i => i.Effects))
            .Concat(db.Bosses.Values.SelectMany(b => b.Effects))
            .Concat(db.Hypnos.Values.SelectMany(h => h.Effects))
            .Concat(db.Events.Values.SelectMany(e => e.Effects));
        foreach (var e in all)
        {
            if (!string.IsNullOrWhiteSpace(e.Script)) Validate(e.Script);
            foreach (var b in e.Actions.Concat(e.Conditions))
                if (b.Args.TryGetValue("code", out var c)) Validate(c);
        }
    }
}

static class ImmutableExt
{
    public static System.Collections.Immutable.ImmutableArray<T> ToImmutableArray<T>(this IEnumerable<T> e) =>
        System.Collections.Immutable.ImmutableArray.CreateRange(e);
}
