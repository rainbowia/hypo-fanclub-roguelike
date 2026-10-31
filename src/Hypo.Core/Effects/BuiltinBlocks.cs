using Hypo.Core.Data;
using Hypo.Core.Runtime;
using Hypo.Core.Scripting;

namespace Hypo.Core.Effects;

// =========================== 条件积木 ===========================

[Block("StatCompare", "条件", "目标的某项四维与数值比较（目标多张时：任意一张满足即可）")]
[Param("target", "target", "self")] [Param("stat", "stat", "Love")] [Param("op", "op", ">")] [Param("value", "expr", "7")]
public class StatCompareCond : ICondition
{
    public bool Check(EffectContext ctx, BlockDef b)
    {
        var st = Compare.ParseStat(b.Get("stat", "Love"));
        return ctx.Select(b.Get("target", "self")).Any(c => Compare.Op(c.GetStat(st), b.Get("op", ">"), ctx.Eval(b.Get("value", "0"))));
    }
}

[Block("TargetCount", "条件", "选择器选中的卡数量比较。例：adjacent|gender=Male  >= 1")]
[Param("target", "target", "adjacent|gender=Male")] [Param("op", "op", ">=")] [Param("value", "expr", "1")]
public class TargetCountCond : ICondition
{
    public bool Check(EffectContext ctx, BlockDef b) =>
        Compare.Op(ctx.Select(b.Get("target")).Count(), b.Get("op", ">="), ctx.Eval(b.Get("value", "1")));
}

[Block("Expr", "条件", "任意公式比较。变量见 EffectContext.Eval")]
[Param("left", "expr", "self_love")] [Param("op", "op", ">")] [Param("right", "expr", "7")]
public class ExprCond : ICondition
{
    public bool Check(EffectContext ctx, BlockDef b) => Compare.Op(ctx.Eval(b.Get("left")), b.Get("op", ">"), ctx.Eval(b.Get("right")));
}

[Block("Chance", "条件", "概率(%)")]
[Param("percent", "expr", "50")]
public class ChanceCond : ICondition
{
    public bool Check(EffectContext ctx, BlockDef b) => ctx.Chance(ctx.Eval(b.Get("percent", "50")));
}

[Block("HasTag", "条件", "目标拥有标签")]
[Param("target", "target", "self")] [Param("tag", "tag", "")] [Param("not", "bool", "false")]
public class HasTagCond : ICondition
{
    public bool Check(EffectContext ctx, BlockDef b)
    {
        bool r = ctx.Select(b.Get("target", "self")).Any(c => c.HasTag(b.Get("tag")));
        return b.GetBool("not") ? !r : r;
    }
}

[Block("InBoss", "条件", "当前是否 Boss 战")]
[Param("value", "bool", "true")]
public class InBossCond : ICondition
{
    public bool Check(EffectContext ctx, BlockDef b) => ctx.S.InBoss == b.GetBool("value", true);
}

[Block("ScriptCond", "条件", "C# 代码条件，返回 bool。例：return Self.Love > 7 && Game.Adjacent(Self).Any();")]
[Param("code", "code", "return true;")]
public class ScriptCond : ICondition
{
    public bool Check(EffectContext ctx, BlockDef b) => ScriptHost.RunBool(b.Get("code"), ctx);
}

// =========================== 动作积木 ===========================

[Block("Produce", "产出", "直接产生欢愉（独立事件产出）")]
[Param("amount", "expr", "5", "可用公式，例 self_desire*2")]
public class ProduceAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) => ctx.Produce(ctx.EvalInt(b.Get("amount", "0")));
}

[Block("AddTempOutput", "产出", "本次结算产出临时加成（只在 结算前 时机有意义）")]
[Param("amount", "expr", "5")]
public class AddTempOutputAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) => ctx.AddTempOutput(ctx.EvalInt(b.Get("amount")));
}

[Block("MultiplyOutput", "产出", "本次结算产出倍率（结算前）")]
[Param("factor", "expr", "2")]
public class MultiplyOutputAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) => ctx.MultiplyOutput(ctx.Eval(b.Get("factor", "1")));
}

[Block("AddFixedOutput", "产出", "永久提高/降低目标固定产出")]
[Param("target", "target", "self")] [Param("amount", "expr", "1")]
public class AddFixedOutputAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        foreach (var c in ctx.Select(b.Get("target", "self")).ToList()) { ctx.Target = c; c.FixedOutput += ctx.EvalInt(b.Get("amount")); }
    }
}

[Block("AddStat", "四维", "目标四维 ±")]
[Param("target", "target", "self")] [Param("stat", "stat", "Love")] [Param("amount", "expr", "1")]
public class AddStatAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        var st = Compare.ParseStat(b.Get("stat"));
        foreach (var c in ctx.Select(b.Get("target", "self")).ToList()) { ctx.Target = c; ctx.AddStat(c, st, ctx.EvalInt(b.Get("amount"))); }
    }
}

[Block("SetStat", "四维", "目标四维设为某值")]
[Param("target", "target", "self")] [Param("stat", "stat", "Love")] [Param("value", "expr", "5")]
public class SetStatAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        var st = Compare.ParseStat(b.Get("stat"));
        foreach (var c in ctx.Select(b.Get("target", "self")).ToList()) { ctx.Target = c; ctx.SetStat(c, st, ctx.EvalInt(b.Get("value"))); }
    }
}

[Block("Interact", "互动", "对目标逐个触发“互动”事件：产出公式可用 self_* 与 t_*；可选完成后转化目标")]
[Param("target", "target", "adjacent|gender=Female")]
[Param("pick", "text", "all", "all / random1 / first")]
[Param("produce", "expr", "self_desire + t_desire")]
[Param("selfStat", "stat", "Desire")] [Param("selfStatDelta", "expr", "0")]
[Param("transformInto", "card", "", "留空则不转化")] [Param("keepItems", "bool", "false")]
public class InteractAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        var list = ctx.Select(b.Get("target")).ToList();
        var pick = b.Get("pick", "all");
        if (pick == "random1" && list.Count > 1) list = new() { ctx.Rng.Pick(list) };
        else if (pick == "first" && list.Count > 1) list = list.Take(1).ToList();
        foreach (var t in list)
        {
            ctx.Target = t;
            ctx.Produce(ctx.EvalInt(b.Get("produce", "0")));
            int d = ctx.EvalInt(b.Get("selfStatDelta", "0"));
            if (d != 0 && ctx.Self != null) ctx.AddStat(ctx.Self, Compare.ParseStat(b.Get("selfStat", "Desire")), d);
            ctx.Game.Emit(new GameEvent(GameEventKind.Interact, ctx.Self, 0, "", t));
            var into = b.Get("transformInto");
            if (!string.IsNullOrEmpty(into) && t.DefId != into) ctx.Transform(t, into, b.GetBool("keepItems"));
        }
    }
}

[Block("Destroy", "生死", "标记撕毁目标（在撕毁判定时处理）")]
[Param("target", "target", "self")] [Param("immediate", "bool", "false", "立即撕毁并执行亡语")]
public class DestroyAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        foreach (var c in ctx.Select(b.Get("target", "self")).ToList())
            if (b.GetBool("immediate")) ctx.DestroyNow(c); else ctx.Destroy(c);
    }
}

[Block("Transform", "生死", "把目标转化为另一张卡（不是撕毁，不触发亡语）")]
[Param("target", "target", "self")] [Param("into", "card", "")] [Param("keepItems", "bool", "true")]
public class TransformAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        foreach (var c in ctx.Select(b.Get("target", "self")).ToList()) ctx.Transform(c, b.Get("into"), b.GetBool("keepItems", true));
    }
}

[Block("Spawn", "生死", "在槽位生成卡。slot: 数字(1-12) / self（自身槽位）/ randomEmpty")]
[Param("card", "card", "")] [Param("slot", "text", "randomEmpty")]
public class SpawnAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        var s = b.Get("slot", "randomEmpty");
        int slot = s switch
        {
            "self" => ctx.Slot >= 0 ? ctx.Slot : ctx.Self?.Slot ?? -1,
            "randomEmpty" => ctx.Game.RandomEmptySlot(),
            _ => int.TryParse(s, out var n) ? n - 1 : -1
        };
        if (slot >= 0 && ctx.S.AtSlot(slot) == null) ctx.Spawn(b.Get("card"), slot);
    }
}

[Block("AddTag", "标签", "给目标加/去标签")]
[Param("target", "target", "self")] [Param("tag", "tag", "")] [Param("remove", "bool", "false")]
public class AddTagAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        foreach (var c in ctx.Select(b.Get("target", "self")))
            if (b.GetBool("remove")) c.ExtraTags.Remove(b.Get("tag")); else if (!c.ExtraTags.Contains(b.Get("tag"))) c.ExtraTags.Add(b.Get("tag"));
    }
}

[Block("SetVar", "变量", "设置变量。scope: self(卡变量) / global / local")]
[Param("scope", "text", "self")] [Param("name", "text", "counter")] [Param("value", "expr", "v_counter + 1")]
public class SetVarAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b)
    {
        // 公式里卡变量写作 self_v_名字；为了方便，这里把 v_ 前缀替换掉
        var expr = b.Get("value").Replace("v_", "self_v_").Replace("self_self_v_", "self_v_");
        double v = ctx.Eval(expr);
        switch (b.Get("scope", "self"))
        {
            case "global": ctx.SetGVar(b.Get("name"), v); break;
            case "local": ctx.Locals[b.Get("name")] = v; break;
            default: ctx.SetVar(b.Get("name"), v); break;
        }
    }
}

[Block("GiveItem", "道具", "获得道具（进入库存）")]
[Param("item", "item", "")]
public class GiveItemAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) => ctx.S.ItemStock.Add(b.Get("item"));
}

[Block("ModifyMoves", "规则", "增减今日选座/请离次数")]
[Param("moves", "expr", "1")] [Param("dismiss", "expr", "0")]
public class ModifyMovesAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) { ctx.S.MovesLeft += ctx.EvalInt(b.Get("moves")); ctx.S.DismissLeft += ctx.EvalInt(b.Get("dismiss")); }
}

[Block("UnlockGallery", "画廊", "解锁画廊条目")]
[Param("id", "gallery", "")]
public class UnlockGalleryAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) => ctx.Unlock(b.Get("id"));
}

[Block("Log", "调试", "向结算日志输出文本")]
[Param("text", "text", "触发了！")]
public class LogAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) => ctx.Log(b.Get("text"));
}

[Block("Script", "代码", "C# 代码块，可访问 ctx / Self / Game / S / Db")]
[Param("code", "code", "// ctx.Produce(Self.Desire * 2);")]
public class ScriptAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) => ScriptHost.Run(b.Get("code"), ctx);
}

[Block("StopIf", "流程", "公式成立则中断本条效果剩余动作")]
[Param("left", "expr", "self_sanity")] [Param("op", "op", "<")] [Param("right", "expr", "3")]
public class StopIfAct : IAction
{
    public void Run(EffectContext ctx, BlockDef b) { if (Compare.Op(ctx.Eval(b.Get("left")), b.Get("op"), ctx.Eval(b.Get("right")))) ctx.Cancel = true; }
}
