# CS2 DemoTracer v0.3.5

v0.3.5 是针对 v0.3.4 中 P0 崩溃问题的热修复版本。

v0.3.4 用户应立即升级。

## 热修复摘要

- 修复一个 CounterStrikeSharp runtime 崩溃路径：在 replay 启动阶段，CS2 客户端可能因为实体/PVS 状态异常而崩溃，典型报错类似：
  `FATAL Error: WriteEnterPVS: GetEntServerClass failed for ent...`。
- 该问题来自 v0.3.4 的 replay loadout 清理路径：它会直接从 pawn inventory 移除 replay weapon，然后 kill 这个 weapon entity。这个 `RemovePlayerItem` + KillEntity 组合在 CS2 中是不安全的，可能让网络快照写入阶段看到不一致的实体状态。
- v0.3.5 改回更安全的 engine drop 路径，并延迟清理掉落后的 weapon entity，避免直接清 slot 内实体。

## 修复内容

- 避免在 replay weapon replacement 中直接执行 `RemovePlayerItem` + entity kill。
- 分配 DTR replay 槽位时优先选择严格 CS2 bot，再考虑 BotHider-managed fallback candidate，降低队内第六人抢占 DTR 槽位导致真实 bot 没有执行 replay 的风险。

## 兼容性

- 不改变 `.dtr` 格式。
- 不改变 manifest ABI。
- 不改变 BotController native ABI。
- 不改变 DemoTracer companion API。
- 不包含 converter 或 runtime command 的破坏性行为变更。

## 升级建议

正在使用 v0.3.4 server bundle 的用户应立即升级到 v0.3.5，尤其是在使用 weapon/loadout alignment 回放 demo round 时。

升级方式：用 v0.3.5 server bundle 替换服务器侧 DemoTracer 包，然后重启服务器。

升级后建议检查：

```text
dtr_runtime
bc_status
```

预期 ABI 仍然是：

```text
expected_abi=16 runtime_abi=16
```

## 发布资产

- `cs2-demotracer-v0.3.5-windows-x64.zip`：converter CLI 和 Rust GUI。
- `cs2-demotracer-server-v0.3.5-windows-x64.zip`：server playback bundle，包含 BotController runtime 和 DemoTracer CounterStrikeSharp plugin。
- `SHA256SUMS.txt`：release assets 的 SHA-256 校验值。

<details>
<summary>English details</summary>

## Hotfix Summary

v0.3.5 is a hotfix release for a P0 bug in v0.3.4.

Users on v0.3.4 should upgrade immediately.

- Fixes a CounterStrikeSharp runtime crash path that could make CS2 clients fail during replay startup with an error like:
  `FATAL Error: WriteEnterPVS: GetEntServerClass failed for ent...`.
- The crash was caused by the v0.3.4 replay loadout cleanup path directly removing a replay weapon from the pawn inventory and then killing that entity. In CS2 this can leave an unsafe entity/PVS state during network snapshot writing.
- DemoTracer now returns to the safer engine drop path and delays cleanup of the dropped weapon entity instead of directly removing the weapon from the slot.

## Fixed

- Avoid direct `RemovePlayerItem` + entity kill during replay weapon replacement.
- Prefer strict CS2 bots before BotHider-managed fallback candidates when assigning replay slots, reducing the chance that a sixth team user occupies a DTR slot before a real bot.

## Compatibility

- No `.dtr` format changes.
- No manifest ABI changes.
- No BotController native ABI changes.
- No DemoTracer companion API changes.
- No behavior-breaking converter or runtime command changes.

## Upgrade Guidance

Upgrade any v0.3.4 server bundle immediately before replaying rounds that need weapon/loadout alignment. Replace the server-side DemoTracer package with the v0.3.5 server bundle, then restart the server.

Recommended post-upgrade checks:

```text
dtr_runtime
bc_status
```

Expected ABI remains:

```text
expected_abi=16 runtime_abi=16
```

## Assets

- `cs2-demotracer-v0.3.5-windows-x64.zip`: converter CLI and Rust GUI.
- `cs2-demotracer-server-v0.3.5-windows-x64.zip`: server playback bundle with BotController runtime and DemoTracer CounterStrikeSharp plugin.
- `SHA256SUMS.txt`: SHA-256 checksums for release assets.

</details>
