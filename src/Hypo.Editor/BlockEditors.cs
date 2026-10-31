using System.ComponentModel;
using Hypo.Core.Data;
using Hypo.Core.Effects;
using Hypo.Core.Scripting;

namespace Hypo.Editor;

/// <summary>编辑器共享上下文：给下拉框提供卡ID/道具ID等候选。</summary>
public static class Ctx
{
    public static Func<IEnumerable<string>> CardIds = () => Array.Empty<string>();
    public static Func<IEnumerable<string>> ItemIds = () => Array.Empty<string>();
    public static Func<IEnumerable<string>> GalleryIds = () => Array.Empty<string>();
    public static readonly Font Mono = new("Consolas", 10f);

    public static IList<string> Options(string kind) => kind switch
    {
        "stat" => new[] { "Sanity", "Desire", "Love", "Body" },
        "op" => new[] { ">", ">=", "<", "<=", "==", "!=" },
        "bool" => new[] { "true", "false" },
        "gender" => Enum.GetNames<Gender>(),
        "card" => CardIds().Prepend("").ToList(),
        "item" => ItemIds().ToList(),
        "gallery" => GalleryIds().ToList(),
        "target" => new[] { "self", "target", "left", "right", "adjacent", "adjacent|gender=Male", "adjacent|gender=Female",
                            "all", "others", "all|!tag=cultist", "random:1", "random:1|others", "slot:7", "roster" },
        _ => null
    };

    public const string TargetHelp =
        "选择器：基础[:数量]|过滤|过滤\n基础：self target left right adjacent all others roster slot:N random:N\n" +
        "过滤：gender=Female  tag=xxx  !tag=xxx  def=卡ID  stat:love>7  notself";
    public const string ExprHelp =
        "公式变量：self_sanity self_desire self_love self_body self_output self_slot，t_*（当前目标），\n" +
        "day stage pleasure boss_pleasure board_count，卡变量 self_v_名字，全局 g_名字。函数 ceil floor round min max abs";
    public const string ScriptHelp =
        "可用：ctx  Self  Target  Game  S(状态)  Db  Rng\n" +
        "ctx.Produce(n)  ctx.AddStat(card, Stat.Love, 1)  ctx.Destroy(card)  ctx.Transform(card, \"id\", true)\n" +
        "ctx.Spawn(\"id\", slot)  ctx.Select(\"adjacent|gender=Male\")  ctx.Adjacent(Self)  ctx.Chance(30)\n" +
        "ctx.AddTempOutput(n)  ctx.MultiplyOutput(2)  ctx.Var(\"x\") / ctx.SetVar(\"x\", v)  ctx.Log(\"文本\")  ctx.Cancel = true\n" +
        "Self.Love / Self.Desire / Self.Sanity / Self.Body / Self.FixedOutput / Self.Slot / Self.HasTag(\"t\")";
}

/// <summary>代码编辑对话框。</summary>
public class CodeDialog : Form
{
    readonly TextBox _box; readonly Label _status;
    public string Code => _box.Text;
    public CodeDialog(string code, bool expectBool = false)
    {
        Text = expectBool ? "C# 条件（需 return bool）" : "C# 代码块"; Width = 900; Height = 640; StartPosition = FormStartPosition.CenterParent;
        _box = new TextBox { Multiline = true, AcceptsTab = true, AcceptsReturn = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = Ctx.Mono, Dock = DockStyle.Fill, Text = code ?? "" };
        var help = new Label { Text = Ctx.ScriptHelp, Dock = DockStyle.Top, Height = 90, Font = Ctx.Mono, ForeColor = Color.DimGray };
        _status = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DarkRed };
        var check = new Button { Text = "编译检查", Width = 100, Dock = DockStyle.Left };
        var ok = new Button { Text = "确定", Width = 90, Dock = DockStyle.Right, DialogResult = DialogResult.OK };
        check.Click += (_, _) => { var e = ScriptHost.Validate(_box.Text); _status.ForeColor = e == null ? Color.DarkGreen : Color.DarkRed; _status.Text = e ?? "✓ 编译通过"; };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 70 };
        bottom.Controls.Add(_status); bottom.Controls.Add(check); bottom.Controls.Add(ok);
        Controls.Add(_box); Controls.Add(help); Controls.Add(bottom);
        AcceptButton = null;
    }
}

/// <summary>积木列表编辑器（条件或动作）。参数表按 [Param] 声明自动生成。</summary>
public class BlockListEditor : UserControl
{
    readonly bool _cond;
    List<BlockDef> _list;
    readonly ListBox _lb = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly ComboBox _types = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly Label _desc = new() { Dock = DockStyle.Top, Height = 34, ForeColor = Color.DimGray };
    public event Action Changed;

    public BlockListEditor(string title, bool isCondition)
    {
        _cond = isCondition;
        var infos = (isCondition ? Blocks.Conditions : Blocks.Actions).OrderBy(b => b.Category).ToList();
        foreach (var b in infos) _types.Items.Add($"{b.Category} / {b.Type}");
        if (_types.Items.Count > 0) _types.SelectedIndex = 0;

        var add = new Button { Text = "+", Width = 30, Dock = DockStyle.Right };
        var del = new Button { Text = "−", Width = 30, Dock = DockStyle.Right };
        var up = new Button { Text = "↑", Width = 30, Dock = DockStyle.Right };
        var dn = new Button { Text = "↓", Width = 30, Dock = DockStyle.Right };
        var bar = new Panel { Dock = DockStyle.Top, Height = 26 };
        bar.Controls.Add(_types); bar.Controls.Add(add); bar.Controls.Add(del); bar.Controls.Add(up); bar.Controls.Add(dn);
        var head = new Label { Text = title, Dock = DockStyle.Top, Height = 20, Font = new Font(Font, FontStyle.Bold) };

        _grid.Columns.Add("k", "参数"); _grid.Columns.Add("v", "值"); _grid.Columns.Add("h", "说明");
        _grid.Columns[0].ReadOnly = true; _grid.Columns[2].ReadOnly = true; _grid.Columns[1].FillWeight = 180;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 160 };
        split.Panel1.Controls.Add(_lb);
        split.Panel2.Controls.Add(_grid); split.Panel2.Controls.Add(_desc);
        Controls.Add(split); Controls.Add(bar); Controls.Add(head);

        add.Click += (_, _) =>
        {
            if (_list == null || _types.SelectedIndex < 0) return;
            _list.Add(infos[_types.SelectedIndex].CreateDefault()); Refresh_(); _lb.SelectedIndex = _list.Count - 1; Changed?.Invoke();
        };
        del.Click += (_, _) => { if (Sel() is { } b) { _list.Remove(b); Refresh_(); Changed?.Invoke(); } };
        up.Click += (_, _) => MoveSel(-1); dn.Click += (_, _) => MoveSel(1);
        _lb.SelectedIndexChanged += (_, _) => ShowParams();
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || Sel() is not { } b) return;
            b.Args[(string)_grid.Rows[e.RowIndex].Cells[0].Value] = _grid.Rows[e.RowIndex].Cells[1].Value?.ToString() ?? "";
            int i = _lb.SelectedIndex; _lb.Items[i] = b.ToString(); Changed?.Invoke();
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || Sel() is not { } b) return;
            var key = (string)_grid.Rows[e.RowIndex].Cells[0].Value;
            var info = Blocks.Info(b.Type);
            if (info?.Params.FirstOrDefault(p => p.Name == key)?.Kind != "code") return;
            using var dlg = new CodeDialog(b.Get(key), _cond);
            if (dlg.ShowDialog(this) == DialogResult.OK) { b.Args[key] = dlg.Code; ShowParams(); Changed?.Invoke(); }
        };
    }

    BlockDef Sel() => _list != null && _lb.SelectedIndex >= 0 && _lb.SelectedIndex < _list.Count ? _list[_lb.SelectedIndex] : null;
    void MoveSel(int d)
    {
        int i = _lb.SelectedIndex, j = i + d;
        if (_list == null || i < 0 || j < 0 || j >= _list.Count) return;
        (_list[i], _list[j]) = (_list[j], _list[i]); Refresh_(); _lb.SelectedIndex = j; Changed?.Invoke();
    }

    public void Bind(List<BlockDef> list) { _list = list; Refresh_(); }
    void Refresh_()
    {
        _lb.Items.Clear();
        if (_list != null) foreach (var b in _list) _lb.Items.Add(b.ToString());
        ShowParams();
    }

    void ShowParams()
    {
        _grid.Rows.Clear();
        var b = Sel(); if (b == null) { _desc.Text = ""; return; }
        var info = Blocks.Info(b.Type);
        _desc.Text = info?.Desc ?? "（未知积木：可能来自未加载的插件）";
        var ps = info?.Params ?? b.Args.Keys.Select(k => new ParamAttribute(k, "text")).ToList();
        foreach (var p in ps)
        {
            var val = b.Get(p.Name, p.Default);
            int row = _grid.Rows.Add(p.Name, val, p.Kind == "code" ? "双击编辑代码" : HelpFor(p));
            var opts = Ctx.Options(p.Kind);
            if (opts != null && p.Kind != "target")
            {
                var cell = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                foreach (var o in opts.Append(val).Distinct()) cell.Items.Add(o);
                cell.Value = val;
                _grid.Rows[row].Cells[1] = cell;
            }
            if (p.Kind == "code") _grid.Rows[row].Cells[1].ReadOnly = true;
        }
    }
    static string HelpFor(ParamAttribute p) => !string.IsNullOrEmpty(p.Desc) ? p.Desc : p.Kind switch
    {
        "target" => "选择器，悬停看语法", "expr" => "数字或公式", _ => p.Kind
    };
}

/// <summary>效果列表编辑器：触发时机 + 条件积木 + 动作积木 + C# 代码块。</summary>
public class EffectsEditor : UserControl
{
    List<EffectDef> _list;
    readonly ListBox _lb = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly ComboBox _trigger = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly CheckBox _once = new() { Text = "每轮限一次", AutoSize = true };
    readonly TextBox _descBox = new() { Width = 420, PlaceholderText = "卡面描述（留空=按积木自动生成）" };
    readonly BlockListEditor _conds = new("条件（全部满足才执行）", true) { Dock = DockStyle.Fill };
    readonly BlockListEditor _acts = new("动作（依序执行）", false) { Dock = DockStyle.Fill };
    readonly TextBox _script = new() { Multiline = true, AcceptsTab = true, AcceptsReturn = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = Ctx.Mono, Dock = DockStyle.Fill };
    readonly Label _scriptStatus = new() { Dock = DockStyle.Right, Width = 380, ForeColor = Color.DarkRed };
    readonly Panel _detail = new() { Dock = DockStyle.Fill, Enabled = false };
    bool _loading;
    public event Action Changed;

    public EffectsEditor()
    {
        foreach (var t in Enum.GetNames<Trigger>()) _trigger.Items.Add(t);
        var add = new Button { Text = "新效果", Dock = DockStyle.Bottom };
        var dup = new Button { Text = "复制", Dock = DockStyle.Bottom };
        var del = new Button { Text = "删除", Dock = DockStyle.Bottom };
        var left = new Panel { Dock = DockStyle.Left, Width = 200 };
        left.Controls.Add(_lb); left.Controls.Add(del); left.Controls.Add(dup); left.Controls.Add(add);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
        top.Controls.AddRange(new Control[] { new Label { Text = "触发", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _trigger, _once, _descBox });
        var blocks = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        blocks.Panel1.Controls.Add(_conds); blocks.Panel2.Controls.Add(_acts);
        var check = new Button { Text = "编译检查", Dock = DockStyle.Left, Width = 90 };
        var scriptBar = new Panel { Dock = DockStyle.Top, Height = 26 };
        scriptBar.Controls.Add(new Label { Text = "C# 代码块（动作执行后运行，可留空）  ? = API", Dock = DockStyle.Fill });
        scriptBar.Controls.Add(_scriptStatus); scriptBar.Controls.Add(check);
        var scriptPanel = new Panel { Dock = DockStyle.Bottom, Height = 170 };
        scriptPanel.Controls.Add(_script); scriptPanel.Controls.Add(scriptBar);
        new ToolTip().SetToolTip(scriptBar.Controls[0], Ctx.ScriptHelp);
        new ToolTip().SetToolTip(_conds, Ctx.TargetHelp + "\n\n" + Ctx.ExprHelp);
        _detail.Controls.Add(blocks); _detail.Controls.Add(scriptPanel); _detail.Controls.Add(top);
        Controls.Add(_detail); Controls.Add(left);

        add.Click += (_, _) => { if (_list == null) return; _list.Add(new EffectDef()); Reload(); _lb.SelectedIndex = _list.Count - 1; Changed?.Invoke(); };
        dup.Click += (_, _) =>
        {
            if (Sel() is not { } e) return;
            var copy = System.Text.Json.JsonSerializer.Deserialize<EffectDef>(System.Text.Json.JsonSerializer.Serialize(e, ContentDb.Json), ContentDb.Json);
            copy.Id = Guid.NewGuid().ToString("N")[..8]; _list.Add(copy); Reload(); _lb.SelectedIndex = _list.Count - 1; Changed?.Invoke();
        };
        del.Click += (_, _) => { if (Sel() is { } e) { _list.Remove(e); Reload(); Changed?.Invoke(); } };
        _lb.SelectedIndexChanged += (_, _) => Show_();
        _trigger.SelectedIndexChanged += (_, _) => { if (!_loading && Sel() is { } e) { e.Trigger = Enum.Parse<Trigger>((string)_trigger.SelectedItem); Label_(); } };
        _once.CheckedChanged += (_, _) => { if (!_loading && Sel() is { } e) { e.OncePerRound = _once.Checked; Changed?.Invoke(); } };
        _descBox.TextChanged += (_, _) => { if (!_loading && Sel() is { } e) { e.Description = _descBox.Text; Changed?.Invoke(); } };
        _script.TextChanged += (_, _) => { if (!_loading && Sel() is { } e) { e.Script = _script.Text; Changed?.Invoke(); } };
        check.Click += (_, _) => { var err = ScriptHost.Validate(_script.Text); _scriptStatus.ForeColor = err == null ? Color.DarkGreen : Color.DarkRed; _scriptStatus.Text = err ?? "✓ 编译通过"; };
        _conds.Changed += () => Changed?.Invoke(); _acts.Changed += () => Changed?.Invoke();
    }

    EffectDef Sel() => _list != null && _lb.SelectedIndex >= 0 && _lb.SelectedIndex < _list.Count ? _list[_lb.SelectedIndex] : null;
    public void Bind(List<EffectDef> list) { _list = list; Reload(); if (_list?.Count > 0) _lb.SelectedIndex = 0; }
    void Reload()
    {
        _lb.Items.Clear();
        if (_list != null) foreach (var e in _list) _lb.Items.Add(Name_(e));
        Show_();
    }
    static string Name_(EffectDef e) => $"[{e.Trigger}]{(e.OncePerRound ? "¹" : "")} {(string.IsNullOrEmpty(e.Description) ? e.Actions.FirstOrDefault()?.Type ?? "(空)" : e.Description)}";
    void Label_() { if (Sel() is { } e) _lb.Items[_lb.SelectedIndex] = Name_(e); Changed?.Invoke(); }

    void Show_()
    {
        var e = Sel(); _detail.Enabled = e != null;
        _loading = true;
        _trigger.SelectedItem = e?.Trigger.ToString(); _once.Checked = e?.OncePerRound ?? false;
        _descBox.Text = e?.Description ?? ""; _script.Text = e?.Script ?? ""; _scriptStatus.Text = "";
        _conds.Bind(e?.Conditions); _acts.Bind(e?.Actions);
        _loading = false;
    }
}
