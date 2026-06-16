# CS2 DemoTracer

Trace CS2 demos into bot-executable route replays.

**Language:** English | [简体中文](docs/README.zh-Hans.md)

Convert CS2 match demos into route replay files, then play those rounds back through bots on a local CS2 server.

If this project helps you, please consider giving it a star. It makes the project easier for other CS2 tool/plugin developers to find.

## Demo

First-person spectator view stays synchronized while bots replay converted CS2 demo movement, view angles, firing, and weapon state.

![First-person CS2 bot replay on Nuke](docs/media/first-person-replay-nuke.gif)

[Watch the 720p/60fps MP4](docs/media/first-person-replay-nuke.mp4)

## What It Does

CS2 DemoTracer takes a `.dem` file, analyzes its rounds, and exports compressed `.dtr` route replay files for each player.

In a local CS2 server, the runtime and CounterStrikeSharp plugin can then make bots replay the demo player's movement, view angles, jumping, crouching, firing, and basic weapon switching.

This is still an MVP, but the full demo -> replay -> in-game bot playback loop is already working.

Plugin/runtime authors who only need to inspect replay fields can read the binary layout in [`docs/FORMAT.md`](docs/FORMAT.md).

## Who This Is For

- People who want to replay pro match movement inside a local CS2 server.
- People who want a simple PowerShell-friendly wizard.
- Developers building CS2 route replay, bot playback, or demo analysis tooling.

## Requirements

- Windows CS2.
- Rust, for running the converter.
- A local CS2 server environment.
- Metamod and CounterStrikeSharp, for loading the playback plugins.

Prebuilt packages are planned. For now, this development version is built locally.

## Quick Start With The Wizard

Open PowerShell:

```powershell
cd cs2-demotracer\converter
cargo run --release -- wizard
```

Packaged Windows releases use the same flow:

```powershell
cs2-demotracer.exe wizard
```

Wizard flow:

1. Paste or type a CS2 `.dem` path.
2. Choose an output folder. The default is `output`.
3. Review the recommended and suspicious round summary.
4. Press Enter to export recommended rounds, or type a comma/range list such as `0,1,5-8`.
5. Choose whether to export full rounds, include suspicious rounds, limit by side, and keep subtick input on auto.
6. Convert and validate the generated `.dtr` files.

By default, exported replays stop before the C4 plant begins. This keeps the first version focused on opening routes; full-round export is available in the wizard and from the CLI with `--full-round`.

The output looks like this:

```text
output/<demo-id>/manifest.json
output/<demo-id>/round00/t/<player>.dtr
output/<demo-id>/round00/ct/<player>.dtr
output/<demo-id>/round01/...
```

`<demo-id>` is `<demo-stem>-<hash12>`, where `hash12` is derived from the demo file contents. This prevents repeated event/map names from overwriting each other.

`manifest.json` is the easiest file to use for playback.

## Build A Mirage Round Pool

If you have many demos, you can build a replay pool and let the plugin choose a similar round by economy:

```powershell
cd cs2-demotracer\converter
cargo run --release -- convert-pool --demo-dir "<demo-root>" --output "..\output\mirage_pool" --map de_mirage --recursive
```

This writes `pool_manifest.json` plus normal per-demo manifests and compressed `.dtr` files under the output folder.

## Play In CS2

Make sure your local CS2 server has loaded:

- the Metamod runtime plugin: `BotController`
- the CounterStrikeSharp plugin: `DemoTracer`

In the server console:

```text
css_plugins reload DemoTracer
dtr_weapon_align 1
dtr_run_manifest "<output-dir>\<demo-id>\manifest.json" 0
```

The last number is the starting round. Use `0` to start from round 0.

To start from a specific round:

```text
dtr_run_manifest "<output-dir>\<demo-id>\manifest.json" 12
```

For a Mirage pool:

```text
dtr_run_pool "<output-dir>\mirage_pool\pool_manifest.json" 0
```

Round 0 and round 12 only match pistol-round candidates from demo round 0 or 12. Other rounds are matched by each side's current equipment value.

Optional team setup:

```text
dtr_team vitality spirit
dtr_team vitality ct
dtr_replay_identity 1
dtr_teams
dtr_team_reload
```

`dtr_team <t-team> <ct-team>` adds named bots, team names, and team logos in one command. Put a custom `teams.json` next to the CSS plugin DLL to override the built-in examples; `css/teams.example.json` shows the format.
`dtr_replay_identity 1` optionally asks BotHider to rename each loaded bot and use the replay manifest's real SteamID64.

Useful checks:

```text
bc_status
dtr_status 0
dtr_bots
```

Stop playback:

```text
dtr_stop_all
```

## Round Quality

The converter marks rounds as recommended or suspicious.

Suspicious rounds usually mean:

- fewer than 10 available players
- wrong T/CT player counts
- abnormally short round window
- broken reconnect data
- post-match garbage rounds at the end of the demo

For normal use, export the recommended rounds only.

## Current Limitations

- Windows x64 local CS2 is the primary target.
- The server should run the same map and have enough bots.
- `.dtr` uses a lossless compressed BotController-compatible replay format. Full offline subtick/usercmd reconstruction is future work.
- Some weapon/loadout details are still limited by CS2 slot behavior, especially default pistols.
- This is for local servers, research, content creation, and plugin development. It is not intended for matchmaking or cheating.

## Advanced CLI

```powershell
cd cs2-demotracer\converter
cargo test
cargo run --release -- wizard
cargo run --release -- inspect --demo <demo.dem>
cargo run --release -- convert --demo <demo.dem> --output <output-dir>
cargo run --release -- validate --input <output-dir>
```

Repository layout:

- `converter/`: Rust CLI and prompt-style wizard converter.
- `runtime/BotController/`: CS2 Metamod runtime.
- `css/`: CounterStrikeSharp control plugin.
- `docs/`: extra docs.
- `third_party/`: vendored third-party source and license notes.

## Credits

Thanks to:

- [XBribo/CS2-Bot-Controller](https://github.com/XBribo/CS2-Bot-Controller): CS2 bot hooks, replay, recording, input injection, and weapon-locking ideas. This project uses the BotController runtime architecture.
- [LaihoE/demoparser](https://github.com/LaihoE/demoparser): Rust CS2 demo parser used by the converter.
- [csgowiki/minidemo-encoder](https://github.com/csgowiki/minidemo-encoder): inspiration for the historical CS:GO demo-to-replay tooling workflow.
- The Metamod:Source and CounterStrikeSharp communities.

This project is licensed under GPL-3.0. See `NOTICE.md` and the vendored source folders for third-party license details.
