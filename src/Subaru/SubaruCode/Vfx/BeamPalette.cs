using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 一套「光柱配色 + 强度」参数。<see cref="BeamQuad"/> 与粒子只吃它，颜色不写死在几何代码里。
///
/// <para>2026-11-22 用户定：白芯保留；**柱体分三段颜色**（顶 → 中 → 底），一档青蓝、二档紫、三档金红。
/// `Top/Mid/Bottom` 是沿柱体高度的三档色标，shader 里做两段线性插值。</para>
/// </summary>
public sealed class BeamPalette
{
    /// <summary>柱体顶端色。</summary>
    public Color Top = new(0.50f, 0.89f, 0.94f);

    /// <summary>柱体中段色（一般最亮，接近白）。</summary>
    public Color Mid = new(0.91f, 0.97f, 1.00f);

    /// <summary>柱体底端色。</summary>
    public Color Bottom = new(0.29f, 0.66f, 0.88f);

    /// <summary>核心色（极亮中芯用）。</summary>
    public Color Core = new(0.96f, 0.99f, 1.00f);

    /// <summary>体积色（带 alpha，决定外圈浓度）。</summary>
    public Color Volume = new(0.37f, 0.82f, 0.91f, 0.30f);

    /// <summary>光条渐变的起止色。</summary>
    public Color RayA = new(0.50f, 0.89f, 0.94f, 0.42f);
    public Color RayB = new(0.29f, 0.66f, 0.88f, 0.42f);

    /// <summary>地面环 / 光池颜色。</summary>
    public Color Ring = new(0.56f, 0.86f, 0.96f, 0.80f);

    /// <summary>核心宽 / 体积宽（设计像素）。</summary>
    public float CoreWidth = 13f;
    public float VolumeWidth = 150f;

    /// <summary>光条数量。</summary>
    public int Rays = 10;

    /// <summary>噪声扰动强度（0 = 纯净渐变，1 = 很脏）。</summary>
    public float Noise = 0.55f;

    /// <summary>外圈辉光衰减指数（越大边缘越硬）。</summary>
    public float GlowPower = 3.0f;

    /// <summary>一档青蓝：顶青 → 中白 → 底蓝。</summary>
    public static BeamPalette BallroyTier1() => new()
    {
        Top = new Color(0.42f, 0.90f, 1.00f),
        Mid = new Color(0.72f, 0.94f, 1.00f),
        Bottom = new Color(0.24f, 0.55f, 0.92f),
        Core = new Color(0.93f, 0.99f, 1.00f),
        Volume = new Color(0.33f, 0.78f, 0.92f, 0.30f),
        RayA = new Color(0.47f, 0.90f, 0.97f, 0.42f),
        RayB = new Color(0.26f, 0.62f, 0.92f, 0.42f),
        Ring = new Color(0.52f, 0.84f, 0.98f, 0.80f),
        CoreWidth = 13f,
        VolumeWidth = 150f,
        Rays = 10,
        Noise = 0.50f,
    };

    /// <summary>二档紫：顶紫 → 中白 → 底深紫。</summary>
    public static BeamPalette BallroyTier2() => new()
    {
        Top = new Color(0.74f, 0.52f, 1.00f),
        Mid = new Color(0.85f, 0.74f, 1.00f),
        Bottom = new Color(0.44f, 0.22f, 0.78f),
        Core = new Color(0.97f, 0.94f, 1.00f),
        Volume = new Color(0.56f, 0.38f, 0.88f, 0.30f),
        RayA = new Color(0.76f, 0.56f, 0.98f, 0.44f),
        RayB = new Color(0.46f, 0.26f, 0.84f, 0.44f),
        Ring = new Color(0.78f, 0.62f, 1.00f, 0.80f),
        CoreWidth = 18f,
        VolumeWidth = 220f,
        Rays = 14,
        Noise = 0.58f,
    };

    /// <summary>三档金红：顶金 → 中暖白 → 底红。</summary>
    public static BeamPalette BallroyTier3() => new()
    {
        Top = new Color(1.00f, 0.84f, 0.42f),
        Mid = new Color(1.00f, 0.88f, 0.56f),
        Bottom = new Color(0.90f, 0.28f, 0.18f),
        Core = new Color(1.00f, 0.98f, 0.92f),
        Volume = new Color(0.88f, 0.58f, 0.26f, 0.32f),
        RayA = new Color(1.00f, 0.78f, 0.40f, 0.46f),
        RayB = new Color(0.86f, 0.30f, 0.20f, 0.46f),
        Ring = new Color(1.00f, 0.70f, 0.40f, 0.82f),
        CoreWidth = 24f,
        VolumeWidth = 300f,
        Rays = 18,
        Noise = 0.66f,
    };

    public static BeamPalette ForTier(int tier) => tier switch
    {
        <= 1 => BallroyTier1(),
        2 => BallroyTier2(),
        _ => BallroyTier3(),
    };
}
