using System.ComponentModel;
using System.Text.Json;
using Hypo.Core.Data;
using Hypo.Core.Effects;
using Hypo.Core.Persistence;
using Hypo.Core.Runtime;
using Hypo.Core.Scripting;

namespace Hypo.Editor;

public interface IPage { void Save(); string Title { get; } }

/// <summary>
/// 通用 Def 编辑页：左列表 / 中 PropertyGrid / 右（下）效果编辑器。
/// 每个 Def 存为 data/{sub}/{Id}.json；改 ID 或删除会同步删除旧文件。
/// </summary>
public class DefPage<T> : UserControl, IPage where T : class, new()
{
    readonly string _dir; readonly Func<T, string> _id; readonly Func<T, List<EffectDef>> _effects; readonly Func<T, string> _label;
    readonly BindingList<T> _items = new();
    readonly Dictionary<T, string> _file = new();
    readonly HashSet<string> _toDelete = new();
    readonly ListBox _lb = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly PropertyGrid _pg = new() { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    readonly EffectsEditor _fx = new() { Dock = DockStyle.Fill };
    readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "搜索…" };
    public string Title { get; }
    public IEnumerable<T> Items => _items;

    public DefPage(string title, string dir, Func<T, string> id, Func<T, string> label, Func<T, List<EffectDef>> effects)
    {
        Title = title; _dir = dir; _id = id; _label = label; _effects = effects; Dock = DockStyle.Fill;
        var bNew = new Button { Text = "新建", Dock = DockStyle.Bottom };
        var bDup = new Button { Text = "复制", Dock = DockStyle.Bottom };
        var bDel = new Button { Text = "删除", Dock = DockStyle.Bottom };
        var left = new Panel { Dock = DockStyle.Left, Width = 220 };
        left.Controls.Add(_lb); left.Controls.Add(_filter); left.Controls.Add(bDel); left.Controls.Add(bDup); left.Controls.Add(bNew);

        Control main = _pg;
        if (effects != null)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 330 };
            split.Panel1.Controls.Add(_pg);
            var fxBox = new GroupBox { Text = "效果（触发 → 条件 → 动作 → 代码块）", Dock = DockStyle.Fill };
            fxBox.Controls.Add(_fx);
            split.Panel2.Controls.Add(fxBox);
            main = split;
        }
        Controls.Add(main); Controls.Add(left);

        _lb.SelectedIndexChanged += (_, _) => Select_();
        _pg.PropertyValueChanged += (_, _) => RefreshLabels();
        _filter.TextChanged += (_, _) => RefreshLabels();
        bNew.Click += (_, _) => { var t = new T(); Add(t, null); RefreshLabels(); SelectItem(t); };
        bDup.Click += (_, _) =>
        {
            if (Cur() is not { } c) return;
            var t = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(c, ContentDb.Json), ContentDb.Json);
            typeof(T).GetProperty("Id")?.SetValue(t, _id(c) + "_copy");
            Add(t, null); RefreshLabels(); SelectItem(t);
        };
        bDel.Click += (_, _) =>
        {
            if (Cur() is not { } c || MessageBox.Show($"删除 {_id(c)}？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (_file.TryGetValue(c, out var f)) _toDelete.Add(f);
            _items.Remove(c); RefreshLabels();
        };
        Load_();
    }

    void Add(T t, string file) { _items.Add(t); if (file != null) _file[t] = file; }
    List<T> _shown = new();
    T Cur() => _lb.SelectedIndex >= 0 && _lb.SelectedIndex < _shown.Count ? _shown[_lb.SelectedIndex] : null;
    void SelectItem(T t) { int i = _shown.IndexOf(t); if (i >= 0) _lb.SelectedIndex = i; }

    void Select_()
    {
        var c = Cur(); _pg.SelectedObject = c;
        if (_effects != null) _fx.Bind(c == null ? null : _effects(c));
    }
    void RefreshLabels()
    {
        var cur = Cur();
        var f = _filter.Text.Trim();
        _shown = _items.Where(x => f == "" || _label(x).Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        _lb.BeginUpdate(); _lb.Items.Clear();
        foreach (var x in _shown) _lb.Items.Add(_label(x));
        _lb.EndUpdate();
        if (cur != null) SelectItem(cur);
    }

    void Load_()
    {
        if (!Directory.Exists(_dir)) { RefreshLabels(); return; }
        foreach (var f in Directory.GetFiles(_dir, "*.json", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var txt = File.ReadAllText(f).TrimStart();
            try
            {
                if (txt.StartsWith("[")) foreach (var t in JsonSerializer.Deserialize<List<T>>(txt, ContentDb.Json)) { Add(t, null); _toDelete.Add(f); }
                else Add(JsonSerializer.Deserialize<T>(txt, ContentDb.Json), f);
            }
            catch (Exception e) { MessageBox.Show($"读取失败 {f}\n{e.Message}"); }
        }
        RefreshLabels();
        if (_shown.Count > 0) _lb.SelectedIndex = 0;
    }

    public void Save()
    {
        Directory.CreateDirectory(_dir);
        var dupes = _items.GroupBy(_id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) throw new InvalidOperationException($"[{Title}] ID 重复：{string.Join(", ", dupes)}");
        var written = new HashSet<string>();
        foreach (var t in _items)
        {
            var path = Path.Combine(_dir, _id(t) + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(t, ContentDb.Json));
            written.Add(Path.GetFullPath(path));
            if (_file.TryGetValue(t, out var old) && Path.GetFullPath(old) != Path.GetFullPath(path)) _toDelete.Add(old);
            _file[t] = path;
        }
        foreach (var f in _toDelete) if (!written.Contains(Path.GetFullPath(f)) && File.Exists(f)) File.Delete(f);
        _toDelete.Clear();
    }
}

/// <summary>单文件页（规则 / 画廊 / 文本表）。</summary>
public class RulesPage : UserControl, IPage
{
    readonly string _file; readonly RulesDef _r;
    public string Title => "规则";
    public RulesPage(string root)
    {
        _file = Path.Combine(root, "rules.json"); Dock = DockStyle.Fill;
        _r = File.Exists(_file) ? JsonSerializer.Deserialize<RulesDef>(File.ReadAllText(_file), ContentDb.Json) : new RulesDef();
        Controls.Add(new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = _r, ToolbarVisible = false });
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 40, Text = "GDD 中所有“待定”数值都在这里。目标公式变量：stage（大关序号）。例：300 * 1.6^(stage-1)" });
    }
    public void Save() => File.WriteAllText(_file, JsonSerializer.Serialize(_r, ContentDb.Json));
}

public class GalleryPage : UserControl, IPage
{
    readonly string _file; readonly BindingList<GalleryDef> _list;
    public string Title => "画廊";
    public IEnumerable<GalleryDef> Items => _list;
    public GalleryPage(string root)
    {
        _file = Path.Combine(root, "gallery.json"); Dock = DockStyle.Fill;
        _list = new BindingList<GalleryDef>(File.Exists(_file) ? JsonSerializer.Deserialize<List<GalleryDef>>(File.ReadAllText(_file), ContentDb.Json) : new());
        var grid = new DataGridView { Dock = DockStyle.Fill, DataSource = _list, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        Controls.Add(grid);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 40, Text = "UnlockEvent：defeat:normal / defeat:boss:{bossId} / victory:boss:{bossId} / destroy:{cardId}，也可在积木 UnlockGallery 里直接解锁。Image 路径相对 data/" });
    }
    public void Save() => File.WriteAllText(_file, JsonSerializer.Serialize(_list.ToList(), ContentDb.Json));
}

public class TextPage : UserControl, IPage
{
    readonly string _dir; readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    public string Title => "文本表";
    public TextPage(string root)
    {
        _dir = Path.Combine(root, "text"); Dock = DockStyle.Fill;
        _grid.Columns.Add("k", "key"); _grid.Columns.Add("v", "正式版 zh.json"); _grid.Columns.Add("s", "绿色版 zh.safe.json（留空=沿用正式版）");
        var a = Read("zh.json"); var b = Read("zh.safe.json");
        foreach (var k in a.Keys.Union(b.Keys).OrderBy(x => x)) _grid.Rows.Add(k, a.GetValueOrDefault(k), b.GetValueOrDefault(k));
        Controls.Add(_grid);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 40, Text = "任何文本字段写成 @key 即引用此表。游戏设置里开启“全年龄文本”时 zh.safe.json 覆盖同名 key。" });
    }
    Dictionary<string, string> Read(string f)
    {
        var p = Path.Combine(_dir, f);
        return File.Exists(p) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) : new();
    }
    public void Save()
    {
        Directory.CreateDirectory(_dir);
        var a = new SortedDictionary<string, string>(); var b = new SortedDictionary<string, string>();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            var k = r.Cells[0].Value?.ToString(); if (string.IsNullOrWhiteSpace(k)) continue;
            a[k] = r.Cells[1].Value?.ToString() ?? "";
            var s = r.Cells[2].Value?.ToString(); if (!string.IsNullOrEmpty(s)) b[k] = s;
        }
        File.WriteAllText(Path.Combine(_dir, "zh.json"), JsonSerializer.Serialize(a, ContentDb.Json));
        File.WriteAllText(Path.Combine(_dir, "zh.safe.json"), JsonSerializer.Serialize(b, ContentDb.Json));
    }
}

/// <summary>沙盒：摆 12 个座位，跑一圈看日志。设计卡牌时用来验证效果。</summary>
public class SandboxPage : UserControl, IPage
{
    readonly Func<string> _root; readonly Action _saveAll;
    readonly ComboBox[] _slots = new ComboBox[12];
    readonly ComboBox _boss = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown _rounds = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 60 };
    readonly TextBox _log = new() { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = Ctx.Mono, ReadOnly = true, WordWrap = false };
    public string Title => "沙盒测试";
    public SandboxPage(Func<string> root, Action saveAll)
    {
        _root = root; _saveAll = saveAll; Dock = DockStyle.Fill;
        var grid = new TableLayoutPanel { Dock = DockStyle.Left, Width = 300, ColumnCount = 2, AutoScroll = true };
        for (int i = 0; i < 12; i++)
        {
            grid.Controls.Add(new Label { Text = $"座位 {i + 1}", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            grid.Controls.Add(_slots[i] = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList });
        }
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        var run = new Button { Text = "保存并运行", Width = 110 };
        var refresh = new Button { Text = "刷新列表", Width = 90 };
        top.Controls.AddRange(new Control[] { refresh, new Label { Text = "Boss", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _boss, new Label { Text = "圈数", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _rounds, run });
        Controls.Add(_log); Controls.Add(grid); Controls.Add(top);
        refresh.Click += (_, _) => Fill(); run.Click += (_, _) => Run();
        Fill();
    }
    void Fill()
    {
        var ids = Ctx.CardIds().Prepend("（空）").ToArray();
        foreach (var c in _slots) { var v = c.SelectedItem; c.Items.Clear(); c.Items.AddRange(ids); c.SelectedItem = v ?? "（空）"; if (c.SelectedIndex < 0) c.SelectedIndex = 0; }
        var db = ContentDb.Load(_root());
        _boss.Items.Clear(); _boss.Items.Add("（无）"); foreach (var b in db.Bosses.Keys) _boss.Items.Add(b); _boss.SelectedIndex = 0;
    }
    void Run()
    {
        try { _saveAll(); } catch (Exception e) { MessageBox.Show(e.Message); return; }
        var lines = new List<string>();
        Action<string> sink = s => lines.Add(s);
        Log.OnLog += sink;
        try
        {
            var db = ContentDb.Load(_root());
            db.Rules.StartingDeck.Clear(); db.Rules.MovesPerDay = 99;
            var g = new GameRunner(db, null);
            g.NewGame(12345);
            g.OnEvent += e =>
            {
                string n = e.Card?.Name(db) ?? "";
                switch (e.Kind)
                {
                    case GameEventKind.Log: lines.Add(e.Text); break;
                    case GameEventKind.Pleasure: lines.Add($"    欢愉 {(e.Value > 0 ? "+" : "")}{e.Value}  ← {n} ({e.Text})"); break;
                    case GameEventKind.StatChanged: lines.Add($"    {n} {e.Text} {(e.Value > 0 ? "+" : "")}{e.Value} → {string.Join("/", e.Card.Stats)}"); break;
                    case GameEventKind.Interact: lines.Add($"    互动 {n} ↔ {e.Other?.Name(db)}"); break;
                }
            };
            for (int i = 0; i < 12; i++) if (_slots[i].SelectedIndex > 0) g.CreateCard((string)_slots[i].SelectedItem, i);
            if (_boss.SelectedIndex > 0)
            {
                var st = g.State; st.BossId = (string)_boss.SelectedItem; st.Day = 7;
                var boss = db.Bosses[st.BossId];
                foreach (var e in boss.Effects.Where(e => e.Trigger == Trigger.BattleStart)) g.RunEffect(e, null, "boss", boss.Id, Trigger.BattleStart, -1);
                g.DestroyCheck();
            }
            for (int r = 0; r < _rounds.Value; r++)
            {
                lines.Add($"===== 第 {r + 1} 圈 =====");
                g.State.Phase = Phase.Prepare;
                foreach (var _ in g.ResolveRound()) { }
                if (g.State.Phase is Phase.Recruit) g.State.RecruitOffer.Clear();
                lines.Add($"  普通欢愉 {g.State.Pleasure}  Boss欢愉 {g.State.BossPleasure}");
                lines.Add("  场上: " + string.Join("  ", g.State.Board.Select(c => $"[{c.Slot + 1}]{c.Name(db)} {string.Join("/", c.Stats)} 产出{c.FixedOutput}")));
            }
        }
        catch (Exception e) { lines.Add("异常: " + e); }
        finally { Log.OnLog -= sink; }
        _log.Text = string.Join(Environment.NewLine, lines);
    }
    public void Save() { }
}

public class MainForm : Form
{
    string _root;
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    readonly List<IPage> _pages = new();

    public MainForm(string root)
    {
        Text = "H.Y.P.O. 内容编辑器"; Width = 1500; Height = 900; StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("文件");
        file.DropDownItems.Add("保存全部 (Ctrl+S)", null, (_, _) => SaveAll(true));
        file.DropDownItems.Add("打开数据目录…", null, (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _root };
            if (d.ShowDialog() == DialogResult.OK) Open(d.SelectedPath);
        });
        file.DropDownItems.Add("预编译检查全部代码块", null, (_, _) => CheckAllScripts());
        var help = new ToolStripMenuItem("帮助");
        help.DropDownItems.Add("目标选择器语法", null, (_, _) => MessageBox.Show(Ctx.TargetHelp));
        help.DropDownItems.Add("公式变量", null, (_, _) => MessageBox.Show(Ctx.ExprHelp));
        help.DropDownItems.Add("代码块 API", null, (_, _) => MessageBox.Show(Ctx.ScriptHelp));
        help.DropDownItems.Add("积木一览", null, (_, _) => MessageBox.Show(string.Join("\n", Blocks.All.OrderBy(b => b.Category).Select(b => $"[{b.Category}] {b.Type} — {b.Desc}"))));
        menu.Items.Add(file); menu.Items.Add(help);
        Controls.Add(_tabs); Controls.Add(menu); MainMenuStrip = menu;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { SaveAll(true); e.Handled = true; } };
        FormClosing += (_, e) =>
        {
            var r = MessageBox.Show("退出前保存？", "编辑器", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) e.Cancel = true; else if (r == DialogResult.Yes) SaveAll(false);
        };
        Open(root);
    }

    void Open(string root)
    {
        _root = root; _tabs.TabPages.Clear(); _pages.Clear();
        Blocks.LoadPlugins(Path.Combine(root, "..", "mods"));
        var cards = new DefPage<CardDef>("卡牌", Path.Combine(root, "cards"), c => c.Id, c => $"{c.Id}  {c.Name}", c => c.Effects);
        var items = new DefPage<ItemDef>("道具", Path.Combine(root, "items"), c => c.Id, c => $"{c.Id}  {c.Name}", c => c.Effects);
        var gallery = new GalleryPage(root);
        Ctx.CardIds = () => cards.Items.Select(c => c.Id);
        Ctx.ItemIds = () => items.Items.Select(c => c.Id);
        Ctx.GalleryIds = () => gallery.Items.Select(g => g.Id);
        AddPage(cards);
        AddPage(new DefPage<BossDef>("Boss", Path.Combine(root, "bosses"), c => c.Id, c => $"{c.Id}  {c.Name}", c => c.Effects));
        AddPage(items);
        AddPage(new DefPage<HypnoDef>("催眠", Path.Combine(root, "hypno"), c => c.Id, c => $"{c.Id}  {c.Name}", c => c.Effects));
        AddPage(new DefPage<DayEventDef>("每日事件", Path.Combine(root, "events"), c => c.Id, c => $"{c.Id}  {c.Name}", c => c.Effects));
        AddPage(gallery);
        AddPage(new RulesPage(root));
        AddPage(new TextPage(root));
        AddPage(new SandboxPage(() => _root, () => SaveAll(false)));
        Text = "H.Y.P.O. 内容编辑器 — " + Path.GetFullPath(root);
    }
    void AddPage(IPage p) { _pages.Add(p); var t = new TabPage(p.Title); t.Controls.Add((Control)p); _tabs.TabPages.Add(t); }

    void SaveAll(bool notify)
    {
        foreach (var p in _pages) p.Save();
        if (notify) MessageBox.Show("已保存到 " + Path.GetFullPath(_root));
    }

    void CheckAllScripts()
    {
        SaveAll(false);
        var db = ContentDb.Load(_root);
        var errs = new List<string>();
        void Check(string where, IEnumerable<EffectDef> fx)
        {
            foreach (var e in fx)
            {
                if (ScriptHost.Validate(e.Script) is { } err) errs.Add($"{where} [{e.Trigger}] 代码块:\n{err}");
                foreach (var b in e.Conditions.Concat(e.Actions))
                {
                    if (Blocks.Info(b.Type) == null) errs.Add($"{where}: 未知积木 {b.Type}");
                    if (b.Args.TryGetValue("code", out var c) && ScriptHost.Validate(c) is { } e2) errs.Add($"{where} {b.Type}:\n{e2}");
                }
            }
        }
        foreach (var c in db.Cards.Values) Check("卡牌 " + c.Id, c.Effects);
        foreach (var c in db.Bosses.Values) Check("Boss " + c.Id, c.Effects);
        foreach (var c in db.Items.Values) Check("道具 " + c.Id, c.Effects);
        foreach (var c in db.Hypnos.Values) Check("催眠 " + c.Id, c.Effects);
        MessageBox.Show(errs.Count == 0 ? "全部通过 ✓" : string.Join("\n\n", errs.Take(20)));
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string root = args.Length > 0 ? args[0] : FindData();
        Application.Run(new MainForm(root));
    }
    /// <summary>从 exe 所在目录向上找 data 文件夹（开发时指向仓库根的 data/）。</summary>
    static string FindData()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var p = Path.Combine(d.FullName, "data");
            if (Directory.Exists(Path.Combine(p, "cards"))) return p;
            d = d.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "data");
    }
}
