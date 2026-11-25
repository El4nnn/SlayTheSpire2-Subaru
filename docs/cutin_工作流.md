# Cut-in（立绘切入）工作流

> 📌 **canon** ｜ 最后核对：2026-11-23 ｜ cut-in 操作手册；§13.4 构图三铁律

> 目标：**游戏外**把演出调好，游戏里只做一次接线。每张新立绘的固定流程是三步，不碰代码。
> 建立 2026-11-21。首个样例：夏乌拉（`shaula`）。
> 当前形态：**剪影层 + 双框（梯形外框 / 平行四边形内框）+ 角色层**，角色允许溢出框外。

---

## 1. 三步循环

```powershell
# ① 出素材：白底立绘 -> 带 alpha 的 1024 宽 PNG（同时更新预览页的下拉清单）
python _tools\new_cutin.py --src _assets\cutin\src\<图>.png --name <名字>

# ② 调演出：双击打开（Edge / Chrome 都行）
_assets\cutin\preview.html

# ③ 导出：页面右下「复制 JSON」/「下载 cutin.json」-> 填进游戏读的那份
```

① 的输出：

| 文件 | 用途 |
|---|---|
| `src\Subaru\Subaru\images\cutin\<名字>.png` | 游戏资源，`res://Subaru/images/cutin/<名字>.png` |
| `_assets\cutin\work\<名字>_on_dark.png` | 深底验收图（看白边/毛边） |
| `_assets\cutin\manifest.js` | 预览页下拉清单（脚本自动生成，别手改） |

裁剪：`--y0/--y1` 指定竖向范围（0 = 图顶 / 省略 y1 = 图底），`--hole` 是封闭白洞的面积门槛（默认 200）。

---

## 2. 形状：梯形 vs 平行四边形（为什么要分开）

| 形状 | 专业叫法 | 作用 | 出处 |
|---|---|---|---|
| 梯形 | 「透视分格」：矩形做透视变形 | **纵深 / 压迫感**，视线高度决定强度 | [CLIP STUDIO TIPS 透视分格](https://tips.clip-studio.com/zh-cn/articles/8706)、[变形→透视→梯形](https://www.163.com/dy/article/JVMSUK960556D8G1.html)、[特殊分镜：倾斜格/变形格](http://mp.weixin.qq.com/s?__biz=MzU3ODI2NDk1OA==&mid=2247500090&idx=2&sn=3cc7141c0a1e5c4ddf2f9f838ef56684) |
| 平行四边形 | 四角斜切 / 剪切 | **速度、不稳定**，没有纵深 | [OpenKoma（矩形/平行四边形/梯形并列）](https://github.com/Reuben-Sun/OpenKoma) |

所以：**外框 A 用梯形（空间），内框 B 用平行四边形（速度）**。
两个框都是「四角独立控制」（`左上/左下/右上/右下`），所以梯形也能再叠倾斜，不受形状限制。
每个框有 4 个一键预设：矩形 / 梯形上宽 / 梯形下宽 / 平行四边形。

---

## 3. 四层结构（从后到前）

| 层 | 默认 | 说明 |
|---|---|---|
| **剪影** | 暗橘 `#8c3b10` α0.85，右移 28px | 角色图的着色副本（`source-in` 上色），在填色之上、描边之下 |
| **框 A** | 黑 `#0b0b10`，描边 5px，梯形，**不填充** | 外框，只描边 |
| **框 B** | 橙 `#e07826`，描边 4px，平行四边形，缩放 0.78、右移 64px，**渐变填充 24% → 0** | 内框：左浓右淡的暗底，把角色从背景里托出来 |
| **角色** | 不裁切 | 默认**允许超出两个框**（像手绘那样出框）；需要裁就点「裁进框 B」 |

绘制顺序（`preview.html` 与游戏侧必须一致）：
**框A 填色 → 框B 填色 → 剪影 → 角色 → 遮罩（整屏压暗 + 框挖洞）→ 框A/B 描边 → 卡名**。
填色在剪影下面，剪影不会被暗底糊掉；描边在最上面，遮罩不会把线压暗。

### 遮罩（veil）：为什么不是「把框填满压在上面」

把框填满、整块压在角色上层，等于把出框切掉（不透明）或把框外角色染色（半透明，接缝明显）。
真正的做法是反过来：**整屏盖一层暖黑，把框挖成洞**（canvas `evenodd`）——效果一样（框内亮、框外暗），
但遮罩是一整层，**没有接缝**，参数也只有一个暗度。

| 参数 | 说明 |
|---|---|
| `veil.on` | 开关 |
| `veil.color` | 暖黑 `#1a0f14`；纯黑会把暖色立绘压死 |
| `veil.alpha` | 暗度，默认 0.35；夏乌拉是深色系，超过 0.40 立绘细节就没了 |
| `veil.hole` | 挖洞用 `"B"`（内框，聚光更紧）或 `"A"`（外框，只压最外面） |

遮罩跟挖洞那一层的**速度/延迟/偏移一起动**（入场时同步滑进来）。

每层都有独立的 `速度 / 延迟`，默认 **框A 0.85× / 框B 1.05× / 剪影 1.35× / 角色 1.55×**，
延迟 0 / 30 / 55 / 70ms —— 四个不同的速度进来就是视差。

**节奏规则**（专业做法，[LottieFiles 编排指南](https://github.com/LottieFiles/motion-design-skill/blob/main/skills/motion-design/director/choreography.md)）：
**所有层用同一条缓动曲线（outCubic），只错开开始时间**；主角位移最大、最抢眼；错开一档 30~70ms。
出场同理，但方向相反：**角色先撤、框后收**（角色 1.20× / 剪影 1.10× / 框B 1.00× / 框A 0.95×）。
层的速度系数与「深度」对应（越远越慢），和 [Godot 视差的 `scroll_scale`](https://docs.godotengine.org/zh-cn/4.x/tutorials/2d/2d_parallax.html) 是同一套思路。

---

## 4. 预览页怎么用

| 操作 | 说明 |
|---|---|
| 拖图进左边 | 换任意 PNG |
| 左下「立绘」下拉 | 从 `images\cutin\` 里挑（清单由脚本生成） |
| 空格 | 播放 / 暂停；← → 单步 10ms |
| 「在角色后面 / 前面」 | 对应游戏 `BackCombatVfxContainer` / `CombatVfxContainer`，**必须肉眼定** |
| 「显示角色」 | 模拟战场里的昴/敌人只画虚线框，关掉可完全干净 |
| 「剪影 开/关」「裁进框 B」 | 见 §3 |
| URL 参数 | `?t=300` 冻结在 300ms；`?front=1` 切到角色前面 |

---

## 5. 导出的 JSON（游戏侧要读的结构）

```json
{
  "shaula": {
    "image": "shaula",
    "band": { "width": 0.58, "height": 0.98, "x": 0, "y": 0.5, "angle": 0 },
    "layers": {
      "silhouette": { "on": true, "color": "#8c3b10", "alpha": 0.85,
                      "scale": 1.02, "dx": 28, "dy": 6,
                      "speed": 1.35, "delayMs": 55,
                      "outSpeed": 1.10, "outDelayMs": 10 },
      "frameA": { "color": "#0b0b10", "width": 5, "fillAlpha": 0,
                  "fillGradient": { "on": false, "to": 0, "dir": "x" }, "glow": 0,
                  "scale": 1.0, "dx": 0,
                  "corners": { "leftTop": -0.03, "leftBottom": 0.03,
                               "rightTop": 0.05, "rightBottom": -0.01 },
                  "speed": 0.85, "delayMs": 0,
                  "outSpeed": 0.95, "outDelayMs": 0, "strokeDelayMs": 0 },
      "frameB": { "color": "#e07826", "width": 4, "fillAlpha": 0.24,
                  "fillGradient": { "on": true, "to": 0, "dir": "x" }, "glow": 0,
                  "scale": 0.78, "dx": 64,
                  "corners": { "leftTop": 0.06, "leftBottom": -0.04,
                               "rightTop": 0.06, "rightBottom": -0.04 },
                  "speed": 1.05, "delayMs": 30,
                  "outSpeed": 1.00, "outDelayMs": 20, "strokeDelayMs": 0 },
      "portrait": { "scale": 1.0, "offsetY": 0, "clip": false,
                    "speed": 1.55, "delayMs": 70,
                    "outSpeed": 1.20, "outDelayMs": 0 }
    },
    "veil": { "on": true, "color": "#1a0f14", "alpha": 0.35, "hole": "B",
              "fadeInMs": 120, "fadeOutMs": 140 },
    "timing": { "inMs": 180, "holdMs": 300, "outMs": 140,
                "hitAt": 0.59, "easeIn": "outCubic", "easeOut": "inCubic" },
    "showText": false,
    "layer": "behindCreatures",
    "shakePx": 7,
    "flashAlpha": 0.34
  }
}
```

| 字段 | 含义 |
|---|---|
| `band` | 所有图层的**母版矩形**：宽高（屏宽/屏高 %）、停位、整块角度 |
| `layers.*.corners` | 四个角相对母版的水平偏移（屏宽 %），**这就是梯形/平行四边形的全部信息来源** |
| `layers.*.scale / dx / dy` | 该层相对母版的缩放与位移（框 B 的 `dx` 决定它比框 A 往右偏多少） |
| `layers.*.speed` | 入场速度倍率（1.0 = 基准 `inMs`）；四个数不同才有视差 |
| `layers.*.delayMs` | 入场延迟，错位用 |
| `layers.*.outSpeed` / `outDelayMs` | **出场速度倍率与时延**（`outMs / outSpeed`）；退场时四层也会散开 |
| `layers.frame*.strokeDelayMs` | 描边比填充晚多少毫秒（`+30~50` 会有「线后追上来」的效果） |
| `veil.fadeInMs / fadeOutMs` | 遮罩自己的淡入淡出（位移仍跟挖洞那个框，否则洞和线会错位） |
| `layers.frame*.width / fillAlpha / fillGradient / glow` | 描边宽度、填充透明度、**渐变填充**（`on` 开关，`fillAlpha` = 起始透明度，`to` = 终点透明度，`dir` = `x` 左→右 / `y` 上→下）、外发光 |
| `layers.portrait.clip` | `false` = 角色允许出框（默认），`true` = 裁进框 B |
| `timing.hitAt` | 伤害落地的时间点（0~1），抖动/闪光/结算都对齐它 |
| `showText` / `layer` / `shakePx` / `flashAlpha` | 卡名开关、层级、抖动幅度、命中闪强度 |

---

## 6. 接进游戏（已接：`Vfx/CutIn.cs`）

| 项 | 位置 |
|---|---|
| 演出参数 | `src\Subaru\Subaru\data\cutin.json` → `res://Subaru/data/cutin.json`（按 id 分组，和导出的 JSON 一模一样） |
| 立绘 | `src\Subaru\Subaru\images\cutin\<image>.png` → `res://Subaru/images/cutin/<image>.png` |
| 播放器 | `SubaruCode\Vfx\CutIn.cs`：`PlayToHitAsync`（命中点返回）/ `PlayAndHoldAsync`（定格，等调用方 `Session.Exit()`）/ `PlayAndHoldForAsync`（定格固定时长后自动退场） |
| 调用方 | `Cards\MonsterCards.cs` 的 `Shaula`（`PlayToHitAsync` 后结算伤害）、`Cards\PuppetStrings.cs` 的 `Yae Tenzen`（`PlayAndHoldAsync` → 切屏演完 → `Exit()`）、`Cards\BeatriceContract.cs` + `Patches\EnemyDefeatSfxPatch.cs` 的贝蒂（见 §12）、`Cards\VincentContract.cs` 的文森特（`CutIn.PlaySignatureAsync`） |

要点：

- **在命中点返回**：`PlayToHitAsync` 等到 `timing.hitAt` 那一帧就返回，伤害由牌自己在那之后结算；演出本身继续播完并 `QueueFree`。
- **定格模式**：`PlayAndHoldAsync` 同样在 `hitAt` 返回，但把 `run.Held` 打开 → 退场时刻变成 `+∞`，所有层的退场进度恒为 0（立绘停在原地）。调用方 `Session.Exit()` 才把 `ExitAt` 写成当前时刻，之后照常滑走。没人放行最多定格 `MaxHoldMs = 9s`。
- **挂载位置**：`Creature.GetBackVfxContainer()`（房间的 `BackCombatVfxContainer`）。**不要用 `TopLevel`**（会让整棵子树跳出房间画序，跑到手牌上面）；根节点用 `RoomVfx.AlignToScreen()` 摆正 —— 位移**和缩放**都要抵消，只减 Origin 在带缩放的容器里会让画面错位/盖不满屏（2026-11-21 实测）。
- **画序用树顺序**（**不要设 `ZIndex`**，那是跨整个 canvas 排序的，一设就顶到 UI 上面）：填色 → 剪影 → 角色 → 遮罩 → 描边。
- **遮罩**是 `ColorRect` + 片元着色器（`SCREEN_UV` 里对一个凸四边形做叉积内外判定），洞的四个点每帧从框的 `Transform` 算，所以洞永远跟着框。
- px 类参数（`dx` / `dy` / `offsetY` / 描边宽 / 剪影偏移）按 **1280 设计宽** 等比缩放到实际分辨率。

### 验证日志（三行定位到底卡在哪一步）

```
[Subaru] cutin spec loaded: 2 entr(ies)      ← json 读到了（数字 = 配了几个角色）
[Subaru] cutin start 'yae' view=1920x1080 band=1114x1058 total=1040ms
[Subaru] cutin 'yae': held at 749ms (waiting for caller)   ← 定格模式：等调用方放行
[Subaru] cutin 'yae': exit at 2043ms (released by caller)  ← 调用方放行，开始退场
[Subaru] cutin freed                          ← 节点回收了
```

被策略挡掉时不会出现 `start`，而是：

```
[Subaru] cutin 'yae': skipped (already shown this turn)
[Subaru] cutin 'yae': skipped (per-turn limit 3 reached)
```

---

## 7. 出静帧（不开浏览器）

```powershell
$edge="C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$url="file:///" + ((Resolve-Path "_assets\cutin\preview.html").Path -replace '\\','/')
& $edge --headless=new --disable-gpu --hide-scrollbars --window-size=1380,760 `
        --virtual-time-budget=900 --screenshot="_assets\cutin\work\_frame_300.png" "$url?t=300"
```

---

## 8. 素材红线

| 项 | 要求 |
|---|---|
| 底色 | 纯白（255,255,255）不透明；半透明/渐变底会留灰边 |
| 宽度 | ≥ 800px（低于 650 会糊） |
| 构图 | 头顶不要贴边；出框效果要求图本身四周留一点空，否则切到刘海 |
| **构图（2026-11-21 用户定，硬性）** | **默认裁半身**（头到腰 / 袍摆上方），不要留全身：全身图缩进同样高的框里，人物只有半身图的一半大，脸就没了。裁法：`new_cutin.py --y1 <腰线>`（斯芬克斯 = 1510 / 源高 1869，切掉脚和小腿）。裁完 `portrait.scale` 统一 1.0。<br>**2026-11-23 修正**：「半身」是默认手法，不是硬性判据——判据是比例落进 **0.60–0.70**（见 §13.4）；帕克全身 0.60、强欲司教 58% 身高 0.65 都合格。**补白只许对称**，单边补必出空带。 |
| 锐度 | 被放大/降噪过的图（拉普拉斯均值 ≲25）干净但线条软，有原图优先用原图 |

---

## 9. 已知限制

- 预览页跑在 `file://`，浏览器禁止读本地 json → 清单用 `manifest.js`（脚本生成），**不要改成 fetch json**。
- 剪影用 `source-in` 给整张图上色，所以**图里的半透明像素也会被染成实色**（目前立绘没有半透明区，不影响）。
- 梯形/平行四边形的斜边会把「裁切模式」下的角色硬切掉，出框模式则不会。
- 导出靠复制/下载，页面不会直接改仓库文件。

---

## 10. 出场策略（谁、一回合几次、几个角色）

2026-11-21 用户定，落在 `Vfx/CutIn.cs` + `Rewind/CombatStats.cs` + 设置页：

```csharp
// 总开关 = 设置页「角色 cut-in」开 && 「每回合最多不同角色」> 0
if (CutIn.MayShow(id)) await CutIn.PlayAndHoldAsync(Owner, id);   // 会记账
// 终结那一击：CutIn.MayShowFinisher(id) —— 不占名额，只记账
```

| 规则 | 默认 | 设置项（`user://subaru_visual.json`） |
|---|---|---|
| 总开关 | 开 | `cutin.enabled` |
| 同一角色可重复 | 关（每角色每回合 1 次） | `cutin.repeat` |
| 每回合最多不同角色 | 3（0 = 关闭） | `cutin.max`（0–9） |

记账在 `CombatStats.CutInsThisTurn`（按角色 id 的 `HashSet<string>`），**每回合开始清空，死归回档也清空**。
`TryTakeCutInSlot(id, max)`：已有记录直接放行（不占新名额），名额满了返回 false。

---

## 11. 素材键白底的两个坑（2026-11-21）

1. **白色衣物会被当「封闭白洞」挖掉**：`_tools/key_portrait.py` 的 `fill_holes` 会把「不和边界连通、面积 ≥ `--hole`」的白块判成背景。八重·天膳的**白色围裙**整个消失了 —— 解决办法是 `--hole 999999999`（等于关掉挖洞），代价是发梢围出来的真空洞也不会被吃。

   ```powershell
   python _tools\new_cutin.py --src _assets\cutin\src\<图>.png --name <name> --hole 999999999
   ```

2. **朝向**：立绘要朝**敌人一侧**（战场右侧）。八重的原图朝左，键完用 PIL 水平镜像（`Image.FLIP_LEFT_RIGHT`），顺手重出 `_assets\cutin\work\<name>_on_dark.png` 深底预览肉眼验收。

白底键出后一定要看深底预览：围裙/白裙摆/白毛边是这类图最容易缺的一块。

---

## 12. 贝蒂（2026-11-21）

| 项 | 内容 |
|---|---|
| 立绘 id | `beatrice`（`images\cutin\beatrice.png`，1024×1059，半身裁到书本/蝴蝶结下方） |
| 素材来源 | 原图**自带透明底**（RGBA WebP）→ `--alpha` 跳过白底键出，只裁切+缩放（走白底键出会按 GBK 之外的 RGB 判黑，整张被吃掉） |
| 触发 1 | 打出「契约·贝蒂」 → `CutIn.MayShowSignature`（招牌演出：只受总开关管，不占每回合名额）→ `PlayToHitAsync` |
| 触发 2 | 贝蒂在场（`BeatricePower`）时用[阴魔法]牌打出**击杀** → 死亡钩子 `EnemyDefeatSfxPatch` → `PlayAndHoldForAsync`，定格到死亡动画播完 |
| 不拦截 | 阴魔法击杀**照播**通用击杀音效 + 击杀特效，只额外加一层立绘（八重那种「完全接管」是另一回事） |

```powershell
# 出素材（透明底原图）
python _tools\new_cutin.py --src _assets\cutin\src\beatrice.png --name beatrice --alpha --y1 1450
# 不开浏览器看静帧（按 cutin.json 的数值直出 1920×1080 两版：深底 / 浅底）
python _tools\preview_beatrice.py --id beatrice --out _assets\cutin\work\_cutin_beatrice_hold.png
```

`preview_beatrice.py` 只复刻 `CutIn.cs` 正式用到的那几层（填色 → 剪影 → 描边 → 立绘 → 遮罩挖洞），
参数直接读 `cutin.json`，所以调数值时不用反复进游戏；最终配色仍以游戏内为准。

验证日志：

```
[Subaru] cutin spec loaded: 3 entr(ies)
[Subaru] cutin start 'beatrice' view=1920x1080 band=1114x972 total=1170ms
[Subaru] cutin 'beatrice': hit at 842ms (target 842ms)          ← 契约牌 / 阴魔法击杀都会出现这行
[Subaru] cutin 'beatrice': held at 842ms (waiting for caller)   ← 阴魔法击杀：定格到死亡动画结束
[Subaru] cutin 'beatrice': exit at 2xxx ms (released by caller)
[Subaru] enemy defeat: yin magic kill with betty -> 贝蒂 cut-in
```

---

## 13. 角色与触发点一览（2026-11-23 更新，56 个）

| id | 立绘 | 触发 | 策略 |
|---|---|---|---|
| `shaula` | 夏乌拉 | 打出「贤者·夏乌拉」 | 普通 `MayShow`；那一击若清场走 `MayShowSignature` |
| `yae` | 八重·天膳 | 打出「八重·天膳」 | `MayShow` |
| `beatrice` | 贝蒂 | 打出「契约·贝蒂」／贝蒂在场时阴魔法击杀 | 契约走 `PlaySignatureAsync`；击杀走 `MayShow` + 定格到死亡动画结束 |
| `vincent` | 文森特 | **只在**打出「鲜血帝·文森特」 | `PlaySignatureAsync`（一局一次，不占名额，只在出牌触发，击杀不触发） |
| `sphinx` | 斯芬克斯 | 打出「大灾·斯芬克斯」「大灾形态」「尸兵·斯芬克斯」「不死王的洗礼」 | `SubaruCard.CutInId` 统一接线：带[契约]的走 `MayShowSignature`，其余走 `MayShow` |
| `echidna` | 艾姬多娜 | 打出「强欲的契约」「魔女的茶会」「强欲魔女·艾姬多娜」 | 同上（`CutInId => "echidna"`），配色取蝴蝶头饰的墨绿 `#1f6b47` |
| `otto` | 奥托 | 打出「言灵的加护」 | `CutInId => "otto"`；技能牌，只有立绘 |
| `reinhard` | 莱茵哈鲁特 | 打出「剑圣之友」 | `CutInId => "reinhard"`；该牌另有专属**斩击/击杀**演出（见 §14），不播通用弹幕与命中音 |
| `reid` | 初代剑圣·雷德 | 打出「初代剑圣·雷德」 | 同 `reinhard`（同一套演出，配色朱红 `#e0552f`；立绘半身裁到髋） |
| `sworddemon` | 剑鬼 | 打出「剑鬼之友」 | 同 `reinhard`（配色冷钢蓝 `#5a6b7d`） |
| `kishia` | 白蜘蛛·奇夏 | 打出「白蜘蛛·奇夏」 | `MayShow`；技能牌，只有立绘 |
| `yonaki` | 极彩色·夜鸣 | 打出「魂婚术」 | `MayShow`；配色深紫红 `#8a2f7a` |
| `cecilus` | 蓝色闪电·塞西鲁斯 | 打出「蓝色闪电·塞西鲁斯」 | `MayShow`；配色电光蓝 `#2f6fe0` |
| `sekhmet` | 怠惰魔女·赛赫麦特 | 打出「怠惰魔女·赛赫麦特」 | `MayShow`；配色梅紫红 `#9b2f7d`（攻击牌但**不接**斩击） |
| `daphne` | 暴食魔女·达芙妮 | 打出「暴食魔女·达芙妮」 | `MayShow`；棺材整体保留并旋转 +10° 摆正，底部水平裁切 |
| `minerva` | 愤怒魔女·密涅瓦 | 打出「愤怒魔女·密涅瓦」 | `MayShow`；配色琥珀金 `#c9962e` |
| `carmilla` | 色欲魔女·卡蜜拉 | 打出「色欲魔女·卡蜜拉」 | `MayShow`；配色暗青 `#1e5551`（用薄荷绿会和她的披风撞色） |
| `typhon` | 傲慢魔女·提丰 | 打出「傲慢魔女·提丰」 | `MayShow`；配色深蓝 `#1d4f8f`；裁到手（y1=1100/1988） |
| `pandora` | 虚饰魔女·潘多拉 | 打出「虚饰魔女·潘多拉」 | `MayShow`；配色冷靛 `#34406b` |
| `hector` | 忧郁魔人·赫克特 | 打出「忧郁魔人·赫克特」 | `MayShow`；配色暗梅紫 `#4a2a52` |
| `arakiya` | 食精灵者·亚拉基亚 | 打出「食精灵者·亚拉基亚」 | `MayShow`；配色青蓝 `#1f8f9f` |
| `orbart` | 毒辣翁·奥尔巴特 | 打出「毒辣翁·奥尔巴特」 | `MayShow`；配色暗青铜 `#8a6a2a`；**左侧补白让脸进框**（见 §13.3） |
| `gotz` | 狮子骑士·哥兹 | 打出「狮子骑士·哥兹」 | `MayShow`；配色深酒红 `#5a1f2a` |
| `gurobi` | 咒具师·葛路比 | 打出「咒具师·葛路比」 | `MayShow`；配色暖棕 `#7a5230` |
| `moguro` | 钢人·莫古洛 | 打出「钢人·莫古洛」 | `MayShow`；配色橄榄绿 `#5f6b22`；横构图（1.15）补白到 1.05 → 1024×975 |
| `ballroy` | 魔弹射手·巴尔罗伊 | 「魔弹射手·巴尔罗伊」+「尸兵·巴尔罗伊」**两张卡共用** | `MayShow`；配色赭褐 `#6b3f22`；比例 1.05 → 1024×975｜**2026-11-23 追加：延迟两段伤害打出[终结必杀]时也出立绘**（`BallroyShotPower` 的终结分支先 `MayShow` + `PlayToHitAsync`，再播终结动画，和本体那条路同节奏） |
| `madeleine` | 飞龙将·玛德琳 | 打出「飞龙将·玛德琳」 | `MayShow`；配色深玫红 `#8e3a5f`；半身（裁到髋）、比例 1.05 |
| `puck` | 帕克 | 打出「帕克」 | `MayShow`；配色冷灰 `#7b8593` / 幕布紫 `#a678d2`；**全身 Q 版**，397×659 = 0.60 |
| `sloth` | 怠惰司教 | 打出「怠惰因子」「真·不可视之手」 | `MayShow`；配色紫袍 `#43284f` / 内衬酒红 `#c8325a`；横构图 976×930 = 1.05（上下对称补白） |
| `regulus` | 强欲司教 | 打出「强欲司教·狮子心脏」「小国王」 | `MayShow`；配色冷紫 `#4a4a86` / 领结蓝 `#33408f`；大半身（裁到 58% 身高），769×1177 = 0.65 |
| `sirius` | 愤怒司教·洗魂 | 打出「愤怒司教·洗魂」 | `MayShow`；配色紫袍 `#43295e` / 内衬红 `#b0455c`；**半身 + 不补白** 900×647 = **1.39**（头顶出内框，双手张链超出画幅，按 2026-11-23 定不裁） |
| `ley` | 暴食司教·月蚀 | 打出「暴食司教·月蚀」 | `MayShow`；配色炭灰 `#3f3f4a` / 酒红 `#9c2f47`；**半身（头到髋）+ 不补白** 582×679 = 0.86（头顶出内框，匕首下端出画幅） |
| `eclipse` | 星蚀 | 打出「星蚀」 | `MayShow`；配色暖褐 `#5c4a2c` / 琥珀金 `#e09a3e`；**半身（头到裙摆）+ 不补白** 1006×771 = 1.31（呆毛与头发顶出内框，左侧发梢超出画幅） |
| `capella` | 色欲司教·变异 | 打出「色欲司教·变异」 | `MayShow`；配色暗紫 `#4d3560` / 品红 `#c04a7a`；**半身 + 不补白** 981×660 = 1.49（**裁窗整体下移一个头高**：头顶出内框、下沿同步下移；翼尖裁掉属用户允许） |
| `pride` | 傲慢司教 | 打出「傲慢司教」 | `MayShow`；配色暗紫 `#3a2a4e` / 亮紫 `#7a4fb0`；**原图即半身 + 不补白** 953×953 = 1.00（头顶出内框，伸展的手留在画面内） |
| `elsa` | 艾尔莎（猎肠者） | 打出「艾尔莎」 | `MayShow`；配色墨紫 `#2a1f2e` / 紫 `#8a4a86`；**原图即半身 + 不补白** 854×1265 = 0.68（头顶出内框） |
| `meili` | 梅莉（魔兽使） | 打出「梅莉」 | `MayShow`；配色墨蓝 `#2b2a3c` / 淡紫蓝 `#6d74c0`；**原图即半身 + 不补白** 921×1490 = 0.62（头顶出内框，怀里抱着魔兽） |
| `volcanica` | 神龙 | 打出「神龙」 | `MayShow`；配色钢蓝 `#2a3348` / 蓝 `#4a6fb0`；**半身 + 不补白** 971×514 = 1.89（同色欲司教处理：顶翼裁掉、双翼超出画幅不裁） |
| `julius` | 尤利乌斯（最优骑士） | 打出「最优骑士·尤利乌斯」（`BestFriend`） | `MayShow`；配色暗紫 `#3a3050` / 淡紫 `#8f7ad0`；**半身（头到髋）+ 不补白** 882×956 = 0.92（头顶出内框） |
| `felix` | 菲利斯（青·菲利斯） | 打出「青·菲利斯」（`BlueFriend`） | `MayShow`；配色墨蓝 `#2a3350` / 宝蓝 `#3b6fc4`；**原图即半身 + 不补白** 685×1123 = 0.61（头顶出内框；**擦掉了原图右上的浮空星星**） |
| `crusch` | 库珥修（风视的加护） | 打出「风视的加护」（`CruschFriend`） | `MayShow`；配色墨绿 `#1f2f2a` / 松绿 `#2f6b4f`；**原图即半身 + 不补白** 884×1278 = 0.69（头顶出内框） |
| `priscilla` | 普莉希拉（太阳的加护） | 打出「太阳的加护」（`PriscillaFriend`） | `MayShow`；配色暗红 `#301a1c` / 暖橙 `#c2552a`；**原图即半身 + 不补白** 921×1059 = 0.87（头顶出内框；扇子与发饰完整） |
| `emilia` | 艾米莉亚（艾米莉亚的祝福） | 打出「艾米莉亚的祝福」（`EmiliaBlessing`） | `MayShow`；配色暗紫 `#2e2742` / 紫 `#6a4aa8`；**原图即半身 + 不补白** 917×1135 = 0.81（头顶出内框；臂上小猫完整） |
| `ram` | 拉姆（千里眼 / 鬼神再临） | 打出「千里眼」（`RamFriend`）+「鬼神再临」（`KishinReturn`）**两张卡共用** | `MayShow`；配色墨紫 `#2e2230` / 玫红 `#c25f8a`；**原图即半身 + 不补白** 836×1223 = 0.68（头顶出内框） |
| `theresia` | 特蕾西亚（死神的加护） | 打出「死神的加护」（`Theresia`） | `MayShow`；配色暗红 `#2e1a20` / 绯红 `#b02a44`；**原图即半身 + 不补白** 716×774 = 0.93（头顶出内框） |
| `mimi` | 米米（三分的加护） | 打出「三分的加护」（`MimiFriend`） | `MayShow`；配色暖褐 `#3a2c1e` / 琥珀 `#d9a03a`；**全身 Q 版、只截掉脚** 994×1424 = 0.70（头顶出内框；法杖完整） |
| `roswaal` | 宫廷小丑·罗兹瓦尔 | 打出「宫廷小丑」（`RoswaalFriend`） | `MayShow`；配色暗紫 `#33224a` / 紫罗兰 `#a067d0`（取外套主色 `#a878c0`）；**原图即半身（头到髋）+ 不补白** 958×945 = 1.01（头顶礼帽出内框，双手超出画幅不裁） |
| `liliana` | 莉莉安娜（传心的加护） | 打出「传心的加护」（`LilianaFriend`） | `MayShow`；配色暖棕 `#4a3318` / 金黄 `#dfa63a`（取金发）；**原图即半身（头到大腿）+ 不补白** 1005×939 = 1.07（头顶呆毛出内框，双马尾向两侧甩开、左端被屏幕边缘切掉，按 2026-11-23 定不裁） |
| `yugard` | 荆棘帝·尤加尔德 | 打出「荆棘帝·尤加尔德」（`Yugard`） | `MayShow`；配色暗红 `#3d1216` / 绯红 `#b3222e`；**半身（头到大腿）+ 不补白** 761×692 = 1.10（王冠与发顶出内框，左手与荆棘杖超出画幅、杖首左侧被屏幕边缘切掉，按 2026-11-23 定不裁） |
| `librey` | 毒蛇·历布莱 | 打出「毒蛇·历布莱」（`Librey`） | `MayShow`；配色蛇绿 `#16302a` / 青绿 `#2f8f6a`；**半身（头到手）+ 不补白** 950×1020 = 0.93（**杖尖高出头顶太多，削掉上方 85px 让头顶冲过内框**，杖身继续出画幅、尾尖在画面内） |
| `celestia` | 过路魔·塞蕾丝缇雅 | 打出「过路魔·塞蕾丝缇雅」（`Celestia`） | `MayShow`；配色炭灰 `#2b2b30` / 亮绿 `#6ec832`（取和服青绿纹）；**半身（头到腰下）+ 不补白** 836×800 = 1.05（原图是**红底**，抠图后残留的红色 + 红边浅粉描边要点掉：按 `r-g>55` 判红清掉，再把 alpha 收 1px 削掉粉边；头顶贴着画幅上沿，右袖与白披风出画幅） |
| `kafma` | 战虫将·卡夫马 | 打出「战虫将·卡夫马」（`Kafma`） | `MayShow`；配色深绿 `#2a2a24` / 草绿 `#4f8f2f`（取绿发绿纹）；**原图即半身（头到大腿）+ 不补白** 815×1351 = 0.60（头顶发尖出内框，右手一直垂到画面下沿所以**不从腰线裁**，免得切断手） |
| `kulgan` | 八臂·库尔刚 | 打出「八臂·库尔刚」（`Kulgan`） | `MayShow`；配色深蓝 `#1e2440` / 靛蓝 `#4a63cc`（取蓝肤色）；**原图即半身 + 不补白** 1680×895 = **1.88**（宽构图，两把大刀被屏幕左右边缘切掉属允许；白底加**浅色刀面**，`cutin_ai.py`(rembg) 会把浅色刀面当背景删掉 → 改用**从四周泛洪去白**（被轮廓线包住的部分保留），再削掉顶部 60px 让头顶出内框） |
| `luan` | 天剑·劳安 | 打出「天剑·劳安」（`Luan`） | `MayShow`；配色墨黑 `#1c1a20` / 金黄 `#cfa634`（取斗篷金线 + 立领）；**半身（头到腰下）+ 不补白** 777×965 = 0.81（斗笠顶出内框；斗篷下摆与刀鞘下端出画幅） |
| `lamia` | 拉米亚·戈德温 | 打出「拉米亚·戈德温」（`Lamia`） | `MayShow`；配色暗红 `#2a1418` / 砖红 `#a63430`（取红袍）；**半身（头到腰）+ 不补白** 647×714 = 0.91（发顶与发冠出内框，左手向左侧伸出、长袍下摆出画幅） |
| `todd` | 陶德·方古 | 打出「陶德·方古」（`ToddFang`） | `MayShow`；配色暗红 `#2a1a1e` / 赤红 `#c02a3e`（取背带与护臂）；**原图即半身（头到大腿）+ 不补白** 799×1197 = 0.67（发顶出内框；**右手垂到腰带以下**，所以按例外三不从腰线裁，免得切断手） |

### 13.3 脸出框 / 构图偏移（2026-11-22 奥尔巴特）

立绘是**居中贴到幕布**的，所以「角色在图里偏左」= 在游戏里也偏左，脸可能贴到框线上。
`cutin.json` 的 `layers.portrait` **只有 `offsetY`，没有 `dx`**，所以横向位置只能靠**改 PNG 本身**：

```
补白量 P 的算法（不裁披风、不溢出幕布的极限值）：
  渲染宽度 = 1058 * (W + P) / H      （立绘高度恒等于幕布高度 0.98*1080）
  要求渲染宽度 <= 1113（幕布宽 0.58*1920）
  => P <= 1113 * H / 1058 - W
```

奥尔巴特实例：W=1024, H=1233 → P = 273 → 1297×1233，脸从 23% 移到 34%，披风自然轮廓正好贴幕布右缘。
**不要为了「脸居中」去裁右侧**——那会把披风/纹章切出直边（用户明确否掉了这一版）。

#### 13.3.1 判定基数是幕布（**出框是设计**，2026-11-22 用户定案）

用户原话：「就沿用之前 24 个角色的标准，**角色要出框**。」

- 判定基数是**幕布** `band 0.58×0.98` → 1113×1058，不是内板；
- 立绘高度恒等于幕布高 → 渲染宽 = **1058 × (W/H)**；
- `portrait.clip = false`：立绘**不被任何框裁剪**——莱茵哈鲁特的头顶在幕布上沿外、奥托同样，24 个角色都这样；
- 唯一硬上限：**比例 ≤ 1.05**（渲染宽 ≤ 1113），超过 1.05 才会被屏幕边缘切掉。

> **2026-11-23 用户覆盖（现行）**：**半身裁切 + 头顶出内框优先**。
> 允许比例 **> 1.05**：横向超出的部分（手 / 链子 / 袖子）**不裁、也不补白**，超出画幅不算问题。
> 直接后果：立绘高度恒等幕布高 → 头顶必定冲过内框；两侧可能被屏幕边缘切掉——用户明确接受。
> 做法：`getbbox` 收紧 → 按腰线裁到半身 → **不做任何 padding**。

| 比例 W/H | 渲染宽 | 结果 |
|---|---|---|
| ≤ 1.05 | ≤ 1113 | 完整显示（**推荐**）；超出内板无所谓，出框是设计 |
| > 1.05 | > 1113 | 被屏幕边缘切，必须补白 |

**张手臂 / 横构图**（巴尔罗伊 1.30、莫古洛 1.15）补透明，不要裁手臂，补到比例 ≤ 1.05 即可：

```
python _tools\cutin_pad_ratio.py --name <id> --max-ratio 1.05
```

> 内板 868 只是**参考**，不是规则：`frameB` 是 `scale 0.78 / dx +64`，横向区间是「中心 −370 … +498」，
> 要完全塞进内板得比例 ≤ **0.70** —— 老角色正好落在 0.62–0.71，所以视觉上也不出内板；
> 横构图（1.05，如巴尔罗伊 / 莫古洛）本来就出框，这是允许的，**别为了「进内板」去裁手**。
> 0.82 也不是阈值（仍会左侧探出约 53px），别拿它当规矩。

### 13.4 构图三条硬规则（2026-11-23 定稿，**取代** 11-21 的旧三条）

> 起因：11-21 的第三条「人别太大、宁可小一圈」被执行成「给 PNG 上下补白」，补白又全加在底部，
> 结果屏幕上人物飘在上半截、下面一条空带。用户原话：「你为什么只把人物往上挪，下面空那么大一截」。
> 证据与验收见 `交接_技术问题_2026-11-23.md` §2。

| 规则 | 做法 |
|---|---|
| **画布 = 内容 bbox** | PNG 高度就是人物高度，**不补白** → 立绘高度恒等幕布高（`p_sc = bh / tex.height`），头顶自然冲过内框（**出框是设计**） |
| **只有 W/H > 1.05 才补** | 横构图（伸胳膊）补到 1.05，且**上下对称**（`cutin_pad_ratio.py` 默认 `align=center`），绝不单边补 |
| **比例对齐老角色** | 目标 W/H ≈ **0.60–0.70**；不够窄就往身体下部多裁（半身 → 大半身 / 全身），**不是靠补白把比例压小** |

补白方向只有一种：**对称**。只补下边 = 必出空带，2026-11-23 因此被要求返工过一次。

> **2026-11-23 追加（优先于上面第一条）**：**不补白**才是现在的默认——先按腰线裁半身，
> 让比例自然 >1（横构图）或 ≈0.8（竖构图），**不 padding**；头顶必然出内框，手/链子超出画幅不管。
> 补白只在「用户明确要求把人缩小」时才用。

**半身仍是默认、但不是硬性**：人型角色裁半身脸才有分量；帕克（Q 版体型）用全身 0.60、
强欲司教裁到 58% 身高 0.65 —— 都以「比例落进 0.60–0.70」为准，不以「裁到腰」为准。

> 已废止：~~「人别太大，宁可小一圈」靠补白实现~~（11-21 旧口径，直接导致了 11-23 的返工）。

### 13.1 嫉妒魔女·莎缇拉（**暂无立绘**，2026-11-22 用户定）

`SatellaWitch` 有卡但没有 `CutInId`：没有官方立绘，先空着。不加 `CutInId` 时
`TryPlayCutIn` 直接返回 false，**不占每回合名额、也不会报错**，等有图再接。

### 13.2 新增角色的最短流程（2026-11-22 定型）

```powershell
# 1) 抠图（三种素材各有对应的路，优先试 rembg）
python _tools\cutin_ai.py --src <图> --name <id> [--y1 N]     # rembg + 裁切 + 缩 1024 + 深底预览
#    白底、绿幕、棋盘格 JPG 都先试这一条；不行再退回
#    _tools\cutin_white.py（白底）/ cutin_chroma.py（绿幕）/ cutin_sworddemon_checker.py（棋盘格）

# 2) 写进 cutin.json（几何自动照抄 reinhard，只换配色）
python _tools\add_cutin_entry.py --name <id> --silhouette <色> --frame-a <色> --frame-b <色> --veil <色>

# 3) 卡上覆写 CutInId => "<id>"，然后 publish → deploy

# 4) 出静帧验收（不开游戏）
python _tools\preview_beatrice.py --id <id> --out _assets\cutin\work\_still_<id>.png
```

**`cutin.json` 的长度单位是「设计像素」**，`portrait.scale` 保持 1.0；立绘在游戏里
**高度恒等于幕布高度**（`p_sc = bh / tex.height`），所以：图越窄 → 幕布内越窄、人越大；
图越宽 → 横向溢出台面。现行做法是**先按腰线裁半身、不补白**，比例自然落在 0.6–1.9 都算正常
（>1.05 允许，见 §13.3.1 / §13.4 的 2026-11-23 覆盖）。

文森特的素材是**白底**（不是透明底）：`new_cutin.py --hole 999999999` 键出后，
右侧手臂与身体之间会留一块「封闭白洞」，用 250 阈值的种子泛洪补掉（脚本见本轮会话），
白衬衫/白手套是真实内容，不能连洞一起挖。


---

### 13.5 待接立绘清单（2026-11-23 按代码扫，勿凭记忆）

> 生成方式：扫 `src\Subaru\SubaruCode` 里所有 `: SubaruCard(` 类，取**没有** `CutInId` 的；
> 再剔除「非角色」（打击/防御/基础魔法/诅咒/纱幕系/纯技能）。脚本：`_tools\tmp\audit_cutin_gap.py`。
> **要确认某人做没做，跑一次脚本，不要凭印象**——2026-11-23 我就把「初代剑圣」错列进待做（其实 `reid` 早做了）。

**已有立绘但走专属演出、不需要 `CutInId`**：`beatrice`（契约/阴魔法击杀）、`shaula`（击杀）、`vincent`（契约）、`yae`（八重·天膳，直接调 `CutIn`）。

**待接：0 个（2026-11-23 全部接完）**

> 口径：**角色的专属技能牌也算**（例：鬼神再临 `KishinReturn` 属拉姆，2026-11-23 随拉姆一起接入）。
> 所以判断「某人做没做」时，要连他/她的技能牌一起看。

| 组 | 角色（类名） |
|---|---|
| 魔兽 / 怪物（0） | —（已全部接完；其余魔兽无立绘） |
| 其他（0） | —（已全部接完；没有立绘的见下一行） |
| 无立绘（设计如此，**不接**） | 嫉妒魔女·莎缇拉 `SatellaWitch`、首席骑士 `FirstKnight`、英雄 `Hero`、柯尔·雷欧尼斯 `CorLeonis`、白鲸·消失之雾 `Hakugei`、黑蛇·疫病 `Kurohebi`、大兔·增殖 `Otogi`、黑龙·瓦尔格伦 `Valgren`、调停者·梅拉奎拉 `MediatorMelakuera`、帕特拉修 `Patrasche`、石块·姆斯佩尔 `Muspel`（**2026-11-23 用户确认这 11 个没有图**；不加 `CutInId` 不占名额、不报错） |
