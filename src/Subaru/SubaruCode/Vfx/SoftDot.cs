using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 运行时生成的**软圆**光点贴图（径向渐变 + 叠加混合），替掉以前的方块 <c>Polygon2D</c>。
///
/// <para>2026-11-22 用户「这个特效太廉价」：方点是最大的廉价信号之一（一眼是「程序画的方块」）。
/// 这里不引入任何美术资源：第一次用到时用 <see cref="Image"/> 生成 64×64 的径向渐变，之后全局复用。</para>
///
/// <para>两种：<see cref="Soft"/>（边缘很散，光尘 / 火花）和 <see cref="Core"/>（中心更实，
/// 蓄势光点 / 切口高光）。<see cref="Make"/> 直接给一个摆好位置和直径的 <see cref="Sprite2D"/>。</para>
/// </summary>
public static class SoftDot
{
    private const int TexSize = 64;

    private static Texture2D? _soft;
    private static Texture2D? _core;
    private static CanvasItemMaterial? _additive;

    /// <summary>边缘很散的一颗（光尘、溅射火花）。</summary>
    public static Texture2D Soft => _soft ??= Build(1.0f);

    /// <summary>中心更实的一颗（蓄势光点、切口高光）。</summary>
    public static Texture2D Core => _core ??= Build(2.4f);

    /// <summary>叠加混合材质（发光不压暗背景）—— 全局共用一份。</summary>
    public static CanvasItemMaterial Additive => _additive ??= new CanvasItemMaterial
    {
        BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
    };

    /// <summary>造一颗光点：位置（屏幕坐标）、直径（设计像素 × 界面缩放）、颜色、是否叠加发光。</summary>
    public static Sprite2D Make(Vector2 position, float diameter, Color color, bool additive, bool core = false)
    {
        var sprite = new Sprite2D
        {
            Texture = core ? Core : Soft,
            Position = position,
            Scale = Vector2.One * (Mathf.Max(0.5f, diameter) / TexSize),
            Modulate = color,
        };

        if (additive)
            sprite.Material = Additive;

        return sprite;
    }

    /// <summary>把一张光点的直径设成 <paramref name="diameter"/>（动画里每帧改大小用）。</summary>
    public static void SetDiameter(Sprite2D sprite, float diameter)
        => sprite.Scale = Vector2.One * (Mathf.Max(0.5f, diameter) / TexSize);

    /// <summary>
    /// <paramref name="power"/> 越大中心越实：`falloff = pow(1 - r, power)`。
    /// 1.0 → 很散（尘埃），2.4 → 有点灯芯（蓄势）。
    /// </summary>
    private static Texture2D Build(float power)
    {
        var img = Image.CreateEmpty(TexSize, TexSize, false, Image.Format.Rgba8);
        var c = (TexSize - 1) * 0.5f;

        for (var y = 0; y < TexSize; y++)
        {
            for (var x = 0; x < TexSize; x++)
            {
                var dx = (x - c) / c;
                var dy = (y - c) / c;
                var r2 = dx * dx + dy * dy;
                var a = r2 >= 1f ? 0f : Mathf.Pow(1f - Mathf.Sqrt(r2), power);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
