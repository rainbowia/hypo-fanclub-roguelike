using System.Numerics;
using System.Text.Json;
using Hypo.Core.Data;
using Hypo.Core.Runtime;
using Hypo.Game.UI;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Hypo.Studio;

/// <summary>
/// Def 存储。dir 是目录 → 每个对象一个 {Id}.json；dir 以 .json 结尾 → 整个列表存一个文件。
/// </summary>
public class DefStore<T> where T : class, new()
{
    public readonly string Path_;
    public readonly List<T> Items = new();
    readonly Func<T, string> _id;
    readonly Dictionary<T, string> _file = new();
    readonly HashSet<string> _delete = new();
    bool Single => Path_.EndsWith(".json");

    public DefStore(string path, Func<T, string> id) { Path_ = path; _id = id; Load(); }

    public void Load()
    {
        Items.Clear(); _file.Clear(); _delete.Clear();
        if (Single)
        {
            if (File.Exists(Path_)) Items.AddRange(JsonSerializer.Deserialize<List<T>>(File.ReadAllText(Path_), ContentDb.Json) ?? new());
            return;
        }
        if (!Directory.Exists(Path_)) return;
        foreach (var f in Directory.GetFiles(Path_, "*.json", SearchOption.AllDirectories).OrderBy(x => x))
        {
            try
            {
                var txt = File.ReadAllText(f).TrimStart();
                if (txt.StartsWith("[")) { Items.AddRange(JsonSerializer.Deserialize<List<T>>(txt, ContentDb.Json)); _delete.Add(f); }
                else { var t = JsonSerializer.Deserialize<T>(txt, ContentDb.Json); Items.Add(t); _file[t] = f; }
            }
            catch (Exception e) { Log.Warn($"读取失败 {f}: {e.Message}"); }
        }
    }

    public void Save()
    {
        if (Single)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
            File.WriteAllText(Path_, JsonSerializer.Serialize(Items, ContentDb.Json));
            return;
        }
        var dup = Items.GroupBy(_id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup.Count > 0) throw new InvalidOperationException("ID 重复：" + string.Join(", ", dup));
        Directory.CreateDirectory(Path_);
        var written = new HashSet<string>();
        foreach (var t in Items)
        {
            var path = Path.GetFullPath(Path.Combine(Path_, _id(t) + ".json"));
            File.WriteAllText(path, JsonSerializer.Serialize(t, ContentDb.Json));
            written.Add(path);
            if (_file.TryGetValue(t, out var old) && Path.GetFullPath(old) != path) _delete.Add(old);
            _file[t] = path;
        }
        foreach (var f in _delete) if (!written.Contains(Path.GetFullPath(f)) && File.Exists(f)) File.Delete(f);
        _delete.Clear();
    }

    public void Remove(T t) { if (_file.TryGetValue(t, out var f)) _delete.Add(f); Items.Remove(t); }

    public T Clone(T t)
    {
        var c = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(t, ContentDb.Json), ContentDb.Json);
        var p = typeof(T).GetProperty("Id");
        if (p != null) p.SetValue(c, p.GetValue(t) + "_copy");
        return c;
    }
}

/// <summary>通用 Def 工作区：左列表 · 中效果编辑（+预览）· 右属性。</summary>
public class DefWorkspace<T> : IWorkspace where T : class, new()
{
    public string Name { get; }
    public readonly DefStore<T> Store;
    readonly Func<T, string> _id, _label;
    readonly Func<T, List<EffectDef>> _effects;
    public Action<T> Preview;            // 中栏顶部预览
    public Action<T> PreRenderHook;
    T _sel; string _filter = "";

    public DefWorkspace(string name, string path, Func<T, string> id, Func<T, string> label, Func<T, List<EffectDef>> effects)
    {
        Name = name; _id = id; _label = label; _effects = effects;
        Store = new DefStore<T>(path, id);
        _sel = Store.Items.FirstOrDefault();
    }

    public static DefWorkspace<T> Simple(string name, string sub, Func<T, string> id, Func<T, string> label, Func<T, List<EffectDef>> fx) =>
        new(name, Path.Combine(Studio.DataDir, sub), id, x => $"{id(x)}  {Hypo.Game.App.Db?.T(label(x)) ?? label(x)}", fx);

    public T Selected => _sel;
    public void Save() => Store.Save();
    public void PreRender() { if (_sel != null) PreRenderHook?.Invoke(_sel); }

    public void Left()
    {
        if (ImGui.Button("新建")) { var t = new T(); Store.Items.Add(t); _sel = t; Studio.MarkDirty(); }
        ImGui.SameLine();
        if (ImGui.Button("复制") && _sel != null) { var c = Store.Clone(_sel); Store.Items.Insert(Store.Items.IndexOf(_sel) + 1, c); _sel = c; Studio.MarkDirty(); }
        ImGui.SameLine();
        if (ImGui.Button("删除") && _sel != null) ImGui.OpenPopup("del");
        if (ImGui.BeginPopup("del"))
        {
            ImGui.TextUnformatted($"删除 {_id(_sel)}？");
            if (ImGui.Button("确定删除")) { int i = Store.Items.IndexOf(_sel); Store.Remove(_sel); _sel = Store.Items.ElementAtOrDefault(Math.Max(0, i - 1)); Studio.MarkDirty(); ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##f", "搜索…", ref _filter, 128);
        ImGui.Separator();
        ImGui.BeginChild("list", Vector2.Zero);
        foreach (var t in Store.Items)
        {
            var l = _label(t) ?? "";
            if (_filter != "" && !l.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            ImGui.PushID(Store.Items.IndexOf(t));
            if (ImGui.Selectable(l.Replace("%", "%%"), t == _sel)) _sel = t;
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    public void Center()
    {
        if (_sel == null) { ImGui.TextDisabled("从左边选择或新建一个"); return; }
        Preview?.Invoke(_sel);
        if (_effects != null)
        {
            ImGui.SeparatorText("效果");
            EffectsEditor.Draw(_effects(_sel));
        }
    }

    public void Right() => Inspector.Draw(_sel);

    // ----------------- 预设 -----------------
    static RenderTexture2D _cardRt;
    public static DefWorkspace<CardDef> Cards()
    {
        var w = new DefWorkspace<CardDef>("卡牌", Path.Combine(Studio.DataDir, "cards"), c => c.Id,
            c => $"{c.Id}  {Hypo.Game.App.Db?.T(c.Name) ?? c.Name}", c => c.Effects);
        EffectsEditor.CardIds = () => w.Store.Items.Select(c => c.Id);
        w.PreRenderHook = c =>
        {
            if (_cardRt.Id == 0) _cardRt = Raylib.LoadRenderTexture(300, 420);
            Raylib.BeginTextureMode(_cardRt);
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            var inst = new CardInstance { DefId = c.Id, Def = c, Stats = (int[])c.Stats.Clone(), FixedOutput = c.BaseOutput };
            CardView.Draw(inst, new Rectangle(20, 20, 260, 364));
            Raylib.EndTextureMode();
        };
        w.Preview = c =>
        {
            if (_cardRt.Id != 0) rlImGui.ImageRect(_cardRt.Texture, 150, 210, new Rectangle(0, 0, 300, -420));
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted($"{Hypo.Game.App.Db?.T(c.Name)}  ({c.Gender})");
            ImGui.TextDisabled($"四维 {string.Join("/", c.Stats)}   基础产出 {c.BaseOutput}");
            ImGui.TextDisabled("标签 " + string.Join(", ", c.Tags));
            ImGui.Spacing();
            foreach (var e in c.Effects) ImGui.TextWrapped(CardView.Describe(e).Replace("%", "%%"));
            ImGui.EndGroup();
        };
        return w;
    }

    public static DefWorkspace<GalleryDef> Gallery()
    {
        var w = new DefWorkspace<GalleryDef>("画廊", Path.Combine(Studio.DataDir, "gallery.json"), g => g.Id, g => $"{g.Id}  {g.Title}", null);
        EffectsEditor.GalleryIds = () => w.Store.Items.Select(g => g.Id);
        w.Preview = g =>
        {
            ImGui.TextDisabled("解锁事件：defeat:normal / defeat:boss:{bossId} / victory:boss:{bossId} / destroy:{cardId}");
            if (Assets.Tex(g.Image) is { } t)
            {
                var avail = ImGui.GetContentRegionAvail();
                float s = MathF.Min(avail.X / t.Width, (avail.Y - 10) / t.Height);
                rlImGui.ImageSize(t, (int)(t.Width * s), (int)(t.Height * s));
            }
            else ImGui.TextDisabled("（没有图片：在右边填路径，或从项目面板拖到“图片”栏）");
        };
        return w;
    }
}

public class RulesWorkspace : IWorkspace
{
    public string Name => "规则";
    readonly string _file = Path.Combine(Studio.DataDir, "rules.json");
    RulesDef _r;
    public RulesWorkspace() { _r = File.Exists(_file) ? JsonSerializer.Deserialize<RulesDef>(File.ReadAllText(_file), ContentDb.Json) : new(); }
    public void Left() { ImGui.TextWrapped("GDD 中所有“待定”的数值都在这里。\n\n目标公式变量：stage（第几关），例：300 * 1.6^(stage-1)"); }
    public void Center() => Inspector.Draw(_r);
    public void Right() { }
    public void Save() => File.WriteAllText(_file, JsonSerializer.Serialize(_r, ContentDb.Json));
}

public class TextWorkspace : IWorkspace
{
    public string Name => "文本表";
    readonly string _dir = Path.Combine(Studio.DataDir, "text");
    readonly List<string[]> _rows = new();
    string _filter = "", _newKey = "";
    public TextWorkspace()
    {
        var a = Read("zh.json"); var b = Read("zh.safe.json");
        foreach (var k in a.Keys.Union(b.Keys).OrderBy(x => x)) _rows.Add(new[] { k, a.GetValueOrDefault(k, ""), b.GetValueOrDefault(k, "") });
    }
    Dictionary<string, string> Read(string f) { var p = Path.Combine(_dir, f); return File.Exists(p) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) : new(); }
    public void Left()
    {
        ImGui.TextWrapped("任何文本字段写 @key 即引用此表。\n游戏设置里开“全年龄文本”时，绿色版那一列覆盖同名 key（留空 = 沿用正式版）。");
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("##nk", "新 key", ref _newKey, 128);
        if (ImGui.Button("添加", new Vector2(-1, 0)) && _newKey != "" && _rows.All(r => r[0] != _newKey)) { _rows.Add(new[] { _newKey, "", "" }); _newKey = ""; Studio.MarkDirty(); }
    }
    public void Center()
    {
        ImGui.SetNextItemWidth(300); ImGui.InputTextWithHint("##f", "搜索…", ref _filter, 128);
        if (!ImGui.BeginTable("t", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY)) return;
        ImGui.TableSetupColumn("key", ImGuiTableColumnFlags.WidthFixed, 200);
        ImGui.TableSetupColumn("正式版 zh.json");
        ImGui.TableSetupColumn("绿色版 zh.safe.json");
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30);
        ImGui.TableHeadersRow();
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (_filter != "" && !string.Join(" ", r).Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            ImGui.PushID(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(r[0]);
            for (int c = 1; c <= 2; c++) { ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1); if (ImGui.InputText("##" + c, ref r[c], 4096)) Studio.MarkDirty(); }
            ImGui.TableNextColumn(); if (ImGui.SmallButton("×")) { _rows.RemoveAt(i); Studio.MarkDirty(); ImGui.PopID(); break; }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }
    public void Right() { }
    public void Save()
    {
        Directory.CreateDirectory(_dir);
        var a = new SortedDictionary<string, string>(); var b = new SortedDictionary<string, string>();
        foreach (var r in _rows) { a[r[0]] = r[1]; if (!string.IsNullOrEmpty(r[2])) b[r[0]] = r[2]; }
        File.WriteAllText(Path.Combine(_dir, "zh.json"), JsonSerializer.Serialize(a, ContentDb.Json));
        File.WriteAllText(Path.Combine(_dir, "zh.safe.json"), JsonSerializer.Serialize(b, ContentDb.Json));
    }
}

/// <summary>沙盒：摆 12 个座位跑几圈，看完整结算日志。</summary>
public class SandboxWorkspace : IWorkspace
{
    public string Name => "沙盒测试";
    readonly string[] _slots = Enumerable.Repeat("", 12).ToArray();
    string _boss = "";
    int _rounds = 1;
    readonly List<string> _log = new();
    public void Left()
    {
        var cards = EffectsEditor.CardIds().Prepend("").ToList();
        for (int i = 0; i < 12; i++)
        {
            ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted($"座位 {i + 1}"); ImGui.SameLine(70); ImGui.SetNextItemWidth(-1);
            Inspector.Combo("##s" + i, ref _slots[i], cards);
        }
        ImGui.Separator();
        ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Boss"); ImGui.SameLine(70); ImGui.SetNextItemWidth(-1);
        Inspector.Combo("##boss", ref _boss, (Hypo.Game.App.Db?.Bosses.Keys ?? Enumerable.Empty<string>()).Prepend(""));
        ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("圈数"); ImGui.SameLine(70); ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("##r", ref _rounds, 1, 10);
        if (ImGui.Button("保存并运行", new Vector2(-1, 34))) Run();
    }
    public void Center()
    {
        ImGui.BeginChild("sb", Vector2.Zero);
        foreach (var l in _log) ImGui.TextUnformatted(l);
        ImGui.EndChild();
    }
    public void Right() => ImGui.TextWrapped("左边摆好座位（可选 Boss），点运行。\n日志会列出每个槽的产出、四维变化、互动、撕毁与转化，用来验证卡牌效果。");
    public void Save() { }

    void Run()
    {
        Studio.SaveAll();
        _log.Clear();
        Action<string> sink = s => _log.Add(s);
        Log.OnLog += sink;
        try
        {
            var db = ContentDb.Load(Studio.DataDir);
            db.Rules.StartingDeck.Clear(); db.Rules.MovesPerDay = 99;
            var g = new GameRunner(db, null);
            g.NewGame(12345);
            g.OnEvent += e =>
            {
                string n = e.Card?.Name(db) ?? "";
                switch (e.Kind)
                {
                    case GameEventKind.Log: _log.Add(e.Text); break;
                    case GameEventKind.Pleasure: _log.Add($"    欢愉 {(e.Value > 0 ? "+" : "")}{e.Value}  ← {n} ({e.Text})"); break;
                    case GameEventKind.StatChanged: _log.Add($"    {n} {e.Text} {(e.Value > 0 ? "+" : "")}{e.Value} → {string.Join("/", e.Card.Stats)}"); break;
                    case GameEventKind.Interact: _log.Add($"    互动 {n} ↔ {e.Other?.Name(db)}"); break;
                }
            };
            for (int i = 0; i < 12; i++) if (_slots[i] != "") g.CreateCard(_slots[i], i);
            if (_boss != "" && db.Bosses.TryGetValue(_boss, out var boss))
            {
                g.State.BossId = _boss; g.State.Day = 7;
                foreach (var e in boss.Effects.Where(e => e.Trigger == Trigger.BattleStart)) g.RunEffect(e, null, "boss", boss.Id, Trigger.BattleStart, -1);
                g.DestroyCheck();
            }
            for (int r = 0; r < _rounds; r++)
            {
                _log.Add($"===== 第 {r + 1} 圈 =====");
                g.State.Phase = Phase.Prepare;
                foreach (var _ in g.ResolveRound()) { }
                g.State.RecruitOffer.Clear();
                _log.Add($"  普通欢愉 {g.State.Pleasure}   Boss欢愉 {g.State.BossPleasure}");
                _log.Add("  场上: " + string.Join("  ", g.State.Board.Select(c => $"[{c.Slot + 1}]{c.Name(db)} {string.Join("/", c.Stats)} 产出{c.FixedOutput}")));
            }
        }
        catch (Exception e) { _log.Add("异常: " + e); }
        finally { Log.OnLog -= sink; }
    }
}

/// <summary>项目面板：浏览 data/，图片缩略图，拖到场景/检查器。</summary>
public static class ProjectPanel
{
    static string _dir = "";
    static double _lastScan;
    static string[] _dirs = Array.Empty<string>(), _files = Array.Empty<string>();
    static float _thumb = 72;

    public static void Draw()
    {
        var root = Studio.DataDir;
        if (Raylib.GetTime() - _lastScan > 2) Scan();
        // 面包屑
        if (ImGui.SmallButton("data")) { _dir = ""; Scan(); }
        var parts = _dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            ImGui.SameLine(); ImGui.TextDisabled("/"); ImGui.SameLine();
            if (ImGui.SmallButton(parts[i] + "##bc" + i)) { _dir = string.Join("/", parts.Take(i + 1)); Scan(); }
        }
        ImGui.SameLine(ImGui.GetWindowWidth() - 330);
        ImGui.SetNextItemWidth(120); ImGui.SliderFloat("##sz", ref _thumb, 40, 160, "缩略图");
        ImGui.SameLine(); if (ImGui.SmallButton("刷新")) { Assets.Clear(); Scan(); }
        ImGui.SameLine(); if (ImGui.SmallButton("在资源管理器打开")) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Path.Combine(root, _dir), UseShellExecute = true });

        ImGui.BeginChild("grid", Vector2.Zero);
        float cell = _thumb + 16, x = 0, avail = ImGui.GetContentRegionAvail().X;
        void Next() { x += cell; if (x + cell <= avail) ImGui.SameLine(); else x = 0; }
        foreach (var d in _dirs)
        {
            ImGui.PushID(d);
            ImGui.BeginGroup();
            if (ImGui.Button("▣", new Vector2(_thumb, _thumb))) { _dir = (_dir == "" ? "" : _dir + "/") + d; Scan(); ImGui.EndGroup(); ImGui.PopID(); break; }
            Label(d);
            ImGui.EndGroup();
            ImGui.PopID();
            Next();
        }
        foreach (var f in _files)
        {
            var rel = (_dir == "" ? "" : _dir + "/") + f;
            ImGui.PushID(rel);
            ImGui.BeginGroup();
            bool img = f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
            if (img && Assets.Tex(rel) is { } t)
            {
                float s = _thumb / Math.Max(t.Width, t.Height);
                var p = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(_thumb), Theme.U(new Vector4(0.08f, 0.07f, 0.08f, 1)), 4);
                ImGui.SetCursorScreenPos(p + new Vector2((_thumb - t.Width * s) / 2, (_thumb - t.Height * s) / 2));
                rlImGui.ImageSize(t, (int)(t.Width * s), (int)(t.Height * s));
                ImGui.SetCursorScreenPos(p);
                ImGui.InvisibleButton("i", new Vector2(_thumb));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{rel}\n{t.Width} × {t.Height}");
            }
            else { ImGui.Button(Path.GetExtension(f).TrimStart('.').ToUpper(), new Vector2(_thumb)); }
            if (ImGui.BeginDragDropSource())
            {
                Studio.DragAsset = rel;
                ImGui.SetDragDropPayload("ASSET", IntPtr.Zero, 0);
                ImGui.TextUnformatted(rel);
                ImGui.EndDragDropSource();
            }
            Label(f);
            ImGui.EndGroup();
            ImGui.PopID();
            Next();
        }
        ImGui.EndChild();
    }

    static void Label(string s)
    {
        var name = s.Length > 12 ? s[..11] + "…" : s;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + _thumb);
        ImGui.TextUnformatted(name);
        ImGui.PopTextWrapPos();
    }

    static void Scan()
    {
        _lastScan = Raylib.GetTime();
        var full = Path.Combine(Studio.DataDir, _dir);
        if (!Directory.Exists(full)) { _dir = ""; full = Studio.DataDir; }
        _dirs = Directory.GetDirectories(full).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        _files = Directory.GetFiles(full).Select(Path.GetFileName).Where(f => !f.StartsWith(".")).OrderBy(x => x).ToArray();
    }
}
