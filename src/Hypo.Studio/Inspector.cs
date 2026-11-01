using System.ComponentModel;
using System.Numerics;
using System.Reflection;
using Hypo.Core.Data;
using Hypo.Core.Effects;
using Hypo.Core.Scripting;
using Hypo.Game.UI;
using ImGuiNET;
using rlImGui_cs;

namespace Hypo.Studio;

/// <summary>反射属性面板：按 [Category] 分组、[DisplayName] 作标签、[Description] 作提示。</summary>
public static class Inspector
{
    static readonly Dictionary<string, string> Labels = new()
    {
        ["Id"] = "ID", ["Name"] = "名字", ["Description"] = "描述", ["Icon"] = "图标", ["Portrait"] = "立绘", ["Intro"] = "介绍",
        ["TargetFormula"] = "目标公式", ["Rounds"] = "圈数", ["SurvivorMustLackTag"] = "存活需缺少标签", ["DefeatGalleryId"] = "战败CG",
        ["VictoryGalleryId"] = "胜利CG", ["Weight"] = "权重", ["MinStage"] = "最早出现关", ["DurationDays"] = "持续天数(-1永久)",
        ["Title"] = "标题", ["Image"] = "图片", ["UnlockEvent"] = "解锁事件", ["Category"] = "分类", ["Rarity"] = "稀有度",
        ["Recruitable"] = "可招募", ["RecruitWeight"] = "招募权重", ["FullArt"] = "详情立绘", ["Flavor"] = "风味文字",
        ["SlotCount"] = "槽位数", ["DaysPerStage"] = "每关天数", ["StageTargetFormula"] = "关卡目标公式", ["PleasureCarriesOver"] = "欢愉跨关保留",
        ["StatMin"] = "四维下限(≤撕毁)", ["StatMax"] = "四维上限(≥撕毁)", ["AdjacentWraps"] = "1号与12号相邻", ["MovesPerDay"] = "每日换座次数",
        ["DismissPerDay"] = "每日请离次数", ["MovesRefreshDaily"] = "换座次数每日刷新", ["RosterCapacity"] = "成员上限", ["RecruitMin"] = "招募最少",
        ["RecruitMax"] = "招募最多", ["BossChoiceDay"] = "Boss二选一(第几天结束,0=周一)", ["HypnoMax"] = "催眠上限", ["HypnoCostPleasure"] = "催眠消耗欢愉",
        ["ItemSlotsPerCard"] = "每卡道具槽",
    };
    static readonly HashSet<string> AssetProps = new() { "Image", "HoverImage", "Icon", "Portrait", "FullArt", "EditorReference" };
    static float LabelW => 120 * Theme.FontSize / 18f;

    public static string LabelOf(PropertyInfo p) =>
        p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? Labels.GetValueOrDefault(p.Name, p.Name);

    /// <summary>画对象的全部可编辑属性，返回是否有修改。</summary>
    public static bool Draw(object o, params string[] skip)
    {
        if (o == null) { ImGui.TextDisabled("（未选择）"); return false; }
        bool changed = false;
        var props = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                        && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false && !skip.Contains(p.Name));
        foreach (var g in props.GroupBy(p => p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "属性"))
        {
            if (!ImGui.CollapsingHeader(g.Key, ImGuiTreeNodeFlags.DefaultOpen)) continue;
            ImGui.Indent(4);
            foreach (var p in g) changed |= Field(o, p);
            ImGui.Unindent(4);
        }
        if (changed) Studio.MarkDirty();
        return changed;
    }

    static bool Field(object o, PropertyInfo p)
    {
        var t = p.PropertyType;
        object v;
        try { v = p.GetValue(o); } catch { return false; }
        bool ch = false;
        ImGui.PushID(p.Name);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(LabelOf(p));
        var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description;
        if (desc != null && ImGui.IsItemHovered()) ImGui.SetTooltip(desc.Replace("%", "%%"));
        ImGui.SameLine(LabelW);
        ImGui.SetNextItemWidth(-1);
        object nv = v;

        if (t == typeof(string))
        {
            string s = (string)v ?? "";
            if (p.Name.EndsWith("Color") || p.Name == "Tint")
            {
                var c = LayoutView.ParseHex(s);
                var vec = new Vector4(c.R, c.G, c.B, c.A) / 255f;
                if (ImGui.ColorEdit4("##v", ref vec, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf))
                { nv = $"#{(int)(vec.X * 255):X2}{(int)(vec.Y * 255):X2}{(int)(vec.Z * 255):X2}{(int)(vec.W * 255):X2}"; ch = true; }
            }
            else if (p.Name is "Script" or "Code" or "Text" or "Intro" or "Flavor" or "Description" && s.Contains('\n') || p.Name == "Script")
            {
                if (ImGui.InputTextMultiline("##v", ref s, 65536, new Vector2(-1, 120))) { nv = s; ch = true; }
            }
            else
            {
                if (ImGui.InputText("##v", ref s, 4096)) { nv = s; ch = true; }
                if (AssetProps.Contains(p.Name))
                {
                    if (Studio.AcceptDrop("ASSET")) { nv = Studio.DragAsset; ch = true; }
                    if (ImGui.IsItemHovered() && Assets.Tex(s) is { } tex) { ImGui.BeginTooltip(); rlImGui.ImageSize(tex, 160, (int)(160f * tex.Height / Math.Max(1, tex.Width))); ImGui.EndTooltip(); }
                }
            }
        }
        else if (t == typeof(int)) { int x = (int)v; if (ImGui.InputInt("##v", ref x)) { nv = x; ch = true; } }
        else if (t == typeof(float)) { float x = (float)v; if (ImGui.DragFloat("##v", ref x, p.Name is "Opacity" or "Round" or "ReferenceOpacity" ? 0.01f : 1f)) { nv = x; ch = true; } }
        else if (t == typeof(double)) { double x = (double)v; if (ImGui.InputDouble("##v", ref x)) { nv = x; ch = true; } }
        else if (t == typeof(bool)) { bool x = (bool)v; if (ImGui.Checkbox("##v", ref x)) { nv = x; ch = true; } }
        else if (t.IsEnum)
        {
            var names = Enum.GetNames(t);
            int i = Array.IndexOf(names, v.ToString());
            if (ImGui.Combo("##v", ref i, names, names.Length)) { nv = Enum.Parse(t, names[i]); ch = true; }
        }
        else ImGui.TextDisabled(t.Name);

        if (ch) { try { p.SetValue(o, nv); } catch { ch = false; } }
        ImGui.PopID();
        return ch;
    }

    /// <summary>带候选项的字符串下拉（可以手输）。</summary>
    public static bool Combo(string id, ref string value, IEnumerable<string> options, Func<string, string> tip = null)
    {
        bool ch = false;
        if (ImGui.BeginCombo(id, string.IsNullOrEmpty(value) ? "（空）" : value, ImGuiComboFlags.HeightLarge))
        {
            foreach (var o in options)
            {
                if (ImGui.Selectable(string.IsNullOrEmpty(o) ? "（空）" : o, o == value)) { value = o; ch = true; }
                if (tip != null && ImGui.IsItemHovered() && tip(o) is { Length: > 0 } t) ImGui.SetTooltip(t.Replace("%", "%%"));
            }
            ImGui.EndCombo();
        }
        return ch;
    }
}

/// <summary>效果列表编辑：触发 → 条件积木 → 动作积木 → C# 代码块。</summary>
public static class EffectsEditor
{
    static readonly Dictionary<string, string> _check = new();
    public static Func<IEnumerable<string>> CardIds = () => Array.Empty<string>();
    public static Func<IEnumerable<string>> ItemIds = () => Array.Empty<string>();
    public static Func<IEnumerable<string>> GalleryIds = () => Array.Empty<string>();
    static readonly string[] TargetPresets = { "self", "target", "left", "right", "adjacent", "adjacent|gender=Male", "adjacent|gender=Female",
        "all", "others", "all|!tag=cultist", "random:1", "random:1|others", "slot:7", "roster" };

    public static string TriggerName(Trigger t) => CardView.TriggerName(t);

    public static void Draw(List<EffectDef> list)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            ImGui.PushID(e.Id + i);
            var title = $"#{i + 1}  【{TriggerName(e.Trigger)}{(e.OncePerRound ? "·每轮一次" : "")}】 {(string.IsNullOrEmpty(e.Description) ? Summary(e) : e.Description)}";
            bool open = ImGui.CollapsingHeader(title.Replace("%", "%%") + "###fx", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 110);
            if (ImGui.SmallButton("↑") && i > 0) { (list[i - 1], list[i]) = (list[i], list[i - 1]); Studio.MarkDirty(); }
            ImGui.SameLine(); if (ImGui.SmallButton("↓") && i < list.Count - 1) { (list[i + 1], list[i]) = (list[i], list[i + 1]); Studio.MarkDirty(); }
            ImGui.SameLine(); if (ImGui.SmallButton("复制")) { list.Insert(i + 1, Clone(e)); Studio.MarkDirty(); }
            ImGui.SameLine(); if (ImGui.SmallButton("×")) { list.RemoveAt(i); Studio.MarkDirty(); ImGui.PopID(); break; }
            if (open)
            {
                ImGui.Indent(10);
                var trig = Enum.GetValues<Trigger>();
                var names = trig.Select(t => $"{TriggerName(t)} ({t})").ToArray();
                int ti = Array.IndexOf(trig, e.Trigger);
                ImGui.SetNextItemWidth(220);
                if (ImGui.Combo("触发", ref ti, names, names.Length)) { e.Trigger = trig[ti]; Studio.MarkDirty(); }
                ImGui.SameLine();
                bool once = e.OncePerRound; if (ImGui.Checkbox("每轮限一次", ref once)) { e.OncePerRound = once; Studio.MarkDirty(); }
                string d = e.Description ?? "";
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##desc", "卡面描述（留空 = 按积木自动生成；可写 @key）", ref d, 1024)) { e.Description = d; Studio.MarkDirty(); }

                BlockList("条件（全部满足才执行）", e.Conditions, true);
                BlockList("动作（依序执行）", e.Actions, false);

                ImGui.TextDisabled("C# 代码块（动作后执行，可留空）");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(Help.ScriptApi);
                string code = e.Script ?? "";
                if (ImGui.InputTextMultiline("##code", ref code, 65536, new Vector2(-1, Math.Clamp(code.Count(c => c == '\n') * 18 + 50, 50, 300)), ImGuiInputTextFlags.AllowTabInput))
                { e.Script = code; Studio.MarkDirty(); }
                CheckButton(code, false);
                ImGui.Unindent(10);
                ImGui.Spacing();
            }
            ImGui.PopID();
        }
        ImGui.Spacing();
        if (ImGui.Button("+ 新效果", new Vector2(-1, 30))) { list.Add(new EffectDef()); Studio.MarkDirty(); }
    }

    static string Summary(EffectDef e) => string.Join("；", e.Actions.Select(a => a.Type)) + (string.IsNullOrWhiteSpace(e.Script) ? "" : " +代码");

    public static void CheckButton(string code, bool ui)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (ImGui.SmallButton("编译检查")) _check[code] = (ui ? ScriptHost.ValidateUi(code) : ScriptHost.Validate(code)) ?? "✓ 编译通过";
        if (_check.TryGetValue(code, out var r))
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, r.StartsWith("✓") ? new Vector4(0.5f, 0.9f, 0.6f, 1) : new Vector4(1f, 0.5f, 0.5f, 1));
            ImGui.TextWrapped(r.Replace("%", "%%"));
            ImGui.PopStyleColor();
        }
    }

    static EffectDef Clone(EffectDef e)
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<EffectDef>(System.Text.Json.JsonSerializer.Serialize(e, ContentDb.Json), ContentDb.Json);
        c.Id = Guid.NewGuid().ToString("N")[..8];
        return c;
    }

    static void BlockList(string title, List<BlockDef> list, bool cond)
    {
        ImGui.PushID(title);
        ImGui.SeparatorText(title);
        for (int j = 0; j < list.Count; j++)
        {
            var b = list[j];
            var info = Blocks.Info(b.Type);
            ImGui.PushID(j);
            bool open = ImGui.TreeNodeEx($"{info?.Category ?? "?"} / {b.Type}###b", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.AllowOverlap);
            if (info != null && ImGui.IsItemHovered()) ImGui.SetTooltip(info.Desc.Replace("%", "%%"));
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 70);
            if (ImGui.SmallButton("↑") && j > 0) { (list[j - 1], list[j]) = (list[j], list[j - 1]); Studio.MarkDirty(); }
            ImGui.SameLine(); if (ImGui.SmallButton("↓") && j < list.Count - 1) { (list[j + 1], list[j]) = (list[j], list[j + 1]); Studio.MarkDirty(); }
            ImGui.SameLine(); bool del = ImGui.SmallButton("×");
            if (open)
            {
                if (info == null) ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "未知积木（可能来自未加载的插件）");
                var ps = info?.Params ?? b.Args.Keys.Select(k => new ParamAttribute(k, "text")).ToList();
                foreach (var p in ps) Param(b, p);
                ImGui.TreePop();
            }
            ImGui.PopID();
            if (del) { list.RemoveAt(j); Studio.MarkDirty(); break; }
        }
        // 添加
        var infos = (cond ? Blocks.Conditions : Blocks.Actions).OrderBy(x => x.Category).ThenBy(x => x.Type).ToList();
        if (ImGui.Button("+ 添加" + (cond ? "条件" : "动作"))) ImGui.OpenPopup("add");
        if (ImGui.BeginPopup("add"))
        {
            foreach (var g in infos.GroupBy(x => x.Category))
                if (ImGui.BeginMenu(g.Key))
                {
                    foreach (var x in g)
                    {
                        if (ImGui.MenuItem(x.Type)) { list.Add(x.CreateDefault()); Studio.MarkDirty(); }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(x.Desc.Replace("%", "%%"));
                    }
                    ImGui.EndMenu();
                }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    static void Param(BlockDef b, ParamAttribute p)
    {
        ImGui.PushID(p.Name);
        string v = b.Get(p.Name, p.Default);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(p.Name);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip((p.Kind switch { "target" => Help.Targets, "expr" => Help.Formula, _ => p.Kind } + (string.IsNullOrEmpty(p.Desc) ? "" : "\n" + p.Desc)).Replace("%", "%%"));
        ImGui.SameLine(110 * Theme.FontSize / 18f);
        ImGui.SetNextItemWidth(-1);
        bool ch = false;
        IEnumerable<string> opts = p.Kind switch
        {
            "stat" => new[] { "Sanity", "Desire", "Love", "Body" },
            "op" => new[] { ">", ">=", "<", "<=", "==", "!=" },
            "bool" => new[] { "true", "false" },
            "gender" => Enum.GetNames<Gender>(),
            "card" => CardIds().Prepend(""),
            "item" => ItemIds(),
            "gallery" => GalleryIds(),
            _ => null
        };
        if (p.Kind == "code")
        {
            if (ImGui.InputTextMultiline("##v", ref v, 65536, new Vector2(-1, 90), ImGuiInputTextFlags.AllowTabInput)) ch = true;
            CheckButton(v, false);
        }
        else if (opts != null) ch = Inspector.Combo("##v", ref v, opts);
        else if (p.Kind == "target")
        {
            ImGui.SetNextItemWidth(-36);
            if (ImGui.InputText("##v", ref v, 512)) ch = true;
            ImGui.SameLine();
            if (ImGui.Button("▼")) ImGui.OpenPopup("presets");
            if (ImGui.BeginPopup("presets"))
            {
                foreach (var t in TargetPresets) if (ImGui.Selectable(t)) { v = t; ch = true; }
                ImGui.EndPopup();
            }
        }
        else if (ImGui.InputText("##v", ref v, 1024)) ch = true;
        if (ch) { b.Args[p.Name] = v; Studio.MarkDirty(); }
        ImGui.PopID();
    }
}

public static class Help
{
    public const string Targets = "目标选择器：基础[:数量]|过滤|过滤\n基础：self target left right adjacent all others roster slot:N random:N\n过滤：gender=Female  tag=xxx  !tag=xxx  def=卡ID  stat:love>7  notself";
    public const string Formula = "公式变量：self_sanity self_desire self_love self_body self_output self_slot，t_*（当前目标），\nday stage pleasure boss_pleasure board_count，卡变量 self_v_名字，全局 g_名字。\n函数：ceil floor round min max abs";
    public const string ScriptApi = "可用：ctx  Self  Target  Game  S(状态)  Db  Rng\nctx.Produce(n)  ctx.AddStat(card, Stat.Love, 1)  ctx.Destroy(card)  ctx.Transform(card, \"id\", true)\nctx.Spawn(\"id\", slot)  ctx.Select(\"adjacent|gender=Male\")  ctx.Adjacent(Self)  ctx.Chance(30)\nctx.AddTempOutput(n)  ctx.MultiplyOutput(2)  ctx.Var(\"x\") / ctx.SetVar(\"x\", v)  ctx.Log(\"文本\")  ctx.Cancel = true\nSelf.Love / Self.Desire / Self.Sanity / Self.Body / Self.FixedOutput / Self.Slot / Self.HasTag(\"t\")";
    public const string UiScriptApi = "UI 代码块可用：Host  Layout  Node  Game(可能为 null)\nCmd(\"StartRound\")  Cmd(\"Show\", \"对象名\")  Find(\"对象名\").Visible = false\nNode.Text = \"...\"  Num(\"pleasure\")  Game?.State.Pleasure";

    static int _show = -1;
    public static void Menu()
    {
        string[] items = { "目标选择器", "公式变量", "卡牌代码块 API", "UI 代码块 API", "UI 变量与命令", "积木一览" };
        for (int i = 0; i < items.Length; i++) if (ImGui.MenuItem(items[i])) _show = i;
    }
    public static void Popup()
    {
        if (_show < 0) return;
        ImGui.OpenPopup("帮助");
        ImGui.SetNextWindowSize(new Vector2(720, 520), ImGuiCond.Appearing);
        bool open = true;
        if (ImGui.BeginPopupModal("帮助", ref open))
        {
            switch (_show)
            {
                case 0: ImGui.TextUnformatted(Targets); break;
                case 1: ImGui.TextUnformatted(Formula); break;
                case 2: ImGui.TextUnformatted(ScriptApi); break;
                case 3: ImGui.TextUnformatted(UiScriptApi); break;
                case 4:
                    ImGui.SeparatorText("变量（文字里写 {名字}，条件/公式里直接写名字）");
                    foreach (var v in UiDocs.Vars) ImGui.BulletText($"{v.name}  —  {v.desc}".Replace("%", "%%"));
                    ImGui.SeparatorText("命令");
                    foreach (var c in UiDocs.Commands.Skip(1)) ImGui.BulletText($"{c.cmd}  —  {c.desc}".Replace("%", "%%"));
                    break;
                case 5:
                    foreach (var b in Blocks.All.OrderBy(b => b.Category)) ImGui.BulletText($"[{b.Category}] {b.Type} — {b.Desc}".Replace("%", "%%"));
                    break;
            }
            if (ImGui.Button("关闭")) { _show = -1; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!open) _show = -1;
    }
}
