using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Hypo.Core.Data;

// ============================================================
//  配置层（Def）：全部是可 JSON 序列化的纯数据，编辑器读写这些。
//  运行时状态见 Runtime/。
// ============================================================

/// <summary>四维。顺序固定：理智 欲望 爱情 躯体。</summary>
public enum Stat { Sanity = 0, Desire = 1, Love = 2, Body = 3 }

public enum Gender { None, Female, Male, Other }

/// <summary>
/// 效果触发时机。卡牌效果和 Boss 全局效果共用。
/// 单次结算顺序：BeforeResolve → OnResolve(产出) → AfterResolve → 撕毁判定 → OnDestroyed(亡语)
/// </summary>
public enum Trigger
{
    BeforeResolve,   // 结算前
    OnResolve,       // 结算时（产生欢愉）
    AfterResolve,    // 结算后
    OnDestroyed,     // 亡语（撕毁时，请离不触发）
    OnDismissed,     // 被请离
    OnEnterBoard,    // 上场
    OnRoundStart,    // 每圈开始（指针转动前）
    OnRoundEnd,      // 每圈结束
    OnMoved,         // 被移动到新槽位

    // ---- 以下主要给 Boss / 全局规则用 ----
    BattleStart,         // Boss 战开始
    AnyCardResolving,    // 任意卡结算时（ctx.Self = 正在结算的卡）
    AnyCardDestroyed,    // 任意卡被撕毁后（ctx.Self = 被撕毁的卡，ctx.Slot = 原槽位）
}

/// <summary>一个可配置参数集合。所有模块化积木都用 Type + Args 描述，Args 值统一存字符串，由积木自己解析。</summary>
public class BlockDef
{
    public string Type { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();

    public string Get(string key, string def = "") => Args.TryGetValue(key, out var v) && v != null ? v : def;
    public int GetInt(string key, int def = 0) => int.TryParse(Get(key), out var v) ? v : def;
    public bool GetBool(string key, bool def = false) => bool.TryParse(Get(key), out var v) ? v : def;
    public override string ToString() => Type + (Args.Count > 0 ? " (" + string.Join(", ", Args.Select(kv => kv.Key + "=" + kv.Value)) + ")" : "");
}

/// <summary>
/// 一条效果 = 触发时机 + 条件积木（全部满足）+ 动作积木（依序执行）+ 可选 C# 代码块。
/// 代码块在动作积木之后执行，可以访问 ctx（见 Scripting/ScriptGlobals）。
/// </summary>
public class EffectDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public Trigger Trigger { get; set; } = Trigger.OnResolve;
    /// <summary>每轮（每圈）限一次。与“每次结算”区分。</summary>
    public bool OncePerRound { get; set; }
    /// <summary>卡面描述文本 key 或直接文本，仅展示用。</summary>
    public string Description { get; set; } = "";
    public List<BlockDef> Conditions { get; set; } = new();
    public List<BlockDef> Actions { get; set; } = new();
    /// <summary>C# 代码块（可空）。例：if (ctx.Self.Love > 7) ctx.Produce(5);</summary>
    public string Script { get; set; } = "";
}

public class CardDef
{
    [Category("基本"), DisplayName("ID"), Description("唯一ID，同时是文件名")] public string Id { get; set; } = "new_card";
    [Category("基本"), DisplayName("名字"), Description("可写 @key 引用文本表")] public string Name { get; set; } = "新成员";            // 可写文本 key（如 "@card.lovebrain.name"）用于绿色版替换
    public string Title { get; set; } = "";                  // 副标题
    [Category("基本"), DisplayName("性别")] public Gender Gender { get; set; } = Gender.Female;
    [Browsable(false)] public List<string> Tags { get; set; } = new();
    [JsonIgnore, Category("基本"), DisplayName("标签"), Description("逗号分隔")] public string TagsText { get => string.Join(",", Tags); set => Tags = Split(value); }
    [Browsable(false)] public int[] Stats { get; set; } = { 5, 5, 5, 5 };      // 理智 欲望 爱情 躯体
    [JsonIgnore, Category("数值"), DisplayName("四维"), Description("理智/欲望/爱情/躯体，例 5/5/5/5")] public string StatsText { get => string.Join("/", Stats); set { var p = value.Split('/', ',', ' ').Where(x => x != "").Select(int.Parse).ToArray(); if (p.Length == 4) Stats = p; } }
    [Category("数值"), DisplayName("基础产出")] public int BaseOutput { get; set; } = 5;
    public int Rarity { get; set; } = 1;
    public bool Recruitable { get; set; } = true;             // 是否进入招募池
    public int RecruitWeight { get; set; } = 10;
    [Category("美术"), DisplayName("卡面图"), Description("相对 data/ 的路径")] public string Portrait { get; set; } = "";
    public string FullArt { get; set; } = "";                 // 详情立绘
    public string Flavor { get; set; } = "";
    [Browsable(false)] public List<EffectDef> Effects { get; set; } = new();
    /// <summary>自由字段：给代码块用的任意数据（数值、字符串）。</summary>
    [Browsable(false)] public Dictionary<string, string> Vars { get; set; } = new();
    [JsonIgnore, Category("代码"), DisplayName("自定义变量"), Description("a=1;b=2  代码块里 Self.Vars[\"a\"]")] public string VarsText
    {
        get => string.Join(";", Vars.Select(kv => kv.Key + "=" + kv.Value));
        set => Vars = value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0].Trim(), x => x[1].Trim());
    }
    internal static List<string> Split(string s) => (s ?? "").Split(',', '，').Select(x => x.Trim()).Where(x => x != "").ToList();
}

public class ItemDef
{
    public string Id { get; set; } = "new_item";
    public string Name { get; set; } = "新道具";
    public string Icon { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>道具效果挂在持有者身上，Trigger 语义同卡牌。</summary>
    [Browsable(false)] public List<EffectDef> Effects { get; set; } = new();
}

/// <summary>催眠（APP 附魔）定义：一个随机强化的候选项。</summary>
public class HypnoDef
{
    public string Id { get; set; } = "new_hypno";
    public string Name { get; set; } = "催眠";
    public string Description { get; set; } = "";
    public int Weight { get; set; } = 10;
    /// <summary>-1 = 永久，否则持续天数。</summary>
    public int DurationDays { get; set; } = -1;
    [Browsable(false)] public List<EffectDef> Effects { get; set; } = new();
}

public class BossDef
{
    public string Id { get; set; } = "new_boss";
    public string Name { get; set; } = "新Boss";
    public string Icon { get; set; } = "";
    public string Portrait { get; set; } = "";
    public string Intro { get; set; } = "";
    /// <summary>本场目标公式。变量：stage（大关序号，从1开始）。支持 + - * / ^ () ceil floor。</summary>
    public string TargetFormula { get; set; } = "200 * 1.5^(stage-1)";
    /// <summary>Boss 战持续圈数。</summary>
    public int Rounds { get; set; } = 1;
    /// <summary>胜利时要求场上至少存活一张“不带此标签”的卡（为空则不检查）。莉莉丝 = "cultist"。</summary>
    public string SurvivorMustLackTag { get; set; } = "";
    /// <summary>全局效果（Trigger 用 BattleStart / OnRoundStart / AnyCardResolving / AnyCardDestroyed / OnRoundEnd）。</summary>
    [Browsable(false)] public List<EffectDef> Effects { get; set; } = new();
    public string DefeatGalleryId { get; set; } = "";
    public string VictoryGalleryId { get; set; } = "";
    public int Weight { get; set; } = 10;
    public int MinStage { get; set; } = 1;
}

public class GalleryDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Image { get; set; } = "";
    /// <summary>解锁事件 key：defeat:normal / defeat:boss:{bossId} / destroy:{cardId} / victory:boss:{bossId} / custom:xxx</summary>
    public string UnlockEvent { get; set; } = "";
    public string Category { get; set; } = "cg";
}

/// <summary>全局规则：GDD 里所有“待定”的数值都放这里，可在编辑器改。</summary>
public class RulesDef
{
    public int SlotCount { get; set; } = 12;
    public int DaysPerStage { get; set; } = 7;
    public string StageTargetFormula { get; set; } = "300 * 1.6^(stage-1)";
    public bool PleasureCarriesOver { get; set; } = true;
    public int StatMin { get; set; } = 0;          // ≤ 此值撕毁
    public int StatMax { get; set; } = 9;          // ≥ 此值撕毁
    public bool AdjacentWraps { get; set; } = true; // 1 号与 12 号相邻
    public int MovesPerDay { get; set; } = 3;
    public int DismissPerDay { get; set; } = 1;
    public bool MovesRefreshDaily { get; set; } = true;
    public int RosterCapacity { get; set; } = 16;
    public int RecruitMin { get; set; } = 1;
    public int RecruitMax { get; set; } = 5;
    public int BossChoiceDay { get; set; } = 3;     // 第几天(0=周一)结束时出现 Boss 二选一
    public int HypnoMax { get; set; } = 3;
    public int HypnoCostPleasure { get; set; } = 50;
    public int ItemSlotsPerCard { get; set; } = 1;
    [Browsable(false)] public List<string> StartingDeck { get; set; } = new();
    [JsonIgnore, DisplayName("初始卡组"), Description("卡ID，逗号分隔，可重复")] public string StartingDeckText { get => string.Join(",", StartingDeck); set => StartingDeck = CardDef.Split(value); }
    [Browsable(false)] public List<string> StartingItems { get; set; } = new();
    [JsonIgnore, DisplayName("初始道具")] public string StartingItemsText { get => string.Join(",", StartingItems); set => StartingItems = CardDef.Split(value); }
}

/// <summary>每日特殊事件（日历上显示）。</summary>
public class DayEventDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Weight { get; set; } = 10;
    [Browsable(false)] public List<EffectDef> Effects { get; set; } = new();
}
