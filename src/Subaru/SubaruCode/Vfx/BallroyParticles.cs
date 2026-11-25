using System;
using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 光柱的粒子层：交给引擎的 <see cref="GpuParticles2D"/>，不再手写「每帧 Position += vel」。
///
/// <para>两组：<see cref="Rise"/>（地面往上飘的光尘，带横向扩散与轻微上浮重力）、
/// <see cref="Burst"/>（命中瞬间向四周溅射的光针，带重力与阻尼）。</para>
///
/// <para>都是 <c>OneShot</c>：播完一个生命周期后自己 <c>QueueFree</c>，调用方不用管。</para>
/// </summary>
public static class BallroyParticles
{
    /// <summary>上升光尘：从落点附近往上飘。</summary>
    public static void Rise(Node2D parent, Vector2 ground, float halfWidth, Color tint, int amount, float s)
    {
        try
        {
            var particles = Create(parent, ground, tint, amount, lifetime: 1.1f, VfxTextures.Dust);
            if (particles == null)
                return;

            if (particles.ProcessMaterial is ParticleProcessMaterial pm)
            {
                pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
                pm.EmissionBoxExtents = new Vector3(Mathf.Max(8f, halfWidth * 0.45f), 6f * s, 0f);
                pm.Direction = new Vector3(0f, -1f, 0f);
                pm.Spread = 32f;
                pm.InitialVelocityMin = 45f * s;
                pm.InitialVelocityMax = 130f * s;
                pm.Gravity = new Vector3(0f, -26f * s, 0f);
                pm.DampingMin = 6f;
                pm.DampingMax = 20f;
                pm.ScaleMin = 0.6f * s;
                pm.ScaleMax = 2.2f * s;
                pm.AngleMin = 0f;
                pm.AngleMax = 360f;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy particles (rise) failed: " + e.Message);
        }
    }

    /// <summary>命中溅射：沿地面向两侧飞出的短光针，受重力下落。</summary>
    public static void Burst(Node2D parent, Vector2 ground, Color tint, int amount, float s)
    {
        try
        {
            var particles = Create(parent, ground, tint, amount, lifetime: 0.55f, VfxTextures.Spark);
            if (particles == null)
                return;

            if (particles.ProcessMaterial is ParticleProcessMaterial pm)
            {
                pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
                pm.EmissionSphereRadius = 12f * s;
                pm.Direction = new Vector3(0f, -1f, 0f);
                pm.Spread = 80f;
                pm.InitialVelocityMin = 200f * s;
                pm.InitialVelocityMax = 420f * s;
                pm.Gravity = new Vector3(0f, 620f * s, 0f);
                pm.DampingMin = 40f;
                pm.DampingMax = 90f;
                pm.ScaleMin = 0.5f * s;
                pm.ScaleMax = 1.4f * s;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] ballroy particles (burst) failed: " + e.Message);
        }
    }

    private static GpuParticles2D? Create(Node2D parent, Vector2 position, Color tint, int amount, float lifetime, Texture2D? texture)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null)
            return null;

        var particles = new GpuParticles2D
        {
            Position = position,
            Amount = Mathf.Max(1, amount),
            Lifetime = lifetime,
            OneShot = true,
            Emitting = true,
            FixedFps = 60,
            Texture = texture ?? SoftDot.Soft,
            Material = new CanvasItemMaterial
            {
                BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
            },
            ProcessMaterial = new ParticleProcessMaterial
            {
                Color = tint,
            },
        };

        parent.AddChild(particles);

        // 生命周期结束 + 余量后自己清掉（不依赖父节点存活时长）
        var timer = tree.CreateTimer(lifetime + 0.5f);
        tree.ToSignal(timer, SceneTreeTimer.SignalName.Timeout)
            .OnCompleted(() =>
            {
                if (GodotObject.IsInstanceValid(particles))
                    particles.QueueFree();
            });

        return particles;
    }
}
