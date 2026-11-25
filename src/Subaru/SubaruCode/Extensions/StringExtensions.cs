using Godot;

namespace Subaru.SubaruCode.Extensions;

//Mostly utilities to get asset paths.
//
//注意：这里绝对不能再用 System.IO.Path.Join。
//它用的是 Windows 反斜杠（res://Subaru\images\powers\x.png），而 Godot 的
//ResourceLoader / 资源缓存只认正斜杠，于是所有图标都加载失败。
//历史上「能力/遗物/大图都没图标」就是这个原因：路径能拼出来，但永远查不到资源。
public static class StringExtensions
{
    private static string Join(params string[] parts)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            if (sb.Length > 0)
                sb.Append('/');

            sb.Append(part.Trim('/'));
        }

        // res:// 前缀被 Trim 会破坏协议头，这里补回来
        var s = sb.ToString();
        if (!s.StartsWith("res://"))
            s = "res://" + s.TrimStart('/');

        return s.Replace('\\', '/');
    }

    public static string ImagePath(this string path)
    {
        return Join(MainFile.ResPath, "images", path);
    }

    public static string CardImagePath(this string path)
    {
        var full = Join(MainFile.ResPath, "images", "card_portraits", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find card image path: " + full);
        return Join(MainFile.ResPath, "images", "card_portraits", "card.png");
    }

    public static string BigCardImagePath(this string path)
    {
        var full = Join(MainFile.ResPath, "images", "card_portraits", "big", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find big card image path: " + full);
        return Join(MainFile.ResPath, "images", "card_portraits", "big", "card.png");
    }

    public static string PowerImagePath(this string path)
    {
        var full = Join(MainFile.ResPath, "images", "powers", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find power image path: " + full);
        return Join(MainFile.ResPath, "images", "powers", "power.png");
    }

    public static string BigPowerImagePath(this string path)
    {
        var full = Join(MainFile.ResPath, "images", "powers", "big", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find big power image path: " + full);
        return Join(MainFile.ResPath, "images", "powers", "big", "power.png");
    }

    public static string RelicImagePath(this string path)
    {
        var full = Join(MainFile.ResPath, "images", "relics", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find relic image path: " + full);
        return Join(MainFile.ResPath, "images", "relics", "relic.png");
    }

    public static string BigRelicImagePath(this string path)
    {
        var full = Join(MainFile.ResPath, "images", "relics", "big", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find big relic image path: " + full);
        return Join(MainFile.ResPath, "images", "relics", "big", "relic.png");
    }

    public static string PotionImagePath(this string path)
    {
        var full = Join(MainFile.ResPath, "images", "potions", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find potion image path: " + full);
        return Join(MainFile.ResPath, "images", "potions", "potion.png");
    }

    public static string PotionOutlineImagePath(this string path)
    {
        // 描边图和主图分目录：images/potions/outline/<id>.png
        var full = Join(MainFile.ResPath, "images", "potions", "outline", path);
        if (ResourceLoader.Exists(full)) return full;

        MainFile.Logger.Info("Could not find potion image path: " + full);
        return Join(MainFile.ResPath, "images", "potions", "outline", "potion.png");
    }

    public static string CharacterUiPath(this string path)
    {
        return Join(MainFile.ResPath, "images", "charui", path);
    }
}
