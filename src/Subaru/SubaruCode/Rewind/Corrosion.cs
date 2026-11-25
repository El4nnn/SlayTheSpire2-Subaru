using System;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace Subaru.SubaruCode.Rewind;

/// <summary>
/// 「腐蚀」标记：带此接口的能力不随死归重置，层数与效果一起进入后续轮回。
///
/// 卡面写了 [gold]腐蚀[/gold] 的牌，它授予的能力实现这个接口就行，
/// 不需要再去 Spike 里手工 Register 一遍。
/// </summary>
public interface ICorrodedPower
{
}

/// <summary>把本 mod 所有带 ICorrodedPower 标记的能力自动登记进「记事本」。</summary>
public static class Corrosion
{
    private static bool _done;

    public static void RegisterAll()
    {
        if (_done)
            return;

        _done = true;

        var count = 0;

        try
        {
            foreach (var type in typeof(Corrosion).Assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                if (!typeof(PowerModel).IsAssignableFrom(type))
                    continue;

                if (!typeof(ICorrodedPower).IsAssignableFrom(type))
                    continue;

                RewindLedger.RegisterType(type);
                count++;
            }
        }
        catch (Exception e)
        {
            GD.PushError("[Subaru] corrosion scan failed: " + e);
        }

        GD.Print($"[Subaru] corrosion: {count} powers persist across rewind");
    }
}
