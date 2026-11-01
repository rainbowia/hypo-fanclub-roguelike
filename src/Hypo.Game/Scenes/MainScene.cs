using System.Numerics;
using Hypo.Core.Data;
using Hypo.Core.Persistence;
using Hypo.Core.Runtime;
using Hypo.Game.UI;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.Scenes;

/// <summary>
/// 主场景。
///  普通视角：3D 圆环轮盘（正面），四角 HUD。
///  选座视角：轮盘变俯视，可点选/拖动；左=详情，右=道具，环内=四维条+名字。
///  浮层：日历、手机、招募、Boss 二选一、战败。
/// </summary>
public partial class MainScene : Scene
{
    GameRunner G => App.Run;
    GameState S => G.State;
    RulesDef R => G.R;

    // ---------------- 视图状态 ----------------
    bool _seatView;          // 选座视角
    float _top;              // 0=正面 1=俯视（插值）
    float _ringAngle, _ringTarget;
    bool _calendar, _phone;
    float _phoneX = 1;       // 0=展开 1=收起（插值）
    CardInstance _sel;
    CardInstance _drag; Vector2 _dragStart; bool _dragging;
    IEnumerator<int> _resolver; float _stepT;
    int _activeSlot = -1;
    readonly Rectangle[] _slotRects = new Rectangle[12];
    readonly float[] _slotDepth = new float[12];
    readonly List<(Vector2 pos, string text, Color col, float t)> _floats = new();
    string _banner = ""; float _bannerT;

    public override void Enter()
    {
        G.OnEvent += OnGameEvent;
        InitLayout();
    }

    void OnGameEvent(GameEvent e)
    {
        switch (e.Kind)
        {
            case GameEventKind.Pleasure:
                _floats.Add((CardPos(e.Card) + new Vector2(Random.Shared.Next(-30, 30), -60), (e.Value > 0 ? "+" : "") + e.Value, e.Value > 0 ? Th.Gold : Th.Red, 1.2f));
                break;
            case GameEventKind.StatChanged:
                int si = (int)Enum.Parse<Stat>(e.Text);
                _floats.Add((CardPos(e.Card) + new Vector2(Random.Shared.Next(-50, 50), 20), $"{Th.StatName[si]}{(e.Value > 0 ? "+" : "")}{e.Value}", Th.Stat[si], 1f));
                break;
            case GameEventKind.Destroyed: _floats.Add((SlotCenter(e.Value), "撕毁", Th.Red, 1.4f)); break;
            case GameEventKind.Transformed: _floats.Add((SlotCenter(e.Value), "转化", Th.PinkHot, 1.4f)); break;
            case GameEventKind.DayAdvanced: App.AutoSave(); break;
            case GameEventKind.BossStart: Banner("星期八 · " + App.Db.T(App.Db.Bosses[e.Text].Name) + " 来访"); break;
            case GameEventKind.Victory: Banner("通过！进入第 " + e.Value + " 关"); App.Profile.BestStage = Math.Max(App.Profile.BestStage, e.Value); App.Profile.Save(); break;
        }
    }
    void Banner(string s) { _banner = s; _bannerT = 2.5f; }

    Vector2 CardPos(CardInstance c) => c != null && c.Slot >= 0 ? SlotCenter(c.Slot) : new Vector2(220, 60);
    Vector2 SlotCenter(int s) => s >= 0 && s < 12 ? new Vector2(_slotRects[s].X + _slotRects[s].Width / 2, _slotRects[s].Y + _slotRects[s].Height / 2) : new Vector2(800, 400);

    // ======================================================================
    public override void Update(float dt)
    {
        // ESC 优先关闭最上层
        if (IsKeyPressed(KeyboardKey.Escape))
        {
            if (_dayDetail >= 0) _dayDetail = -1;
            else if (_calendar) _calendar = false;
            else if (_phone) { if (_app >= 0) _app = -1; else _phone = false; }
            else if (_seatView) ExitSeat();
            else App.SettingsUi.Show(true);
        }
        if (IsKeyPressed(KeyboardKey.Tab) && S.Phase == Phase.Prepare) { if (_seatView) ExitSeat(); else EnterSeat(); }
        if (IsKeyPressed(KeyboardKey.Space) && G.CanStartRound && !_seatView) StartRound();

        _top += ((_seatView ? 1 : 0) - _top) * Math.Min(1, dt * 8);
        _phoneX += ((_phone ? 0 : 1) - _phoneX) * Math.Min(1, dt * 10);
        _ringAngle += (_ringTarget - _ringAngle) * Math.Min(1, dt * 6);
        _bannerT -= dt;
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            var f = _floats[i]; f.t -= dt; f.pos.Y -= dt * 40;
            if (f.t <= 0) _floats.RemoveAt(i); else _floats[i] = f;
        }

        // 结算步进
        if (_resolver != null)
        {
            _stepT -= dt * App.Settings.ResolveSpeed;
            if (_stepT <= 0)
            {
                if (_resolver.MoveNext())
                {
                    _activeSlot = _resolver.Current;
                    if (_activeSlot >= 0) _ringTarget = NearestAngle(-_activeSlot * MathF.Tau / 12);
                    _stepT = S.AtSlot(Math.Max(0, _activeSlot)) != null ? 0.55f : 0.12f;
                }
                else { _resolver = null; _activeSlot = -1; }
            }
        }
        // 正面视角滚轮转动轮盘
        if (!_seatView && _resolver == null && !_calendar && Ui.Wheel != 0 && Ui.Mouse.X < 1150)
            _ringTarget += Ui.Wheel * MathF.Tau / 12;
    }

    float NearestAngle(float a)
    {
        while (a - _ringAngle > MathF.PI) a -= MathF.Tau;
        while (a - _ringAngle < -MathF.PI) a += MathF.Tau;
        return a;
    }

    void StartRound()
    {
        if (!G.CanStartRound) return;
        _sel = null;
        _resolver = G.ResolveRound().GetEnumerator();
        _stepT = 0.3f;
    }
    void EnterSeat() { if (S.Phase != Phase.Prepare) return; _seatView = true; _phone = false; _calendar = false; _ringTarget = NearestAngle(0); }
    void ExitSeat() { _seatView = false; _sel = null; _drag = null; }

    // ======================================================================
    public override void Draw()
    {
        bool modal = _calendar || S.Phase is Phase.Recruit or Phase.BossChoice or Phase.GameOver || _dayDetail >= 0;
        bool block = Ui.Consumed;          // 设置面板打开时为 true
        if (modal) Ui.Consumed = true;

        if (_hud != null && !_seatView) _hud.Input();   // 布局按钮先抢点击
        DrawBackground();
        DrawWheel();
        if (_seatView) DrawSeatUi(); else if (_hud != null) { _hud.Draw(); DrawResolveLog(); } else DrawHud();
        DrawPhone();

        foreach (var f in _floats)
            Ui.TextShadow(f.text, f.pos.X, f.pos.Y, 30, Th.A(f.col, Math.Min(1, f.t * 2)), Align.Center);

        if (modal && !block) Ui.Consumed = false;
        if (_calendar) DrawCalendar();
        if (S.Phase == Phase.Recruit && _resolver == null) DrawRecruit();
        if (S.Phase == Phase.BossChoice) DrawBossChoice();
        if (S.Phase == Phase.GameOver && _resolver == null) DrawGameOver();
        if (_dayDetail >= 0) DrawDayDetail();
        if (_bannerT > 0)
        {
            float a = Math.Min(1, _bannerT);
            DrawRectangle(0, 380, Ui.W, 110, Th.A(Th.Wine, 0.85f * a));
            Ui.TextShadow(_banner, Ui.W / 2, 410, 48, Th.A(Th.White, a), Align.Center);
        }
        if (_drag != null && _dragging)
            CardView.Draw(_drag, Ui.R(Ui.Mouse.X - 55, Ui.Mouse.Y - 77, 110, 154), 0.85f);
    }

    void DrawBackground()
    {
        var bg = Assets.UiTex(S.InBoss ? "bg_boss" : "bg_main");
        if (bg.HasValue) Assets.Draw(bg.Value, Ui.R(0, 0, Ui.W, Ui.H), Th.A(Color.White, 1 - _top * 0.5f), true);
        // 选座视角：催眠螺旋背景（对应草图 HY.P.O）
        if (_top > 0.01f)
        {
            var c = new Vector2(800, 430);
            for (int i = 0; i < 10; i++)
                DrawRing(c, 60 + i * 50, 66 + i * 50, (float)GetTime() * 20 + i * 36, (float)GetTime() * 20 + i * 36 + 270, 48, Th.A(Th.PinkHot, 0.18f * _top));
            Ui.Text("H.Y.P.O", 60, 340, 96, Th.A(Th.PinkHot, 0.25f * _top));
            Ui.Text("H.Y.P.O", Ui.W - 60, 340, 96, Th.A(Th.PinkHot, 0.25f * _top), Align.Right);
        }
    }

    // ======================= 轮盘 =======================
    void DrawWheel()
    {
        // 正面：椭圆环（近大远小）；俯视：正圆。按 _top 插值。
        var center = Vector2.Lerp(new Vector2(800, 300), new Vector2(800, 430), _top);
        float rx = Lerp(470, 330, _top), ry = Lerp(120, 330, _top);
        float step = MathF.Tau / 12;
        var order = new int[12];
        for (int i = 0; i < 12; i++)
        {
            float a = _ringAngle + i * step;
            float depth = MathF.Cos(a);                       // 1 = 最前
            var p = new Vector2(center.X + rx * MathF.Sin(a), center.Y + ry * depth);
            float sc = Lerp(Lerp(0.55f, 1.25f, (depth + 1) / 2), 0.62f, _top);
            float w = CardView.W * sc, h = CardView.H * sc;
            _slotRects[i] = Ui.R(p.X - w / 2, p.Y - h * Lerp(0.85f, 0.5f, _top), w, h);
            _slotDepth[i] = Lerp(depth, 0, _top);
            order[i] = i;
        }
        Array.Sort(order, (a, b) => _slotDepth[a].CompareTo(_slotDepth[b]));

        // 圆桌
        DrawEllipse((int)center.X, (int)(center.Y + Lerp(10, 0, _top)), rx * 0.82f, ry * 0.82f, Th.A(Th.Wine, Lerp(0.55f, 0.35f, _top)));

        int hover = -1;
        bool blockedByPhone = _phoneX < 0.9f && Ui.Mouse.X > 1150;
        if (!Ui.Consumed && !blockedByPhone)
            for (int k = 11; k >= 0; k--) { int i = order[k]; if (Ui.Hover(_slotRects[i])) { hover = i; break; } }

        foreach (var i in order)
        {
            var c = S.AtSlot(i);
            float alpha = Lerp(Math.Clamp(0.45f + 0.55f * (_slotDepth[i] + 1) / 2, 0, 1), 1, _top);
            if (c == null || c == _drag && _dragging) CardView.Empty(_slotRects[i], alpha, hover == i && _seatView);
            else CardView.Draw(c, _slotRects[i], alpha, c == _sel, i == _activeSlot);
            if (_top > 0.5f) Ui.Text((i + 1).ToString(), _slotRects[i].X + 4, _slotRects[i].Y - 22, 18, Th.A(Th.PinkPale, _top));
        }
        if (_activeSlot >= 0) Ui.Sparkle(new Vector2(_slotRects[_activeSlot].X + _slotRects[_activeSlot].Width / 2, _slotRects[_activeSlot].Y - 30), 22, Th.Gold);

        // ---------- 交互 ----------
        if (hover >= 0 && Ui.Click && S.Phase == Phase.Prepare && _resolver == null)
        {
            Ui.Consumed = true;
            var c = S.AtSlot(hover);
            if (!_seatView) { if (c != null) { EnterSeat(); _sel = c; } }
            else if (c != null) { _sel = c; _drag = c; _dragStart = Ui.Mouse; _dragging = false; }
            else if (_sel != null) G.Move(_sel, hover);          // 点选模式：选中后点空位 = 移动
        }
        HandleDragRelease(hover);
    }

    void HandleDragRelease(int hoverSlot)
    {
        if (_drag == null) return;
        if (Ui.Down && Vector2.Distance(_dragStart, Ui.Mouse) > 10) _dragging = true;
        if (!Ui.Released) return;
        if (_dragging)
        {
            if (hoverSlot >= 0 && hoverSlot != _drag.Slot) G.Move(_drag, hoverSlot);
            else if (_seatView && Ui.Hover(RosterStrip) && _drag.OnBoard) G.Move(_drag, -1);
        }
        _drag = null; _dragging = false;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
