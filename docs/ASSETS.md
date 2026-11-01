# 素材清单

路径都相对 `data/`。**缺的素材游戏会自动用占位图形，可以边做边放。** 放进去后，编辑器项目面板点「刷新」，游戏重开即可看到。
游戏现在按屏幕实际分辨率渲染，所以**背景建议 3840×2160（至少 2560×1440）**，其它素材画大一点也没关系（会自动生成缩小用的 mipmap）。

## 一、背景与大图

| 文件 | 用途 | 建议尺寸 / 说明 |
|---|---|---|
| `assets/ui/title_bg.png` | 标题背景 | 3840×2160 |
| `assets/ui/logo.png` | 标题 Logo | 约 1800×660，透明底 |
| `assets/ui/bg_main.png` | 主场景背景（活动室） | 3840×2160 |
| `assets/ui/bg_boss.png` | 星期八背景 | 3840×2160 |
| `assets/ui/ring_table.png` | 圆桌（正面视角，椭圆桌面） | 透明底；会被拉伸到转盘椭圆外接框 ×1.05 |
| `assets/ui/ring_table_top.png` | 圆桌（俯视，正圆） | 正方形透明底，选座视角淡入 |
| `assets/ui/ring_pointer.png` | 结算指针（在当前座位上方上下浮动） | 正方形，约 256×256 |

## 二、界面框

| 文件 | 用途 | 说明 |
|---|---|---|
| `assets/ui/card_frame.png` | 座位上的卡框 | 比例 150:210，中间透明（立绘画在里面） |
| `assets/ui/phone_frame.png` | 右侧手机外框 | 比例 450:700，屏幕区域透明 |
| （已有）Icons2_DetailBoard | 选座视角左右面板底 | ✓ |
| 可选：日历 / 设置 / 招募 / Boss 二选一 / 战败画面的面板底图 | 现在是代码画的圆角框 | 做好告诉我文件名再接 |

## 三、内容图（每张卡 / Boss / CG 各一张）

| 文件 | 用途 | 建议 |
|---|---|---|
| `cards/{卡ID}.png` | 卡面立绘（座位上的小卡） | 约 600×630，会居中裁切填满 |
| 卡牌「详情立绘」字段 | 选座视角左侧大图 | 约 1100×900，可留空（用卡面图） |
| `bosses/{id}.png` | Boss 立绘（二选一界面） | 约 1100×900 |
| `bosses/{id}_icon.png` | Boss 图标（日历星期八） | 正方形 |
| `gallery/*.png` | CG（战败 / 胜利 / 撕毁） | 16:9，1920×1080 起 |
| 道具「图标」字段 | 道具图标 | 正方形 256 |

样例数据里需要的：`cards/lovebrain_girl` `nervous_guy` `cultist` `club_senpai` `club_manager` `echo_twin`，`bosses/lilith` `lilith_icon`，`gallery/defeat_normal` `defeat_lilith` `victory_lilith` `destroy_lovebrain`。

## 四、特效（可选，需要接代码）

欢愉跳出的心形粒子、撕毁时的撕裂效果、转化时的螺旋、催眠光效。做成序列帧（横排 sprite sheet）后告诉我帧数即可接入。

---

# 音效清单

放 `assets/sfx/{名字}.wav`（也支持 .ogg / .mp3）。高频的（tick、欢愉+）会自动限速并轻微变调，避免听腻。

| 名字 | 什么时候响 |
|---|---|
| `ui_click` | 点任何按钮 |
| `view_seat` / `view_normal` | 进入 / 退出选座视角 |
| `card_place` | 换座、拖卡落座 |
| `card_dismiss` | 请离 |
| `round_start` / `round_end` | 一圈结算开始 / 结束 |
| `pointer_tick` | 指针转到每个座位 |
| `pleasure_gain` / `pleasure_loss` | 产出欢愉 / 扣欢愉 |
| `stat_up` / `stat_down` | 四维上升 / 下降 |
| `interact` | 两张卡互动 |
| `card_destroy` | 撕毁 |
| `card_transform` | 转化（如变成信徒） |
| `card_spawn` | 生成新卡 |
| `recruit` | 招募成员 |
| `hypno` | 使用催眠 APP |
| `day_next` | 进入下一天 |
| `boss_appear` | 星期八 Boss 登场 |
| `victory` / `defeat` | 通关 / 失败 |

背景音乐放 `assets/bgm/{名字}.ogg`（或 .mp3）：

| 名字 | 场景 |
|---|---|
| `title` | 标题 |
| `main` | 主场景（普通日） |
| `boss` | 星期八 |
| `defeat` | 战败 |
