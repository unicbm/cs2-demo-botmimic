# CS2 DemoTracer

Trace CS2 demos into bot-executable route replays.

**语言：** [English](../README.md) | 简体中文

把 CS2 比赛 demo 里的真人路线，转换成 bot 可以在本地服务器里执行的 route replay。

如果这个项目对你有帮助，欢迎给一个 Star。这样其他 CS2 工具和插件开发者也更容易找到它。

## 演示

bot 会回放从 CS2 demo 转换出的移动、视角、开火和武器状态；第一人称观察视角会跟随 replay 状态同步。

![Nuke 第一人称 CS2 bot replay](media/first-person-replay-nuke.gif)

[查看 720p/60fps MP4](media/first-person-replay-nuke.mp4)

简单说：你给它一个 `.dem`，它会分析每个回合，导出压缩 `.dtr` 回放文件。进 CS2 本地服务器后，插件可以按回合让 bot 复刻 demo 里的走位、视角、跳跃、下蹲、开火和基础武器切换。

这个项目还在 MVP 阶段，但已经可以做端到端测试。

如果你只想查看 `.dtr` 字段布局，可以看 [`FORMAT.md`](FORMAT.md)。

## 适合谁

- 想把职业比赛 demo 里的 10 人轨迹搬进本地 CS2 服务器。
- 想用简单向导选择 demo、分析回合、导出回放文件。
- 想做 CS2 路线回放、bot playback 或 demo 分析工具。

## 你需要准备什么

- Windows 版 CS2。
- Rust，用来运行转换器。
- 本地 CS2 服务器环境。
- Metamod + CounterStrikeSharp，用来加载播放插件。

后面会尽量提供打包好的 exe 和插件包；当前开发版需要本地构建。

## 第一步：用向导转换 demo

打开 PowerShell：

```powershell
cd cs2-demotracer\converter
cargo run --release -- wizard
```

打包好的 Windows 版本也是同一个入口：

```powershell
cs2-demotracer.exe wizard
```

向导里按这个流程：

1. 粘贴或输入 CS2 `.dem` 路径。
2. 选择输出目录，默认是 `output`。
3. 查看回合分析摘要。
4. 直接回车导出推荐回合，或者输入 `0,1,5-8` 这样的回合列表。
5. 选择是否导出整回合、是否允许可疑回合、是否只导出单边、subtick 是否保持 auto。
6. 开始转换并自动校验生成的 `.dtr` 文件。

默认导出的 replay 会在 C4 开始安放前截断，先专注“开局路线”。如果要整回合导出，向导里可以选择，CLI 也可以加 `--full-round`。

导出后会生成类似这样的目录：

```text
output/<demo-id>/manifest.json
output/<demo-id>/round00/t/<玩家>.dtr
output/<demo-id>/round00/ct/<玩家>.dtr
output/<demo-id>/round01/...
```

`<demo-id>` 是 `<demo-stem>-<hash12>`，其中 `hash12` 来自 demo 文件内容。这样即使不同赛事的文件名很像，也不会互相覆盖。

`manifest.json` 是播放时最方便使用的入口文件。

## 批量生成 Mirage 回合池

如果你有很多 demo，可以先生成一个 Mirage 回合池，让插件按双方经济自动挑相似回合：

```powershell
cd cs2-demotracer\converter
cargo run --release -- convert-pool --demo-dir "<demo根目录>" --output "..\output\mirage_pool" --map de_mirage --recursive
```

输出目录里会有 `pool_manifest.json`，以及每个 demo 自己的 manifest 和压缩 `.dtr` 文件。

## 第二步：进游戏播放

先确保 CS2 本地服务器已经加载：

- Metamod runtime：`BotController`
- CounterStrikeSharp 插件：`DemoTracer`

进入服务器后，在控制台输入：

```text
css_plugins reload DemoTracer
dtr_weapon_align 1
dtr_run_manifest "<输出目录>\<demo-id>\manifest.json" 0
```

含义：

- `dtr_run_manifest` 会按回合顺序播放。
- 最后的 `0` 表示从 round 0 开始。
- 插件会在 `round_start` 准备 bot，在 `round_freeze_end` 开始播放。

如果只想测试某一回合，可以把最后的数字改成对应 round：

```text
dtr_run_manifest "<输出目录>\<demo-id>\manifest.json" 12
```

如果使用 Mirage 回合池：

```text
dtr_run_pool "<输出目录>\mirage_pool\pool_manifest.json" 0
```

round 0 和 round 12 只会匹配 demo 的 round 0/12 手枪局；其他回合会按双方当前装备价值粗略匹配 eco / force / full。

可选：一条命令换职业队 bot：

```text
dtr_team vitality spirit
dtr_team vitality ct
dtr_replay_identity 1
dtr_teams
dtr_team_reload
```

`dtr_team <T队> <CT队>` 会一次性添加两边 bot、设置队名和队标；`dtr_team <队伍> <t|ct>` 只换一边。想自定义队伍时，把 `css/teams.example.json` 复制到 CSS 插件 DLL 同目录并改名为 `teams.json`。
`dtr_replay_identity 1` 会在加载 replay 时请求 BotHider 把已分配 bot 改名，并使用 manifest 里的真实 SteamID64，默认关闭。

查看状态：

```text
bc_status
dtr_status 0
dtr_bots
```

停止：

```text
dtr_stop_all
```

## 回合表怎么看

转换器会把每个 round 标成“推荐”或“可疑”。

常见可疑原因：

- 人数不足 10 个。
- T 或 CT 人数不正常。
- 回合太短。
- demo 尾部有比赛结束后的垃圾回合。
- 断线重连导致轨迹缺失。

普通使用建议只导出推荐回合。可疑回合一般不适合作为训练或复刻数据。

## 当前限制

- 目前主要面向 Windows x64 本地 CS2 环境。
- 需要同一张地图，并且服务器里要有足够的 bot。
- `.dtr` 是无损压缩的 BotController 兼容 replay 格式；离线 subtick 和完整 usercmd 还会继续补。
- 某些武器和皮肤/默认手枪配置在 CS2 里比较麻烦，目前优先保证不崩服和基本行为正确。
- 这个工具不是作弊工具，也不会接入匹配服务器；它面向本地服务器、研究和内容制作。

## 开发者入口

常用命令：

```powershell
cd cs2-demotracer\converter
cargo test
cargo run --release -- wizard
cargo run --release -- inspect --demo <demo.dem>
cargo run --release -- convert --demo <demo.dem> --output <输出目录>
cargo run --release -- validate --input <输出目录>
```

目录：

- `converter/`：Rust CLI 和 prompt-style 向导转换器。
- `runtime/BotController/`：CS2 Metamod runtime。
- `css/`：CounterStrikeSharp 控制插件。
- `docs/`：格式和使用补充说明。
- `third_party/`：保留的第三方源码和许可说明。

## Credits

感谢这些项目和作者：

- [XBribo/CS2-Bot-Controller](https://github.com/XBribo/CS2-Bot-Controller)：CS2 bot hook、录制/回放、输入注入和武器锁定思路，本项目使用 BotController runtime 架构。
- [LaihoE/demoparser](https://github.com/LaihoE/demoparser)：Rust CS2 demo parser，本项目 converter 使用它解析 demo。
- [csgowiki/minidemo-encoder](https://github.com/csgowiki/minidemo-encoder)：历史 CS:GO demo-to-replay 工具链思路参考。
- Metamod:Source 和 CounterStrikeSharp 社区：CS2 本地插件生态。

本项目使用 GPL-3.0 license。第三方项目的原始许可见 `NOTICE.md` 和对应源码目录。
