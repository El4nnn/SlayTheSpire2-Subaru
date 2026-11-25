# -*- coding: utf-8 -*-
"""把烘焙后的角色资源同步到 viewer 的 pack 目录。

来源：mod 工程里的 subaru.spine-json / subaru.atlas / subaru_0..7.png
        （已压色、已重锚、已加回 41 帧重击、时间轴已压到 1.25 倍速）
去向：_extract/subaru3d_fist/pack/   （add-subaru.mjs 读这里）
"""
import io, json, os, shutil
from PIL import Image

MOD = r"C:\Users\Elan\Desktop\塔2mod\src\Subaru\Subaru\animations\characters\subaru"
PACK = r"C:\Users\Elan\Desktop\塔2mod\_extract\subaru3d_fist\pack"
BAK = r"C:\Users\Elan\Desktop\塔2mod\dist\backup\viewer_pack_before_sync"
WEBP_Q = 92

os.makedirs(BAK, exist_ok=True)

skel = json.load(io.open(os.path.join(MOD, "subaru.spine-json"), encoding="utf-8"))
atlas = io.open(os.path.join(MOD, "subaru.atlas"), encoding="utf-8").read()

pages = [l.strip() for l in atlas.splitlines() if l.strip().lower().endswith(".png")]
print("atlas pages:", pages)

# ---- animation durations: max key time across every timeline ----
def duration(anim):
    best = 0.0
    for group in ("slots", "bones", "ik", "transform", "deform", "events", "drawOrder", "attachments"):
        for _, tracks in (anim.get(group) or {}).items():
            if isinstance(tracks, dict):
                iter_tracks = tracks.values()
            elif isinstance(tracks, list):
                iter_tracks = [tracks]
            else:
                continue
            for keys in iter_tracks:
                if not isinstance(keys, list):
                    continue
                for k in keys:
                    if isinstance(k, dict) and isinstance(k.get("time"), (int, float)):
                        best = max(best, float(k["time"]))
    return round(best, 3)

clips = {}
for name, a in skel["animations"].items():
    d = duration(a)
    if d > 0:
        clips[name] = {"frames": None, "duration": d}
print("clips:", {k: v["duration"] for k, v in sorted(clips.items())})

# ---- backup old pack, then write new ----
for f in os.listdir(PACK):
    src = os.path.join(PACK, f)
    if os.path.isfile(src):
        shutil.copy2(src, os.path.join(BAK, f))
print("backed up old pack ->", BAK)

# remove stale pages
for f in os.listdir(PACK):
    if f.lower().endswith(".webp"):
        os.remove(os.path.join(PACK, f))

io.open(os.path.join(PACK, "subaru.json"), "w", encoding="utf-8", newline="\n").write(
    json.dumps(skel, ensure_ascii=False, indent=1))
# viewer 的 images 用 .webp 作 key，atlas 的页名必须跟着改成 .webp，否则贴图找不到
import re as _re
atlas_for_viewer, _n = _re.subn(r"(?m)^(subaru_\d+)\.png$", r"\1.webp", atlas)
print("atlas page headers -> .webp:", _n)
io.open(os.path.join(PACK, "subaru.atlas"), "w", encoding="utf-8", newline="\n").write(atlas_for_viewer)

for page in pages:
    src = os.path.join(MOD, page)
    im = Image.open(src).convert("RGBA")
    dst = os.path.join(PACK, page.replace(".png", ".webp"))
    im.save(dst, "WEBP", quality=WEBP_Q, method=6)
    print("  %-14s -> %-16s %dx%d  %.0f KB" % (page, os.path.basename(dst), im.width, im.height,
                                               os.path.getsize(dst) / 1024))

# frames count for the flipbook clips (from attachment timelines)
for name in list(clips):
    att = skel["animations"][name].get("slots", {}).get("body", {}).get("attachment")
    clips[name]["frames"] = len(att) if att else 0

manifest = {
    "pages": [p.replace(".png", ".webp") for p in pages],
    "fps": 20,
    "canvas": [300, 450],
    "clips": clips,
}
io.open(os.path.join(PACK, "manifest.json"), "w", encoding="utf-8", newline="\n").write(
    json.dumps(manifest, ensure_ascii=False, indent=1))
print("\nmanifest pages:", len(manifest["pages"]), "clips:", len(clips))
