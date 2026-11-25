# 菜月昴 · Natsuki Subaru（Slay the Spire 2 mod）

《Re:Zero》菜月昴的自制角色 Mod，含独立角色、卡牌、遗物、时间线解锁与过场演出（cut-in）。

- **当前版本**：`0.3.39`
- **适配游戏**：Slay the Spire 2 **β v0.114.0**
- **Steam 创意工坊**：item `3840131390`
- **本仓库**：源码 + 美术资源 + 设计/技术文档；可直接运行的发行包见 Releases。

> 非官方同人作品。Re:Zero 的角色、名称与视听素材版权归原作者及权利方所有，本 Mod 仅作非商业用途。

---

## 运行要求

| 依赖 | 版本 | 说明 |
|---|---|---|
| Slay the Spire 2 | **v0.114.0**（β 分支） | 低于此版本需用旧版 Mod |
| [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127) | ≥ 3.4.8 | 角色/卡牌/遗物基类与资源注册 |
| [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) | **≥ 0.6.16** | 时间线、纪元、设置页 |

> ⚠️ **RitsuLib 0.6.15 及更早版本不能用于 0.114**。它的 `compat` 只有 `0.107.1 / 0.111.0 / 0.113.0`，在 0.114 上会因 `CardModel.UpgradeInternal` / `DowngradeInternal` 签名变化导致两个 critical 补丁失败，进而回滚全部补丁、框架不初始化——表现是**所有 mod 角色显示为锁定、时间线节点全部“未知”**（存档本身不损坏）。

## 安装

**方式一（推荐）**：创意工坊订阅 `3840131390`，游戏启动时自动加载。

**方式二（手动）**：从 [Releases](../../releases) 下载 `Subaru_v<版本>.zip`，解压到

```
Slay the Spire 2/mods/Subaru/Subaru/
    Subaru.dll
    Subaru.pck
    Subaru.json
```

注意是两层 `Subaru` 目录（加载器按 `mods/<id>/<id>/<manifest>.json` 查找）。

## 从源码构建

需要：

- **Godot 4.5.1 .NET（mono）**——必须与游戏一致，版本更高会导致 `.pck` 无法加载
- **.NET 9 SDK**
- BaseLib / RitsuLib 的引用 DLL（`RitsuLib.References.props` 会从已安装的 Mod 目录自动定位）

`src/Subaru/Directory.Build.props` **不入库**（含个人绝对路径），需自行创建：

```xml
<Project>
  <PropertyGroup>
    <GodotPath>C:/path/to/MegaDot_v4.5.1-stable_mono_win64.exe</GodotPath>
    <Sts2Path>D:/SteamLibrary/steamapps/common/Slay the Spire 2</Sts2Path>
    <DotnetRoot>C:/path/to/dotnet</DotnetRoot>
  </PropertyGroup>
</Project>
```

构建与导出流程见 [`docs/构建与部署.md`](docs/构建与部署.md)。

## 目录结构

```
src/Subaru/            Godot 工程 + C# 源码
  SubaruCode/          卡牌 / 能力 / 遗物 / 补丁 / 时间线 / 演出
  Subaru/              资源：images / audio / video / animations / localization / scenes
  Subaru.json          Mod 清单
src/SubaruVariants/    变体工程
docs/                  设计与技术文档（基准数字、构建流程、根因库、交接记录）
```

## 已知问题

- **0.114 上 `UnblockableIgnoresHardToKillPatch` 被跳过**：`HardToKillPower` 的方法签名在该版本变化，该补丁暂未适配（效果缺失，不会崩溃）。
- 创意工坊更新在 2026-11 期间会进入 Valve 的人工审核队列，新版本可能需要等待数小时到一天；Releases 里的发行包不受影响。

## 文档

| 文档 | 内容 |
|---|---|
| [`docs/知识库/README.md`](docs/知识库/README.md) | 文档结构与阅读顺序 |
| [`docs/知识库/基准数字.md`](docs/知识库/基准数字.md) | 当前版本、文件指纹、各类数量（唯一权威数字来源） |
| [`docs/构建与部署.md`](docs/构建与部署.md) | 构建、导出 `.pck`、部署与发版流程 |
| [`docs/交接_2026-11-25_0.114适配与BaseLib依赖.md`](docs/交接_2026-11-25_0.114适配与BaseLib依赖.md) | 0.114 适配状态与框架依赖说明 |
| [`docs/交接_技术问题_2026-11-24.md`](docs/交接_技术问题_2026-11-24.md) | 根因库（现象 → 根因 → 判据） |

## 版权与许可

- 代码：仅供学习与二次开发参考，转载或使用请注明出处。
- 美术、音频、视频素材：来自《Re:Zero》相关作品，版权归原作者及权利方所有；本仓库仅作为非商业同人项目的资源备份，请勿商用或二次分发。
