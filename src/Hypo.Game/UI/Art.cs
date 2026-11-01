using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.UI;

/// <summary>
/// 美术素材对照表（data/assets/ui/*.png）。换图只要换同名文件；改名在这里改一处即可。
/// 缺图时各处会自动退回到代码画的占位图形。
/// </summary>
public static class Art
{
    public const string Dir = "assets/ui/";
    public static string P(string name) => Dir + name + ".png";
    public static Texture2D? T(string name) => Assets.Tex(P(name));

    // HUD
    public const string Pleasure = "Icons3_GlowHYPO", PleasureBar = "Icons1_pleasureBar", PleasureFill = "Icons1_pleasureBarFill";
    public const string Settings = "Icons1_Setting", Calendar = "Icons1_calender", Phone = "Icons1_PhoneIcon";
    public const string BtnMain = "ButtonsIcon_main_Button", BtnMainHover = "ButtonsIcon_main_ButtonHovered", BtnMainPressed = "ButtonsIcon_main_ButtonPressed";
    public const string SeatView = "Icons2_NumberFrameCircle", Confirm = "Icons2_Confirm", Cancel = "Icons2_Cancel";
    // 卡 / 座位
    public const string AddNew = "Icons2_AddNew", Locked = "Icons2_Locked", CardBack = "Icons1_FrameBack", NamePlate = "Icons1_FrameName";
    public const string DetailBoard = "Icons2_DetailBoard", CircleMenu = "Icons3_CircleMenu";
    // 日历 / 手机
    public const string Boss = "Icons1_Boss", Event = "Icons1_EventSmall", Person = "Icons1_personellSmall", Item = "Icons1_ItemSmall";
    public const string AppMember = "Icons1_Personel", AppHypo = "Icons1_HYPO", AppKeeper = "Icons1_Codex", AppGuest = "Icons1_Boss";
    public const string TutFigure = "Icons1_TutFigure";

    /// <summary>四维（理智 欲望 爱情 躯体）彩色图标 / 扁平图标 / 环形分段（上右下左）。</summary>
    public static readonly string[] StatIcon = { "Icons3_Sanity", "Icons3_Desire", "Icons3_Love", "Icons3_Stamina" };
    public static readonly string[] StatFlat = { "Icons2_SanityIcon", "Icons2_DesireIcon", "Icons2_LoveIcon", "Icons2_StaminaIcon" };
    public static readonly string[] Segment = { "Icons3_MenuUp", "Icons3_MenuRight", "Icons3_MenuDown", "Icons3_MenuLeft" };
    /// <summary>稀有度 1-5 星。</summary>
    public static readonly string[] Level = { "Icons3_level1", "Icons3_Level2", "Icons3_Level3", "Icons3_Level4", "Icons3_Level5" };

    /// <summary>保持比例画进矩形。缺图返回 false。</summary>
    public static bool Draw(string name, Rectangle r, Color tint, bool cover = false)
    {
        var t = T(name); if (!t.HasValue) return false;
        Assets.Draw(t.Value, r, tint, cover); return true;
    }
    /// <summary>拉伸填满矩形。</summary>
    public static bool Stretch(string name, Rectangle r, Color tint)
    {
        var t = T(name); if (!t.HasValue) return false;
        DrawTexturePro(t.Value, new Rectangle(0, 0, t.Value.Width, t.Value.Height), r, System.Numerics.Vector2.Zero, 0, tint);
        return true;
    }
    /// <summary>按源图比例裁一部分（u0..u1, v0..v1 为 0-1）画进 r。</summary>
    public static void Part(Texture2D t, float u0, float v0, float u1, float v1, Rectangle r, Color tint) =>
        DrawTexturePro(t, new Rectangle(t.Width * u0, t.Height * v0, t.Width * (u1 - u0), t.Height * (v1 - v0)), r, System.Numerics.Vector2.Zero, 0, tint);
}
