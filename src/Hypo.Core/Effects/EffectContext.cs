using Hypo.Core.Data;
using Hypo.Core.Runtime;
using Hypo.Core.Util;

namespace Hypo.Core.Effects;

/// <summary>
/// 效果执行上下文。积木和 C# 代码块都通过它操作游戏。
/// 代码块里直接写 ctx.xxx 或简写 Self / Game / S（见 ScriptGlobals）。
/// </summary>
public class EffectContext
{
    public GameRunner Game { get; init; }
    public GameState S => Game.State;
    public ContentDb Db => Game.Db;
    /// <summary>效果拥有者（卡牌效果 = 这张卡；Boss 的 Any* 触发 = 事件相关卡；否则 null）。</summary>
    public CardInstance Self { get; set; }
    /// <summary>当前积木选中的目标（Interact 等积木会逐个设置）。</summary>
    public CardInstance Target { get; set; }
    public int Slot { get; set; } = -1;
    public Trigger Trigger { get; init; }
    public EffectDef Effect { get; init; }
    public string SourceKind { get; init; } = "card";   // card / item / hypno / boss / event
    public string SourceId { get; init; } = "";
    /// <summary>设为 true 中断本条效果剩余动作。</summary>
    public bool Cancel { get; set; }
    /// <summary>本条效果内的临时变量。</summary>
    public Dictionary<string, double> Locals { get; } = new();

    public RuleRng Rng => Game.Rng;

    // ======== 常用 API（积木和代码块都用这些） ========
    public void Produce(int amount, CardInstance from = null) => Game.AddPleasure(amount, from ?? Self, SourceKind + ":" + SourceId);
    public void AddTempOutput(int amount) { if (Self != null) Self.TempOutput += amount; }
    public void MultiplyOutput(double f) { if (Self != null) Self.OutputMultiplier *= f; }
    public void AddStat(CardInstance c, Stat s, int d) => Game.ChangeStat(c, s, d);
    public void SetStat(CardInstance c, Stat s, int v) => Game.ChangeStat(c, s, v - c.GetStat(s));
    public void Destroy(CardInstance c) { if (c != null) c.MarkedDestroy = true; }
    public void DestroyNow(CardInstance c) => Game.DestroyCard(c, this);
    public CardInstance Transform(CardInstance c, string defId, bool keepItems) => Game.TransformCard(c, defId, keepItems);
    public CardInstance Spawn(string defId, int slot) => Game.SpawnCard(defId, slot);
    public void Log(string msg) => Game.Emit(new GameEvent(GameEventKind.Log, Self, 0, Db.T(msg)));
    public void Unlock(string galleryId) => Game.Profile?.Unlock(galleryId);

    public IEnumerable<CardInstance> Adjacent(CardInstance c) => Game.Adjacent(c);
    public IEnumerable<CardInstance> Select(string selector) => Targets.Select(this, selector);
    public bool Chance(double percent) => Rng.NextDouble() * 100 < percent;

    public double Var(string name) => Self != null && Self.Vars.TryGetValue(name, out var v) ? v : 0;
    public void SetVar(string name, double v) { if (Self != null) Self.Vars[name] = v; }
    public double GVar(string name) => S.Vars.TryGetValue(name, out var v) ? v : 0;
    public void SetGVar(string name, double v) => S.Vars[name] = v;

    /// <summary>为公式提供变量：self_sanity/self_desire/self_love/self_body/self_output/self_slot，
    /// t_* 为当前 Target，另有 day stage pleasure boss_pleasure，以及卡变量 v_名字、全局变量 g_名字。</summary>
    public double Eval(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0;
        if (double.TryParse(expr, out var n)) return n;
        var d = new Dictionary<string, double>
        {
            ["day"] = S.Day, ["stage"] = S.Stage, ["pleasure"] = S.Pleasure, ["boss_pleasure"] = S.BossPleasure,
            ["board_count"] = S.Board.Count(),
        };
        void Put(string p, CardInstance c)
        {
            if (c == null) return;
            d[p + "sanity"] = c.Sanity; d[p + "desire"] = c.Desire; d[p + "love"] = c.Love; d[p + "body"] = c.Body;
            d[p + "output"] = c.FixedOutput; d[p + "slot"] = c.Slot + 1;
            foreach (var kv in c.Vars) d[p + "v_" + kv.Key] = kv.Value;
        }
        Put("self_", Self); Put("t_", Target);
        foreach (var kv in S.Vars) d["g_" + kv.Key] = kv.Value;
        foreach (var kv in Locals) d[kv.Key] = kv.Value;
        try { return Formula.Eval(expr, d); }
        catch (Exception e) { Data.Log.Warn($"[{SourceId}] 公式 '{expr}' 错误: {e.Message}"); return 0; }
    }
    public int EvalInt(string expr) => (int)Math.Round(Eval(expr));
}

/// <summary>可复现的随机数（存档记录 Seed + 调用次数）。</summary>
public class RuleRng
{
    Random _r; readonly GameState _s;
    public RuleRng(GameState s) { _s = s; _r = new Random(s.Seed); for (int i = 0; i < s.RngCalls; i++) _r.Next(); }
    public int Next(int max) { _s.RngCalls++; return _r.Next(max); }
    public int Range(int min, int maxInclusive) { _s.RngCalls++; return _r.Next(min, maxInclusive + 1); }
    public double NextDouble() { _s.RngCalls++; return _r.Next() / (double)int.MaxValue; }
    public T Pick<T>(IList<T> list) => list.Count == 0 ? default : list[Next(list.Count)];
    public T Weighted<T>(IList<T> list, Func<T, int> w)
    {
        int total = list.Sum(w); if (total <= 0) return Pick(list);
        int r = Next(total);
        foreach (var x in list) { r -= w(x); if (r < 0) return x; }
        return list[^1];
    }
    public void Shuffle<T>(IList<T> l) { for (int i = l.Count - 1; i > 0; i--) { int j = Next(i + 1); (l[i], l[j]) = (l[j], l[i]); } }
}
