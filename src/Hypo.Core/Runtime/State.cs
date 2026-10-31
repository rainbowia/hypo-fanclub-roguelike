using System.Text.Json.Serialization;
using Hypo.Core.Data;

namespace Hypo.Core.Runtime;

/// <summary>卡牌实例（运行状态）。可序列化进存档。</summary>
public class CardInstance
{
    public int Uid { get; set; }
    public string DefId { get; set; } = "";
    public int[] Stats { get; set; } = new int[4];
    /// <summary>固定产出 = 基础产出 + 永久强化。</summary>
    public int FixedOutput { get; set; }
    public List<string> ExtraTags { get; set; } = new();
    public List<string> Items { get; set; } = new();
    public List<HypnoInstance> Hypnos { get; set; } = new();
    /// <summary>-1 = 在背包（联系人）里。</summary>
    public int Slot { get; set; } = -1;
    /// <summary>卡自由变量，代码块可读写，会存档。</summary>
    public Dictionary<string, double> Vars { get; set; } = new();
    /// <summary>是否被标记为撕毁（等待撕毁判定）。</summary>
    public bool MarkedDestroy { get; set; }

    // ---- 非持久 ----
    [JsonIgnore] public CardDef Def { get; set; }
    [JsonIgnore] public HashSet<string> FiredThisRound { get; } = new();
    /// <summary>本次结算的临时产出加成（每次结算前清零）。</summary>
    [JsonIgnore] public int TempOutput { get; set; }
    [JsonIgnore] public double OutputMultiplier { get; set; } = 1;

    [JsonIgnore] public int Sanity { get => Stats[0]; set => Stats[0] = value; }
    [JsonIgnore] public int Desire { get => Stats[1]; set => Stats[1] = value; }
    [JsonIgnore] public int Love { get => Stats[2]; set => Stats[2] = value; }
    [JsonIgnore] public int Body { get => Stats[3]; set => Stats[3] = value; }
    [JsonIgnore] public Gender Gender => Def?.Gender ?? Gender.None;
    [JsonIgnore] public bool OnBoard => Slot >= 0;

    public bool HasTag(string t) => ExtraTags.Contains(t) || (Def?.Tags.Contains(t) ?? false);
    public int GetStat(Stat s) => Stats[(int)s];
    public void SetStat(Stat s, int v) => Stats[(int)s] = v;
    public void AddStat(Stat s, int d) => Stats[(int)s] += d;
    public string Name(ContentDb db) => db.T(Def?.Name ?? DefId);
    public override string ToString() => $"{DefId}#{Uid}@{Slot}";
}

public class HypnoInstance
{
    public string DefId { get; set; } = "";
    public int DaysLeft { get; set; } = -1;
}

/// <summary>日历历史：一天的记录。</summary>
public class DayRecord
{
    public int Stage { get; set; }
    public int Day { get; set; }          // 0=周一 … 6=周日, 7=星期八
    public int Pleasure { get; set; }
    public List<string> Gained { get; set; } = new();   // DefId
    public List<string> Lost { get; set; } = new();     // DefId（撕毁）
    public List<string> Dismissed { get; set; } = new();
    public string EventId { get; set; } = "";
    public bool Done { get; set; }
}

public enum Phase { Prepare, Resolving, Recruit, BossChoice, GameOver, Victory }

/// <summary>单局完整状态 = 存档内容。</summary>
public class GameState
{
    public int Version { get; set; } = 1;
    public int Seed { get; set; }
    public int RngCalls { get; set; }
    public int NextUid { get; set; } = 1;

    public int Stage { get; set; } = 1;
    public int Day { get; set; } = 0;            // 0..6；7 = 星期八 Boss
    public long Pleasure { get; set; }           // 普通累计欢愉（跨大关保留）
    public long StageTarget { get; set; }
    public long BossPleasure { get; set; }       // Boss 战独立计分
    public long BossTarget { get; set; }
    public string BossId { get; set; } = "";     // 已选 Boss
    public List<string> BossOffer { get; set; } = new();
    public int BossRoundsDone { get; set; }

    public Phase Phase { get; set; } = Phase.Prepare;
    public int MovesLeft { get; set; }
    public int DismissLeft { get; set; }
    public List<CardInstance> Cards { get; set; } = new();   // 场上 + 背包
    public List<string> ItemStock { get; set; } = new();     // 未装备道具
    public List<string> RecruitOffer { get; set; } = new();
    public List<DayRecord> History { get; set; } = new();
    public Dictionary<int, string> DayEvents { get; set; } = new(); // day -> eventId
    public Dictionary<string, double> Vars { get; set; } = new();   // 全局自由变量
    public DateTime SavedAt { get; set; }
    public string LastDefeatKey { get; set; } = "";

    [JsonIgnore] public bool InBoss => Day >= 7;
    public CardInstance AtSlot(int slot) => Cards.FirstOrDefault(c => c.Slot == slot);
    public IEnumerable<CardInstance> Board => Cards.Where(c => c.Slot >= 0).OrderBy(c => c.Slot);
    public IEnumerable<CardInstance> Roster => Cards.Where(c => c.Slot < 0);
    public DayRecord Today => History.LastOrDefault(h => h.Stage == Stage && h.Day == Day && !h.Done);
}
