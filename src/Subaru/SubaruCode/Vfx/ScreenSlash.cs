using System;
using System.Threading.Tasks;
using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// **屏幕级**斩击反馈：沿切线把画面「撕开一道」+ 边缘色差（2026-11-22 T2）。
///
/// <para>为什么需要：刀光画得再细也只是一张贴图，玩家感觉到「重」是因为**画面本身**动了。
/// 这里读屏幕纹理，在切线附近（高斯带内）把两侧像素朝相反方向推 9~14px、红蓝通道分离 2~3px，
/// 110~150ms 内衰减干净 —— 这就是「贵的打光」里最便宜、性价比最高的一条。</para>
///
/// <para><b>层级</b>：挂在 <see cref="RoomVfx.Front"/>（= 房间的 <c>CombatVfxContainer</c>），
/// 手牌 UI 在那个容器的**上面**，所以全屏 shader 不会糊到手牌，也不会让手牌错位。</para>
///
/// <para><b>失败即降级</b>：整段包在 try/catch 里。shader 编译失败（D3D12 PSO 未缓存 / 驱动问题）时
/// <see cref="Shader.GetShaderUniformList"/> 会拿到空的，我们在**加节点之前**就判掉并打日志，
/// 绝不把一个坏的 <see cref="ColorRect"/> 留在屏幕上（那会整屏变黑）。</para>
/// </summary>
public static class ScreenSlash
{
    /// <summary>
    /// 屏幕空间着色器。所有量都按**像素**算（用 <c>SCREEN_PIXEL_SIZE</c> 换），
    /// 所以分辨率、UI 缩放都不用管。
    /// </summary>
    private const string Source = @"
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform vec2 cut_origin_px = vec2(0.0, 0.0);
uniform vec2 cut_normal = vec2(0.0, 1.0);
uniform vec2 push_px = vec2(0.0, 0.0);
uniform float ab_px = 0.0;
uniform float band_px = 170.0;
uniform float glow_px = 6.0;
uniform float glow = 0.0;

void fragment() {
	vec2 px = SCREEN_UV / SCREEN_PIXEL_SIZE;
	float d = dot(px - cut_origin_px, cut_normal);

	// 切线两侧的高斯衰减：离切线越远越不动，所以是「一道裂口」而不是整屏位移
	float w = exp(-(d * d) / max(1.0, band_px * band_px));
	float side = d >= 0.0 ? 1.0 : -1.0;

	vec2 uv = SCREEN_UV + side * push_px * w * SCREEN_PIXEL_SIZE;

	// 色差：越靠近切线越明显（整体留一点底噪，边缘才有金属感）
	vec2 ca = vec2(ab_px * (0.25 + 0.75 * w) * SCREEN_PIXEL_SIZE.x, 0.0);
	vec3 col;
	col.r = texture(screen_tex, clamp(uv + ca, vec2(0.0, 0.0), vec2(1.0, 1.0))).r;
	col.g = texture(screen_tex, clamp(uv, vec2(0.0, 0.0), vec2(1.0, 1.0))).g;
	col.b = texture(screen_tex, clamp(uv - ca, vec2(0.0, 0.0), vec2(1.0, 1.0))).b;

	// 切口上的冷白光（刀锋本身的高光）
	float g = exp(-(d * d) / max(1.0, glow_px * glow_px));
	col += vec3(0.88, 0.94, 1.0) * g * glow;

	COLOR = vec4(col, 1.0);
}";

    /// <summary>shader 至少要有这么多个 uniform 才算编译成功（少了就是坏了）。</summary>
    private const int ExpectedUniforms = 7;

    /// <summary>
    /// 播一次屏幕撕裂。**不阻塞**：内部自己起一个后台任务，到时间自己清理。
    /// 调用点不需要 await，失败也只是少一层效果。
    /// </summary>
    public static void Play(Vector2 view, Vector2 center, float deg, float designScale, bool heavy)
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
                return;

            var root = RoomVfx.Front("SubaruReinhardScreen");
            if (root == null)
            {
                GD.Print("[Subaru] reinhard screen: slice skipped (no front container)");
                return;
            }

            _ = RunAsync(tree, root, view, center, deg, designScale, heavy);
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] reinhard screen failed: " + e.Message);
        }
    }

    private static async Task RunAsync(SceneTree tree, Node2D root, Vector2 view, Vector2 center,
        float deg, float s, bool heavy)
    {
        var logged = false;

        try
        {
            var shader = new Shader { Code = Source };
            var uniforms = shader.GetShaderUniformList();
            if (uniforms == null || uniforms.Count < ExpectedUniforms)
            {
                GD.Print($"[Subaru] reinhard screen: slice skipped (shader failed, uniforms={uniforms?.Count ?? 0})");
                return;
            }

            var mat = new ShaderMaterial { Shader = shader };

            var th = Mathf.DegToRad(deg);
            var normal = new Vector2(-Mathf.Sin(th), Mathf.Cos(th));

            mat.SetShaderParameter("cut_origin_px", center);
            mat.SetShaderParameter("cut_normal", normal);
            mat.SetShaderParameter("band_px", 170f * s);
            mat.SetShaderParameter("glow_px", (heavy ? 9f : 6f) * s);

            // 屏幕纹理必须先拷一份后台缓冲，否则读到的是「正在画的那一帧」（会自反馈）
            var buf = new BackBufferCopy
            {
                CopyMode = BackBufferCopy.CopyModeEnum.Viewport,
                Rect = new Rect2(Vector2.Zero, view),
            };
            root.AddChild(buf);

            var rect = new ColorRect
            {
                Position = Vector2.Zero,
                Size = view,
                Color = Colors.White,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = mat,
            };
            root.AddChild(rect);

            var durMs = heavy ? 150f : 110f;
            var pushMax = (heavy ? 14f : 9f) * s;
            var abMax = (heavy ? 3.2f : 2.2f) * s;

            GD.Print($"[Subaru] reinhard screen: slice on (shader=ok, buf=ok, heavy={heavy}, " +
                     $"dur={durMs:F0}ms, push={pushMax:F1}px, ab={abMax:F1}px)");
            logged = true;

            await Anim.Ms(tree, durMs, el =>
            {
                if (!GodotObject.IsInstanceValid(rect))
                    return;

                var rise = Mathf.Clamp(el / 24f, 0f, 1f);
                var fall = Mathf.Pow(Mathf.Clamp(1f - Mathf.Max(0f, el - 24f) / Mathf.Max(1f, durMs - 24f), 0f, 1f), 2f);
                var k = rise * fall;

                mat.SetShaderParameter("push_px", normal * (pushMax * k));
                mat.SetShaderParameter("ab_px", abMax * k);
                mat.SetShaderParameter("glow", 0.55f * k);
            });
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] reinhard screen failed: " + e.Message);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(root))
                root.QueueFree();

            if (logged)
                GD.Print("[Subaru] reinhard screen: done");
        }
    }
}
