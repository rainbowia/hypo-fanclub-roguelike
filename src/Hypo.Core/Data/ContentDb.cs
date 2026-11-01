using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypo.Core.Data;

/// <summary>
/// 内容数据库。目录结构：
///   data/cards/*.json   data/items/*.json   data/bosses/*.json   data/hypno/*.json
///   data/events/*.json  data/gallery.json   data/rules.json
///   data/text/zh.json （文本表） data/text/zh.safe.json（绿色版覆盖，可选）
/// 每个 json 文件 = 一个 Def，或一个 Def 数组。
/// </summary>
public class ContentDb
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Root { get; private set; } = "data";
    public Dictionary<string, CardDef> Cards { get; } = new();
    public Dictionary<string, ItemDef> Items { get; } = new();
    public Dictionary<string, BossDef> Bosses { get; } = new();
    public Dictionary<string, HypnoDef> Hypnos { get; } = new();
    public Dictionary<string, DayEventDef> Events { get; } = new();
    public Dictionary<string, UiLayout> Layouts { get; } = new();
    /// <summary>取替换某内置界面的布局（hud / title），没有返回 null。</summary>
    public UiLayout LayoutFor(string replaces) => Layouts.Values.FirstOrDefault(l => l.Enabled && l.Replaces == replaces);
    public List<GalleryDef> Gallery { get; private set; } = new();
    public RulesDef Rules { get; private set; } = new();
    public TextDb Text { get; } = new();

    public static ContentDb Load(string root, bool safeMode = false)
    {
        var db = new ContentDb { Root = root };
        db.LoadDir<CardDef>("cards", d => db.Cards[d.Id] = d);
        db.LoadDir<ItemDef>("items", d => db.Items[d.Id] = d);
        db.LoadDir<BossDef>("bosses", d => db.Bosses[d.Id] = d);
        db.LoadDir<HypnoDef>("hypno", d => db.Hypnos[d.Id] = d);
        db.LoadDir<DayEventDef>("events", d => db.Events[d.Id] = d);
        db.LoadDir<UiLayout>("ui", d => db.Layouts[d.Id] = d);
        var g = Path.Combine(root, "gallery.json");
        if (File.Exists(g)) db.Gallery = JsonSerializer.Deserialize<List<GalleryDef>>(File.ReadAllText(g), Json) ?? new();
        var r = Path.Combine(root, "rules.json");
        if (File.Exists(r)) db.Rules = JsonSerializer.Deserialize<RulesDef>(File.ReadAllText(r), Json) ?? new();
        db.Text.Load(Path.Combine(root, "text"), "zh", safeMode);
        return db;
    }

    void LoadDir<T>(string sub, Action<T> add)
    {
        var dir = Path.Combine(Root, sub);
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var txt = File.ReadAllText(f).TrimStart();
                if (txt.StartsWith("["))
                    foreach (var d in JsonSerializer.Deserialize<List<T>>(txt, Json) ?? new()) add(d);
                else add(JsonSerializer.Deserialize<T>(txt, Json));
            }
            catch (Exception e) { Log.Warn($"读取 {f} 失败: {e.Message}"); }
        }
    }

    // ---------- 保存（编辑器用） ----------
    public static void SaveDef<T>(string root, string sub, string id, T def)
    {
        var dir = Path.Combine(root, sub);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"), JsonSerializer.Serialize(def, Json));
    }
    public static void SaveFile<T>(string root, string file, T obj)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, file), JsonSerializer.Serialize(obj, Json));
    }

    public string T(string s) => Text.Resolve(s);
}

/// <summary>
/// 文本表。任何字段写成 "@key" 都会去查表；不以 @ 开头则原样显示。
/// 绿色版：放一个 zh.safe.json，同 key 覆盖即可，不用动卡牌数据。
/// </summary>
public class TextDb
{
    readonly Dictionary<string, string> _map = new();
    public IReadOnlyDictionary<string, string> All => _map;

    public void Load(string dir, string lang, bool safe)
    {
        Merge(Path.Combine(dir, lang + ".json"));
        if (safe) Merge(Path.Combine(dir, lang + ".safe.json"));
    }
    void Merge(string file)
    {
        if (!File.Exists(file)) return;
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
        if (d != null) foreach (var kv in d) _map[kv.Key] = kv.Value;
    }
    public string Resolve(string s)
    {
        if (string.IsNullOrEmpty(s) || s[0] != '@') return s ?? "";
        return _map.TryGetValue(s[1..], out var v) ? v : s[1..];
    }
    public string this[string key] => _map.TryGetValue(key, out var v) ? v : key;
}

public static class Log
{
    public static event Action<string> OnLog;
    public static void Info(string s) { OnLog?.Invoke(s); Console.WriteLine(s); }
    public static void Warn(string s) { OnLog?.Invoke("[警告] " + s); Console.WriteLine("[WARN] " + s); }
}
