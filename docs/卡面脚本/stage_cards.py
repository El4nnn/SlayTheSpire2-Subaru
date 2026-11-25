# -*- coding: utf-8 -*-
"""Stage the supplied card art into the card frame aspect (1000x760 = 1.3158).

Writes into _tools/tmp/card_stage first (NOT the mod), plus a preview sheet,
so a bad crop costs nothing.

Source images live in several drop folders (see SRCS); the same file name is
looked up in each in order, so batches from different folders coexist in one MAP.
"""
import os
from PIL import Image, ImageDraw

SRCS = [
    r"C:\Users\Elan\Desktop\卡面",    # 批次一 / 二（9 张）
    r"C:\Users\Elan\Desktop\卡面2",   # 批次三（14 张）
    r"C:\Users\Elan\Desktop\卡面3",   # 批次四（26 张）
    r"C:\Users\Elan\Desktop\卡面4",   # 批次五及以后
]
SRC = SRCS[0]
STAGE = r"C:\Users\Elan\Desktop\塔2mod\_tools\tmp\card_stage"
TMP = r"C:\Users\Elan\Desktop\塔2mod\_tools\tmp"
BIG = (1000, 760)
SMALL = (500, 380)
ASPECT = BIG[0] / BIG[1]

MAP = {
    # --- 批次一 / 二（Pictures\卡面）---
    "契约贝蒂.png": ("beatrice_contract", "center"),
    "嫉妒魔女莎提娜.png": ("satella_witch", "top"),
    "艾米莉亚的祝福.png": ("emilia_blessing", "center"),
    "魔女瘴气 .png": ("curse_witch_miasma", "center"),
    "埃尔迷尼亚.png": ("el_minya", "center"),
    "大兔.png": ("otogi", "center"),
    "迷尼亚.png": ("minya", "center"),
    "选择我.png": ("choose_me", "center"),
    # --- 批次三 2026-11-14（桌面\卡面2），14 张 ---
    # 竖图里头顶贴边的用 top（只从顶部让 6%），抠得多且头在上面的场景防切头顶
    "传心加护.jpg": ("liliana_friend", "top"),
    "忧郁魔女.jpg": ("witch_of_melancholy", "top"),
    "魂婚术.png": ("soul_marriage", "top"),
    "星食.jpg": ("star_eclipse", "top"),
    "食精灵者.jpg": ("spirit_eater", "center"),
    "青.png": ("blue_friend", "center"),
    "太阳.png": ("priscilla_friend", "center"),
    "暴食司教.jpg": ("ley", "center"),
    "死神加护.jpg": ("theresia", "center"),
    "剑鬼.png": ("sword_demon_friend", "center"),
    "白鲸.jpg": ("hakugei", "center"),
    "神龙.jpg": ("volcanica", "center"),
    "风视加护.jpg": ("crusch_friend", "center"),
    "黑蛇.jpg": ("kurohebi", "center"),
    # --- 批次四 2026-11-14（桌面\卡面3），26 张 ---
    "不可视之手.png": ("invisible_providence", "center"),
    "乌尔纱幕.png": ("ul_shamak", "center"),
    "傲慢司教.png": ("archbishop_pride", "center"),
    "傲慢魔女.png": ("typhon_witch", "top"),
    "夏乌拉.jpg": ("shaula", "center"),
    "宫廷小丑.png": ("roswaal_friend", "center"),
    "强欲司教.jpg": ("regulus", "top"),
    "强欲魔女.png": ("echidna_witch", "center"),
    "怠惰因子.png": ("sloth_factor", "center"),
    "怠惰魔女.png": ("sekhmet_witch", "center"),
    "愤怒司教.png": ("sirius", "center"),
    "愤怒魔女.png": ("minerva_witch", "top"),
    "手机.png": ("keitai", "center"),
    "暴食魔女.png": ("daphne_witch", "top"),
    "柯尔·雷欧尼斯.png": ("cor_leonis", "center"),
    "真 不可视之手.png": ("true_invisible_providence", "center"),
    "纱幕.png": ("shamak", "center"),
    "色欲司教.png": ("capella", "top"),
    "色欲魔女.png": ("carmilla_witch", "top"),
    "莱茵哈鲁特.jpg": ("sword_saint_friend", "top"),
    "虚饰魔女.png": ("pandora_witch", "top"),
    "门.png": ("gate", "center"),
    "阿尔纱幕.png": ("al_shamak", "center"),
    "雷德.jpg": ("reid", "top"),
    "首席骑士.png": ("first_knight", "top"),
    # --- 批次五 2026-11-14：B 站截图，带水印/字幕，走 CROP_OVERRIDE ---
    "双胞胎.jpg": ("rem_friend", "center"),
    "茶会.jpg": ("witch_tea_party", "center"),
    "强欲契约.jpg": ("greed_contract", "center"),
    "龙历石.png": ("dragon_tablet", "center"),
    "冷静思考.jpg": ("composure", "center"),
    "鞭子.png": ("guiltywhip", "top"),
    "跑酷.png": ("parkour", "center"),
    "三分.png": ("mimi_friend", "top"),
    "言灵.png": ("otto_friend", "top"),
    "幽暗纱幕.png": ("gloom_shamak", "center"),
    "龙血.png": ("dragon_blood", "center"),   # 覆盖旧版（昴+龙爪手）
    "硬撑.png": ("endure", "center"),
    "英雄.png": ("hero", "center"),
    "宣讲.png": ("sermon", "center"),
    "最优.png": ("best_friend", "center"),
    "艾尔莎.png": ("elsa", "center"),
    "千里眼.png": ("ram_friend", "center"),
    "帕特拉修.png": ("patrasche", "center"),
    "操丝术.png": ("puppet_strings", "center"),
    "埃尔纱幕.png": ("el_shamak", "center"),
    "打击.png": ("strike_subaru", "top"),
    "多重吟唱.png": ("meteor", "center"),
    "治愈.png": ("healing_magic", "center"),
    "emt.png": ("emt", "center"),
    "净化.png": ("mana_purge", "center"),
    "EMM.png": ("emm", "center"),
    "防御.png": ("defend_subaru", "center"),
    "迷失.png": ("lost", "center"),
    "福音书2.png": ("gospel", "center"),   # 2026-11-14 换图：黑书特写
    "去诅咒.png": ("remove_curse", "center"),
    "幼女使者.png": ("lolimancer", "top"),
    "穆拉克.png": ("murak", "center"),
    "大贤者.png": ("great_sage", "center"),
    "忧郁魔人.png": ("hector", "top"),
    "蓝色闪电.png": ("blue_lightning", "center"),
}

# 手动裁切框：card_id -> (x0, y0, 宽, 高)。比例必须等于 1.3158。
# 用来切掉截图里固定位置的水印条 / 烧进画面的字幕。
CROP_OVERRIDE = {
    "rem_friend":      (268, 0, 1500, 1140),
    "witch_tea_party": (450, 0, 1500, 1140),
    "greed_contract":  (383, 0, 1500, 1140),
    "otto_friend":     (66, 0, 789, 600),  # 新版横图，切掉底部 Vic.1120 字样
    "hector":          (0, 0, 1176, 894),  # 头顶贴边，一点都不能往上切
    "blue_lightning":  (0, 0, 1089, 828),  # 左边是持刀的手，别居中削掉


}


def resolve(fname):
    """Find fname in SRCS in order; tolerate the stray trailing-space duplicates."""
    if os.path.isabs(fname):
        return (fname, os.path.basename(fname)) if os.path.exists(fname) else (None, None)
    for d in SRCS:
        if not os.path.isdir(d):
            continue
        p = os.path.join(d, fname)
        if os.path.exists(p):
            return p, fname
        cands = [n for n in os.listdir(d) if n.strip() == fname.strip()]
        if cands:
            return os.path.join(d, cands[0]), cands[0]
    return None, None


os.makedirs(STAGE, exist_ok=True)
rows = []

for fname, (card_id, bias) in MAP.items():
    p, real = resolve(fname)
    if p is None:
        # 源图可能已经被清掉：只要成品还在，就当成已完成，不刷 MISSING 噪音
        if os.path.exists(os.path.join(STAGE, card_id + "_big.png")):
            print("skip (already installed):", card_id)
        else:
            print("MISSING:", fname)
        continue

    fname = real
    im = Image.open(p).convert("RGBA")
    W, H = im.size
    a = W / H
    if card_id in CROP_OVERRIDE:
        # 手动指定的裁切框，优先于自动规则（用于切掉水印/字幕这类固定位置的杂物）
        x0, y0, cw, ch = CROP_OVERRIDE[card_id]
        how = "custom"
    elif a > ASPECT:                    # too wide -> trim left/right
        cw = int(round(H * ASPECT)); ch = H
        x0 = (W - cw) // 2; y0 = 0
        how = "wipe sides"
    else:                               # too tall -> trim top/bottom
        cw = W; ch = int(round(W / ASPECT))
        x0 = 0
        if bias == "top":
            y0 = int(round(0.06 * H))   # keep the upper part (faces live there)
        else:
            y0 = (H - ch) // 2
        how = "wipe top/bottom (%s)" % bias

    if how != "custom":
        y0 = max(0, min(y0, H - ch))
    box = (x0, y0, x0 + cw, y0 + ch)
    crop = im.crop(box)
    big = crop.resize(BIG, Image.LANCZOS)
    small = crop.resize(SMALL, Image.LANCZOS)
    big.save(os.path.join(STAGE, card_id + "_big.png"))
    small.save(os.path.join(STAGE, card_id + "_small.png"))
    rows.append((card_id, fname, p, "%dx%d" % (W, H), box, how, BIG[0] / cw))
    print("%-22s %-18s %dx%d  crop=%s  %-24s upscale=%.2fx" % (
        card_id, fname, W, H, box, how, BIG[0] / cw))

# ---------- preview ----------
CW = 330
CH = int(CW / ASPECT)
tiles = []
for card_id, fname, path, src_size, box, how, up in rows:
    src = Image.open(path).convert("RGBA")
    s = CW / src.width
    sv = src.resize((CW, max(1, int(src.height * s))), Image.LANCZOS)
    bg = Image.new("RGBA", sv.size, (24, 24, 30, 255)); bg.alpha_composite(sv)
    d0 = ImageDraw.Draw(bg)
    d0.rectangle([box[0] * s, box[1] * s, box[2] * s - 1, box[3] * s - 1], outline=(255, 60, 60, 255), width=3)
    st = Image.new("RGBA", (CW, CH), (24, 24, 30, 255))
    st.alpha_composite(Image.open(os.path.join(STAGE, card_id + "_big.png")).convert("RGBA"), (0, 0))
    tiles.append((card_id, bg, st, up))

hrow = max(max(t[1].height, t[2].height) for t in tiles)
sheet = Image.new("RGBA", (2 * CW + 24, (hrow + 46) * len(tiles) + 10), (18, 18, 22, 255))
d = ImageDraw.Draw(sheet)
d.text((8, 8), "left: source + crop box    right: staged 1000x760", fill=(255, 235, 140, 255))
y = 34
for card_id, srcimg, st, up in tiles:
    sheet.alpha_composite(srcimg, (8, y + 20))
    sheet.alpha_composite(st, (8 + CW + 16, y + 20))
    d.text((8, y + 2), "%s   upscale %.2fx" % (card_id, up), fill=(150, 220, 255, 255))
    y += hrow + 46
sheet.save(os.path.join(TMP, "card_import_preview.png"))
print("saved preview", sheet.size)
