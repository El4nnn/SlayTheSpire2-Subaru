# -*- coding: utf-8 -*-
"""Apply the agreed 2D look to every art asset, idempotently.

Standard
    art   -> HSV saturation x1.4, luminance snapped to 5 bands, alpha untouched
    skip  -> alpha/outline masks, UI energy symbols, placeholder card art,
             and the character atlas (baked by its own script)

Idempotency is CONTENT-hash based, not "backup file exists":
if an asset is later replaced with new art, its hash no longer matches the
recorded output hash and it gets re-processed automatically.  A stale backup
must never suppress a genuine re-bake.

State lives in dist/backup/images_orig/STATE.json; the pre-bake copy of each
processed file is kept beside it, mirroring the source tree.
"""
import os, sys, json, hashlib, shutil, datetime
import numpy as np
from PIL import Image

ROOT = r"C:\Users\Elan\Desktop\塔2mod\src\Subaru\Subaru"
BACK = r"C:\Users\Elan\Desktop\塔2mod\dist\backup\images_orig"
STATE = os.path.join(BACK, "STATE.json")
SAT, BANDS = 1.4, 5
EXT = (".png", ".jpg", ".jpeg", ".webp")


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def skip_reason(rel):
    r = "/" + rel.replace("\\", "/").lower()
    if "/animations/characters/subaru/" in r:
        return "character atlas (baked by bake_2d.py)"
    if "_outline" in r or "/outline/" in r:
        return "alpha/outline mask"
    if "/charui/" in r and r.endswith("_energy.png"):
        return "UI symbol"
    # not-yet-produced art is tracked by content hash in STATE.json "pending",
    # so a finished asset flips to BAKED automatically; no path rule needed here.
    return None


def rgb2hsv(rgb):
    r_, g_, b_ = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-8), 0.0)
    h = np.zeros_like(mx); m = d > 1e-8
    idx = m & (mx == r_); h[idx] = ((g_[idx] - b_[idx]) / d[idx]) % 6
    idx = m & (mx == g_) & ~(mx == r_); h[idx] = (b_[idx] - r_[idx]) / d[idx] + 2
    idx = m & (mx == b_) & ~(mx == r_) & ~(mx == g_); h[idx] = (r_[idx] - g_[idx]) / d[idx] + 4
    return h / 6.0, s, mx


def hsv2rgb(h, s, v):
    i = np.floor(h * 6.0).astype(np.int32) % 6
    f = h * 6.0 - np.floor(h * 6.0)
    p = v * (1 - s); q = v * (1 - f * s); t = v * (1 - (1 - f) * s)
    out = np.zeros(h.shape + (3,), np.float32)
    for k, (aa, bb, cc) in enumerate([(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)]):
        m = i == k
        for c, val in enumerate((aa, bb, cc)):
            if isinstance(val, np.ndarray):
                out[..., c][m] = val[m]
            else:
                out[..., c][m] = val
    return np.clip(out, 0, 1)


def transform(im):
    arr = np.array(im.convert("RGBA"), np.float32) / 255.0
    h, s, v = rgb2hsv(arr[..., :3])
    rgb = hsv2rgb(h, np.clip(s * SAT, 0, 1), np.round(v * (BANDS - 1)) / (BANDS - 1))
    out = np.concatenate([rgb, arr[..., 3:4]], -1)
    return Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8))


state = {"files": {}, "runs": []}
if os.path.exists(STATE):
    try:
        state = json.load(open(STATE, encoding="utf-8"))
    except Exception:
        pass
if "files" not in state:
    state["files"] = {}
if "runs" not in state:
    state["runs"] = []

files = []
for dirpath, _, names in os.walk(ROOT):
    for n in names:
        if os.path.splitext(n)[1].lower() in EXT:
            files.append(os.path.relpath(os.path.join(dirpath, n), ROOT))
files.sort()

done, skipped, failed, alpha_bad = [], [], [], []
for rel in files:
    src = os.path.join(ROOT, rel)
    why = skip_reason(rel)
    if why is None:
        h_now = sha(src)
        # not-yet-produced art: identified by the placeholder's own hash, so
        # dropping the real asset in place flips it to "bake" with no code change
        if state.get("pending", {}).get(rel) == h_now:
            why = "pending art (placeholder unchanged)"
        else:
            rec = state["files"].get(rel)
            if rec and rec.get("out") == h_now:
                why = "already processed"
    if why is not None:
        skipped.append((rel, why))
        continue
    try:
        im = Image.open(src)
        alpha_before = np.array(im.convert("RGBA"))[..., 3].copy()
        out = transform(im)
        bak = os.path.join(BACK, rel)
        os.makedirs(os.path.dirname(bak), exist_ok=True)
        shutil.copy2(src, bak)          # backup reflects the current pre-bake state
        out.save(src)
        alpha_after = np.array(Image.open(src).convert("RGBA"))[..., 3]
        if int((alpha_before != alpha_after).sum()):
            alpha_bad.append(rel)
        state["files"][rel] = {"src": h_now, "out": sha(src), "at": datetime.datetime.now().isoformat()}
        state.get("pending", {}).pop(rel, None)
        done.append(rel)
    except Exception as e:
        failed.append((rel, str(e)[:80]))

state["runs"].append({
    "at": datetime.datetime.now().isoformat(),
    "processed": len(done), "skipped": len(skipped), "failed": len(failed),
    "params": {"sat": SAT, "bands": BANDS},
})
state.setdefault("pending", {})
os.makedirs(BACK, exist_ok=True)
json.dump(state, open(STATE, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

print("scanned=%d processed=%d skipped=%d failed=%d" % (len(files), len(done), len(skipped), len(failed)))
for rel in done:
    print("  BAKED   ", rel)
for rel, e in failed:
    print("  FAILED  ", rel, e)
print("alpha changed on %d files (must be 0)" % len(alpha_bad))
from collections import Counter
for reason, n in Counter(w for _, w in skipped).most_common():
    print("  skipped %-3d  %s" % (n, reason))
print("state ->", STATE)
