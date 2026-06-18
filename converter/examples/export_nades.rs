use cs2_demotracer::prelude::*;
use std::env;
use std::error::Error;
use std::path::PathBuf;

fn main() -> Result<(), Box<dyn Error>> {
    let args = env::args().collect::<Vec<_>>();
    if args.len() != 3 {
        eprintln!("usage: cargo run --example export_nades -- <demo.dem> <output-dir>");
        std::process::exit(2);
    }

    let demo_path = PathBuf::from(&args[1]);
    let output_dir = PathBuf::from(&args[2]);
    let mut request = NadeClipExportRequest::new(demo_path, output_dir);
    request.side = Side::Both;
    request.context = NadeContextOptions {
        pre_roll_seconds: 1.0,
        post_roll_seconds: 0.5,
        opening_seconds: 20.0,
    };

    let report = export_nade_clips_from_demo_path(&request)?;
    println!(
        "wrote {} nade clips under {} (skipped {})",
        report.clips_written,
        report.root.display(),
        report.skipped
    );
    println!("manifest {}", report.manifest_path.display());
    Ok(())
}
