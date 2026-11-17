# Unity 接入说明

> 当前版本:**v0.01early**(六塔全升级树 + 144 个升级节点)

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
   - Play 后先进入**布防阶段**；点顶部「开始交战」才会出兵。
   - 左键点击战场上的白色圆形空位 → 高亮并弹出建造菜单。
   - 左键点击已建塔 → 显示射程圈，并弹出「详细数据 / 升级 / 拆除」。
   - 右键或中键拖动平移，滚轮缩放（默认只显示城墙与下半张地图）。
   - 顶部按钮：暂停 / 速度 ×1→×2→×5。

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
                               AudioService / BattleSlotPicker / BattleUiController
├─ Vfx                         BattleVfxSink
├─ Main Camera                 正交,俯视 -Y,CameraRig(拖动 / 缩放)
├─ World
│  ├─ Ground                   Plane 64×120(白色)
│  ├─ Wall                     横跨整张地图的城墙(基地)
│  └─ Slots                    Slot0..5 白底黑框圆盘
└─ Canvas                      Screen Space - Overlay (1920×1080 缩放)
   ├─ HUD                      金币 / 城墙 / 波次 / 状态 / 开始交战 / 暂停 / 速度
   └─ Hint                     操作提示
```

所有弹出菜单与确认窗由 `BattleUiController` 在运行时生成(白底 + 黑描边),不再需要左侧建造池。

`BattleDriver` 的 Inspector 参数：

- `difficultyId`：`easy` / `normal` / `hard`
- `mapId`：`standard`
- `maxWaves`：默认 30
- `seed`：确定性种子（Unity 不支持序列化 `ulong`，内部转 `ulong`）
- `autoPlay`：`true` = AI 自动建造升级（回归/演示）；`false` = 玩家用 UI 建造
  （一键生成默认 `false`）
- `manualWaveStart`：`true` = 布防阶段等待玩家点「开始交战」（默认开启）
- `startGoldOverride`：初始金币覆盖，默认 **1000000** 方便快速测试；正式发布改为 `0` 即使用难度表的起始资金

## 4. 坐标约定

逻辑单位为地图单位（`maps.json` 中 64 × 120）。

- 逻辑 `(x, y)` → 世界 `worldOrigin + (x, 0, y) × unitScale`
- 默认 `unitScale = 1`，即 1 逻辑单位 = 1 米；地图为 64 × 120 米
- 基地 = `y = 2.5` 的贴地城墙(线稿贴图,不再是有高度的 3D 墙);敌人沿自己竖线直线下行,撞墙即被吸收
- 出生点从中轴开始向两侧铺开,到边界后回绕中轴继续;可用宽度随波次增长
- 建造槽位是城墙前的一条直线（`y = 12`，x = 7 / 17 / 27 / 37 / 47 / 57，共 6 个）
- 射程硬上限 60（`TowerStatsBuilder.MaxRange`），防止后期覆盖整张地图
- 地图外为淡灰色底,地图四周只有细黑线,底边由城墙贴图接管
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
- 30 Hz 固定步长驱动、暂停、1×/2×/5× 倍速
- **布防阶段 + 手动开战**：`manualWaveStart = true` 时不会自动出兵，点「开始交战」才进入本波；
  每波结束回到布防时会清空临时物、补满全塔弹药并收回无人机编队
- 逻辑 → 视图同步：敌人、塔（炮塔朝向 / 等级点 / 选中）、投射物、地面区域、无人机编队、避雷针
- 弹道表现：机枪实弹为黑色曳光（塔外 1 米起、3 帧拉伸到射程端点、保持 8 帧后渐隐，起点先淡出）；激光为判定矩形同尺寸的白底浅灰边光束（保持 8 帧后渐隐）；持续照射型激光不可中断，会走完剩余照射时间
- 落点特效按 `Mechanic` 派发（`BattleVfxSink`）
- 玩家建造 / 升级 / 拆除：`BattleDriver.TryBuild` / `TryUpgrade` / `TrySell`（拆除返还：建造价 100% + 升级花费 50%）
- 升级树：六塔各 24 节点（Lv2/Lv3 三选一、Lv4 两个唯一升级、Lv5/Lv6 分支三选一、Lv7 四终点），共 144 个节点；升级窗按 `requires` 自动列出当前可选项
- 唯一升级机制：机枪（燃烧弹 / 高爆弹 / 低温钢芯 / 贯穿内爆）、激光（聚焦 / 扫荡持续照射、热熔、量子光压、散射棱镜、富集钚）、迫击炮（空爆 / 破片 / 燃烧 / 低温 / 阵列 / 贯顶内爆）、火箭（蜂窝 / 发射井 / 电浆 / 分裂 / 温压 / 氢弹）、电弧（电容 / 球形闪电 / 裂变 / 引雷针 / 漏电 / 雷电滚滚）、无人机（飓风 / 极光 / 雷暴 / 移动工厂 / 大舰巨炮 / 精装修）
- 经济：三档难度金币率统一 +10%（简单 1.32 / 普通 1.10 / 困难 0.99），用于轻微提升所有敌人金币掉落
- 点击交互：空位高亮 + 建造菜单；塔射程圈 + 详细数据 / 升级 / 拆除；确认窗与飘字提示
- 索敌：集中 / 扫射 / 战略（斩首）；无射程内目标时按 125% 侦察范围预转向；发射井、大舰巨炮使用战略索敌
- 朝向：需要转向的塔显示一条短炮口指示线（射程 20%、最长 10）；持续照射激光与免转向塔不显示
- 相机：右键（或中键）拖动平移、滚轮缩放，默认只显示城墙与下半张地图；视野按正交尺寸与宽高比夹取，不会移出地图边界
- 白色主题：白色地面 / 白色面板、黑色描边；基地为横跨整张地图的城墙
- 一键生成：材质 / 6 种塔预制体 / 13 种敌人预制体 / 投射物 / 编队 / 区域 / 避雷针 / 特效 / UGUI / 场景

## 7. 待补

- 拖拽建造、悬停高亮等更细的交互反馈
- 敌人受击 / 死亡、塔开火、投射物飞行的动画与音效挂点
- 波次预告、结算面板、失败重开流程
- 美术替换：当前全部为纯色几何体占位；正式资源按第 5 节命名后，在 `AssetCatalog` 里替换预制体即可
- 若后续换成 URP：区域/地面材质用的是 `Unlit/Color`，需要改 `Universal Render Pipeline/Unlit`
