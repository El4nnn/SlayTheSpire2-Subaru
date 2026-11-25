using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Subaru.SubaruCode.UI;

/// <summary>
/// 死归黑幕里那张「嫉妒魔女」的素材池。
///
/// 一条素材 = 一张剪影图 + 一段语音；死归演出时等概率抽一条（洗牌袋，不会连着两次同一张）。
/// 素材 1~3 共用同一个语音 <c>res://Subaru/audio/witch_face.mp3</c>，素材 4 自带语音；
/// 黑幕停留时长按被抽中那条的语音长度算（1.384 s / 1.749 s），见 <see cref="RewindAnimation"/>。
///
/// 素材 5 是视频，只在「这次死归触发癫狂」时参与，权重 50%——那部分还没接，
/// 因为癫狂期间死归本身是关闭的，触发时机得先定（见交接文档）。
/// </summary>
public static class RewindWitchPool
{
    /// <summary>一条素材：图 + 语音。Id 只用于日志。</summary>
    public sealed record Asset(string Id, string FacePath, string AudioPath);

    private const string SharedAudio = "res://Subaru/audio/witch_face.mp3";

    /// <summary>
    /// 素材 5：视频。只在「这次死归会触发癫狂」时参与，权重 50%。
    /// 这个引擎没有 Theora 的导入器（.ogv 不会被 import），所以它作为**原始文件**
    /// 打进 pck，运行时用 <see cref="VideoStreamTheora"/> 直接按路径读。
    /// </summary>
    private const string VideoPath = "res://Subaru/video/rewind_witch5.ogv";

    private static readonly Asset[] Assets =
    [
        new("1", "res://Subaru/images/ui/rewind_face.png", SharedAudio),
        new("2", "res://Subaru/images/ui/rewind_face2.png", SharedAudio),
        new("3", "res://Subaru/images/ui/rewind_face3.png", SharedAudio),
        new("4", "res://Subaru/images/ui/rewind_face4.png", "res://Subaru/audio/witch_face4.mp3"),
    ];

    // 洗牌袋：一袋里每条素材各出现一次，抽空再洗，避免「随机」连着给同一张
    private static readonly List<int> Bag = [];
    private static int _last = -1;

    public static int Count => Assets.Length;

    /// <summary>等概率抽一条素材。</summary>
    public static Asset Pick()
    {
        if (Bag.Count == 0)
            Refill();

        var idx = Bag[^1];
        Bag.RemoveAt(Bag.Count - 1);
        _last = idx;

        var asset = Assets[idx];
        GD.Print($"[Subaru] rewind witch asset: {asset.Id}");
        return asset;
    }

    /// <summary>
    /// 素材 5 的抽取：只有预测到这次死归会触发癫狂时才掷骰，50% 走视频。
    /// 掷骰失败（没到癫狂 / 视频没配）就不消耗图片池的洗牌袋。
    /// </summary>
    public static bool TryTakeVideo(bool madnessLikely, out string path)
    {
        path = VideoPath;

        if (!madnessLikely || string.IsNullOrEmpty(VideoPath))
            return false;

        var takeVideo = Random.Shared.NextDouble() < 0.5;
        GD.Print($"[Subaru] rewind witch roll: madness=1 => {(takeVideo ? "video" : "image")}");
        return takeVideo;
    }

    private static void Refill()
    {
        var order = Enumerable.Range(0, Assets.Length).ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // 袋与袋之间也要断开：上一袋最后抽到的排到本袋末尾之后才出现
        if (order.Count > 1 && order[^1] == _last)
            (order[0], order[^1]) = (order[^1], order[0]);

        Bag.AddRange(order);
    }
}
