using System;
using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 一个 quad + 一个 shader 做出来的「光柱」。
///
/// <para>2026-11-22 第二版：形状改由**贴图**决定（`beam_core.png` 剖面 + `beam_noise.png` 滚动噪声），
/// shader 只负责调制（强度 / 溶解 / 颜色）。贴图缺失时 `use_tex=0`，退回片元里的程序形状。</para>
///
/// <para>参数来自 <see cref="BeamPalette"/>；拿不到 shader 时 <see cref="Create"/> 返回 null，
/// 调用方退回首版的三层 Line2D（见 BallroyBeam）。</para>
/// </summary>
public sealed class BeamQuad
{
    private const int MinUniforms = 8;

    private const string Source = """
    shader_type canvas_item;
    render_mode blend_add, unshaded;

    uniform sampler2D beam_tex : hint_default_black, filter_linear, repeat_disable;
    uniform sampler2D noise_tex : hint_default_black, filter_linear, repeat_enable;
    uniform float use_tex = 1.0;

    uniform vec4 color_top : source_color = vec4(0.50, 0.89, 0.94, 1.0);
    uniform vec4 color_mid : source_color = vec4(0.91, 0.97, 1.00, 1.0);
    uniform vec4 color_bottom : source_color = vec4(0.29, 0.66, 0.88, 1.0);
    uniform vec4 core_color : source_color = vec4(1.0, 1.0, 1.0, 0.95);
    uniform vec4 volume_color : source_color = vec4(0.60, 0.74, 0.92, 0.30);
    uniform float core_width = 0.12;
    uniform float volume_width = 0.55;
    uniform float glow_power = 3.0;
    uniform float rim_strength = 0.55;
    uniform float noise_strength = 0.55;
    uniform float noise_scale = 9.0;
    uniform float scroll_speed = 1.8;
    uniform float intensity = 1.0;
    uniform float dissolve = 0.0;
    uniform float time_offset = 0.0;

    float hash21(vec2 p) {
        p = fract(p * vec2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return fract(p.x * p.y);
    }

    float vnoise(vec2 p) {
        vec2 i = floor(p);
        vec2 f = fract(p);
        vec2 u = f * f * (3.0 - 2.0 * f);
        float a = hash21(i);
        float b = hash21(i + vec2(1.0, 0.0));
        float c = hash21(i + vec2(0.0, 1.0));
        float d = hash21(i + vec2(1.0, 1.0));
        return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
    }

    void fragment() {
        float d = abs(UV.x - 0.5) * 2.0;
        float t = TIME * scroll_speed + time_offset;

        // 程序形状（贴图缺失时的退路）
        float core = smoothstep(core_width, 0.0, d);
        float vol = pow(clamp(1.0 - d / max(volume_width, 0.001), 0.0, 1.0), glow_power);
        float rim = smoothstep(volume_width * 0.90, volume_width, d) * rim_strength;
        float proc = core + vol * volume_color.a + rim * 0.22;

        // 贴图形状：剖面图沿柱体拉伸
        float tex = texture(beam_tex, vec2(UV.x, 1.0 - UV.y)).r;
        float shape = mix(proc, tex, use_tex);

        // 噪声：优先采样贴图，缺失时退回程序噪声
        float tn = texture(noise_tex, vec2(UV.x * 1.7, UV.y * 2.4 - t * 0.35)).r;
        float pn = 0.5 + 0.5 * vnoise(vec2(UV.x * noise_scale, UV.y * noise_scale * 0.45 - t));
        float n = mix(pn, tn, use_tex);
        float flow = mix(1.0, 0.42 + 1.25 * n, noise_strength);

        float cap = smoothstep(0.0, 0.05, UV.y) * smoothstep(1.0, 0.95, UV.y);

        float a = shape * flow * cap * intensity;
        a *= 1.0 - dissolve * (0.55 + 0.45 * n);

        // 三段颜色：顶 → 中 → 底
        vec3 grad = UV.y < 0.5
            ? mix(color_top.rgb, color_mid.rgb, smoothstep(0.0, 0.5, UV.y))
            : mix(color_mid.rgb, color_bottom.rgb, smoothstep(0.5, 1.0, UV.y));
        vec3 col = mix(grad, core_color.rgb, clamp(core * 0.45, 0.0, 0.85));
        COLOR = vec4(col * a, a);
    }
    """;

    private static Shader? _shader;
    private static bool _shaderBroken;

    private readonly MeshInstance2D _node;
    private readonly ShaderMaterial _mat;
    private readonly float _width;

    private BeamQuad(MeshInstance2D node, ShaderMaterial mat, float width)
    {
        _node = node;
        _mat = mat;
        _width = width;
    }

    /// <summary>节点（调用方自己负责 QueueFree）。</summary>
    public MeshInstance2D Node => _node;

    /// <summary>建一根光柱：<paramref name="from"/> 到 <paramref name="to"/>（屏幕坐标）。失败返回 null。</summary>
    public static BeamQuad? Create(Node2D parent, Vector2 from, Vector2 to, BeamPalette p, float s, float intensity = 0f)
    {
        try
        {
            var shader = GetShader();
            if (shader == null)
                return null;

            var width = Mathf.Max(8f, p.VolumeWidth * s);
            var length = Mathf.Max(8f, from.DistanceTo(to));

            var mesh = new QuadMesh { Size = new Vector2(width, length) };
            var mat = new ShaderMaterial { Shader = shader };

            var core = VfxTextures.BeamCore;
            var noise = VfxTextures.BeamNoise;
            var useTex = core != null ? 1f : 0f;
            if (core != null)
                mat.SetShaderParameter("beam_tex", core);
            if (noise != null)
                mat.SetShaderParameter("noise_tex", noise);
            mat.SetShaderParameter("use_tex", useTex);

            mat.SetShaderParameter("color_top", p.Top);
            mat.SetShaderParameter("color_mid", p.Mid);
            mat.SetShaderParameter("color_bottom", p.Bottom);
            mat.SetShaderParameter("core_color", p.Core);
            mat.SetShaderParameter("volume_color", p.Volume);
            mat.SetShaderParameter("core_width", Mathf.Clamp(p.CoreWidth / Mathf.Max(1f, p.VolumeWidth), 0.01f, 0.9f));
            mat.SetShaderParameter("volume_width", 0.96f);
            mat.SetShaderParameter("glow_power", p.GlowPower);
            mat.SetShaderParameter("noise_strength", p.Noise);
            mat.SetShaderParameter("noise_scale", 9.0f);
            mat.SetShaderParameter("scroll_speed", 1.8f);
            mat.SetShaderParameter("intensity", intensity);
            mat.SetShaderParameter("dissolve", 0f);
            mat.SetShaderParameter("time_offset", (float)GD.RandRange(0.0, 40.0));

            var node = new MeshInstance2D
            {
                Mesh = mesh,
                Material = mat,
                Position = (from + to) * 0.5f,
            };

            parent.AddChild(node);
            var beam = new BeamQuad(node, mat, width);
            beam.SetSpan(from, to);
            return beam;
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] beam quad failed: " + e.Message);
            return null;
        }
    }

    /// <summary>改长度（落下 / 收窄用）。</summary>
    public void SetSpan(Vector2 from, Vector2 to)
    {
        if (!GodotObject.IsInstanceValid(_node))
            return;

        var length = Mathf.Max(1f, from.DistanceTo(to));
        if (_node.Mesh is QuadMesh qm)
            qm.Size = new Vector2(_width, length);

        _node.Position = (from + to) * 0.5f;
        _node.Rotation = (to - from).Angle() + Mathf.Pi * 0.5f;
    }

    /// <summary>整体强度（0 = 全透明）。</summary>
    public void SetFade(float fade) => _mat.SetShaderParameter("intensity", Mathf.Max(0f, fade));

    /// <summary>溶解（0 = 完整，1 = 碎光散尽）。</summary>
    public void SetDissolve(float d) => _mat.SetShaderParameter("dissolve", Mathf.Clamp(d, 0f, 1f));

    private static Shader? GetShader()
    {
        if (_shader != null)
            return _shader;
        if (_shaderBroken)
            return null;

        try
        {
            var shader = new Shader { Code = Source };
            var uniforms = shader.GetShaderUniformList();
            if (uniforms == null || uniforms.Count < MinUniforms)
            {
                _shaderBroken = true;
                GD.Print($"[Subaru] beam shader failed (uniforms={uniforms?.Count ?? 0}), fallback to lines");
                return null;
            }

            _shader = shader;
            return _shader;
        }
        catch (Exception e)
        {
            _shaderBroken = true;
            GD.PushWarning("[Subaru] beam shader failed: " + e.Message);
            return null;
        }
    }
}
