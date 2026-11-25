using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 光柱用的贴图（`res://Subaru/textures/vfx/`，黑底 RGB，叠加混合下黑色=透明）。
///
/// <para>由 `_tools/gen_vfx_textures.py` 程序生成，可复现；贴图缺失时各调用方自行降级（返回 null 就退回几何方案）。</para>
/// </summary>
public static class VfxTextures
{
    private const string Dir = "res://Subaru/textures/vfx/";

    private static Texture2D? _beamCore;
    private static Texture2D? _beamNoise;
    private static Texture2D? _ring;
    private static Texture2D? _beamPool;
    private static Texture2D? _spark;
    private static Texture2D? _dust;
    private static readonly Texture2D?[] Impacts = new Texture2D?[4];
    private static bool _loaded;

    /// <summary>柱体剖面（横向高斯 + 两端收尖 + 细纹）。</summary>
    public static Texture2D? BeamCore => LoadOnce(ref _beamCore, "beam_core.png");

    /// <summary>可平铺噪声（shader 里滚动采样）。</summary>
    public static Texture2D? BeamNoise => LoadOnce(ref _beamNoise, "beam_noise.png");

    /// <summary>地面冲击环。</summary>
    public static Texture2D? Ring => LoadOnce(ref _ring, "ring.png");

    /// <summary>地面亮光池（命中后踩在地上的那滩光）。</summary>
    public static Texture2D? BeamPool => LoadOnce(ref _beamPool, "beam_pool.png");

    /// <summary>光针 / 溅射条。</summary>
    public static Texture2D? Spark => LoadOnce(ref _spark, "spark.png");

    /// <summary>光尘软圆。</summary>
    public static Texture2D? Dust => LoadOnce(ref _dust, "dust.png");

    /// <summary>命中冲击帧（1~4）。</summary>
    public static Texture2D? Impact(int frame)
    {
        var i = Mathf.Clamp(frame, 1, 4) - 1;
        if (!_loaded)
        {
            _loaded = true;
            for (var k = 0; k < 4; k++)
                Impacts[k] = TryLoad($"impact_{k + 1}.png");
        }

        return Impacts[i];
    }

    private static Texture2D? LoadOnce(ref Texture2D? slot, string file)
    {
        slot ??= TryLoad(file);
        return slot;
    }

    private static Texture2D? TryLoad(string file)
    {
        try
        {
            var path = Dir + file;
            return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        }
        catch (System.Exception e)
        {
            GD.PushWarning("[Subaru] vfx texture failed: " + file + " (" + e.Message + ")");
            return null;
        }
    }
}
