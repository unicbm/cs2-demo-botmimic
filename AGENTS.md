# Agent Guidance

This repository is the standalone public project for CS2 demo-to-bot-replay work. Keep it independent from any CS:GO SourceMod/BotMimic codebase, private bot code, or local server setup.

## Project Scope

- The core product is CS2 `.dem` -> `.cs2rec` conversion plus local CS2 bot replay.
- The repository should stay focused on this project itself. Do not turn README or user docs into troubleshooting notes for unrelated bot AI plugins.
- Do not add CSV, Parquet, raw-position dumps, or other intermediate export formats unless explicitly requested. The converter should write native `.cs2rec`, `manifest.json`, and logs.
- Current converter replay data is tick-level. The `.cs2rec` format can store subtick moves, and the runtime can read/replay them, but the offline converter currently writes empty subtick arrays. Do not document converter-generated subtick replay as complete until it is implemented and validated.

## Repository Layout

- `converter/`: Rust GUI/CLI converter.
- `css/`: CounterStrikeSharp control plugin and C ABI wrapper.
- `runtime/BotController/`: CS2 Metamod runtime based on XBribo/CS2-Bot-Controller.
- `docs/`: user-facing supplemental docs.
- `third_party/`: vendored third-party source and attribution.

Keep module boundaries clear:

- Rust converter owns demo parsing, round quality analysis, `.cs2rec` writing, GUI, and manifest generation.
- Metamod runtime owns CS2 hooks, replay buffers, movement injection, weapon locking, and C ABI exports.
- CounterStrikeSharp plugin owns commands, manifest loading, bot-slot assignment, replay sequencing, handoff policy, and user-facing server messages.

## Public Hygiene

- Never commit local machine paths, Steam install paths, demo dataset paths, usernames, private repo names, or server-specific deployment paths.
- Use placeholders such as `<demo.dem>`, `<output-dir>`, and `<manifest.json>` in docs and examples.
- Do not commit `.dem`, `.cs2rec`, `output/`, `tmp/`, build outputs, `target/`, `bin/`, `obj/`, generated logs, or local deployment packages unless the user explicitly requests release packaging.
- This repo is public GPL-3.0. Preserve third-party license notices and attribution in `NOTICE.md` and vendored folders.
- Avoid rewriting public Git history unless the user explicitly asks for history cleanup.

## Converter Rules

- Support CS2 demos only. Do not mix in Source1/CS:GO parser paths.
- Keep round analysis visible and configurable: recommended/suspicious rounds, player counts, duration, and problem text matter for real HLTV demos.
- Default conversion should prefer recommended rounds and avoid suspicious tail/garbage rounds.
- Per-player export is one `.cs2rec` per player per round under `output/<demo>/roundNN/t|ct/`.
- Do not silently include dead-player tail data. The current model exports alive rows inside the selected round window.
- If reducing file size later, prefer explicit format-versioned changes such as delta encoding, keyframes, quantization, or compression. Do not break existing reader/runtime compatibility without a version bump.

## Runtime And CSS Rules

- `CS2BM_ABI` / expected ABI values must stay synchronized across Rust manifest generation, C# BotController wrapper, and native runtime.
- Never assign replay control to real human players. Safe candidates are strict CS2 bots or slots known to be bot-managed by the local bot-hider/shared-state path.
- `cs2bm_handoff death_or_contact slot` is the intended safe default: replay controls opening movement, then releases only the contacted/dead replay slot after contact/death. Use `all` only for explicit experiments where one trigger should release every replaying bot.
- On stop, unload, finish, or handoff, release replay state: stop replay, clear input injection, unlock weapon locks, clear pending weapon alignment, and reset bot brain state that would bias native AI.
- Weapon alignment is intentionally soft. Do not delete/replace conflicting primary or secondary slot weapons during live replay; that has caused unstable entities and crashes. Prefer round-start inventory preset work for future stronger alignment.
- Avoid teleport-as-primary-playback. Movement replay should flow through the runtime movement hooks with snapshots used for state seeding/correction.
- Keep server commands concise and stable. If adding commands, make them useful for local testing and status diagnosis.

## Documentation

- `README.md` should be simple and focused on how to convert demos and play `.cs2rec` locally.
- Keep English README and `docs/README.zh-Hans.md` aligned at a high level.
- Do not mention private local paths or private repositories in docs.
- Do not add detailed discussion of external aim plugins, headshot behavior, or unrelated bot AI modules to the main README.
- Credits should remain factual and concise: CS2-Bot-Locker, LaihoE/demoparser, minidemo/BotMimic inspiration, Metamod:Source, and CounterStrikeSharp.

## Validation

Run the narrowest relevant checks after changes:

```powershell
cd converter
cargo test
```

For CSS plugin changes, build the CounterStrikeSharp project with an available .NET SDK:

```powershell
dotnet build css\Cs2DemoBotMimic.csproj -c Release
```

For runtime C++ changes, build with the local CS2 Metamod/SDK toolchain if configured. If the native toolchain is unavailable, state that explicitly in the final response.

Before publishing, also check:

```powershell
git status -sb
git diff --check
```


Also scan README/docs/source changes for accidental local absolute paths before publishing.

`git diff --check` may report trailing whitespace inside vendored third-party source. Do not reformat vendored source solely to satisfy whitespace checks.

## Third-Party Source

- Treat `third_party/demoparser` as vendored source. Keep local changes minimal and document why they were necessary.
- Do not reformat or mechanically rewrite vendored source unless the user explicitly asks for a vendor refresh or patch.
- If updating vendored projects, preserve upstream license files and update attribution notes.
