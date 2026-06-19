# Examples

These examples are for users who want to automate DemoTracer without writing
Rust. They call the `cs2-demotracer` CLI and read the generated `manifest.json`.
They are integration examples, not stable Python or Node SDK bindings.

No raw `.dem` files or generated `.dtr` output are committed here. Use your own
demo file and output directory.

## Python

```powershell
python examples\python\convert_round.py --demo "<demo.dem>" --output "<output-dir>" --rounds 0
```

The script runs `cs2-demotracer.exe convert`, locates the newest generated
`manifest.json`, prints a short summary, and prints a CS2 console command for
loading the selected round.

## Node.js

```powershell
node examples\node\convert-round.mjs --demo "<demo.dem>" --output "<output-dir>" --rounds 0
```

This does the same thing as the Python example using only Node's built-in
modules.

## Rust API

Use the Rust API only when you are writing a Rust tool and want to avoid
spawning the CLI. See [`docs/USAGE.md`](../docs/USAGE.md#5-rust-api).
