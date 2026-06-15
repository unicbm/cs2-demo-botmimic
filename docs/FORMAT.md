# `.cs2rec` v2 Format

All values are little-endian. v2 is the only supported format after the BotController migration.

## Header

| Field | Type | Notes |
| --- | --- | --- |
| magic | 8 bytes | `CS2BMREC` |
| version | `u32` | `2` |
| tick_rate | `f32` | Demo tickrate estimate |
| round | `u32` | `total_rounds_played` window |
| side | `u8` | `2=T`, `3=CT`, `0=unknown` |
| flags | `u32` | Reserved |
| steam_id | `u64` | Player SteamID64 |
| tick_count | `u32` | Number of replay ticks |
| subtick_count | `u32` | Number of subtick moves |
| map | `u16 len + utf8` | Map name |
| player_name | `u16 len + utf8` | Demo player name |

## ReplayTickV2

Each tick stores:

- `pre: MovementSnapshotV2`
- `post: MovementSnapshotV2`
- `weapon_def_index: i32`
- `num_subtick: u32`

The sum of all `num_subtick` values must equal header `subtick_count`.

## MovementSnapshotV2

This layout matches BotController ABI 10 (`92` bytes with `Pack=4`).

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

## SubtickMoveV2

| Field | Type |
| --- | --- |
| when | `f32` |
| button | `u32` |
| pressed | `f32` |
| analog_forward | `f32` |
| analog_left | `f32` |
| pitch_delta | `f32` |
| yaw_delta | `f32` |

The current offline converter may emit zero subticks. BotController accepts empty subtick arrays and replays tick snapshots plus reconstructed button edge states.
