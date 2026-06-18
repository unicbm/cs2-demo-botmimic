use cs2_demotracer::model::ProjectileKind;
use cs2_demotracer::prelude::*;
use std::collections::BTreeMap;
use std::env;
use std::error::Error;
use std::path::PathBuf;

fn main() -> Result<(), Box<dyn Error>> {
    let args = env::args().collect::<Vec<_>>();
    if args.len() != 2 {
        eprintln!(
            "usage: cargo run --example read_nade_manifest -- <nade_manifest.json|nade_manifest.json.br>"
        );
        std::process::exit(2);
    }

    let path = PathBuf::from(&args[1]);
    let manifest = read_nade_manifest(&path)?;
    let mut by_bucket = BTreeMap::new();
    for clip in &manifest.clips {
        let key = format!("{}/{}/{}", clip.side, clip.phase_name(), clip.kind_name());
        *by_bucket.entry(key).or_insert(0_usize) += 1;
    }

    println!(
        "demo={} map={} clips={} skipped={} tick_rate={:.1}",
        manifest.demo_id,
        manifest.map,
        manifest.clips.len(),
        manifest.skipped.len(),
        manifest.tick_rate
    );
    for (bucket, count) in by_bucket {
        println!("{bucket}: {count}");
    }
    Ok(())
}

trait NadeClipSummary {
    fn phase_name(&self) -> &'static str;
    fn kind_name(&self) -> &'static str;
}

impl NadeClipSummary for cs2_demotracer::nade_export::NadeClip {
    fn phase_name(&self) -> &'static str {
        match self.phase {
            cs2_demotracer::nade_export::NadePhase::Opening => "opening",
            cs2_demotracer::nade_export::NadePhase::Combat => "combat",
            cs2_demotracer::nade_export::NadePhase::Retake => "retake",
        }
    }

    fn kind_name(&self) -> &'static str {
        match self.kind {
            ProjectileKind::Smoke => "smoke",
            ProjectileKind::Flash => "flash",
            ProjectileKind::He => "he",
            ProjectileKind::Molotov => "molotov",
            ProjectileKind::Decoy => "decoy",
            ProjectileKind::Unknown => "unknown",
        }
    }
}
