# Unity 接入说明

本文件说明如何把 `Assets/Scripts` 的纯逻辑层接入 Unity 场景。
仓库现在已经是**可直接用 Unity 打开的工程骨架**（含 `ProjectSettings/ProjectVersion.txt`、
`Packages/manifest.json`），场景、预制体与 UGUI 由编辑器脚本一键生成，不需要手动拖引用。

## 0. 第一次落地（没有 Unity 经验照这个顺序做）

1. **安装 Unity Hub**：https://unity.com/download ，装完后登录账号。
2. **安装编辑器**：Hub → Installs → Install Editor → 选 **Unity 2022.3 LTS** 的最新补丁版。
   - 本仓库 `ProjectSettings/ProjectVersion.txt` 写的是 `2022.3.62f1`。
   - 如果你装的是别的 2022.3 补丁版，打开时弹「版本不一致」直接点 **Continue / 升级** 即可；
     也可以把该文件里的版本号改成你实际安装的版本。
3. **添加工程**：Hub → Projects → Add → Add project from disk → 选 `d:\MyWork`。
4. **首次打开**：Unity 会导入资源并下载 `com.unity.nuget.newtonsoft-json`（几分钟）。
   期间 Console 出现 "Compiling scripts" 属正常。
5. **生成演示场景**：菜单栏 **Tools → 防线指挥 → 一键生成演示场景**。
   脚本会自动创建材质、预制体、Canvas、GameRoot，并保存场景到 `Assets/Scenes/Battle.unity`。
6. **运行**：确认当前场景是 `Battle`，点顶部 **▶ Play**。
7. **操作**：
   - 左侧选一张塔卡 → 点战场上的圆形槽位 → 立即建造。
   - 点已建成的塔 → 右侧出现下一级可选升级，点「升级」消耗金币。
   - 顶部按钮：暂停 / 速度 ×1→×2→×3。

如果菜单没有出现，通常是脚本还在编译：等 Console 的编译转圈结束，或点 **Assets → Reimport All**。

### 常见问题

| 现象 | 处理 |
| --- | --- |
| 打开工程提示版本不一致 | 点 Continue；或改 `ProjectSettings/ProjectVersion.txt` |
| 菜单 Tools/防线指挥 不存在 | 脚本未编译完 / 有编译错误，先看 Console 第一条红字 |
| 场景全黑,只有 UI | Game 视图分辨率太窄；Camera rect 固定为屏幕中间 68%,建议 16:9 |
| 塔卡点了没反应 | 先点战场上的圆形槽位再点塔卡；金币不足时卡片会变暗 |
| 报 `Newtonsoft` 找不到 | Packages/manifest.json 里的包没下载成功，检查网络后在 Package Manager 里 Refresh |

## 1. 分层与目录

| 目录 | 内容 | 编译方式 |
| --- | --- | --- |
| `Assets/Scripts/**` | Core / Model / Combat / Entity / Battle / Targeting，纯 C#，无 UnityEngine 依赖 | `*.csproj`（`dotnet build`）+ Unity 的 Assembly-CSharp |
| `Assets/Unity/Runtime/**` | 宿主与表现：`BattleDriver`、`BattleViewRoot`、各类 View、资源索引、音频服务 | 仅 Unity |
| `Assets/Unity/UI/**` | UGUI 面板骨架：HUD、建造面板、升级面板、槽位拾取 | 仅 Unity |
| `Assets/Unity/Editor/**` | 一键生成场景的编辑器脚本（`ProjectSetupWizard`） | 仅 Unity 编辑器 |
| `Assets/Unity/Generated/**` | 自动生成的材质 / 预制体 / 索引表（可随时删除重生成） | 仅 Unity |
| `Tools/SimHarness/**` | 控制台模拟与自检（`--selftest` / `--waves` / `--towersim`） | `dotnet run` |
| `Tools/UiPrototype/**` | HTML 界面设计原型（静态示意，不接逻辑） | 浏览器 |

`Assets/Unity` 不在任何 csproj 的包含范围内，因此 `dotnet build` 不会因为 UnityEngine 依赖而失败。

> 重要：`Directory.Build.props` 把 dotnet 的 `obj/` `bin/` 重定向到了仓库根目录的 `.build/`。
> 否则 Unity 会把 `Assets/Scripts/*/obj` 里的 AssemblyInfo 一起编译，产生重复特性错误。


## 2. 依赖

- `com.unity.nuget.newtonsoft-json`（读取 `towers.json` 等配置表；Unity 自带 `JsonUtility` 不支持属性与枚举字符串）
- UGUI（`com.unity.ugui`，2021 起为内置包）
- 配置表直接使用 `Assets/StreamingAssets/Defs/*.json`，无需转换

## 3. 场景结构（一键生成的实际结果）

```
GameRoot                       BattleDriver / BattleViewRoot / BattleViewBinder
                               AudioService / BattleSlotPicker / BuildController
├─ Vfx                         BattleVfxSink
├─ Main Camera                 正交,俯视 -Y,viewport 中间 68%
├─ World
│  ├─ Ground                   Plane 80×120
│  ├─ Base                     基地标记
│  └─ Slots                    Slot0..3 槽位标记
└─ Canvas                      Screen Space - Overlay (1920×1080 缩放)
   ├─ HUD                      金币 / 基地 / 波次 / 状态 / 暂停 / 速度
   ├─ BuildPanel               BuildPanelView + 6 张 TowerCard 预制体
   ├─ UpgradePanel             UpgradePanelView + UpgradeNode 预制体
   └─ Hint                     操作提示
```

`BattleDriver` 的 Inspector 参数：

- `difficultyId`：`easy` / `normal` / `hard`
- `mapId`：`standard`
- `maxWaves`：默认 30
- `seed`：确定性种子（Unity 不支持序列化 `ulong`，内部转 `ulong`）
- `autoPlay`：`true` = AI 自动建造升级（回归/演示）；`false` = 玩家用 UI 建造
  （一键生成默认 `false`；想直接看战斗，把它勾上即可）

## 4. 坐标约定

逻辑单位为地图单位（`maps.json` 中 80 × 120）。

- 逻辑 `(x, y)` → 世界 `worldOrigin + (x, 0, y) × unitScale`
- 默认 `unitScale = 1`，即 1 逻辑单位 = 1 米；地图为 80 × 120 米
- 基地在 `(40, 8)`，敌人从上方 `spawnY` 随机横向位置向下推进
- `BattleViewRoot.ToLogical()` 提供反向换算，供点击拾取使用

## 5. 资源库约定

```
Assets/Art/Towers/<tower_id>_icon.png       塔图标（正方形，建议 256）
Assets/Art/Towers/<tower_id>.prefab         塔视图预制体（TowerView）
Assets/Art/Enemies/<enemy_id>.prefab        敌人视图预制体（EnemyView）
Assets/Art/Projectiles/<mechanic>.prefab    投射物视图（留空走默认）
Assets/Art/Vfx/<mechanic>.prefab            落点特效（cluster / napalm / ...）
Assets/Art/UI/*.png                         面板底、按钮、图标
Assets/Audio/Music/*.ogg
Assets/Audio/Sfx/Towers/<tower_id>_fire.wav
Assets/Audio/Sfx/Towers/<tower_id>_upgrade.wav
Assets/Audio/Sfx/Enemies/<enemy_id>_death.wav
Assets/Audio/Sfx/UI/ui_build.wav / ui_upgrade.wav / ui_slot.wav
```

- `AssetCatalog`（ScriptableObject）把 id 映射到图标 / 预制体 / 音效；缺失时留空即可，运行时自动跳过。
- `AudioLibrary` 一条 id 可挂多个片段随机轮换，`AudioService.Play("...")` 调用。
- 目前已有预览图：`Assets/Art/Towers/preview_machine_gun.jpg`、`preview_laser.jpg`、`preview_mortar.jpg`；
  火箭塔 / 电弧塔 / 无人机库的预览图待补绘。

## 6. 已实现（骨架）

- 配置表加载 + 启动期 `ModelValidator` 硬校验
- 30 Hz 固定步长驱动、暂停、1×/2×/3× 倍速
- 逻辑 → 视图同步：敌人、塔（炮塔朝向 / 等级点 / 选中）、投射物、地面区域、无人机编队、避雷针
- 落点特效按 `Mechanic` 派发（`BattleVfxSink`）
- 玩家建造 / 升级入口：`BattleDriver.TryBuild` / `TryUpgrade` → `BattleSimulation.TryBuildTower` / `TryUpgradeTower`
- UGUI 面板骨架：金币 / 基地耐久 / 波次 / 暂停 / 倍速、塔卡列表、升级节点列表、槽位点击拾取
- 一键生成：材质 / 6 种塔预制体 / 13 种敌人预制体 / 投射物 / 编队 / 区域 / 避雷针 / 特效 / UGUI / 场景

## 7. 待补

- 具体的 UI 布局、动效与交互反馈（悬停、禁用态、射程预览、拖拽建造）
- 敌人受击 / 死亡、塔开火、投射物飞行的动画与音效挂点
- 波次预告、结算面板、失败重开流程
- 美术替换：当前全部为纯色几何体占位；正式资源按第 5 节命名后，在 `AssetCatalog` 里替换预制体即可
- 若后续换成 URP：区域/地面材质用的是 `Unlit/Color`，需要改 `Universal Render Pipeline/Unlit`
