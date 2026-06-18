use cs2_demotracer::prelude::*;
use std::env;
use std::error::Error;
use std::path::PathBuf;

fn main() -> Result<(), Box<dyn Error>> {
    let args = env::args().collect::<Vec<_>>();
    if args.len() < 3 || args.len() > 4 {
        eprintln!(
            "usage: cargo run --example build_nade_library -- <demo-dir> <output-dir> [jobs]"
        );
        std::process::exit(2);
    }

    let demo_dir = PathBuf::from(&args[1]);
    let output_dir = PathBuf::from(&args[2]);
    let jobs = args
        .get(3)
        .map(|value| value.parse::<usize>())
        .transpose()?
        .unwrap_or(1)
        .max(1);

    let mut request = NadeLibraryExportRequest::new(demo_dir, output_dir);
    request.recursive = true;
    request.jobs = jobs;
    request.context = NadeContextOptions::default();
    request.dedupe = NadeDedupeOptions::default();

    let report = build_nade_library_with_progress(&request, |event| match event {
        NadeLibraryProgress::Started { demos, jobs, .. } => {
            println!("queued {demos} demo(s), jobs={jobs}");
        }
        NadeLibraryProgress::Demo {
            total,
            done,
            status,
            ..
        } => {
            println!("[{done}/{total}] {status:?}");
        }
        NadeLibraryProgress::Aggregated {
            maps_written,
            source_clips,
            clips,
            ..
        } => {
            println!("aggregated maps={maps_written} source_clips={source_clips} clips={clips}");
        }
        NadeLibraryProgress::AggregateOnly { .. } => {}
    })?;

    println!(
        "done demos={} failures={} maps={} clips={} root={}",
        report.demos_done,
        report.failures,
        report.maps_written,
        report.clips,
        report.root.display()
    );
    Ok(())
}
