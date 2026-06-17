# `.dtr` v3 Format

All values are little-endian. v3 is the only supported replay file format.

The format is lossless: movement snapshots and subtick records are written with their original `f32` and integer bit patterns. The body removes duplicated adjacent tick snapshots and is then compressed with Brotli.

## Header

| Field | Type | Notes |
| --- | --- | --- |
| magic | 8 bytes | `CSDTRREC` |
| version | `u32` | `3` |
| tick_rate | `f32` | Demo tickrate estimate |
| round | `u32` | `total_rounds_played` window |
| side | `u8` | `2=T`, `3=CT`, `0=unknown` |
| flags | `u32` | Reserved |
| steam_id | `u64` | Player SteamID64 |
| tick_count | `u32` | Number of replay ticks |
| subtick_count | `u32` | Number of subtick moves |
| map | `u16 len + utf8` | Map name |
| player_name | `u16 len + utf8` | Demo player name |
| codec | `u8` | `1 = Brotli` |
| body_uncompressed_len | `u64` | Expected decoded body byte length |
| body_compressed_len | `u64` | Compressed body byte length |

The next `body_compressed_len` bytes are a Brotli stream.

## Decoded Body

After decompression, the body layout is:

| Part | Count | Bytes Each |
| --- | ---: | ---: |
| `MovementSnapshotV3` | `0 if tick_count == 0, else tick_count + 1` | 92 |
| tick metadata | `tick_count` | 8 |
| `SubtickMoveV3` | `subtick_count` | 28 |

Tick metadata is:

| Field | Type |
| --- | --- |
| weapon_def_index | `i32` |
| num_subtick | `u32` |

Reconstruct replay ticks as:

- `tick[i].pre = snapshots[i]`
- `tick[i].post = snapshots[i + 1]`
- `tick[i].weapon_def_index = metadata[i].weapon_def_index`
- `tick[i].num_subtick = metadata[i].num_subtick`

The sum of all `num_subtick` values must equal header `subtick_count`.

## MovementSnapshotV3

This layout matches BotController ABI 11 (`92` bytes with `Pack=4`).

| Field | Type |
| --- | --- |
| origin | `f32[3]` |
| velocity | `f32[3]` |
| angles | `f32[3]` pitch/yaw/roll |
| entity_flags | `u32` |
| move_type | `u8` |
| pad | 3 bytes |
| buttons | `u64` |
| buttons1 | `u64` |
| buttons2 | `u64` |
| duck_amount | `f32` |
| duck_speed | `f32` |
| ladder_normal | `f32[3]` |
| ducked | `u8` |
| ducking | `u8` |
| desires_duck | `u8` |
| actual_move_type | `u8` |

## SubtickMoveV3

| Field | Type |
| --- | --- |
| when | `f32` |
| button | `u32` |
| pressed | `f32` |
| analog_forward | `f32` |
| analog_left | `f32` |
| pitch_delta | `f32` |
| yaw_delta | `f32` |

## Parser Checklist

1. Read and validate magic `CSDTRREC`.
2. Require `version == 3`.
3. Read `tick_count`, `subtick_count`, `map`, and `player_name`.
4. Require `codec == 1`.
5. Check `body_uncompressed_len == snapshot_count * 92 + tick_count * 8 + subtick_count * 28`, where `snapshot_count` is `0` for empty replays and `tick_count + 1` otherwise.
6. Read and Brotli-decompress exactly `body_compressed_len` bytes.
7. Rebuild ticks from the snapshot chain and metadata.
8. Sum all tick `num_subtick` values and verify it equals `subtick_count`.
