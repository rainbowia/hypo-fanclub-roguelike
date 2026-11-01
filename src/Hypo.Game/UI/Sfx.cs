using Hypo.Core.Runtime;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Hypo.Game.UI;

/// <summary>
/// 音效 / 音乐。文件放 data/assets/sfx/{名字}.wav|.ogg|.mp3 与 data/assets/bgm/{名字}.ogg|.mp3。
/// 没有文件就静默跳过，所以可以边做边放。名字清单见 docs/ASSETS.md。
/// </summary>
public static class Sfx
{
    static readonly Dictionary<string, Sound?> _sounds = new();
    static Music? _music; static string _musicName = "";
    static readonly string[] Ext = { ".wav", ".ogg", ".mp3" };
    static double _lastTick;

    static string Find(string dir, string name)
    {
        foreach (var e in Ext)
        {
            var p = Path.Combine(Assets.Root, "assets", dir, name + e);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static void Play(string name, float pitchJitter = 0)
    {
        if (!IsAudioDeviceReady()) return;
        if (!_sounds.TryGetValue(name, out var s))
        {
            var p = Find("sfx", name);
            s = p != null ? LoadSound(p) : null;
            _sounds[name] = s;
        }
        if (!s.HasValue) return;
        SetSoundVolume(s.Value, App.Settings?.SfxVolume ?? 1);
        SetSoundPitch(s.Value, 1 + (pitchJitter > 0 ? (float)(Random.Shared.NextDouble() * 2 - 1) * pitchJitter : 0));
        PlaySound(s.Value);
    }

    /// <summary>短时间内只响一次（指针 tick、欢愉跳字等高频音效用）。</summary>
    public static void PlayThrottled(string name, double minGap = 0.06, float jitter = 0.05f)
    {
        if (GetTime() - _lastTick < minGap) return;
        _lastTick = GetTime();
        Play(name, jitter);
    }

    /// <summary>切换背景音乐（同名不重启）。</summary>
    public static void Music(string name)
    {
        if (!IsAudioDeviceReady() || name == _musicName) return;
        if (_music.HasValue) { StopMusicStream(_music.Value); UnloadMusicStream(_music.Value); _music = null; }
        _musicName = name;
        var p = Find("bgm", name);
        if (p == null) return;
        var m = LoadMusicStream(p);
        PlayMusicStream(m);
        _music = m;
    }

    public static void Update()
    {
        if (!_music.HasValue) return;
        SetMusicVolume(_music.Value, App.Settings?.MusicVolume ?? 1);
        UpdateMusicStream(_music.Value);
    }

    /// <summary>游戏事件 → 音效名。</summary>
    public static void OnGameEvent(GameEvent e)
    {
        switch (e.Kind)
        {
            case GameEventKind.Pointer: PlayThrottled("pointer_tick", 0.05, 0.03f); break;
            case GameEventKind.Pleasure: if (e.Value > 0) PlayThrottled("pleasure_gain", 0.08, 0.08f); else Play("pleasure_loss"); break;
            case GameEventKind.StatChanged: PlayThrottled(e.Value > 0 ? "stat_up" : "stat_down", 0.08, 0.05f); break;
            case GameEventKind.Destroyed: Play("card_destroy"); break;
            case GameEventKind.Transformed: Play("card_transform"); break;
            case GameEventKind.Spawned: Play("card_spawn"); break;
            case GameEventKind.Moved: Play("card_place", 0.05f); break;
            case GameEventKind.Dismissed: Play("card_dismiss"); break;
            case GameEventKind.Interact: Play("interact", 0.05f); break;
            case GameEventKind.RoundStart: Play("round_start"); break;
            case GameEventKind.RoundEnd: Play("round_end"); break;
            case GameEventKind.DayAdvanced: Play("day_next"); break;
            case GameEventKind.BossStart: Play("boss_appear"); Music("boss"); break;
            case GameEventKind.Victory: Play("victory"); Music("main"); break;
            case GameEventKind.GameOver: Play("defeat"); Music("defeat"); break;
            case GameEventKind.Recruited: Play("recruit"); break;
            case GameEventKind.Hypno: Play("hypno"); break;
        }
    }
}
