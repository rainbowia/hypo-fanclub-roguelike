using System.Text.Json;
using Hypo.Core.Data;
using Hypo.Core.Runtime;

namespace Hypo.Core.Persistence;

/// <summary>存档目录：%AppData%/HypoFanclub（Windows）或 ~/.config/HypoFanclub。</summary>
public static class Paths
{
    public static string Root
    {
        get
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HypoFanclub");
            Directory.CreateDirectory(p); return p;
        }
    }
}

/// <summary>单局存档。槽位 0 = 自动存档。</summary>
public static class SaveSystem
{
    public const int SlotCount = 4;
    static string File_(int slot) => Path.Combine(Paths.Root, $"save_{slot}.json");

    public static void Save(int slot, GameState s)
    {
        s.SavedAt = DateTime.Now;
        var tmp = File_(slot) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, ContentDb.Json));
        File.Move(tmp, File_(slot), true);   // 原子替换，防止写一半断电坏档
    }
    public static GameState Load(int slot)
    {
        var f = File_(slot);
        if (!File.Exists(f)) return null;
        try { return JsonSerializer.Deserialize<GameState>(File.ReadAllText(f), ContentDb.Json); }
        catch (Exception e) { Log.Warn("存档损坏: " + e.Message); return null; }
    }
    public static bool Exists(int slot) => File.Exists(File_(slot));
    public static void Delete(int slot) { if (Exists(slot)) File.Delete(File_(slot)); }
    public static string Describe(int slot)
    {
        var s = Load(slot);
        return s == null ? "— 空 —" : $"第{s.Stage}关 · {DayName(s.Day)} · 欢愉 {s.Pleasure} · {s.SavedAt:MM-dd HH:mm}";
    }
    public static string DayName(int d) => d switch { 0 => "周一", 1 => "周二", 2 => "周三", 3 => "周四", 4 => "周五", 5 => "周六", 6 => "周日", _ => "星期八" };
}

/// <summary>跨局永久数据：画廊解锁、图鉴、统计。与单局存档分离。</summary>
public class Profile
{
    public HashSet<string> Unlocked { get; set; } = new();
    public HashSet<string> SeenCards { get; set; } = new();
    public int Runs { get; set; }
    public int BestStage { get; set; }

    public event Action<string> OnUnlock;
    static string File_ => Path.Combine(Paths.Root, "profile.json");

    public static Profile Load()
    {
        try { if (File.Exists(File_)) return JsonSerializer.Deserialize<Profile>(File.ReadAllText(File_), ContentDb.Json) ?? new(); }
        catch { }
        return new Profile();
    }
    public void Save() => File.WriteAllText(File_, JsonSerializer.Serialize(this, ContentDb.Json));

    public void Unlock(string galleryId)
    {
        if (string.IsNullOrEmpty(galleryId) || !Unlocked.Add(galleryId)) return;
        OnUnlock?.Invoke(galleryId); Save();
    }
    /// <summary>事件 key → 解锁所有匹配的画廊条目。</summary>
    public void Fire(string eventKey, ContentDb db)
    {
        foreach (var g in db.Gallery.Where(g => g.UnlockEvent == eventKey)) Unlock(g.Id);
    }
}

public class Settings
{
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.7f;
    public float SfxVolume { get; set; } = 0.8f;
    public bool Fullscreen { get; set; }
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;
    public float ResolveSpeed { get; set; } = 1f;   // 结算动画倍速
    public bool SafeMode { get; set; }              // 绿色版文本
    public bool AutoSave { get; set; } = true;

    static string File_ => Path.Combine(Paths.Root, "settings.json");
    public static Settings Load()
    {
        try { if (File.Exists(File_)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(File_), ContentDb.Json) ?? new(); }
        catch { }
        return new Settings();
    }
    public void Save() => File.WriteAllText(File_, JsonSerializer.Serialize(this, ContentDb.Json));
}
