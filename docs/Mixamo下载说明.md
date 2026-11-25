# Mixamo 下载说明（给昴做 5 段动作）

> 📌 **reference** ｜ 最后核对：2026-11-23 ｜ 下载参数

目标：拿到 5 个「只有骨架 + 动作」的 FBX，我把它们重定向到 Tripo 的 78 骨绑定上，
再渲成 20fps 的 2D 逐帧。

**不需要上传我们的模型**——我只要骨头怎么动，不需要 Mixamo 的皮。

---

## 一、登录

打开 <https://www.mixamo.com>，用 Adobe ID 登录（免费，没有就注册一个，或用 Google 账号）。

登录后进的是 **Animations** 标签页（顶部三个标签：Characters / Animations / Upload）。

---

## 二、下这 5 个动作

在左上搜索框里逐个搜，点缩略图让它跑起来，确认动作方向后再下载：

| 存成 | 搜索关键词 | 备选（搜不到时） | 目标时长 |
|---|---|---|---|
| `idle.fbx` | **Breathing Idle** | Idle / Standing Idle | 2.0s |
| `attack.fbx` | **Punch** | Cross Punch / Jab | 1.2s |
| `cast.fbx` | **Cast Spell** | Standing Cast Spell / Magic Cast | 1.6s |
| `hurt.fbx` | **Hit Reaction** | Standing React | 1.0s |
| `die.fbx` | **Death From Front** | Falling Back Death | 2.3s |

时长不用跟表里完全一致——我会在 Blender 里重采样到目标时长，但**差太多就不好看**（比如 0.8s 的死亡硬拉成 2.3s 会像慢放）。

---

## 三、下载设置（关键，逐个核对）

点开动画预览窗口右上角的 **Download** 按钮，弹窗里选：

| 选项 | 选什么 | 为什么 |
|---|---|---|
| Format | **FBX Binary (.fbx)** | Blender 读得最稳 |
| Skin | **Without Skin** | 只要骨架，体积从 5MB 降到 300KB |
| Frames per Second | **30** | 与游戏一致 |
| Keyframe Reduction | **none** | 动作细节不丢 |
| In Place | **☑ 勾上** | 去掉根位移，否则角色会跑出画面 |

> **In Place 只有带位移的动作才显示这个勾**（比如 Death From Front 可能没有）。没有就说明它本身是原地动作，不用管。

---

## 四、存到哪

全部改好名字，丢进这个文件夹：

```
C:\Users\Elan\Desktop\塔2mod\_extract\mixamo\
```

最终应该是 5 个文件：

```
idle.fbx
attack.fbx
cast.fbx
hurt.fbx
die.fbx
```

---

## 五、下载完告诉我

我会接着做：

1. 写 Mixamo（`mixamorig:Hips` 等 65 骨）→ Tripo 78 骨的映射表
2. 在 Blender 里重定向，把动作搬到昴身上
3. 每段重采样到目标时长，渲 20fps、300×450、55°/6° 正交
4. 自动裁边、打图集、生成 spine flipbook
5. 塞进现有 viewer 当第 7 个角色，你先看效果

如果某个动作搜不到，直接告诉我，我换关键词或改用 Blender 手摆。
