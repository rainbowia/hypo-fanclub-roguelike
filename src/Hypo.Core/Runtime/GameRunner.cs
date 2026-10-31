using Hypo.Core.Data;
using Hypo.Core.Effects;
using Hypo.Core.Persistence;
using Hypo.Core.Scripting;
using Hypo.Core.Util;

namespace Hypo.Core.Runtime;

public enum GameEventKind { Log, Pointer, Pleasure, StatChanged, Destroyed, Dismissed, Transformed, Spawned, Moved, Interact, RoundStart, RoundEnd, DayAdvanced, BossStart, GameOver, Victory, Recruited, Hypno }

public record GameEvent(GameEventKind Kind, CardInstance Card, int Value = 0, string Text = "", CardInstance Other = null);

/// <summary>
/// 游戏流程 + 结算器。UI 只调用公开方法、订阅 OnEvent，不直接改 State。
/// 结算是可步进的：ResolveRound() 每处理完一个槽 yield 一次，UI 自己控制节奏/动画。
/// </summary>
public class GameRunner
{
    public ContentDb Db { get; }
    public GameState State { get; private set; }
    public RulesDef R => Db.Rules;
    public RuleRng Rng { get; private set; }
    public Profile Profile { get; }
    public event Action<GameEvent> OnEvent;
    public List<string> ResolveLog { get; } = new();
    public int Pointer { get; private set; } = -1;

    public GameRunner(ContentDb db, Profile profile) { Db = db; Profile = profile; }

    public void Emit(GameEvent e)
    {
        if (e.Kind == GameEventKind.Log) ResolveLog.Add(e.Text);
        OnEvent?.Invoke(e);
    }
    void L(string s) => Emit(new GameEvent(GameEventKind.Log, null, 0, s));

    // ======================== 开局 / 读档 ========================
    public void NewGame(int? seed = null)
    {
        State = new GameState { Seed = seed ?? Environment.TickCount };
        Rng = new RuleRng(State);
        foreach (var id in R.StartingDeck) CreateCard(id, -1);
        State.ItemStock.AddRange(R.StartingItems);
        // 初始随机入座
        foreach (var c in State.Roster.ToList()) { int s = RandomEmptySlot(); if (s >= 0) { c.Slot = s; } }
        BeginStage();
    }

    public void Load(GameState s)
    {
        State = s; Rng = new RuleRng(s);
        foreach (var c in s.Cards) c.Def = Db.Cards.GetValueOrDefault(c.DefId) ?? new CardDef { Id = c.DefId, Name = c.DefId };
    }

    void BeginStage()
    {
        State.Day = 0; State.BossId = ""; State.BossOffer.Clear(); State.BossRoundsDone = 0;
        State.StageTarget = Formula.EvalCeil(R.StageTargetFormula, new Dictionary<string, double> { ["stage"] = State.Stage });
        if (!R.PleasureCarriesOver) State.Pleasure = 0;
        State.DayEvents.Clear();
        if (Db.Events.Count > 0)
            for (int d = 0; d < R.DaysPerStage; d++)
                if (Rng.Next(100) < 30) State.DayEvents[d] = Rng.Weighted(Db.Events.Values.ToList(), e => e.Weight).Id;
        BeginDay();
    }

    void BeginDay()
    {
        if (R.MovesRefreshDaily || State.Day == 0) State.MovesLeft = R.MovesPerDay;
        State.DismissLeft = R.DismissPerDay;
        State.Phase = Phase.Prepare;
        State.History.Add(new DayRecord { Stage = State.Stage, Day = State.Day, EventId = State.DayEvents.GetValueOrDefault(State.Day, "") });
        Emit(new GameEvent(GameEventKind.DayAdvanced, null, State.Day));
    }

    // ======================== 卡牌工厂 ========================
    public CardInstance CreateCard(string defId, int slot)
    {
        var def = Db.Cards.GetValueOrDefault(defId);
        if (def == null) { Log.Warn("不存在的卡 " + defId); def = new CardDef { Id = defId, Name = defId }; }
        var c = new CardInstance
        {
            Uid = State.NextUid++, DefId = defId, Def = def, Slot = slot,
            Stats = (int[])def.Stats.Clone(), FixedOutput = def.BaseOutput,
        };
        foreach (var kv in def.Vars) if (double.TryParse(kv.Value, out var v)) c.Vars[kv.Key] = v;
        State.Cards.Add(c);
        return c;
    }

    public int RandomEmptySlot()
    {
        var empty = Enumerable.Range(0, R.SlotCount).Where(i => State.AtSlot(i) == null).ToList();
        return empty.Count == 0 ? -1 : Rng.Pick(empty);
    }

    public CardInstance Neighbor(CardInstance c, int dir)
    {
        if (c == null || c.Slot < 0) return null;
        int s = c.Slot + dir;
        if (s < 0 || s >= R.SlotCount) { if (!R.AdjacentWraps) return null; s = (s + R.SlotCount) % R.SlotCount; }
        var n = State.AtSlot(s);
        return n == c ? null : n;
    }
    public IEnumerable<CardInstance> Adjacent(CardInstance c)
    {
        var l = Neighbor(c, -1); var r = Neighbor(c, +1);
        if (l != null) yield return l;
        if (r != null && r != l) yield return r;
    }

    // ======================== 效果分发 ========================
    IEnumerable<(EffectDef e, string kind, string id)> EffectsOf(CardInstance c)
    {
        if (c.Def != null) foreach (var e in c.Def.Effects) yield return (e, "card", c.DefId);
        foreach (var it in c.Items) if (Db.Items.TryGetValue(it, out var idef)) foreach (var e in idef.Effects) yield return (e, "item", it);
        foreach (var h in c.Hypnos) if (Db.Hypnos.TryGetValue(h.DefId, out var hd)) foreach (var e in hd.Effects) yield return (e, "hypno", h.DefId);
    }

    public void FireCard(CardInstance c, Trigger t)
    {
        if (c == null) return;
        foreach (var (e, kind, id) in EffectsOf(c).ToList())
            if (e.Trigger == t) RunEffect(e, c, kind, id, t, c.Slot);
    }

    /// <summary>Boss 与当日事件的全局效果。</summary>
    public void FireGlobal(Trigger t, CardInstance about = null, int slot = -1)
    {
        if (State.InBoss && Db.Bosses.TryGetValue(State.BossId, out var boss))
            foreach (var e in boss.Effects.Where(e => e.Trigger == t).ToList()) RunEffect(e, about, "boss", boss.Id, t, slot);
        if (!State.InBoss && State.DayEvents.TryGetValue(State.Day, out var evId) && Db.Events.TryGetValue(evId, out var ev))
            foreach (var e in ev.Effects.Where(e => e.Trigger == t).ToList()) RunEffect(e, about, "event", ev.Id, t, slot);
    }

    public void RunEffect(EffectDef e, CardInstance self, string kind, string id, Trigger t, int slot)
    {
        if (e.OncePerRound && self != null)
        {
            var key = kind + id + e.Id;
            if (self.FiredThisRound.Contains(key)) return;
        }
        var ctx = new EffectContext { Game = this, Self = self, Trigger = t, Effect = e, SourceKind = kind, SourceId = id, Slot = slot };
        foreach (var cond in e.Conditions) if (!Blocks.Check(ctx, cond)) return;
        if (e.OncePerRound && self != null) self.FiredThisRound.Add(kind + id + e.Id);
        foreach (var a in e.Actions)
        {
            if (ctx.Cancel) return;
            ctx.Target = null;
            Blocks.Run(ctx, a);
        }
        if (!ctx.Cancel && !string.IsNullOrWhiteSpace(e.Script)) ScriptHost.Run(e.Script, ctx);
    }

    // ======================== 原子操作（带事件） ========================
    public void AddPleasure(int amount, CardInstance from, string src)
    {
        if (amount == 0) return;
        if (State.InBoss) State.BossPleasure += amount; else State.Pleasure += amount;
        var rec = State.Today; if (rec != null) rec.Pleasure += amount;
        Emit(new GameEvent(GameEventKind.Pleasure, from, amount, src));
    }

    public void ChangeStat(CardInstance c, Stat s, int d)
    {
        if (c == null || d == 0) return;
        c.AddStat(s, d);   // 不在此处判死，等撕毁判定
        Emit(new GameEvent(GameEventKind.StatChanged, c, d, s.ToString()));
    }

    public bool IsDying(CardInstance c) =>
        c.MarkedDestroy || c.Stats.Any(v => v <= R.StatMin || v >= R.StatMax);

    /// <summary>撕毁：亡语 → 离场 → 记录/画廊 → 全局 AnyCardDestroyed（Boss 替换等）。</summary>
    public void DestroyCard(CardInstance c, EffectContext by = null)
    {
        if (c == null || !State.Cards.Contains(c)) return;
        int slot = c.Slot;
        c.MarkedDestroy = false;
        FireCard(c, Trigger.OnDestroyed);            // 亡语
        State.Cards.Remove(c);
        State.Today?.Lost.Add(c.DefId);
        Profile?.Fire("destroy:" + c.DefId, Db);
        Emit(new GameEvent(GameEventKind.Destroyed, c, slot));
        L($"✖ {c.Name(Db)} 被撕毁");
        c.Slot = slot;                                // 让全局效果拿到原槽位
        FireGlobal(Trigger.AnyCardDestroyed, c, slot);
    }

    public CardInstance TransformCard(CardInstance c, string defId, bool keepItems)
    {
        if (c == null || !Db.Cards.ContainsKey(defId)) return null;
        int slot = c.Slot; var items = c.Items.ToList();
        State.Cards.Remove(c);
        var n = CreateCard(defId, slot);
        if (keepItems) n.Items.AddRange(items); else State.ItemStock.AddRange(items);
        // 转化后的卡本圈视为已行动过（避免原地新卡再次结算）由结算循环控制
        Emit(new GameEvent(GameEventKind.Transformed, n, slot, "", c));
        L($"↻ {c.Name(Db)} → {n.Name(Db)}");
        return n;
    }

    public CardInstance SpawnCard(string defId, int slot)
    {
        var n = CreateCard(defId, slot);
        Emit(new GameEvent(GameEventKind.Spawned, n, slot));
        FireCard(n, Trigger.OnEnterBoard);
        return n;
    }

    // ======================== 结算 ========================
    public bool CanStartRound => State.Phase == Phase.Prepare;

    /// <summary>
    /// 一整圈结算，可步进。每处理完一个槽 yield 一次（返回槽号）。
    /// 规则：指针 0→11 只访问一次；卡被移动到尚未访问的槽会再次结算；
    /// 某槽撕毁后原地生成/转化的新卡不在本槽再次结算。
    /// </summary>
    public IEnumerable<int> ResolveRound()
    {
        State.Phase = Phase.Resolving;
        ResolveLog.Clear();
        foreach (var c in State.Cards) c.FiredThisRound.Clear();
        Emit(new GameEvent(GameEventKind.RoundStart, null));
        FireGlobal(Trigger.OnRoundStart);
        foreach (var c in State.Board.ToList()) FireCard(c, Trigger.OnRoundStart);
        DestroyCheck();

        for (int slot = 0; slot < R.SlotCount; slot++)
        {
            Pointer = slot;
            Emit(new GameEvent(GameEventKind.Pointer, null, slot));
            var c = State.AtSlot(slot);
            if (c != null) ResolveCard(c);
            yield return slot;
        }
        Pointer = -1;
        foreach (var c in State.Board.ToList()) FireCard(c, Trigger.OnRoundEnd);
        FireGlobal(Trigger.OnRoundEnd);
        DestroyCheck();
        EndRound();
        yield return -1;
    }

    /// <summary>不需要动画时一次性跑完。</summary>
    public void ResolveRoundInstant() { foreach (var _ in ResolveRound()) { } }

    void ResolveCard(CardInstance c)
    {
        c.TempOutput = 0; c.OutputMultiplier = 1;
        L($"▶ [{c.Slot + 1}] {c.Name(Db)}");
        FireCard(c, Trigger.BeforeResolve);
        if (!State.Cards.Contains(c)) return;
        FireGlobal(Trigger.AnyCardResolving, c, c.Slot);
        if (!State.Cards.Contains(c)) return;
        int output = (int)Math.Round((c.FixedOutput + c.TempOutput) * c.OutputMultiplier);
        AddPleasure(output, c, "base");
        FireCard(c, Trigger.OnResolve);
        FireCard(c, Trigger.AfterResolve);
        DestroyCheck();
    }

    /// <summary>撕毁判定：检查全场（GDD 待定项，可改为只查当前卡）。</summary>
    public void DestroyCheck()
    {
        for (int guard = 0; guard < 64; guard++)  // 亡语可能引发连锁
        {
            var dying = State.Board.Where(IsDying).ToList();
            if (dying.Count == 0) return;
            foreach (var d in dying) DestroyCard(d);
        }
        Log.Warn("撕毁连锁超过 64 次，已中止（检查是否存在替换死循环）");
    }

    void EndRound()
    {
        Emit(new GameEvent(GameEventKind.RoundEnd, null));
        foreach (var c in State.Cards)
            foreach (var h in c.Hypnos.ToList())
                if (h.DaysLeft > 0 && --h.DaysLeft == 0) c.Hypnos.Remove(h);

        if (State.InBoss)
        {
            State.BossRoundsDone++;
            var boss = Db.Bosses.GetValueOrDefault(State.BossId);
            bool survivors = string.IsNullOrEmpty(boss?.SurvivorMustLackTag)
                ? State.Board.Any()
                : State.Board.Any(c => !c.HasTag(boss.SurvivorMustLackTag));
            if (!survivors) { Defeat("defeat:boss:" + State.BossId); return; }
            if (State.BossRoundsDone < (boss?.Rounds ?? 1)) { State.Phase = Phase.Prepare; State.MovesLeft = R.MovesPerDay; return; }
            if (State.BossPleasure >= State.BossTarget)
            {
                Profile?.Fire("victory:boss:" + State.BossId, Db);
                MarkDayDone();
                State.Stage++;
                Emit(new GameEvent(GameEventKind.Victory, null, State.Stage));
                BeginStage();
            }
            else Defeat("defeat:boss:" + State.BossId);
            return;
        }
        RollRecruit();
    }

    void Defeat(string key)
    {
        State.Phase = Phase.GameOver; State.LastDefeatKey = key;
        Profile?.Fire(key, Db);
        Emit(new GameEvent(GameEventKind.GameOver, null, 0, key));
    }

    void MarkDayDone() { var t = State.Today; if (t != null) t.Done = true; }

    // ======================== 招募 ========================
    void RollRecruit()
    {
        State.Phase = Phase.Recruit;
        var pool = Db.Cards.Values.Where(c => c.Recruitable).ToList();
        int n = Rng.Range(R.RecruitMin, R.RecruitMax);
        State.RecruitOffer = Enumerable.Range(0, Math.Min(n, pool.Count)).Select(_ => Rng.Weighted(pool, c => c.RecruitWeight).Id).ToList();
        if (State.RecruitOffer.Count == 0) Recruit(null);
    }

    /// <summary>选择一名候选（null = 放弃）。新成员随机入座空位，没空位则进联系人。</summary>
    public void Recruit(string defId)
    {
        if (State.Phase != Phase.Recruit) return;
        if (defId != null)
        {
            var c = CreateCard(defId, RandomEmptySlot());
            State.Today?.Gained.Add(defId);
            Emit(new GameEvent(GameEventKind.Recruited, c));
            if (c.OnBoard) FireCard(c, Trigger.OnEnterBoard);
        }
        State.RecruitOffer.Clear();
        AfterRecruit();
    }

    public bool OverCapacity => State.Cards.Count > R.RosterCapacity;

    void AfterRecruit()
    {
        if (State.Day == R.BossChoiceDay && string.IsNullOrEmpty(State.BossId) && State.BossOffer.Count == 0)
        {
            var pool = Db.Bosses.Values.Where(b => b.MinStage <= State.Stage).ToList();
            if (pool.Count > 0)
            {
                var a = Rng.Weighted(pool, b => b.Weight); pool.Remove(a);
                State.BossOffer.Add(a.Id);
                if (pool.Count > 0) State.BossOffer.Add(Rng.Weighted(pool, b => b.Weight).Id);
                State.Phase = Phase.BossChoice;
                return;
            }
        }
        NextDay();
    }

    public void ChooseBoss(string id)
    {
        if (State.Phase != Phase.BossChoice || !State.BossOffer.Contains(id)) return;
        State.BossId = id; State.BossOffer.Clear();
        NextDay();
    }

    void NextDay()
    {
        MarkDayDone();
        State.Day++;
        if (State.Day >= R.DaysPerStage)
        {
            if (State.Pleasure < State.StageTarget) { Defeat("defeat:normal"); return; }
            if (string.IsNullOrEmpty(State.BossId)) { State.Stage++; BeginStage(); return; }
            StartBoss();
            return;
        }
        BeginDay();
    }

    void StartBoss()
    {
        var boss = Db.Bosses[State.BossId];
        State.Day = 7; State.BossPleasure = 0; State.BossRoundsDone = 0;
        State.BossTarget = Formula.EvalCeil(boss.TargetFormula, new Dictionary<string, double> { ["stage"] = State.Stage });
        BeginDay();
        Emit(new GameEvent(GameEventKind.BossStart, null, 0, boss.Id));
        FireGlobal(Trigger.BattleStart);
        DestroyCheck();
    }

    // ======================== 选座操作 ========================
    public bool CanEdit => State.Phase == Phase.Prepare;

    /// <summary>移动到槽位（目标有人则交换）。slot = -1 表示取回联系人。每次消耗 1 次。</summary>
    public bool Move(CardInstance c, int slot)
    {
        if (!CanEdit || c == null || State.MovesLeft <= 0 || c.Slot == slot) return false;
        var other = slot >= 0 ? State.AtSlot(slot) : null;
        int from = c.Slot;
        c.Slot = slot;
        if (other != null) other.Slot = from;
        if (slot < 0) UnequipAll(c);
        if (other != null && from < 0) UnequipAll(other);
        State.MovesLeft--;
        Emit(new GameEvent(GameEventKind.Moved, c, slot, "", other));
        if (slot >= 0) FireCard(c, from < 0 ? Trigger.OnEnterBoard : Trigger.OnMoved);
        return true;
    }

    /// <summary>请离（不是撕毁，不触发亡语）。超出背包容量时免费。</summary>
    public bool Dismiss(CardInstance c)
    {
        if (c == null) return false;
        bool free = OverCapacity;
        if (!free && (!CanEdit || State.DismissLeft <= 0)) return false;
        if (!free) State.DismissLeft--;
        FireCard(c, Trigger.OnDismissed);
        UnequipAll(c);
        State.Cards.Remove(c);
        State.Today?.Dismissed.Add(c.DefId);
        Emit(new GameEvent(GameEventKind.Dismissed, c));
        return true;
    }

    void UnequipAll(CardInstance c) { State.ItemStock.AddRange(c.Items); c.Items.Clear(); }

    public bool Equip(CardInstance c, string itemId)
    {
        if (c == null || !c.OnBoard || c.Items.Count >= R.ItemSlotsPerCard || !State.ItemStock.Remove(itemId)) return false;
        c.Items.Add(itemId); return true;
    }
    public void Unequip(CardInstance c, string itemId) { if (c.Items.Remove(itemId)) State.ItemStock.Add(itemId); }

    // ======================== 催眠 APP ========================
    public int ActiveHypnoCount => State.Cards.Sum(c => c.Hypnos.Count);
    public bool CanHypno(CardInstance c) => c != null && Db.Hypnos.Count > 0 && ActiveHypnoCount < R.HypnoMax
                                            && State.Pleasure >= R.HypnoCostPleasure && !State.InBoss;

    public HypnoDef ApplyHypno(CardInstance c)
    {
        if (!CanHypno(c)) return null;
        State.Pleasure -= R.HypnoCostPleasure;
        var h = Rng.Weighted(Db.Hypnos.Values.ToList(), x => x.Weight);
        c.Hypnos.Add(new HypnoInstance { DefId = h.Id, DaysLeft = h.DurationDays });
        Emit(new GameEvent(GameEventKind.Hypno, c, 0, h.Id));
        return h;
    }
}
