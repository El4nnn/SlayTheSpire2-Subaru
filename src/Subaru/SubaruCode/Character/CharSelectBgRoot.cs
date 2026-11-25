using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Saves;

namespace Subaru.SubaruCode.Character;

/// <summary>
/// 选人界面（菜月昴）的自定义背景：左侧阵营小卡循环、右侧立绘静止、进入即播选人曲。
///
/// 结构（见 char_select_bg_subaru.tscn）：
///   Bg            1920×1080 整页底图（柔光 + 已融合的右侧立绘）
///   CardBeatrice  阵营小卡（904×1080，带 18px 外环，透明边）
///   CardVincent
///   CardSphinx
///
/// 小卡是「同一位置叠三张、靠 alpha 交叉淡入」——不是位移，所以三张必须严格对齐到 (60,0)。
/// </summary>
public partial class CharSelectBgRoot : Control
{
    /// <summary>选人曲：低音量铺底，不抢选人音效。</summary>
    private const string MusicPath = "res://Subaru/audio/char_select.ogg";

    /// <summary>音乐音量（dB）。-14 dB ≈ 原曲的 20%。</summary>
    private const float MusicVolumeDb = -14f;

    /// <summary>每张卡停留时间（秒）。</summary>
    private const double HoldSeconds = 4.5;

    /// <summary>交叉淡入时间（秒）。</summary>
    private const float FadeSeconds = 0.45f;

    private static readonly string[] CardNodes = { "CardBeatrice", "CardVincent", "CardSphinx" };

    /// <summary>上一次展示的卡序号：避免连抽同一张（1/3 概率，看起来会像没生效）。</summary>
    private static int _lastIndex = -1;

    private TextureRect[] _cards = Array.Empty<TextureRect>();
    private TextureRect _bg;
    private VideoStreamPlayer _intro;
    private int _index;
    private double _acc;
    private AudioStreamPlayer _music;
    private Tween _tween;
    private bool _started;
    private bool _layoutLogged;
    private bool _bgmDucked;

    /// <summary>本实例真正显示过一次之后才参与「是否已经离开」的判断。</summary>
    private bool _seenVisible;

    /// <summary>见过主菜单隐藏（= 我们确实在选人页里），之后再看到主菜单显示才算离开。</summary>
    private bool _menuHiddenSeen;

    /// <summary>
    /// 背景矩形尺寸。原版选人背景根节点是 anchors_preset=8 + offset(-960,-521)..(1600,679)，
    /// 也就是 **2560×1200 的居中矩形**（见原版 char_select_bg_regent.tscn / _ironclad.tscn）。
    /// 引擎 display/window/size/viewport=1920×1080 + stretch=canvas_items + aspect=expand，
    /// 所以这块矩形里**真正可见的只有正中的 1920×1080**，1K 屏和 2K 屏一样（只是像素密度不同）。
    /// 素材按 2560×1200 烘焙、内容 1:1 摆在正中，两边自然适配。
    /// </summary>
    private const float DesignW = 2560f;
    private const float DesignH = 1200f;

    /// <summary>阵营小卡的设计尺寸与左上角（画布坐标，= 设计区 (60,0) + 居中偏移 (320,60)）。</summary>
    private const float CardW = 904f;
    private const float CardH = 1080f;

    /// <summary>卡片在设计坐标里的左上角。</summary>
    private static readonly Vector2 CardOffset = new(380f, 60f);

    public override void _Ready()
    {
        try
        {
            _bg = GetNodeOrNull<TextureRect>("Bg");
            _intro = GetNodeOrNull<VideoStreamPlayer>("Intro");
            if (_intro != null)
            {
                // 视频自己循环播（和卡片轮播、音乐各自独立循环，互不干涉）
                _intro.Loop = true;
                _intro.Finished += OnIntroFinished;
            }

            var list = new System.Collections.Generic.List<TextureRect>();
            foreach (var name in CardNodes)
            {
                var node = GetNodeOrNull<TextureRect>(name);
                if (node != null)
                    list.Add(node);
            }

            _cards = list.ToArray();
            if (_cards.Length == 0)
            {
                GD.PushWarning("[Subaru] char select bg: no cards found");
                return;
            }

            _index = PickStartIndex();
            for (var i = 0; i < _cards.Length; i++)
            {
                // 场景里两张卡默认 visible=false：这里统一打开，之后只靠 alpha 控制显隐
                _cards[i].Visible = true;
                _cards[i].Modulate = new Color(1, 1, 1, i == _index ? 1f : 0f);
            }

            _started = true;
            ApplyLayout();
            // 首帧时控件可能还没被父节点排版好，等一帧再算一次
            Callable.From(ApplyLayout).CallDeferred();

            var vp = GetViewport();
            if (vp != null)
                vp.SizeChanged += ApplyLayout;

            // 音乐跟视频同一时刻起（StartMusic 在 PlayIntro 里调）
            PlayIntro();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] char select bg ready failed: " + e.Message);
        }
    }

    /// <summary>
    /// 按当前控件尺寸重排。
    ///
    /// 素材按 2560×1200（原版背景矩形）烘焙，内容 1:1 摆在正中 —— 所以这里**不需要放大**：
    /// 控件是 2560×1200 时 s 就是 1，立绘/卡片按设计像素原尺寸显示。
    /// 用 min() 而不是 max()：万一控件的宽高比不是 2.133:1，也只会等比缩小、不会拉变形。
    ///
    /// 之前用 max()（cover）把 1920×1080 的设计拉满 2560×1200，等于整体放大 1.333 倍，
    /// 这就是「部件全都很大」的根因。
    /// </summary>
    private void ApplyLayout()
    {
        try
        {
            var vpSize = Size;
            if (vpSize.X <= 1f || vpSize.Y <= 1f)
                vpSize = GetViewportRect().Size;
            if (vpSize.X <= 1f || vpSize.Y <= 1f)
                return;

            var s = Mathf.Min(vpSize.X / DesignW, vpSize.Y / DesignH);
            var offset = (vpSize - new Vector2(DesignW, DesignH) * s) * 0.5f;

            if (!_layoutLogged)
            {
                _layoutLogged = true;
                GD.Print($"[Subaru] char select bg layout: control={vpSize.X}x{vpSize.Y} s={s:F3} offset={offset.X:F0},{offset.Y:F0}");
            }

            if (_bg != null)
            {
                _bg.Position = offset;
                _bg.Size = new Vector2(DesignW, DesignH) * s;
                _bg.StretchMode = TextureRect.StretchModeEnum.Scale;
                _bg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            }

            if (_intro != null)
            {
                _intro.Position = offset;
                _intro.Size = new Vector2(DesignW, DesignH) * s;
            }

            foreach (var card in _cards)
            {
                card.Scale = new Vector2(s, s);
                card.Position = offset + CardOffset * s;
                card.Size = new Vector2(CardW, CardH);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] char select bg layout failed: " + e.Message);
        }
    }

    /// <summary>三张里抽一张；和上次相同就再抽一次。</summary>
    private int PickStartIndex()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        var idx = (int)(rng.Randi() % (uint)_cards.Length);
        if (_cards.Length > 1 && idx == _lastIndex)
            idx = (idx + 1 + (int)(rng.Randi() % (uint)(_cards.Length - 1))) % _cards.Length;

        _lastIndex = idx;
        return idx;
    }

    public override void _Process(double delta)
    {
        WatchLeftScreen();

        if (!_started)
            return;

        _acc += delta;
        if (_acc < HoldSeconds)
            return;

        _acc = 0;
        Advance();
    }

    /// <summary>
    /// 离开选人页时把音乐停掉。
    ///
    /// 日志实证（`godot.log`）：`char select music stopped` 只出现在两种时刻 ——
    /// 换角色导致背景场景被重建、以及退出游戏那一刻。**返回主菜单时这个场景并没有被销毁**，
    /// 所以 `_ExitTree` 抓不到「退回主菜单」，曲子会一直循环下去。
    ///
    /// 这里每帧看两个信号，任一成立就停（并且还原原版 BGM 音量）：
    ///   1) 自己已经不在可见树里 —— 场景被隐藏；
    ///   2) 主菜单重新显示 —— 先见过它隐藏、再看到它显示才算，避免进场那一帧误判。
    /// </summary>
    private void WatchLeftScreen()
    {
        // 还没真正显示过就不参与判断（进场头一两帧可能还不在可见状态）
        if (!_seenVisible)
        {
            if (IsVisibleInTree())
                _seenVisible = true;
            return;
        }

        var playing = _music != null && GodotObject.IsInstanceValid(_music) && _music.Playing;
        if (!playing && !_bgmDucked)
        {
            _menuHiddenSeen = false;
            return;
        }

        var hidden = !IsVisibleInTree();

        var menuShown = NGame.Instance?.MainMenu is CanvasItem menu && menu.IsVisibleInTree();
        if (!menuShown)
            _menuHiddenSeen = true;

        if (!hidden && !(menuShown && _menuHiddenSeen))
            return;

        GD.Print($"[Subaru] char select music stopped (reason={(hidden ? "hidden" : "mainmenu")})");
        StopMusic();
        _menuHiddenSeen = false;
    }

    private void Advance()
    {
        try
        {
            var from = _index;
            _index = (_index + 1) % _cards.Length;
            _lastIndex = _index;

            _tween?.Kill();
            _tween = CreateTween();
            _tween.SetParallel(true);

            var a = _cards[from];
            var b = _cards[_index];
            b.Visible = true;
            b.Modulate = new Color(1, 1, 1, 0);

            _tween.TweenProperty(a, "modulate:a", 0f, FadeSeconds);
            _tween.TweenProperty(b, "modulate:a", 1f, FadeSeconds);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] char select bg advance failed: " + e.Message);
        }
    }

    /// <summary>
    /// 起选人曲。**独立循环**：和视频、卡片轮播各走各的节奏（曲长 4:01，视频 29.6 s）。
    /// 只在进选人页时起一次，离开时由 <see cref="_ExitTree"/> 停掉。
    /// </summary>
    private void StartMusic()
    {
        try
        {
            // 已经在播：不重来（点了角色重播视频时，音乐保持自己的循环相位）
            if (_music != null && GodotObject.IsInstanceValid(_music))
                return;

            if (!ResourceLoader.Exists(MusicPath))
            {
                GD.Print($"[Subaru] char select music missing: {MusicPath}");
                return;
            }

            var stream = ResourceLoader.Load<AudioStream>(MusicPath);
            if (stream == null)
                return;

            if (stream is AudioStreamOggVorbis ogg)
                ogg.Loop = true;
            else if (stream is AudioStreamWav wav)
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;

            _music = new AudioStreamPlayer
            {
                Name = "SubaruCharSelectMusic",
                Bus = "Master",
                VolumeDb = MusicVolumeDb,
                Stream = stream,
            };

            AddChild(_music);
            _music.Play();

            GD.Print($"[Subaru] char select music started ({MusicVolumeDb} dB, loop)");
            DuckVanillaBgm();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] char select music failed: " + e.Message);
        }
    }

    /// <summary>停选人曲并还原原版 BGM 音量（视频停 / 节点销毁时调用）。</summary>
    private void StopMusic()
    {
        try
        {
            if (_music != null && GodotObject.IsInstanceValid(_music))
            {
                _music.Stop();
                GD.Print("[Subaru] char select music stopped");
            }
        }
        catch
        {
            // 忽略
        }

        RestoreVanillaBgm();
    }

    /// <summary>
    /// 播放右侧开场动画。视频自己循环（<c>Loop = true</c>），播完接着从头播；
    /// 音乐独立循环，不跟着视频走。
    /// </summary>
    private void PlayIntro()
    {
        try
        {
            if (_intro == null)
                return;

            _intro.Visible = true;
            _intro.Loop = true;
            _intro.Play();
            GD.Print("[Subaru] char select intro started (loop)");

            StartMusic();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] intro play failed: " + e.Message);
        }
    }

    /// <summary>外部（点击角色）调用：只把动画从头重播，音乐按自己的循环走、不打断。</summary>
    public void ReplayIntro()
    {
        try
        {
            if (_intro == null)
                return;

            _intro.Stop();
            _intro.Visible = true;
            _intro.Loop = true;
            _intro.Play();
            GD.Print("[Subaru] char select intro replayed");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] intro replay failed: " + e.Message);
        }
    }

    private void OnIntroFinished()
    {
        // Loop = true 时不会触发；留作兜底：万一有人把循环关掉，也只是停在末帧，不动音乐。
        try
        {
            if (_intro != null)
            {
                _intro.Visible = false;
                GD.Print("[Subaru] char select intro finished (holding last frame)");
            }
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 把我们自己的曲子播上之后，把原版 BGM 音量压到 0（此时响的是主菜单曲）。
    ///
    /// 为什么用音量而不是 StopMusic()：StopMusic 走的是 FMOD 音乐事件，
    /// 我们之前在战斗音乐里踩过 —— 它会把环境音事件一起停掉并置空，
    /// 之后进休息处的 campfire 回调作用在空事件上直接 0xc0000005。
    /// 只改总线音量不动事件，退出时再按玩家设置还原，跟「进游戏 / 退菜单」的时序无关。
    /// </summary>
    private void DuckVanillaBgm()
    {
        try
        {
            var audio = NAudioManager.Instance;
            if (audio == null)
                return;

            audio.SetBgmVol(0f);
            _bgmDucked = true;
            GD.Print("[Subaru] menu bgm ducked");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] duck bgm failed: " + e.Message);
        }
    }

    /// <summary>还原 BGM 音量：取玩家设置里的 VolumeBgm（NGame.GameStartup 也是这么还原的）。</summary>
    private void RestoreVanillaBgm()
    {
        if (!_bgmDucked)
            return;

        _bgmDucked = false;

        try
        {
            var audio = NAudioManager.Instance;
            if (audio == null)
                return;

            var vol = 0.7f;
            try
            {
                var saved = SaveManager.Instance?.SettingsSave;
                if (saved != null)
                    vol = saved.VolumeBgm;
            }
            catch
            {
                // 拿不到设置就用默认 0.7
            }

            audio.SetBgmVol(vol);
            GD.Print($"[Subaru] menu bgm restored ({vol:F2})");
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] restore bgm failed: " + e.Message);
        }
    }

    public override void _ExitTree()
    {
        try
        {
            _tween?.Kill();
            _tween = null;

            StopMusic();
        }
        catch
        {
            // 忽略：节点正在销毁
        }
    }
}
