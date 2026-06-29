# CS2 DemoTracer 0.3.3-beta.1

## Highlights

- 新增三层 runtime 命令体系：`dtr_align`、`dtr_match`、`dtr_cosmetics`
- GUI 保持完整工作台模式，删除额外模式切换
- GUI 不再生成 `dtr_set align scoreboard off`
- `kickid` 或单个 replay slot 断开时，不再清空全部 DTR replay 状态
- Runtime 配置新增 `fidelity`、`match`、`cosmetics` 三段；旧 `align` 继续兼容
- 旧命令继续兼容：`dtr_set align ...` 和 `dtr_*_align` 仍可用，并提示迁移

## 新命令体系

### Replay 保真

```text
dtr_align default
dtr_align handoff_safe
dtr_align off
dtr_align weapons on
dtr_align projectiles on
dtr_align crosshair on
dtr_align left_hand off
```

### 赛事展示

```text
dtr_match scoreboard
dtr_match off
```

### 饰品风险

```text
dtr_cosmetics off
dtr_cosmetics weapons
dtr_cosmetics basic
dtr_cosmetics full
```

`dtr_cosmetics` 默认关闭。它只消费显式导出的 demo 饰品证据，并可能带来 GSLT/server guideline 风险。

## GUI 更新

GUI 仍使用完整工作台：选择 `.dem`、解析回合、按需调整回合和导出选项，再转换并复制生成的 `dtr_go ...` 命令。

饰品导出仍然默认关闭。开启时需要确认风险。GUI 会把普通 replay 命令和带饰品 replay 命令分开显示。

## `kickid` 单 slot 断开修复

以前某个 bot slot 被 `kickid` 或断开时，插件可能清空全部 replay lifecycle state。现在只释放断开的 slot，其他 slot 的 replay 不再一损俱损。

## Legacy compatibility

以下旧命令仍可用：

```text
dtr_set align weapons on
dtr_set align projectiles on
dtr_set align cosmetics on
dtr_set align stickers on
dtr_set align charms on
dtr_set align crosshair on
dtr_set align left_hand on
dtr_set align scoreboard on

dtr_weapon_align
dtr_projectile_align
dtr_cosmetic_align
dtr_sticker_align
dtr_charm_align
dtr_crosshair_align
dtr_left_hand_desired
```

新脚本建议迁移到：

```text
dtr_align ...
dtr_match ...
dtr_cosmetics ...
```

<details>
<summary>English details</summary>

## Highlights

- Added a clearer runtime command layout: `dtr_align`, `dtr_match`, and `dtr_cosmetics`.
- The GUI keeps the full workbench flow and removes the extra mode toggle.
- The GUI no longer emits `dtr_set align scoreboard off`.
- Disconnecting or `kickid`-ing one replay slot no longer clears all DTR replay state.
- Runtime config now supports `fidelity`, `match`, and `cosmetics` sections while keeping legacy `align` compatibility.
- Legacy commands remain compatible during the beta migration window.

## Command migration

Replay-fidelity controls now live under `dtr_align`.

```text
dtr_align default
dtr_align handoff_safe
dtr_align off
```

Match-presentation controls now live under `dtr_match`.

```text
dtr_match scoreboard
dtr_match off
```

High-risk cosmetic replay controls now live under `dtr_cosmetics`.

```text
dtr_cosmetics off
dtr_cosmetics basic
dtr_cosmetics full
```

Legacy commands such as `dtr_set align ...` and `dtr_*_align` still work, but new docs and GUI output use the new command layout.

</details>
