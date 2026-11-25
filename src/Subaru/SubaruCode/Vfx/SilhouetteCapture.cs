using System;
using System.Threading.Tasks;
using Godot;

namespace Subaru.SubaruCode.Vfx;

/// <summary>
/// 把一只怪**这一刻的样子**抓成一张平涂贴图（透明底），用来切块 / 切两半。
///
/// <para>做法：把 <c>visuals</c> 复制一份，套上平涂材质，放进一个和可视矩形等大的
/// <see cref="SubViewport"/>（透明背景）里，抓一帧。抓到的贴图在视口存活期间有效，
/// 所以视口要挂在一个活到演出结束的宿主节点上（我们挂在演出自己的 root 上，一起 <c>QueueFree</c>）。</para>
///
/// <para>2026-11-22 从 <see cref="YaeAtomicSlash"/> 里抽出来的公共版（八重那边暂时保持原样不动，
/// 等这条路验证通过再迁过来，避免一次改两处）。</para>
/// </summary>
public static class SilhouetteCapture
{
    /// <summary>抓帧时四周留的边距（像素）。</summary>
    public const int Pad = 6;

    /// <summary>抓帧结果。</summary>
    public sealed class Shot
    {
        public Texture2D? Tex;
        public Image? Img;

        /// <summary>贴图左上角对应的屏幕坐标（切块 / 抽粉尘位置时用）。</summary>
        public Vector2 DrawOrigin;

        public int Width;
        public int Height;
    }

    /// <summary>
    /// 抓一张。<paramref name="top"/>/<paramref name="bottom"/> 是平涂材质的上下色
    /// （剑圣用「冷白 → 银灰」，在压暗的背景上看得见；八重那种「黑剪影」在暗底上会看不见）。
    /// 返回 null = 环境不具备 / 复制失败，调用方要能退化成纯色块。
    /// </summary>
    public static async Task<Shot?> CaptureAsync(SceneTree tree, Node2D visuals, Rect2 rect, Node2D host,
        Color top, Color bottom)
    {
        SubViewport? vp = null;

        try
        {
            var w = Mathf.Max(16, Mathf.RoundToInt(rect.Size.X) + Pad * 2);
            var h = Mathf.Max(16, Mathf.RoundToInt(rect.Size.Y) + Pad * 2);

            vp = new SubViewport
            {
                Size = new Vector2I(w, h),
                TransparentBg = true,
                Disable3D = true,
                HandleInputLocally = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            host.AddChild(vp);

            if (visuals.Duplicate() is not Node2D copy)
            {
                vp.QueueFree();
                return null;
            }

            vp.AddChild(copy);

            // 复制品在视口里的位置：屏幕坐标 − 视口原点（视口原点 = rect.Position − pad）
            var xf = visuals.GetGlobalTransformWithCanvas();
            copy.GlobalTransform = new Transform2D(xf.X, xf.Y,
                xf.Origin - rect.Position + new Vector2(Pad, Pad));

            KillEffect.ApplyMaterial(copy, KillEffect.BuildFlatMaterial(top, bottom));

            vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var tex = vp.GetTexture();

            Image? img = null;
            try
            {
                img = tex?.GetImage();
            }
            catch (Exception e)
            {
                GD.PushWarning("[Subaru] silhouette capture: image failed: " + e.Message);
            }

            GD.Print($"[Subaru] silhouette capture: {w}x{h} " +
                     $"tex={(tex != null ? "ok" : "null")} " +
                     $"img={(img != null ? $"{img.GetWidth()}x{img.GetHeight()}" : "null")}");

            return new Shot
            {
                Tex = tex,
                Img = img,
                DrawOrigin = rect.Position - new Vector2(Pad, Pad),
                Width = w,
                Height = h,
            };
        }
        catch (Exception e)
        {
            GD.PushWarning("[Subaru] silhouette capture failed: " + e.Message);

            if (vp != null && GodotObject.IsInstanceValid(vp))
                vp.QueueFree();

            return null;
        }
    }
}
