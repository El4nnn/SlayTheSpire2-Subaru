using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Subaru.SubaruCode.UI;

/// <summary>
/// 死归的视觉反馈：致命伤害被截断的那一刻，
/// 屏幕闪一下紫色 + 玩家的血条短暂变紫，让玩家明白「刚刚死了一次，被死归拉回来了」，
/// 而不是看到血量停在 1 像锁血。
///
/// 只做表现，不动任何数值与流程。
/// </summary>
public static class RewindVisuals
{
    private static readonly Color FlashColor = new(0.45f, 0.10f, 0.62f);

    /// <summary>血条染紫色。RewindHealthMask 也用这个值，两处必须一致。</summary>
    internal static readonly Color BarTint = new(0.72f, 0.38f, 1.0f);

    /// <summary>死归触发时调用（紫光 A）。</summary>
    public static void PlayDeathFlash(Creature player)
    {
        // 第三方求解器在线程池线程里模拟致命伤害时也会走到这里（它的 hook 镜像会调我们的补丁）。
        // Godot 不允许非主线程 AddChild —— 玩家日志里就是这么炸出
        // "Adding children to a node inside the SceneTree is only allowed from the main thread"。
        // 纯视觉，模拟里直接跳过即可。
        if (!Subaru.SubaruCode.Rewind.MainThreadGuard.IsMainThread)
            return;

        try
        {
            BeginDeathFlash();
            TintHealthBar(player);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] rewind visual failed: " + e.Message);
        }
    }

    /// <summary>
    /// 兜底：把整套死归覆盖层（紫光 A / 黑幕 / 闪光）全部强制拆掉。
    ///
    /// 死归流程里必须调一次。曾经出过「回档后整屏一直是紫的」：
    /// 同一回合里挨了第二次致命伤害，又调了一次 PlayDeathFlash 建了新的紫层，
    /// 而演出早就跑完、没人再去 EndDeathFlash 它，那层就永远亮着。
    /// </summary>
    public static void ForceClearAll()
    {
        _deathFlashActive = false;

        try
        {
            if (_deathFlashLayer != null && GodotObject.IsInstanceValid(_deathFlashLayer))
                _deathFlashLayer.QueueFree();
        }
        catch { /* 忽略 */ }

        _deathFlashLayer = null;

        try
        {
            if (_blackout != null && GodotObject.IsInstanceValid(_blackout))
                _blackout.QueueFree();
        }
        catch { /* 忽略 */ }

        _blackout = null;

        // 名字兜底：万一有孤儿层（上一帧建的、字段还没赋值就被顶掉），按名字清
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
            {
                foreach (var child in tree.Root.GetChildren())
                {
                    if (child is CanvasLayer layer && layer.Name.ToString().StartsWith("Subaru"))
                        layer.QueueFree();
                }
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>紫光 A 的淡入时长（秒）。</summary>
    private const double DeathFlashFadeIn = 0.10;

    /// <summary>紫光 A 的峰值不透明度。</summary>
    private const float DeathFlashPeak = 0.5f;

    private static CanvasLayer _deathFlashLayer;
    private static ulong _deathFlashStartMs;
    private static bool _deathFlashActive;

    /// <summary>
    /// 紫光 A：淡入到峰值后**一直亮着**，不再自己淡出——
    /// 它要盖住整段「挨打到跪地」，直到魔女剪影出现的那一刻才由 EndDeathFlash 撤掉。
    /// </summary>
    private static void BeginDeathFlash()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return;

        if (_deathFlashLayer != null && GodotObject.IsInstanceValid(_deathFlashLayer))
        {
            _deathFlashLayer.QueueFree();
            _deathFlashLayer = null;
        }

        _deathFlashStartMs = Time.GetTicksMsec();
        _deathFlashActive = true;

        _deathFlashLayer = new CanvasLayer { Layer = 128, Name = "SubaruDeathFlashA" };
        var rect = new ColorRect
        {
            Color = new Color(FlashColor.R, FlashColor.G, FlashColor.B, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _deathFlashLayer.AddChild(rect);
        tree.Root.AddChild(_deathFlashLayer);

        // 只淡入，不排淡出：峰值之后原地停住
        var tween = tree.CreateTween();
        tween.TweenProperty(rect, "color:a", DeathFlashPeak, DeathFlashFadeIn);
    }

    /// <summary>紫光 A 已经亮了多久（秒）。没亮着就是 0。</summary>
    public static double ElapsedSinceDeathFlash
        => _deathFlashActive ? (Time.GetTicksMsec() - _deathFlashStartMs) / 1000.0 : 0.0;

    /// <summary>把紫光 A 撤掉（魔女出现的同一帧调用）。</summary>
    public static void EndDeathFlash(double fadeSeconds = 0.15)
    {
        _deathFlashActive = false;

        if (_deathFlashLayer == null || !GodotObject.IsInstanceValid(_deathFlashLayer))
            return;

        var layer = _deathFlashLayer;
        _deathFlashLayer = null;

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            layer.QueueFree();
            return;
        }

        var rect = layer.GetChildOrNull<ColorRect>(0);
        if (rect == null || !GodotObject.IsInstanceValid(rect))
        {
            layer.QueueFree();
            return;
        }

        var tween = tree.CreateTween();
        tween.TweenProperty(rect, "color:a", 0.0f, fadeSeconds);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(layer))
                layer.QueueFree();
        }));
    }

    /// <summary>
    /// 「复活」那一帧的闪光：死亡动画播完、人物从躺着瞬间跳回待机的交界处。
    /// 比死亡闪光更短更亮，用来盖住那一帧的跳变。
    /// </summary>
    public static void PlayReviveFlash()
    {
        try
        {
            PlayScreenFlash(0.62f, 0.05f, 0.30f);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] revive flash failed: " + e.Message);
        }
    }

    private static CanvasLayer _blackout;

    /// <summary>死归黑屏底图的兜底路径（素材池取不到时用）。缺图时退化成纯黑，不会崩。</summary>
    private const string DefaultFaceTexturePath = "res://Subaru/images/ui/rewind_face.png";

    /// <summary>剪影的信号不稳效果（撕裂 / 扫描线 / 色差 / 频闪）。缺了只是没有特效。</summary>
    private const string FaceShaderPath = "res://Subaru/shaders/rewind_face.gdshader";

    /// <summary>黑幕淡入时长（秒）。shader 的时钟是「淡入 + 停留」，所以它要参与计算。</summary>
    private const double FaceFadeIn = 0.10;

    /// <summary>
    /// 压黑：死亡动画结束后先黑一下（现在这一下是嫉妒魔女的剪影），再闪紫光。
    /// 黑层在闪光下面（127 vs 128），所以后面的紫闪仍然能盖在图上。
    ///
    /// 淡入淡出统一动根节点的 modulate，黑底和剪影一起变透明，
    /// 这样 RevealFromBlackout 只需要处理一个属性。
    /// </summary>
    public static async Task BlackoutAsync(string faceTexturePath, double holdSeconds)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                return;

            if (_blackout != null && GodotObject.IsInstanceValid(_blackout))
            {
                _blackout.QueueFree();
                _blackout = null;
            }

            var shader = GD.Load<Shader>(FaceShaderPath);
            if (shader == null)
                GD.PushWarning("[Subaru] rewind face shader missing: " + FaceShaderPath);

            // shader 时钟覆盖「淡入 + 停留」整段，频闪按归一化进度分布，改时长不用动 shader
            var clockSeconds = FaceFadeIn + holdSeconds;
            ShaderMaterial faceMat = null;

            _blackout = new CanvasLayer { Layer = 127, Name = "SubaruRewindBlackout" };

            var root = new Control
            {
                Name = "Root",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = new Color(1f, 1f, 1f, 0f),
            };
            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _blackout.AddChild(root);

            // 黑底垫在剪影下面：剪影按 16:9 裁过，遇到超宽/超长屏不会露出战场
            var curtain = new ColorRect
            {
                Color = Colors.Black,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            curtain.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(curtain);

            var facePath = string.IsNullOrEmpty(faceTexturePath) ? DefaultFaceTexturePath : faceTexturePath;
            var face = ResourceLoader.Exists(facePath) ? GD.Load<Texture2D>(facePath) : null;
            if (face != null)
            {
                var image = new TextureRect
                {
                    Texture = face,
                    // 关键：默认会按贴图原始像素撑大所在容器，必须忽略 + 铺满裁切
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

                // 信号不稳：撕裂 / 扫描线 / 色差 / 频闪全在 shader 里按归一化进度算，
                // 这里只负责把「已经过去多久」喂进去。shader 缺了也不影响出图。
                if (shader != null)
                {
                    var mat = new ShaderMaterial { Shader = shader, ResourceName = "SubaruRewindFaceFx" };
                    var size = tree.Root.GetVisibleRect().Size;
                    mat.SetShaderParameter("u_res", size);
                    mat.SetShaderParameter("u_total", (float)clockSeconds);
                    mat.SetShaderParameter("u_u", 0.0f);
                    image.Material = mat;
                    faceMat = mat;
                }

                root.AddChild(image);
            }
            else
            {
                GD.PushWarning("[Subaru] rewind face texture missing: " + facePath);
            }

            tree.Root.AddChild(_blackout);

            var tween = tree.CreateTween();
            tween.TweenProperty(root, "modulate:a", 1.0f, (float)FaceFadeIn);

            // 频闪时钟：和淡入同一帧起跑，跑满「淡入 + 停留」这么长。
            // 用 TweenMethod 推进，不需要挂 _Process。
            if (faceMat != null)
            {
                var clock = tree.CreateTween();
                var mat = faceMat;
                clock.TweenMethod(
                    Callable.From<float>(v => mat.SetShaderParameter("u_u", v)),
                    0.0f, (float)clockSeconds, clockSeconds);
            }

            await tree.ToSignal(tween, Tween.SignalName.Finished);

            if (holdSeconds > 0)
                await tree.ToSignal(tree.CreateTimer(holdSeconds), SceneTreeTimer.SignalName.Timeout);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] blackout failed: " + e.Message);
        }
    }

    /// <summary>
    /// 素材 5：把这一整段黑幕换成视频（视频自带音轨，图与语音都不出）。
    ///
    /// 返回视频长度（秒）；<b>返回 0 表示视频用不了</b>（引擎没编译 Theora、或文件没打进 pck），
    /// 调用方据此回落到图片素材，不会出现「黑着不动」。
    ///
    /// 这个引擎没有 .ogv 的导入器，所以文件是当原始资源打进 pck、
    /// 运行时用 <see cref="VideoStreamTheora"/> 按路径直接读的——不能用 GD.Load。
    ///
    /// 播完只拆视频节点，黑幕留着给 <see cref="RevealFromBlackout"/> 淡出，和图片那条路的收尾一致。
    /// </summary>
    public static async Task<double> PlayWitchVideoAsync(string videoPath)
    {
        CanvasLayer layer = null;
        Control root = null;
        VideoStreamPlayer video = null;

        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                return 0;

            if (string.IsNullOrEmpty(videoPath))
                return 0;

            if (_blackout != null && GodotObject.IsInstanceValid(_blackout))
            {
                _blackout.QueueFree();
                _blackout = null;
            }

            var stream = new VideoStreamTheora { File = videoPath };

            layer = new CanvasLayer { Layer = 127, Name = "SubaruRewindBlackout" };
            _blackout = layer;

            root = new Control
            {
                Name = "Root",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = new Color(1f, 1f, 1f, 0f),
            };
            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(root);

            var curtain = new ColorRect
            {
                Color = Colors.Black,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            curtain.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(curtain);

            video = new VideoStreamPlayer
            {
                Name = "SubaruWitchVideo",
                Stream = stream,
                Expand = true,
                Loop = false,
                Bus = "Master",
                // 素材 5 的响度补偿：视频音轨本身偏轻，播放时抬 6 dB（不改文件）
                VolumeDb = 6.0f,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(video);

            tree.Root.AddChild(layer);

            var tween = tree.CreateTween();
            tween.TweenProperty(root, "modulate:a", 1.0f, (float)FaceFadeIn);

            video.Play();

            // 等两帧才有 stream length：播放实例是在 Play 之后才建起来的
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var length = video.GetStreamLength();
            if (length <= 0)
            {
                GD.Print("[Subaru] rewind witch video unavailable: " + videoPath);
                CleanupVideoLayer(ref layer, ref root);
                return 0;
            }

            GD.Print($"[Subaru] rewind witch video playing {length:0.###}s");

            await tree.ToSignal(
                tree.CreateTimer(length + 0.15), SceneTreeTimer.SignalName.Timeout);

            if (video != null && GodotObject.IsInstanceValid(video))
            {
                video.Stop();
                video.QueueFree();
            }

            return length;
        }
        catch (Exception e)
        {
            GD.Print("[Subaru] rewind witch video failed: " + e.Message);
            CleanupVideoLayer(ref layer, ref root);
            return 0;
        }
    }

    /// <summary>视频这条路失败时把整个黑幕拆掉，让图片素材能从干净状态开始。</summary>
    private static void CleanupVideoLayer(ref CanvasLayer layer, ref Control root)
    {
        try
        {
            if (root != null && GodotObject.IsInstanceValid(root))
                root.QueueFree();

            if (layer != null && GodotObject.IsInstanceValid(layer))
                layer.QueueFree();

            if (ReferenceEquals(_blackout, layer))
                _blackout = null;
        }
        catch
        {
            // 节点已销毁时忽略
        }
    }

    /// <summary>把黑层淡掉，露出已经站回待机的角色。</summary>
    public static async void RevealFromBlackout(double fadeSeconds = 0.25)
    {
        try
        {
            if (_blackout == null || !GodotObject.IsInstanceValid(_blackout))
                return;

            if (Engine.GetMainLoop() is not SceneTree tree)
                return;

            var root = _blackout.GetNodeOrNull<Control>("Root");
            if (root == null || !GodotObject.IsInstanceValid(root))
            {
                _blackout.QueueFree();
                _blackout = null;
                return;
            }

            var tween = tree.CreateTween();
            tween.TweenProperty(root, "modulate:a", 0.0f, fadeSeconds);
            await tree.ToSignal(tween, Tween.SignalName.Finished);

            if (_blackout != null && GodotObject.IsInstanceValid(_blackout))
                _blackout.QueueFree();

            _blackout = null;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] blackout reveal failed: " + e.Message);
        }
    }

    private static async void PlayScreenFlash(float peak, double fadeIn, double fadeOut)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                return;

            var layer = new CanvasLayer { Layer = 128, Name = "SubaruRewindFlash" };
            var rect = new ColorRect
            {
                Color = new Color(FlashColor.R, FlashColor.G, FlashColor.B, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(rect);
            tree.Root.AddChild(layer);

            var tween = tree.CreateTween();
            tween.TweenProperty(rect, "color:a", peak, fadeIn);
            tween.TweenProperty(rect, "color:a", 0.0f, fadeOut);
            await tree.ToSignal(tween, Tween.SignalName.Finished);

            if (GodotObject.IsInstanceValid(layer))
                layer.QueueFree();
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] screen flash failed: " + e.Message);
        }
    }

    private static async void TintHealthBar(Creature creature)
    {
        try
        {
            if (creature == null)
                return;

            var node = creature.GetCreatureNode();
            if (node == null)
                return;

            var bar = FindHealthBar(node);
            if (bar == null)
                return;

            // 遮罩已经把血条染紫并会在演出结束时自己还原，
            // 这里再染一次只会把「原色」污染成紫色。
            if (RewindHealthMask.IsActive)
                return;

            var original = bar.Modulate;
            bar.Modulate = BarTint;

            if (Engine.GetMainLoop() is not SceneTree tree)
                return;

            await tree.ToSignal(tree.CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);

            // 死归遮罩接管了这条血条（要把紫色保持整段演出），
            // 这里就别把它还原成白色，否则演出中途血条会闪一下。
            if (RewindHealthMask.IsActive)
                return;

            if (GodotObject.IsInstanceValid(bar))
                bar.Modulate = original;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] health bar tint failed: " + e.Message);
        }
    }

    /// <summary>递归找血条节点（类型名里带 HealthBar 的 Control）。</summary>
    private static Control FindHealthBar(Node root)
    {
        if (root == null)
            return null;

        if (root is Control control && root.GetType().Name.Contains("HealthBar"))
            return control;

        foreach (var child in root.GetChildren())
        {
            var found = FindHealthBar(child);
            if (found != null)
                return found;
        }

        return null;
    }
}
