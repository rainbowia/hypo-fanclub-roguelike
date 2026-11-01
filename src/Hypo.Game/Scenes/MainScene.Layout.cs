using Hypo.Core.Data;
using Hypo.Core.Persistence;
using Hypo.Core.Runtime;
using Hypo.Game.UI;

namespace Hypo.Game.Scenes;

/// <summary>主场景作为 UI 布局的宿主：提供变量 + 执行命令。布局 Replaces = "hud" 时替换内置 HUD。</summary>
public partial class MainScene : IUiHost
{
    LayoutView _hud;
    GameRunner IUiHost.Game => G;

    void InitLayout()
    {
        var l = App.Db.LayoutFor("hud");
        _hud = l != null ? new LayoutView(l, this) : null;
    }

    public bool TryGet(string name, out double num, out string str)
    {
        num = 0; str = null;
        var s = S;
        switch (name)
        {
            case "pleasure": num = s.InBoss ? s.BossPleasure : s.Pleasure; return true;
            case "target": num = s.InBoss ? s.BossTarget : s.StageTarget; return true;
            case "pleasure_normal": num = s.Pleasure; return true;
            case "stage_target": num = s.StageTarget; return true;
            case "boss_pleasure": num = s.BossPleasure; return true;
            case "boss_target": num = s.BossTarget; return true;
            case "boss_name": str = App.Db.Bosses.TryGetValue(s.BossId ?? "", out var b) ? App.Db.T(b.Name) : ""; return true;
            case "inboss": num = s.InBoss ? 1 : 0; return true;
            case "stage": num = s.Stage; return true;
            case "day": num = s.Day + 1; return true;
            case "dayname": str = SaveSystem.DayName(s.Day); return true;
            case "dayabbr": str = DayAbbr[Math.Min(s.Day, 7)]; return true;
            case "moves": num = s.MovesLeft; return true;
            case "moves_max": num = R.MovesPerDay; return true;
            case "dismiss": num = s.DismissLeft; return true;
            case "dismiss_max": num = R.DismissPerDay; return true;
            case "phase": str = s.Phase.ToString(); return true;
            case "can_start": num = G.CanStartRound && _resolver == null ? 1 : 0; return true;
            case "roster": num = s.Cards.Count; return true;
            case "roster_max": num = R.RosterCapacity; return true;
            case "hypno": num = G.ActiveHypnoCount; return true;
            case "hypno_max": num = R.HypnoMax; return true;
            case "seat_view": num = _seatView ? 1 : 0; return true;
            case "runs": num = App.Profile.Runs; return true;
        }
        if (s.Vars.TryGetValue(name, out var gv)) { num = gv; return true; }
        return false;
    }

    public void Command(string cmd, string arg)
    {
        switch (cmd)
        {
            case "StartRound": if (G.CanStartRound && !_seatView) StartRound(); break;
            case "EnterSeat": EnterSeat(); break;
            case "ExitSeat": ExitSeat(); break;
            case "OpenCalendar": _calendar = true; break;
            case "TogglePhone": _phone = !_phone; _app = -1; break;
            case "OpenApp": _phone = true; _app = int.TryParse(arg, out var a) ? Math.Clamp(a, 0, 3) : -1; break;
            case "OpenSettings": App.SettingsUi.Show(true); break;
            case "GoTitle": App.AutoSave(); App.Go(new TitleScene()); break;
            case "Quit": App.AutoSave(); App.Quit = true; break;
            case "Log": Hypo.Core.Data.Log.Info(arg); break;
            default: Hypo.Core.Data.Log.Warn("未知 UI 命令 " + cmd); break;
        }
    }
}
