# -*- coding: utf-8 -*-
"""Re-crop ONE card art from an arbitrary source image.

    python recrop_card.py <source_image> <card_id> [top|center]

Crop rule is identical to install_cards.py (16:9-ish 1000x760 canvas):
 - wider than the canvas -> trim the sides, keep full height
 - taller -> keep full width, shift down from `top` bias (6% down) or centre
Outputs the same two files install_cards.py writes and updates STATE.json,
so the build cache stays consistent.
"""
import os, sys, json, hashlib, datetime
from PIL import Image

ROOT = r"C:\Users\Elan\Desktop\塔2mod\src\Subaru\Subaru"
STATE = r"C:\Users\Elan\Desktop\塔2mod\dist\backup\images_orig\STATE.json"
BIG_MAX = (1000, 760)
SMALL = (500, 380)
ASPECT = BIG_MAX[0] / BIG_MAX[1]


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as fh:
        for c in iter(lambda: fh.read(1 << 20), b""):
            h.update(c)
    return h.hexdigest()


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    src = sys.argv[1]
    card_id = sys.argv[2]
    bias = sys.argv[3] if len(sys.argv) > 3 else "top"

    im = Image.open(src).convert("RGBA")
    W, H = im.size

    if W / H > ASPECT:
        cw = int(round(H * ASPECT))
        ch = H
        x0 = (W - cw) // 2
        y0 = 0
    else:
        cw = W
        ch = int(round(W / ASPECT))
        x0 = 0
        y0 = max(0, min(int(round(0.06 * H)) if bias == "top" else (H - ch) // 2, H - ch))

    crop = im.crop((x0, y0, x0 + cw, y0 + ch))

    bw = min(BIG_MAX[0], int(round(cw * 2)))
    bh = int(round(bw / ASPECT))
    big = crop.resize((bw, bh), Image.LANCZOS)
    small = crop.resize(SMALL, Image.LANCZOS)

    pb = os.path.join(ROOT, "images", "card_portraits", "big", card_id + ".png")
    ps = os.path.join(ROOT, "images", "card_portraits", card_id + ".png")
    big.save(pb)
    small.save(ps)

    print("source %s %dx%d -> crop (%d,%d,%d,%d) -> big %dx%d, small %dx%d"
          % (os.path.basename(src), W, H, x0, y0, cw, ch, bw, bh, *SMALL))

    state = json.load(open(STATE, encoding="utf-8"))
    for relp, path in ((r"images\card_portraits\big\%s.png" % card_id, pb),
                       (r"images\card_portraits\%s.png" % card_id, ps)):
        h = sha(path)
        state["pending"].pop(relp, None)
        state["files"][relp] = {"src": h, "out": h,
                                "at": datetime.datetime.now().isoformat(),
                                "note": "card art re-crop from %s" % os.path.basename(src)}
    json.dump(state, open(STATE, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("state updated")
    return 0


if __name__ == "__main__":
    sys.exit(main())
