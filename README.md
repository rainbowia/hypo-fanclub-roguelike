# H.Y.P.O. Fanclub — 圆桌轮盘卡牌（原型框架）

纯 C#，不用 Unity：游戏用 **Raylib-cs**，编辑器用 **WinForms**，两者共享 **Hypo.Core** 逻辑库。

```
HypoFanclub.sln
├─ src/Hypo.Core     规则/结算/积木/代码块/存档（与画面无关，可单测）
├─ src/Hypo.Game     游戏本体（标题 / 主场景 / 画廊 / 设置 / 存读档）
├─ src/Hypo.Editor   内容编辑器（卡牌 / Boss / 道具 / 催眠 / 事件 / 画廊 / 规则 / 文本表 / 沙盒）
├─ data/             所有内容（JSON + 图片），编辑器直接读写这里
└─ mods/             （可选）自定义积木 dll
```

## 运行

需要 .NET 8 或更新的 SDK。双击：

- `play.bat` — 自动 git pull 更新，然后启动游戏
- `editor.bat` — 自动更新，然后启动编辑器
- `update.bat` — 只更新并编译

或命令行：`dotnet run --project src/Hypo.Game` / `dotnet run --project src/Hypo.Editor`

**字体**放在 `data/assets/fonts/`（不进 git，每台电脑自己放），按文件名自动识别：

| 槽位 | 文件名包含 |
|---|---|
| 标题·中文 | `shijinhei` |
| 正文·中文 | `wangxingyuan` |
| 标题·英文 | `fulbo` 或 `retro` |
| 正文·英文 | `krist` |

也可以写 `fonts.json` 指定。Raylib 不支持 CFF 格式的 .otf，需要转成 .ttf。

> ⚠️ 这版代码是在没有 .NET 编译器的环境里写的，第一次编译可能有少量小错误（拼写/API 版本），按报错修即可，结构不受影响。

## 操作

| | |
|---|---|
| 空格 | 开始结算 |
| Tab / 底部 ◎ 按钮 / 点场上卡 | 进入选座视角 |
| 选座视角 | 拖动卡到其他座位=移动/交换；或先点选再点空位；拖到左下联系人栏=取回 |
| ESC | 逐层关闭（日历→手机→选座→设置） |
| 滚轮 | 普通视角转动轮盘；日历中向上翻历史 |

## 核心规则实现位置

`src/Hypo.Core/Runtime/GameRunner.cs`

- 一圈：指针 1→12 各访问一次；**结算前 → 结算时（产出） → 结算后 → 撕毁判定 → 亡语** → 下一槽
- 移到尚未访问的槽会再次结算；"每轮限一次"按圈记录，不会重复
- 撕毁原地生成/转化的新卡本槽不再结算
- 请离 ≠ 撕毁（不触发亡语）；换座道具随卡，回联系人卸下
- 7 天未达标→战败；提前达标仍跑满 7 天；普通欢愉跨关累计，Boss 独立计分
- `ResolveRound()` 是可步进的 `IEnumerable`，UI 控制动画节奏

GDD 中所有"待定"数值都在 `data/rules.json`（编辑器"规则"页）。

## 卡牌制作器：三层自由度

每条**效果** = 触发时机 + 条件积木（全部满足） + 动作积木（依序） + 可选 **C# 代码块**。

1. **积木**（无需写代码）：Produce / AddStat / Interact / Transform / Spawn / Destroy / SetVar / StopIf …
   参数支持公式：`self_desire + t_desire`、`self_v_echo * 2`
   目标选择器：`adjacent|gender=Male`、`all|!tag=cultist`、`random:1|others`、`slot:7`
2. **代码块**：完整 C#（Roslyn 运行时编译），例：
   ```csharp
   int n = S.Board.Count(c => c.Gender == Gender.Female);
   ctx.Produce(n * 3);
   ```
   编辑器里有"编译检查"，帮助菜单列出全部 API。积木里也有 `Script` / `ScriptCond`。
3. **新积木类型**：在 Core 或 mods dll 里写一个类：
   ```csharp
   [Block("Heal", "四维", "回复全部四维")]
   [Param("target", "target", "self")]
   public class HealAct : IAction {
       public void Run(EffectContext ctx, BlockDef b) { /* ... */ }
   }
   ```
   编辑器通过反射自动出现，不用改编辑器。

**沙盒**页：摆 12 个座位（可选 Boss）直接跑几圈看完整结算日志，用来验证卡效果。

样例：`恋爱脑的女生`、`紧张的弱鸡男`（GDD 原样）、`欢愉信徒` + **莉莉丝** Boss（全部用积木配置，见 `data/bosses/lilith.json`）、`社团经理`（代码块示例）、`复读双子`（自定义变量示例）。

## 绿色版文本

任何文本字段写 `@key` 引用 `data/text/zh.json`；`zh.safe.json` 里同 key 覆盖。设置里"全年龄文本"开关切换。卡牌数据不用复制两份。

## 美术接入（文件名约定，缺图自动显示占位图形）

路径相对 `data/`：

| 文件 | 用途 |
|---|---|
| `assets/ui/icon_pleasure.png` | 左上欢愉图标（粉色✦螺旋） |
| `assets/ui/icon_settings.png` / `icon_calendar.png` / `icon_phone.png` | 齿轮 / 日历 / 手机 |
| `assets/ui/app_member.png` `app_hypo.png` `app_keeper.png` `app_guest.png` | 手机四个 APP |
| `assets/ui/phone_frame.png` | 手机外框 |
| `assets/ui/card_frame.png` / `slot_empty.png` | 卡框 / 空座位 |
| `assets/ui/bg_main.png` `bg_boss.png` `title_bg.png` `logo.png` | 背景 / 标题 |
| `cards/{卡ID}.png` | 卡面（卡牌 Portrait 字段可改） |
| `bosses/*.png`、`gallery/*.png` | Boss 图、CG |

手机四个 APP 暂定：**Member=联系人**、**H.Y.P.O.=催眠**、**Keeper=道具**、**Guest=图鉴**（按美术稿上的四个词对应，可改 `MainScene.Overlays.cs` 顶部 `Apps`）。

## Hypo Studio 编辑器（`editor.bat`）

类 Unity 布局：左 **层级** · 中 **场景/主编辑区** · 右 **检查器** · 下 **项目 / 控制台**。顶部切换工作区：

- **UI 布局**：做界面。
  - 从「项目」面板把图片拖到画布 = 新建图片对象；拖到检查器的“图片”栏 = 换图
  - 拖动移动，拖角点缩放（Shift 保持比例，Alt 不吸附），方向键微调（Shift ×10），Ctrl+D 复制，Del 删除，Ctrl+Z 撤销
  - 层级里拖动对象 = 改父子关系；右键菜单可上移/下移/置顶
  - 类型：Group / Image / Text / Button / Panel / Bar；图片模式支持 **九宫格**（对话框、面板随意拉伸不变形）
  - 文字里写 `{pleasure} / {target}` 显示游戏数据；“显示条件”写 `phase == Prepare`、`moves > 0` 等
  - **行为（对象编程）**：事件（点击/移入/显示/每帧）→ 命令（StartRound、OpenCalendar、Show 对象名…）→ 可选 C# 代码
  - 「预览运行」直接在画布里点按钮测试；「预览变量」改样例数据
  - 布局设置里“参考图”可以放草图对位；“替换内置界面”填 `hud` 并勾“启用” → 游戏主场景 HUD 改用这个布局（`title` = 标题界面）
- **卡牌 / Boss / 道具 / 催眠 / 每日事件**：左列表，中间效果编辑（积木 + 代码块，卡牌带卡面预览），右边属性
- **画廊 / 规则 / 文本表 / 沙盒测试**

Ctrl+S 保存全部，F5 保存并运行游戏。帮助菜单里有选择器、公式、代码 API、UI 变量与命令的说明。
旧版 WinForms 编辑器仍保留：`editor-legacy.bat`。

## 存档

`%AppData%/HypoFanclub/`：`save_0.json`（自动存档，每天开始时）、`save_1..3.json`、`profile.json`（画廊解锁，跨局）、`settings.json`。

## 下一步建议

- 结算动画/音效（订阅 `GameRunner.OnEvent`）
- P0 待定规则确认后改 `rules.json` 或 `GameRunner`
- 正式美术切图后按上表放文件即可替换
