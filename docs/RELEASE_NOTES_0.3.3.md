# CS2 DemoTracer 0.3.3

## Highlights

- Exports demo-observed music kit IDs into replay manifests.
- Applies replay bot music kits at load time and rebroadcasts MVP events with the replay kit ID.
- Exports weapon econ identity fields for demo-backed cosmetics: original owner SteamID, item account ID, and item ID.
- Applies those econ identity fields at runtime so transferred or dropped demo-backed weapons can retain native CS2 owner naming.
- Keeps item drop and pickup high-fidelity events record-only during live replay; ordinary world-entity drop chains are still not scripted.

## Notes

Cosmetic, sticker, charm, and econ identity export remain explicit opt-in converter features and keep the same GSLT/server guideline warning model. DemoTracer does not generate fallback cosmetics or random inventory state.

Music kit export is manifest metadata and does not require cosmetic export. Runtime music kit application is scoped to loaded replay bots.

## Validation

- Full normal conversion of the Falcons vs G2 Inferno reference demo with cosmetics, stickers, and charms.
- Local CS2 plugin install smoke test for weapon owner naming on picked-up/transferred weapons.
- Local CS2 plugin install smoke test for music kit runtime path.
