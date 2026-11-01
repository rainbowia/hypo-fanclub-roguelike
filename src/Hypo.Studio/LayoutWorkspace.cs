using System.Numerics;
using System.Text.Json;
using Hypo.Core.Data;
using Hypo.Core.Runtime;
using Hypo.Game.UI;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Hypo.Studio;

/// <summary>编辑器里的预览宿主：变量用样例值（可改），命令输出到控制台。</summary>
public class EditorHost : IUiHost
{
    public readonly Dictionary<string, string> Vars = UiDocs.Vars.ToDictionary(v => v.name, v => v.sample);
    public GameRunner Game => null;
    public bool TryGet(string name, out double num, out string str)
    {
        num = 0; str = null;
        if (!Vars.TryGetValue(name, out var v)) return false;
        if (double.TryParse(v, out num)) return true;
        str = v; return true;
    }
    public void Command(string cmd, string arg) => Log.Info($"[UI 命令] {cmd} {arg}");
}

/// <summary>UI 布局编辑：层级 · 场景（拖动/缩放/吸附/预览运行）· 检查器（属性 + 行为）。</summary>
public class LayoutWorkspace : IWorkspace
{
    public string Name => "UI 布局";
    readonly DefStore<UiLayout> _store = new(Path.Combine(Studio.DataDir, "ui"), l => l.Id);
    UiLayout _layout;
    UiNode _sel;
    readonly EditorHost _host = new();
    LayoutView _edit, _play;
    RenderTexture2D _rt;
    bool _grid = true, _snap = true;
    int _gridSize = 10;

    // 场景视图几何（上一帧）
    Vector2 _p0; float _scale = 0.5f; bool _canvasHover;
    // 拖动状态
    enum Drag { None, Move, Resize }
    Drag _drag; int _handle; Vector2 _dragStart; float _sx, _sy, _sw, _sh;
    // 撤销
    readonly Stack<string> _undo = new();
    double _lastUndoPush;

    public LayoutWorkspace()
    {
        if (_store.Items.Count == 0) _store.Items.Add(new UiLayout { Id = "main_hud", Name = "主场景 HUD", Replaces = "hud", Enabled = false });
        _layout = _store.Items[0];
    }
    public void Save() => _store.Save();

    UiLayout L => _layout;

    void PushUndo(bool force = true)
    {
        if (!force && Raylib.GetTime() - _lastUndoPush < 0.8) return;
        _lastUndoPush = Raylib.GetTime();
        _undo.Push(JsonSerializer.Serialize(L, ContentDb.Json));
        if (_undo.Count > 100) { var a = _undo.ToArray().Take(100).Reverse().ToList(); _undo.Clear(); foreach (var s in a) _undo.Push(s); }
    }
    void Undo()
    {
        if (_undo.Count == 0) return;
        var restored = JsonSerializer.Deserialize<UiLayout>(_undo.Pop(), ContentDb.Json);
        int i = _store.Items.IndexOf(_layout);
        _store.Items[i] = restored; _layout = restored;
        _sel = _sel != null ? L.ById(_sel.Id) : null;
        Studio.MarkDirty();
    }

    // =========================== 渲染到 RT ===========================
    public void PreRender()
    {
        if (_rt.Id == 0) { _rt = Raylib.LoadRenderTexture(Ui.W, Ui.H); Raylib.SetTextureFilter(_rt.Texture, TextureFilter.Bilinear); }
        if (_edit == null || _edit.Layout != L) _edit = new LayoutView(L, _host, false) { EditorMode = true };

        var view = _play ?? _edit;
        // 预览运行时把鼠标映射进画布
        Ui.Mouse = _play != null && _canvasHover ? (Raylib.GetMousePosition() - _p0) / _scale : new Vector2(-9999);
        Ui.Consumed = _play == null || !_canvasHover;
        Ui.Wheel = 0;
        if (_play != null) _play.Input(); else _edit.Compute();

        Raylib.BeginTextureMode(_rt);
        Raylib.ClearBackground(new Color(24, 18, 22, 255));
        if (Assets.Tex(L.EditorReference) is { } refTex)
            Assets.Draw(refTex, new Rectangle(0, 0, Ui.W, Ui.H), Th.A(Color.White, L.ReferenceOpacity));
        if (_grid && _play == null)
            for (int x = 0; x <= Ui.W; x += 50) Raylib.DrawLine(x, 0, x, Ui.H, new Color(255, 255, 255, x % 200 == 0 ? 22 : 10));
        if (_grid && _play == null)
            for (int y = 0; y <= Ui.H; y += 50) Raylib.DrawLine(0, y, Ui.W, y, new Color(255, 255, 255, y % 200 == 0 ? 22 : 10));
        view.Draw();
        Raylib.EndTextureMode();
    }

    // =========================== 左：层级 ===========================
    public void Left()
    {
        // 布局选择
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##layout", $"{L.Name}  ({L.Id})"))
        {
            foreach (var l in _store.Items)
                if (ImGui.Selectable($"{l.Name}  ({l.Id}){(l.Enabled && l.Replaces != "" ? "  ← " + l.Replaces : "")}", l == L)) { _layout = l; _sel = null; _undo.Clear(); StopPlay(); }
            ImGui.EndCombo();
        }
        if (ImGui.SmallButton("新布局")) { var l = new UiLayout { Id = "layout_" + _store.Items.Count }; _store.Items.Add(l); _layout = l; _sel = null; Studio.MarkDirty(); }
        ImGui.SameLine(); if (ImGui.SmallButton("复制布局")) { var c = _store.Clone(L); _store.Items.Add(c); _layout = c; _sel = null; Studio.MarkDirty(); }
        ImGui.SameLine(); if (ImGui.SmallButton("删除布局") && _store.Items.Count > 1) ImGui.OpenPopup("dell");
        if (ImGui.BeginPopup("dell"))
        {
            if (ImGui.Button($"确定删除 {L.Id}")) { _store.Remove(L); _layout = _store.Items[0]; _sel = null; Studio.MarkDirty(); ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        ImGui.Separator();
        if (ImGui.Button("+ 添加对象", new Vector2(-1, 0))) ImGui.OpenPopup("addnode");
        if (ImGui.BeginPopup("addnode")) { AddMenu(_sel?.Id ?? ""); ImGui.EndPopup(); }

        ImGui.BeginChild("tree", Vector2.Zero);
        Tree("");
        // 拖到空白处 = 移到根
        ImGui.Dummy(new Vector2(-1, Math.Max(30, ImGui.GetContentRegionAvail().Y)));
        if (Studio.AcceptDrop("NODE")) Reparent(L.ById(Studio.DragNode), "");
        if (ImGui.IsItemClicked()) _sel = null;
        ImGui.EndChild();
        Shortcuts(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows));
    }

    static string Icon(UiNodeType t) => t switch
    {
        UiNodeType.Group => "□", UiNodeType.Image => "▣", UiNodeType.Text => "Ｔ", UiNodeType.Button => "◆", UiNodeType.Panel => "■", UiNodeType.Bar => "▬", _ => "·"
    };

    void Tree(string parent)
    {
        foreach (var n in L.Children(parent).ToList())
        {
            bool hasKids = L.Nodes.Any(c => c.ParentId == n.Id);
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.AllowOverlap;
            if (!hasKids) flags |= ImGuiTreeNodeFlags.Leaf;
            if (n == _sel) flags |= ImGuiTreeNodeFlags.Selected;
            if (!n.Visible) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.45f, 0.48f, 1));
            bool open = ImGui.TreeNodeEx($"{Icon(n.Type)} {n.Name}###{n.Id}", flags);
            if (!n.Visible) ImGui.PopStyleColor();
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) _sel = n;
            if (ImGui.BeginDragDropSource()) { Studio.DragNode = n.Id; ImGui.SetDragDropPayload("NODE", IntPtr.Zero, 0); ImGui.TextUnformatted(n.Name); ImGui.EndDragDropSource(); }
            if (Studio.AcceptDrop("NODE")) Reparent(L.ById(Studio.DragNode), n.Id);
            if (ImGui.BeginPopupContextItem())
            {
                _sel = n;
                if (ImGui.BeginMenu("添加子对象")) { AddMenu(n.Id); ImGui.EndMenu(); }
                if (ImGui.MenuItem("复制", "Ctrl+D")) Duplicate(n);
                if (ImGui.MenuItem("上移")) MoveSibling(n, -1);
                if (ImGui.MenuItem("下移")) MoveSibling(n, 1);
                if (ImGui.MenuItem(n.Visible ? "隐藏" : "显示")) { PushUndo(); n.Visible = !n.Visible; Studio.MarkDirty(); }
                ImGui.Separator();
                if (ImGui.MenuItem("删除", "Del")) Delete(n);
                ImGui.EndPopup();
            }
            // 显隐小开关
            ImGui.SameLine(ImGui.GetWindowWidth() - 36);
            ImGui.PushID(n.Id);
            if (ImGui.SmallButton(n.Visible ? "●" : "○")) { PushUndo(); n.Visible = !n.Visible; Studio.MarkDirty(); }
            ImGui.PopID();
            if (open) { Tree(n.Id); ImGui.TreePop(); }
        }
    }

    void AddMenu(string parent)
    {
        foreach (var t in Enum.GetValues<UiNodeType>())
            if (ImGui.MenuItem($"{Icon(t)} {TypeName(t)}")) Create(t, parent, null);
    }
    static string TypeName(UiNodeType t) => t switch
    {
        UiNodeType.Group => "空组 Group", UiNodeType.Image => "图片 Image", UiNodeType.Text => "文字 Text", UiNodeType.Button => "按钮 Button",
        UiNodeType.Panel => "面板 Panel", UiNodeType.Bar => "进度条 Bar", _ => t.ToString()
    };

    UiNode Create(UiNodeType t, string parent, Vector2? at, string image = null)
    {
        PushUndo();
        var n = new UiNode { Type = t, ParentId = parent ?? "", Name = TypeName(t).Split(' ')[0] };
        switch (t)
        {
            case UiNodeType.Text: n.W = 240; n.H = 40; n.Text = "文字"; n.Type = UiNodeType.Text; break;
            case UiNodeType.Button: n.W = 220; n.H = 64; n.Text = "按钮"; n.Behaviours.Add(new UiBehaviour { Command = "Log", Arg = "点击了按钮" }); break;
            case UiNodeType.Bar: n.W = 380; n.H = 36; n.BarValue = "pleasure"; n.BarMax = "target"; n.Text = "{pleasure} / {target}"; break;
            case UiNodeType.Panel: n.W = 400; n.H = 300; break;
            case UiNodeType.Group: n.W = 300; n.H = 200; break;
        }
        if (image != null && Assets.Tex(image) is { } tex)
        {
            n.Image = image; n.Name = Path.GetFileNameWithoutExtension(image);
            float s = Math.Min(1, Math.Min(Ui.W * 0.6f / tex.Width, Ui.H * 0.6f / tex.Height));
            n.W = MathF.Round(tex.Width * s); n.H = MathF.Round(tex.Height * s);
        }
        // 位置：给定点（画布坐标）或父对象中心
        var pr = string.IsNullOrEmpty(parent) ? new Rectangle(0, 0, Ui.W, Ui.H) : _edit?.RectOf(L.ById(parent)) ?? new Rectangle(0, 0, Ui.W, Ui.H);
        var c = at ?? new Vector2(pr.X + pr.Width / 2, pr.Y + pr.Height / 2);
        n.X = MathF.Round(c.X - pr.X - n.W / 2); n.Y = MathF.Round(c.Y - pr.Y - n.H / 2);
        L.Nodes.Add(n);
        _sel = n;
        Studio.MarkDirty();
        return n;
    }

    void Reparent(UiNode n, string newParent)
    {
        if (n == null || n.Id == newParent || (newParent != "" && L.IsDescendantOf(L.ById(newParent), n.Id)) || n.ParentId == newParent) return;
        PushUndo();
        // 保持屏幕位置不变
        _edit?.Compute();
        var abs = _edit?.RectOf(n) ?? default;
        var pr = newParent == "" ? default : _edit?.RectOf(L.ById(newParent)) ?? default;
        n.ParentId = newParent; n.X = abs.X - pr.X; n.Y = abs.Y - pr.Y;
        // 移到列表末尾（新父对象的最上层）
        var sub = L.Subtree(n); L.Nodes.RemoveAll(sub.Contains); L.Nodes.AddRange(sub);
        Studio.MarkDirty();
    }

    void MoveSibling(UiNode n, int dir)
    {
        var sibs = L.Children(n.ParentId).ToList();
        int i = sibs.IndexOf(n), j = i + dir;
        if (j < 0 || j >= sibs.Count) return;
        PushUndo();
        int a = L.Nodes.IndexOf(n), b = L.Nodes.IndexOf(sibs[j]);
        (L.Nodes[a], L.Nodes[b]) = (L.Nodes[b], L.Nodes[a]);
        // 子孙跟随：整体重排为 父在前、子孙紧随（保持各自相对顺序）
        var ordered = new List<UiNode>();
        void Walk(string p) { foreach (var c in L.Nodes.Where(x => x.ParentId == p).ToList()) { ordered.Add(c); Walk(c.Id); } }
        Walk("");
        ordered.AddRange(L.Nodes.Except(ordered));
        L.Nodes.Clear(); L.Nodes.AddRange(ordered);
        Studio.MarkDirty();
    }

    void Duplicate(UiNode n)
    {
        PushUndo();
        var sub = L.Subtree(n);
        var map = new Dictionary<string, string>();
        var copies = sub.Select(x =>
        {
            var c = JsonSerializer.Deserialize<UiNode>(JsonSerializer.Serialize(x, ContentDb.Json), ContentDb.Json);
            map[x.Id] = c.Id = Guid.NewGuid().ToString("N")[..8];
            return c;
        }).ToList();
        foreach (var c in copies) if (map.TryGetValue(c.ParentId, out var np)) c.ParentId = np;
        copies[0].Name += " 副本"; copies[0].X += 20; copies[0].Y += 20;
        L.Nodes.AddRange(copies);
        _sel = copies[0];
        Studio.MarkDirty();
    }

    void Delete(UiNode n)
    {
        if (n == null) return;
        PushUndo();
        var sub = L.Subtree(n);
        L.Nodes.RemoveAll(sub.Contains);
        if (sub.Contains(_sel)) _sel = null;
        Studio.MarkDirty();
    }

    void Shortcuts(bool focused)
    {
        var io = ImGui.GetIO();
        if (!focused || io.WantTextInput) return;
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z)) Undo();
        if (_sel == null) return;
        if (ImGui.IsKeyPressed(ImGuiKey.Delete)) Delete(_sel);
        else if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.D)) Duplicate(_sel);
        else
        {
            float step = io.KeyShift ? 10 : 1;
            Vector2 d = Vector2.Zero;
            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) d.X -= step;
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) d.X += step;
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) d.Y -= step;
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) d.Y += step;
            if (d != Vector2.Zero) { PushUndo(false); _sel.X += d.X; _sel.Y += d.Y; Studio.MarkDirty(); }
        }
    }

    // =========================== 中：场景 ===========================
    void StopPlay() => _play = null;

    public void Center()
    {
        // 工具条
        bool playing = _play != null;
        ImGui.PushStyleColor(ImGuiCol.Button, playing ? new Vector4(0.75f, 0.25f, 0.3f, 1) : new Vector4(0.25f, 0.55f, 0.35f, 1));
        if (ImGui.Button(playing ? "■ 停止预览" : "▶ 预览运行")) { if (playing) StopPlay(); else _play = new LayoutView(L, _host) { EditorMode = false }; }
        ImGui.PopStyleColor();
        ImGui.SameLine(); ImGui.Checkbox("网格", ref _grid);
        ImGui.SameLine(); ImGui.Checkbox("吸附", ref _snap);
        ImGui.SameLine(); ImGui.SetNextItemWidth(70); ImGui.InputInt("##gs", ref _gridSize, 0); _gridSize = Math.Clamp(_gridSize, 1, 200);
        ImGui.SameLine(); if (ImGui.Button("预览变量")) ImGui.OpenPopup("vars");
        ImGui.SameLine(); if (ImGui.Button("撤销 Ctrl+Z")) Undo();
        ImGui.SameLine(); ImGui.TextDisabled(playing ? "预览中：可以点按钮测试，命令输出到控制台" : "拖动=移动 · 角点=缩放 · 方向键微调 · 从项目面板拖图片进来");
        if (ImGui.BeginPopup("vars"))
        {
            ImGui.TextDisabled("编辑器里用这些样例值显示绑定");
            foreach (var v in UiDocs.Vars)
            {
                var s = _host.Vars[v.name];
                ImGui.SetNextItemWidth(160);
                if (ImGui.InputText(v.name, ref s, 128)) _host.Vars[v.name] = s;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(v.desc);
            }
            ImGui.EndPopup();
        }

        // 画布
        var avail = ImGui.GetContentRegionAvail();
        _scale = MathF.Max(0.05f, MathF.Min(avail.X / Ui.W, avail.Y / Ui.H));
        var size = new Vector2(Ui.W, Ui.H) * _scale;
        var start = ImGui.GetCursorScreenPos();
        _p0 = start + new Vector2((avail.X - size.X) / 2, 0);
        ImGui.SetCursorScreenPos(_p0);
        rlImGui.ImageRect(_rt.Texture, (int)size.X, (int)size.Y, new Rectangle(0, 0, Ui.W, -Ui.H));
        ImGui.SetCursorScreenPos(_p0);
        ImGui.InvisibleButton("canvas", size);
        _canvasHover = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(_p0, _p0 + size, Theme.U(new Vector4(1, 1, 1, 0.15f)));

        // 拖入资源 → 新建图片
        if (Studio.AcceptDrop("ASSET") && Studio.DragAsset != null)
        {
            var at = (ImGui.GetMousePos() - _p0) / _scale;
            Create(UiNodeType.Image, "", at, Studio.DragAsset);
        }

        if (!playing) EditInteract(dl);
        Shortcuts(ImGui.IsWindowFocused());
    }

    Vector2 ToScreen(float x, float y) => _p0 + new Vector2(x, y) * _scale;
    float Snap(float v) => _snap && !ImGui.GetIO().KeyAlt ? MathF.Round(v / _gridSize) * _gridSize : MathF.Round(v);

    void EditInteract(ImDrawListPtr dl)
    {
        var m = (ImGui.GetMousePos() - _p0) / _scale;
        var hover = _canvasHover ? _edit.Pick(m, false) : null;
        if (hover != null && hover != _sel && _drag == Drag.None)
        {
            var r = _edit.RectOf(hover);
            dl.AddRect(ToScreen(r.X, r.Y), ToScreen(r.X + r.Width, r.Y + r.Height), Theme.U(new Vector4(1, 1, 1, 0.35f)));
        }

        // 选中框 + 角点
        Vector2[] handles = Array.Empty<Vector2>();
        if (_sel != null && L.Nodes.Contains(_sel))
        {
            var r = _edit.RectOf(_sel);
            var a = ToScreen(r.X, r.Y); var b = ToScreen(r.X + r.Width, r.Y + r.Height);
            dl.AddRect(a - Vector2.One, b + Vector2.One, Theme.U(Theme.AccentHi));
            dl.AddRect(a, b, Theme.U(Theme.AccentHi));
            handles = new[] { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y) };
            foreach (var h in handles) dl.AddRectFilled(h - new Vector2(5), h + new Vector2(5), Theme.U(new Vector4(1, 1, 1, 1)));
            dl.AddText(a + new Vector2(0, -20), Theme.U(Theme.AccentHi), $"{_sel.Name}  {r.X:0},{r.Y:0}  {r.Width:0}×{r.Height:0}");
        }

        var mp = ImGui.GetMousePos();
        if (ImGui.IsItemActivated())
        {
            _handle = Array.FindIndex(handles, h => Vector2.Distance(h, mp) < 9);
            if (_handle >= 0) _drag = Drag.Resize;
            else
            {
                _sel = hover;
                _drag = hover != null ? Drag.Move : Drag.None;
            }
            if (_sel != null) { PushUndo(); _dragStart = m; _sx = _sel.X; _sy = _sel.Y; _sw = _sel.W; _sh = _sel.H; }
        }
        if (ImGui.IsItemActive() && _sel != null && _drag != Drag.None)
        {
            var d = m - _dragStart;
            if (_drag == Drag.Move)
            {
                if (d.LengthSquared() > 4) { _sel.X = Snap(_sx + d.X); _sel.Y = Snap(_sy + d.Y); Studio.MarkDirty(); }
            }
            else
            {
                bool left = _handle is 0 or 3, topH = _handle is 0 or 1;
                bool keep = ImGui.GetIO().KeyShift;        // Shift = 保持比例
                float nw = left ? _sw - d.X : _sw + d.X, nh = topH ? _sh - d.Y : _sh + d.Y;
                if (keep && _sw > 0 && _sh > 0) { float s = MathF.Max(nw / _sw, nh / _sh); nw = _sw * s; nh = _sh * s; }
                nw = MathF.Max(4, Snap(nw)); nh = MathF.Max(4, Snap(nh));
                _sel.W = nw; _sel.H = nh;
                _sel.X = left ? _sx + (_sw - nw) : _sx;
                _sel.Y = topH ? _sy + (_sh - nh) : _sy;
                Studio.MarkDirty();
            }
        }
        if (ImGui.IsItemDeactivated()) _drag = Drag.None;

        // 右键菜单
        if (ImGui.BeginPopupContextItem("canvasctx"))
        {
            if (hover != null) _sel = hover;
            if (ImGui.BeginMenu("添加对象")) { AddMenu(_sel?.Id ?? ""); ImGui.EndMenu(); }
            if (_sel != null)
            {
                if (ImGui.MenuItem("复制", "Ctrl+D")) Duplicate(_sel);
                if (ImGui.MenuItem("置于顶层")) { PushUndo(); var sub = L.Subtree(_sel); L.Nodes.RemoveAll(sub.Contains); L.Nodes.AddRange(sub); Studio.MarkDirty(); }
                if (ImGui.MenuItem("按图片原尺寸") && Assets.Tex(_sel.Image) is { } t) { PushUndo(); _sel.W = t.Width; _sel.H = t.Height; Studio.MarkDirty(); }
                if (ImGui.MenuItem("删除", "Del")) Delete(_sel);
            }
            ImGui.EndPopup();
        }
    }

    // =========================== 右：检查器 ===========================
    public void Right()
    {
        if (_sel == null || !L.Nodes.Contains(_sel))
        {
            ImGui.SeparatorText("布局设置");
            if (Inspector.Draw(L)) PushUndo(false);
            ImGui.Spacing();
            ImGui.TextWrapped("“替换内置界面”填 hud 并勾选“启用”后，游戏主场景的普通视角会改用这个布局（选座视角、日历、手机仍是内置的）。填 title 则替换标题界面。");
            return;
        }
        if (Inspector.Draw(_sel)) PushUndo(false);

        ImGui.SeparatorText("行为（对象编程）");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Help.UiScriptApi);
        var list = _sel.Behaviours;
        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            ImGui.PushID(i);
            ImGui.BeginGroup();
            var evs = Enum.GetNames<UiEvent>();
            var evNames = new[] { "点击时", "鼠标移入时", "显示时", "每帧" };
            int ei = (int)b.Event;
            ImGui.SetNextItemWidth(120);
            if (ImGui.Combo("##ev", ref ei, evNames, evNames.Length)) { b.Event = (UiEvent)ei; Studio.MarkDirty(); }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-30);
            var cmd = b.Command;
            if (Inspector.Combo("##cmd", ref cmd, UiDocs.Commands.Select(c => c.cmd), c => UiDocs.Commands.FirstOrDefault(x => x.cmd == c).desc)) { b.Command = cmd; Studio.MarkDirty(); }
            ImGui.SameLine(); if (ImGui.SmallButton("×")) { list.RemoveAt(i); Studio.MarkDirty(); ImGui.EndGroup(); ImGui.PopID(); break; }
            var arg = b.Arg;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##arg", "参数（对象名 / APP 编号 / 文本，可用 {变量}）", ref arg, 256)) { b.Arg = arg; Studio.MarkDirty(); }
            var code = b.Script ?? "";
            if (ImGui.InputTextMultiline("##code", ref code, 65536, new Vector2(-1, Math.Clamp(code.Count(c => c == '\n') * 18 + 44, 44, 220)), ImGuiInputTextFlags.AllowTabInput))
            { b.Script = code; Studio.MarkDirty(); }
            if (string.IsNullOrEmpty(code) && !ImGui.IsItemActive())
            {
                var p = ImGui.GetItemRectMin() + new Vector2(6, 4);
                ImGui.GetWindowDrawList().AddText(p, Theme.U(new Vector4(1, 1, 1, 0.3f)), "C# 代码（可选）例：Find(\"手机\").Visible = true;");
            }
            EffectsEditor.CheckButton(code, true);
            ImGui.EndGroup();
            ImGui.Separator();
            ImGui.PopID();
        }
        if (ImGui.Button("+ 添加行为", new Vector2(-1, 0))) { list.Add(new UiBehaviour()); Studio.MarkDirty(); }
    }
}
