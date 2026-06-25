use crate::demo_id::output_demo_id;
use crate::demo_reader::read_demo;
use crate::export::{
    export_demo_with_progress, ConversionProgress, ConversionReport, ConvertOptions,
    DEFAULT_FREEZE_PREROLL_SECONDS,
};
use crate::model::{DemoAnalysis, ParsedDemo, RoundStatus, Side, SubtickMode};
use crate::quality::{analyze_demo, AnalysisOptions};
use crate::validate::validate_dtr_path;
use eframe::egui::{self, Color32, FontId, RichText, ScrollArea, TextStyle};
use egui_extras::{Column, TableBuilder};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::mpsc::{self, Receiver, Sender};
use std::sync::Arc;
use std::thread;
use std::time::Duration;

const COSMETIC_CONFIRMATION_PHRASE: &str = "I ACCEPT COSMETIC EXPORT RISK";
const GOOD: Color32 = Color32::from_rgb(80, 210, 146);
const WARN: Color32 = Color32::from_rgb(242, 170, 76);
const DANGER: Color32 = Color32::from_rgb(255, 92, 92);
const INFO: Color32 = Color32::from_rgb(92, 178, 255);
const PANEL: Color32 = Color32::from_rgb(24, 30, 38);
const PANEL_DEEP: Color32 = Color32::from_rgb(13, 18, 24);
const MUTED: Color32 = Color32::from_rgb(158, 171, 188);

pub fn run_gui() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1180.0, 760.0])
            .with_min_inner_size([960.0, 620.0]),
        ..Default::default()
    };
    eframe::run_native(
        "CS2 DemoTracer",
        options,
        Box::new(|cc| Ok(Box::new(DemoTracerGui::new(cc)))),
    )
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(default)]
struct GuiSettings {
    demo_path: String,
    output_dir: String,
    side: Side,
    include_suspicious: bool,
    full_round: bool,
    freeze_preroll_seconds: f32,
    export_cosmetics: bool,
    export_stickers: bool,
}

impl Default for GuiSettings {
    fn default() -> Self {
        Self {
            demo_path: String::new(),
            output_dir: "output".to_string(),
            side: Side::Both,
            include_suspicious: false,
            full_round: false,
            freeze_preroll_seconds: DEFAULT_FREEZE_PREROLL_SECONDS,
            export_cosmetics: false,
            export_stickers: false,
        }
    }
}

struct DemoTracerGui {
    settings: GuiSettings,
    parsed: Option<Arc<ParsedDemo>>,
    analysis: Option<DemoAnalysis>,
    round_selection: BTreeMap<u32, bool>,
    result: Option<ConversionResultView>,
    pending_overwrite: Option<PendingConversion>,
    show_cosmetic_disclaimer: bool,
    cosmetic_confirmation: String,
    cosmetic_acknowledged: bool,
    receiver: Option<Receiver<WorkerMessage>>,
    running: Option<RunningTask>,
    progress: GuiProgress,
    logs: Vec<String>,
    error: Option<String>,
}

impl DemoTracerGui {
    fn new(cc: &eframe::CreationContext<'_>) -> Self {
        apply_visuals(&cc.egui_ctx);
        let settings = load_settings();
        let mut progress = GuiProgress::default();
        progress.begin("Choose a demo to begin", Some(0.0));
        Self {
            settings,
            parsed: None,
            analysis: None,
            round_selection: BTreeMap::new(),
            result: None,
            pending_overwrite: None,
            show_cosmetic_disclaimer: false,
            cosmetic_confirmation: String::new(),
            cosmetic_acknowledged: false,
            receiver: None,
            running: None,
            progress,
            logs: Vec::new(),
            error: None,
        }
    }

    fn is_running(&self) -> bool {
        self.running.is_some()
    }

    fn analyze(&mut self) {
        if self.is_running() {
            return;
        }
        let demo_path = PathBuf::from(self.settings.demo_path.trim());
        if !is_demo_file(&demo_path) {
            self.error = Some("Choose a .dem file before analyzing.".to_string());
            return;
        }
        self.save_settings();
        self.parsed = None;
        self.analysis = None;
        self.round_selection.clear();
        self.result = None;
        self.error = None;
        self.logs.clear();
        self.progress.begin("Parsing demo", None);

        let (tx, rx) = mpsc::channel();
        self.receiver = Some(rx);
        self.running = Some(RunningTask::Analyze);
        thread::spawn(move || analyze_worker(demo_path, tx));
    }

    fn request_convert(&mut self) {
        if self.is_running() {
            return;
        }
        let Some(parsed) = self.parsed.clone() else {
            self.error = Some("Analyze a demo before converting.".to_string());
            return;
        };
        let selected_rounds = self.selected_rounds();
        if selected_rounds.is_empty() {
            self.error = Some("Select at least one round to export.".to_string());
            return;
        }
        let output_dir = PathBuf::from(self.settings.output_dir.trim());
        if output_dir.as_os_str().is_empty() {
            self.error = Some("Choose an output directory.".to_string());
            return;
        }
        if self.settings.export_stickers && !self.settings.export_cosmetics {
            self.settings.export_stickers = false;
        }
        if !cosmetic_export_ready(&self.settings, self.cosmetic_acknowledged) {
            self.show_cosmetic_disclaimer = true;
            self.cosmetic_confirmation.clear();
            self.error = Some("Confirm cosmetic export risk before converting.".to_string());
            return;
        }
        self.save_settings();

        let demo_id = match output_demo_id(&parsed.stem, &parsed.demo_sha256, None) {
            Ok(value) => value,
            Err(err) => {
                self.error = Some(err.to_string());
                return;
            }
        };
        let root = output_dir.join(demo_id);
        let options = self.convert_options(output_dir, selected_rounds);
        let pending = PendingConversion {
            parsed,
            options,
            overwrite_root: root.clone(),
        };
        if root.exists() {
            self.pending_overwrite = Some(pending);
        } else {
            self.start_convert(pending, false);
        }
    }

    fn start_convert(&mut self, pending: PendingConversion, clear_existing: bool) {
        self.error = None;
        self.result = None;
        self.logs.clear();
        self.progress.begin("Preparing export", Some(0.0));

        let (tx, rx) = mpsc::channel();
        self.receiver = Some(rx);
        self.running = Some(RunningTask::Convert);
        thread::spawn(move || convert_worker(pending, clear_existing, tx));
    }

    fn convert_options(
        &self,
        output_dir: PathBuf,
        selected_rounds: BTreeSet<u32>,
    ) -> ConvertOptions {
        ConvertOptions {
            output_dir,
            output_stem: None,
            side: self.settings.side,
            selected_rounds: Some(selected_rounds),
            include_suspicious: self.settings.include_suspicious,
            cut_before_bomb_plant: !self.settings.full_round,
            subtick_mode: SubtickMode::Auto,
            freeze_preroll_seconds: self.settings.freeze_preroll_seconds,
            export_cosmetics: self.settings.export_cosmetics,
            export_stickers: self.settings.export_stickers,
            analysis: AnalysisOptions::default(),
        }
    }

    fn selected_rounds(&self) -> BTreeSet<u32> {
        self.round_selection
            .iter()
            .filter_map(|(round, selected)| selected.then_some(*round))
            .collect()
    }

    fn reconcile_round_selection(&mut self) {
        if self.settings.include_suspicious {
            return;
        }
        if let Some(analysis) = &self.analysis {
            for round in &analysis.rounds {
                if round.status == RoundStatus::Suspicious {
                    self.round_selection.insert(round.round, false);
                }
            }
        }
    }

    fn receive_worker_messages(&mut self, ctx: &egui::Context) {
        let mut clear_receiver = false;
        if let Some(receiver) = self.receiver.take() {
            while let Ok(message) = receiver.try_recv() {
                match message {
                    WorkerMessage::Log(message) => self.push_log(message),
                    WorkerMessage::AnalysisComplete { parsed, analysis } => {
                        self.progress
                            .finish(format!("Parsed {} rounds", analysis.rounds.len()));
                        self.round_selection = default_round_selection(&analysis);
                        self.parsed = Some(parsed);
                        self.analysis = Some(analysis);
                        self.result = None;
                        self.error = None;
                        clear_receiver = true;
                    }
                    WorkerMessage::ConversionProgress(event) => {
                        self.progress.apply_conversion_event(&event);
                        self.push_log(format_progress_event(&event));
                    }
                    WorkerMessage::ConversionComplete { report, validated } => {
                        self.progress.finish("Conversion complete");
                        self.result = Some(ConversionResultView::from_report(report, validated));
                        self.error = None;
                        clear_receiver = true;
                    }
                    WorkerMessage::Failed(message) => {
                        self.progress.fail("Failed");
                        self.error = Some(message);
                        clear_receiver = true;
                    }
                }
            }
            if !clear_receiver {
                self.receiver = Some(receiver);
            }
        }
        if clear_receiver {
            self.running = None;
        }
        if self.is_running() {
            ctx.request_repaint_after(Duration::from_millis(100));
        }
    }

    fn push_log(&mut self, message: String) {
        if message.trim().is_empty() {
            return;
        }
        self.logs.push(message);
        if self.logs.len() > 240 {
            let drop_count = self.logs.len() - 240;
            self.logs.drain(0..drop_count);
        }
    }

    fn save_settings(&mut self) {
        self.settings.freeze_preroll_seconds =
            self.settings.freeze_preroll_seconds.clamp(0.0, 120.0);
        if !self.settings.export_cosmetics {
            self.settings.export_stickers = false;
        }
        if let Err(err) = save_settings(&self.settings) {
            self.push_log(format!("settings not saved: {err}"));
        }
    }

    fn browse_demo(&mut self) {
        if let Some(path) = rfd::FileDialog::new()
            .add_filter("CS2 demo", &["dem"])
            .pick_file()
        {
            self.settings.demo_path = path.display().to_string();
            self.save_settings();
        }
    }

    fn browse_output(&mut self) {
        if let Some(path) = rfd::FileDialog::new().pick_folder() {
            self.settings.output_dir = path.display().to_string();
            self.save_settings();
        }
    }

    fn handle_drops(&mut self, ctx: &egui::Context) {
        let dropped_files = ctx.input(|input| input.raw.dropped_files.clone());
        for dropped in dropped_files {
            let Some(path) = dropped.path else {
                continue;
            };
            if is_demo_file(&path) {
                self.settings.demo_path = path.display().to_string();
                self.save_settings();
            } else if path.is_dir() {
                self.settings.output_dir = path.display().to_string();
                self.save_settings();
            }
        }
    }

    fn open_result_folder(&mut self) {
        let Some(path) = self
            .result
            .as_ref()
            .map(|result| result.manifest_path.clone())
        else {
            return;
        };
        if let Err(err) = reveal_path(&path) {
            self.push_log(format!("could not open result folder: {err}"));
        }
    }
}

impl eframe::App for DemoTracerGui {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.handle_drops(ctx);
        self.receive_worker_messages(ctx);
        self.reconcile_round_selection();

        egui::TopBottomPanel::top("workspace-top")
            .resizable(false)
            .show(ctx, |ui| {
                ui.add_space(4.0);
                self.draw_header(ui);
                ui.add_space(8.0);
                self.draw_controls(ui);
                ui.add_space(8.0);
                self.draw_progress(ui);
                ui.add_space(8.0);
                self.draw_workflow(ui);
                ui.add_space(6.0);
            });

        egui::TopBottomPanel::bottom("workspace-log")
            .resizable(false)
            .exact_height(176.0)
            .show(ctx, |ui| {
                ui.add_space(4.0);
                self.draw_logs(ui);
            });

        egui::CentralPanel::default().show(ctx, |ui| {
            let available = ui.available_size();
            let left_width = if available.x > 840.0 {
                (available.x * 0.66).min(available.x - 300.0)
            } else {
                available.x * 0.60
            };

            ui.horizontal(|ui| {
                ui.allocate_ui_with_layout(
                    egui::vec2(left_width, available.y),
                    egui::Layout::top_down(egui::Align::Min),
                    |ui| self.draw_rounds(ui),
                );
                ui.separator();
                ui.allocate_ui_with_layout(
                    egui::vec2(ui.available_width(), available.y),
                    egui::Layout::top_down(egui::Align::Min),
                    |ui| self.draw_result(ui),
                );
            });
        });

        self.draw_overwrite_dialog(ctx);
        self.draw_cosmetic_disclaimer_dialog(ctx);
    }
}

impl DemoTracerGui {
    fn draw_header(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            ui.heading(
                RichText::new("CS2 DemoTracer")
                    .strong()
                    .color(Color32::WHITE),
            );
            ui.label(RichText::new("single demo conversion workbench").color(MUTED));
        });
        ui.label(
            RichText::new("Drop a .dem file here, inspect round quality, export selected rounds, then use the generated manifest in CS2.")
                .color(Color32::from_rgb(190, 198, 210)),
        );
    }

    fn draw_controls(&mut self, ui: &mut egui::Ui) {
        egui::Frame::new()
            .fill(PANEL)
            .stroke(egui::Stroke::new(1.0, Color32::from_rgb(54, 66, 82)))
            .corner_radius(6)
            .inner_margin(egui::Margin::symmetric(12, 10))
            .show(ui, |ui| {
            ui.vertical(|ui| {
                ui.horizontal(|ui| {
                    ui.label("Demo");
                    ui.add_sized(
                        [ui.available_width() - 108.0, 28.0],
                        egui::TextEdit::singleline(&mut self.settings.demo_path),
                    );
                    if ui.button("Browse").clicked() {
                        self.browse_demo();
                    }
                });
                ui.horizontal(|ui| {
                    ui.label("Output");
                    ui.add_sized(
                        [ui.available_width() - 108.0, 28.0],
                        egui::TextEdit::singleline(&mut self.settings.output_dir),
                    );
                    if ui.button("Folder").clicked() {
                        self.browse_output();
                    }
                });
                ui.horizontal(|ui| {
                    egui::ComboBox::from_label("Side")
                        .selected_text(self.settings.side.to_string())
                        .show_ui(ui, |ui| {
                            ui.selectable_value(&mut self.settings.side, Side::Both, "both");
                            ui.selectable_value(&mut self.settings.side, Side::T, "t");
                            ui.selectable_value(&mut self.settings.side, Side::Ct, "ct");
                        });
                    ui.checkbox(&mut self.settings.full_round, "full round");
                    let suspicious_changed = ui
                        .checkbox(&mut self.settings.include_suspicious, "include suspicious")
                        .changed();
                    if suspicious_changed {
                        self.reconcile_round_selection();
                    }
                    ui.label("freeze pre-roll");
                    ui.add(
                        egui::DragValue::new(&mut self.settings.freeze_preroll_seconds)
                            .speed(0.5)
                            .range(0.0..=120.0)
                            .suffix("s"),
                    );
                });
                ui.horizontal_wrapped(|ui| {
                    ui.label("Optional metadata");
                    let cosmetics_changed = ui
                        .checkbox(&mut self.settings.export_cosmetics, "export cosmetics")
                        .changed();
                    if cosmetics_changed {
                        self.cosmetic_acknowledged = false;
                        self.cosmetic_confirmation.clear();
                        if self.settings.export_cosmetics {
                            self.show_cosmetic_disclaimer = true;
                        } else {
                            self.settings.export_stickers = false;
                            self.show_cosmetic_disclaimer = false;
                        }
                    }
                    ui.add_enabled_ui(self.settings.export_cosmetics, |ui| {
                        let stickers_changed = ui
                            .checkbox(&mut self.settings.export_stickers, "export stickers")
                            .changed();
                        if stickers_changed && self.settings.export_stickers {
                            self.cosmetic_acknowledged = false;
                            self.cosmetic_confirmation.clear();
                            self.show_cosmetic_disclaimer = true;
                        }
                    });
                    if !self.settings.export_cosmetics {
                        self.settings.export_stickers = false;
                    }
                    if self.settings.export_cosmetics && self.cosmetic_acknowledged {
                        ui.colored_label(Color32::from_rgb(80, 210, 146), "risk confirmed");
                    } else if self.settings.export_cosmetics {
                        ui.colored_label(WARN, "confirmation required");
                    }
                });
                if self.settings.export_cosmetics {
                    warning_strip(
                        ui,
                        "High-risk metadata option",
                        "Cosmetic and sticker evidence will be written into manifest JSON only after explicit confirmation.",
                    );
                }
                ui.horizontal(|ui| {
                    ui.add_enabled_ui(!self.is_running(), |ui| {
                        if ui
                            .add(
                                egui::Button::new(RichText::new("Analyze demo").strong())
                                    .fill(Color32::from_rgb(38, 86, 118))
                                    .min_size(egui::vec2(132.0, 32.0)),
                            )
                            .clicked()
                        {
                            self.analyze();
                        }
                        if ui
                            .add(
                                egui::Button::new(RichText::new("Convert selected").strong())
                                    .fill(Color32::from_rgb(28, 118, 82))
                                    .min_size(egui::vec2(158.0, 32.0)),
                            )
                            .clicked()
                        {
                            self.request_convert();
                        }
                    });
                    if ui.button("Open result").clicked() {
                        self.open_result_folder();
                    }
                });
            });
        });
    }

    fn draw_progress(&mut self, ui: &mut egui::Ui) {
        let progress_color = if self.error.is_some() {
            DANGER
        } else if self.result.is_some() {
            GOOD
        } else if self.is_running() {
            INFO
        } else if self.analysis.is_some() {
            INFO
        } else {
            WARN
        };
        egui::Frame::new()
            .fill(PANEL_DEEP)
            .stroke(egui::Stroke::new(1.0, Color32::from_rgb(45, 56, 70)))
            .corner_radius(6)
            .inner_margin(egui::Margin::symmetric(10, 8))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.label(
                        RichText::new(&self.progress.stage)
                            .strong()
                            .color(progress_color),
                    );
                    let progress = self.progress.fraction.unwrap_or(0.0);
                    let mut bar = egui::ProgressBar::new(progress)
                        .desired_width(ui.available_width())
                        .fill(progress_color);
                    if self.progress.fraction.is_some() {
                        bar = bar.show_percentage();
                    } else {
                        bar = bar.animate(true);
                    }
                    ui.add(bar);
                });
                if let Some(error) = &self.error {
                    ui.colored_label(DANGER, error);
                }
            });
    }

    fn draw_workflow(&mut self, ui: &mut egui::Ui) {
        let demo_loaded = !self.settings.demo_path.trim().is_empty();
        let parsed = self.analysis.is_some();
        let selected = parsed && !self.selected_rounds().is_empty();
        let complete = self.result.is_some();
        let running_analyze = self.running == Some(RunningTask::Analyze);
        let running_convert = self.running == Some(RunningTask::Convert);

        ui.horizontal_wrapped(|ui| {
            workflow_step(ui, "1", "Demo", demo_loaded, false, "source selected");
            workflow_step(
                ui,
                "2",
                "Parse",
                parsed,
                running_analyze,
                self.analysis
                    .as_ref()
                    .map(|analysis| format!("{} rounds", analysis.rounds.len()))
                    .unwrap_or_else(|| "analyze demo".to_string())
                    .as_str(),
            );
            workflow_step(
                ui,
                "3",
                "Select",
                selected,
                false,
                &format!("{} rounds selected", self.selected_rounds().len()),
            );
            workflow_step(
                ui,
                "4",
                "Convert",
                complete,
                running_convert,
                self.result
                    .as_ref()
                    .map(|result| format!("{} .dtr ready", result.files_written))
                    .unwrap_or_else(|| "waiting".to_string())
                    .as_str(),
            );
        });
    }

    fn draw_rounds(&mut self, ui: &mut egui::Ui) {
        ui.heading(RichText::new("Rounds").color(Color32::WHITE));
        let Some(analysis) = self.analysis.clone() else {
            empty_panel(
                ui,
                "No analysis yet.",
                "Choose a demo and run Analyze demo.",
            );
            return;
        };
        egui::Frame::new()
            .fill(PANEL)
            .stroke(egui::Stroke::new(1.0, Color32::from_rgb(50, 62, 78)))
            .corner_radius(6)
            .inner_margin(egui::Margin::symmetric(10, 8))
            .show(ui, |ui| {
                ui.horizontal_wrapped(|ui| {
                    metric_chip(ui, "map", &analysis.map, INFO);
                    metric_chip(ui, "tick", &format!("{:.1}", analysis.tick_rate), INFO);
                    metric_chip(ui, "rows", &analysis.row_count.to_string(), INFO);
                    metric_chip(
                        ui,
                        "recommended",
                        &analysis
                            .rounds
                            .iter()
                            .filter(|round| round.status == RoundStatus::Recommended)
                            .count()
                            .to_string(),
                        GOOD,
                    );
                    metric_chip(
                        ui,
                        "suspicious",
                        &analysis
                            .rounds
                            .iter()
                            .filter(|round| round.status == RoundStatus::Suspicious)
                            .count()
                            .to_string(),
                        WARN,
                    );
                });
            });
        ui.add_space(8.0);
        let table_height = ui.available_height().max(180.0);
        TableBuilder::new(ui)
            .id_salt("round-table")
            .striped(true)
            .resizable(true)
            .auto_shrink([false, false])
            .max_scroll_height(table_height)
            .column(Column::exact(34.0))
            .column(Column::exact(58.0))
            .column(Column::exact(112.0))
            .column(Column::exact(78.0))
            .column(Column::exact(70.0))
            .column(Column::exact(82.0))
            .column(Column::exact(58.0))
            .column(Column::remainder().at_least(220.0))
            .header(28.0, |mut header| {
                header.col(|ui| table_header_text(ui, ""));
                header.col(|ui| table_header_text(ui, "Round"));
                header.col(|ui| table_header_text(ui, "Status"));
                header.col(|ui| table_header_text(ui, "Time"));
                header.col(|ui| table_header_text(ui, "T/CT"));
                header.col(|ui| table_header_text(ui, "Rows"));
                header.col(|ui| table_header_text(ui, "Files"));
                header.col(|ui| table_header_text(ui, "Notes"));
            })
            .body(|mut body| {
                for round in &analysis.rounds {
                    let allowed = round.status == RoundStatus::Recommended
                        || self.settings.include_suspicious;
                    let files = self.files_for_round(round.round);
                    let selected = self.round_selection.entry(round.round).or_insert(false);
                    if !allowed {
                        *selected = false;
                    }
                    body.row(34.0, |mut row| {
                        row.set_selected(*selected);
                        row.col(|ui| {
                            let checkbox_response =
                                ui.add_enabled(allowed, egui::Checkbox::without_text(selected));
                            if !allowed {
                                checkbox_response.on_disabled_hover_text(
                                    "Enable include suspicious to select this round",
                                );
                            }
                        });
                        row.col(|ui| {
                            table_text(ui, format!("{:02}", round.round), Color32::WHITE, true)
                        });
                        row.col(|ui| {
                            let status_color = match round.status {
                                RoundStatus::Recommended => GOOD,
                                RoundStatus::Suspicious => WARN,
                            };
                            table_text(ui, format!("{:?}", round.status), status_color, true);
                        });
                        row.col(|ui| {
                            table_text(
                                ui,
                                format!("{:.1}s", round.duration_seconds),
                                Color32::WHITE,
                                false,
                            );
                        });
                        row.col(|ui| {
                            table_text(
                                ui,
                                format!("{}/{}", round.t_players, round.ct_players),
                                Color32::WHITE,
                                false,
                            );
                        });
                        row.col(|ui| {
                            table_text(ui, round.valid_rows.to_string(), Color32::WHITE, false)
                        });
                        row.col(|ui| table_text(ui, files, Color32::WHITE, false));
                        row.col(|ui| {
                            let notes = if round.problems.is_empty() {
                                "ok".to_string()
                            } else {
                                round.problems.join("; ")
                            };
                            ui.add(egui::Label::new(RichText::new(notes).color(MUTED)).wrap());
                        });
                    });
                }
            });
    }

    fn draw_result(&mut self, ui: &mut egui::Ui) {
        ui.heading(RichText::new("Output").color(Color32::WHITE));
        ScrollArea::vertical()
            .id_salt("result-scroll")
            .auto_shrink([false, false])
            .show(ui, |ui| {
                let Some(result) = self.result.as_ref() else {
                    empty_panel(
                        ui,
                        "Waiting for conversion",
                        "After conversion this panel shows output size, rounds, players, manifest, and CS2 commands.",
                    );
                    return;
                };

                let manifest_text = result.manifest_path.display().to_string();
                let root_text = result.root.display().to_string();
                let command = result.console_command(self.first_selected_or_exported_round());
                let players = result.players.clone();

                egui::Frame::new()
                    .fill(Color32::from_rgb(18, 58, 42))
                    .stroke(egui::Stroke::new(1.5, GOOD))
                    .corner_radius(6)
                    .inner_margin(egui::Margin::symmetric(12, 10))
                    .show(ui, |ui| {
                        ui.label(
                            RichText::new("Conversion complete")
                                .strong()
                                .size(22.0)
                                .color(Color32::WHITE),
                        );
                        ui.label(
                            RichText::new("Validated manifest and replay files are ready.")
                                .color(Color32::from_rgb(196, 232, 214)),
                        );
                    });
                ui.add_space(10.0);
                ui.horizontal_wrapped(|ui| {
                    summary_tile(ui, "Output", &format_bytes(result.output_bytes), GOOD);
                    summary_tile(ui, "Rounds", &result.rounds_exported.to_string(), INFO);
                    summary_tile(ui, "Players", &players.len().to_string(), INFO);
                    summary_tile(ui, ".dtr files", &result.files_written.to_string(), GOOD);
                    summary_tile(ui, "Validated", &result.validated.to_string(), GOOD);
                });
                if result.cosmetic_files > 0 {
                    ui.add_space(6.0);
                    warning_strip(
                        ui,
                        "Cosmetic metadata exported",
                        &format!(
                            "{} replay files include cosmetics; {} include stickers.",
                            result.cosmetic_files, result.sticker_files
                        ),
                    );
                }
                ui.add_space(10.0);
                egui::Frame::new()
                    .fill(PANEL)
                    .stroke(egui::Stroke::new(1.0, Color32::from_rgb(50, 62, 78)))
                    .corner_radius(6)
                    .inner_margin(egui::Margin::symmetric(10, 8))
                    .show(ui, |ui| {
                        ui.label(RichText::new("Players").strong().color(Color32::WHITE));
                        ui.add_space(4.0);
                        if players.is_empty() {
                            ui.label(RichText::new("No player files were exported.").color(MUTED));
                        } else {
                            for player in players.iter().take(12) {
                                ui.horizontal(|ui| {
                                    let side_color = if player.side.eq_ignore_ascii_case("t") {
                                        Color32::from_rgb(238, 190, 92)
                                    } else {
                                        Color32::from_rgb(92, 178, 255)
                                    };
                                    ui.label(
                                        RichText::new(player.side.to_uppercase())
                                            .strong()
                                            .color(side_color),
                                    );
                                    ui.label(RichText::new(&player.name).strong());
                                    ui.label(
                                        RichText::new(format!(
                                            "{} rounds / {} files / {}",
                                            player.rounds, player.files, player.steam_id
                                        ))
                                        .color(MUTED),
                                    );
                                });
                            }
                            if players.len() > 12 {
                                ui.label(
                                    RichText::new(format!("+{} more players", players.len() - 12))
                                        .color(MUTED),
                                );
                            }
                        }
                    });
                ui.add_space(10.0);
                path_block(ui, "Root", &root_text);
                path_block(ui, "Manifest", &manifest_text);
                ui.add_space(8.0);
                let mut copy_command = false;
                let mut copy_manifest = false;
                ui.horizontal(|ui| {
                    ui.label(RichText::new("CS2 console").strong());
                    copy_command = ui.button("Copy command").clicked();
                    copy_manifest = ui.button("Copy manifest").clicked();
                });
                if copy_command {
                    ui.ctx().copy_text(command.clone());
                    self.push_log("copied CS2 console command".to_string());
                }
                if copy_manifest {
                    ui.ctx().copy_text(manifest_text.clone());
                    self.push_log("copied manifest path".to_string());
                }
                let mut command_text = command;
                ui.add(
                    egui::TextEdit::multiline(&mut command_text)
                        .desired_rows(3)
                        .code_editor()
                        .interactive(false),
                );
            });
    }

    fn draw_logs(&mut self, ui: &mut egui::Ui) {
        ui.heading("Log");
        egui::Frame::group(ui.style()).show(ui, |ui| {
            ScrollArea::vertical()
                .stick_to_bottom(true)
                .id_salt("log-scroll")
                .auto_shrink([false, false])
                .max_height(132.0)
                .show(ui, |ui| {
                    for line in &self.logs {
                        ui.label(line);
                    }
                });
        });
    }

    fn draw_overwrite_dialog(&mut self, ctx: &egui::Context) {
        if self.pending_overwrite.is_none() {
            return;
        };
        let path = self
            .pending_overwrite
            .as_ref()
            .map(|pending| pending.overwrite_root.display().to_string())
            .unwrap_or_default();
        egui::Window::new("Output already exists")
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label("The target demo output directory already exists.");
                ui.label(path);
                ui.horizontal(|ui| {
                    if ui.button("Clear and convert").clicked() {
                        if let Some(pending) = self.pending_overwrite.take() {
                            self.start_convert(pending, true);
                        }
                    }
                    if ui.button("Cancel").clicked() {
                        self.pending_overwrite = None;
                    }
                });
            });
    }

    fn draw_cosmetic_disclaimer_dialog(&mut self, ctx: &egui::Context) {
        if !self.show_cosmetic_disclaimer {
            return;
        }
        egui::Window::new("Cosmetic export confirmation")
            .anchor(egui::Align2::CENTER_CENTER, egui::Vec2::ZERO)
            .collapsible(false)
            .resizable(false)
            .frame(
                egui::Frame::new()
                    .fill(Color32::from_rgb(52, 18, 20))
                    .stroke(egui::Stroke::new(2.0, DANGER))
                    .corner_radius(8)
                    .inner_margin(egui::Margin::symmetric(18, 16)),
            )
            .show(ctx, |ui| {
                ui.label(
                    RichText::new("HIGH RISK: GSLT / cosmetic export")
                        .strong()
                        .size(24.0)
                        .color(Color32::WHITE),
                );
                ui.label(
                    RichText::new(
                        "This writes demo-observed weapon, knife, glove, and optional sticker metadata into manifest JSON.",
                    )
                    .color(Color32::from_rgb(255, 220, 220)),
                );
                ui.add_space(8.0);
                egui::Frame::new()
                    .fill(Color32::from_rgb(76, 24, 27))
                    .stroke(egui::Stroke::new(1.0, Color32::from_rgb(180, 66, 66)))
                    .corner_radius(6)
                    .inner_margin(egui::Margin::symmetric(12, 10))
                    .show(ui, |ui| {
                        ui.label(RichText::new("Before enabling this, confirm:").strong());
                        ui.label("- You have assessed Valve server guideline and GSLT risk.");
                        ui.label("- Runtime cosmetic/sticker alignment stays default-off.");
                        ui.label("- Do not expose simulated cosmetics to public or human-controlled bot usage unless you accept that risk.");
                    });
                ui.add_space(10.0);
                ui.label(RichText::new("Type exactly to unlock export:").strong());
                ui.label(
                    RichText::new(COSMETIC_CONFIRMATION_PHRASE)
                        .monospace()
                        .strong()
                        .color(Color32::from_rgb(255, 214, 102)),
                );
                ui.add_sized(
                    [420.0, 28.0],
                    egui::TextEdit::singleline(&mut self.cosmetic_confirmation),
                );
                let confirmed = cosmetic_confirmation_matches(&self.cosmetic_confirmation);
                if !confirmed {
                    ui.colored_label(DANGER, "Export remains disabled until the phrase matches.");
                }
                ui.add_space(8.0);
                ui.horizontal(|ui| {
                    if ui
                        .add_enabled(
                            confirmed,
                            egui::Button::new(RichText::new("Enable risky export").strong())
                                .fill(DANGER)
                                .min_size(egui::vec2(172.0, 34.0)),
                        )
                        .clicked()
                    {
                        self.cosmetic_acknowledged = true;
                        self.show_cosmetic_disclaimer = false;
                        self.error = None;
                        self.push_log("cosmetic export risk confirmed".to_string());
                    }
                    if ui.button("Cancel").clicked() {
                        self.settings.export_cosmetics = false;
                        self.settings.export_stickers = false;
                        self.cosmetic_acknowledged = false;
                        self.cosmetic_confirmation.clear();
                        self.show_cosmetic_disclaimer = false;
                    }
                });
            });
    }

    fn files_for_round(&self, round: u32) -> String {
        self.result
            .as_ref()
            .and_then(|result| result.files_by_round.get(&round).copied())
            .map(|files| files.to_string())
            .unwrap_or_else(|| "-".to_string())
    }

    fn first_selected_or_exported_round(&self) -> Option<u32> {
        self.selected_rounds().into_iter().next().or_else(|| {
            self.result
                .as_ref()
                .and_then(|result| result.files_by_round.keys().next().copied())
        })
    }
}

#[derive(Clone)]
struct PendingConversion {
    parsed: Arc<ParsedDemo>,
    options: ConvertOptions,
    overwrite_root: PathBuf,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum RunningTask {
    Analyze,
    Convert,
}

enum WorkerMessage {
    Log(String),
    AnalysisComplete {
        parsed: Arc<ParsedDemo>,
        analysis: DemoAnalysis,
    },
    ConversionProgress(ConversionProgress),
    ConversionComplete {
        report: ConversionReport,
        validated: usize,
    },
    Failed(String),
}

#[derive(Default)]
struct GuiProgress {
    stage: String,
    fraction: Option<f32>,
    file_units_done: usize,
    file_units_total: usize,
    artifact_units_done: usize,
    artifact_units_total: usize,
}

impl GuiProgress {
    fn begin(&mut self, stage: impl Into<String>, fraction: Option<f32>) {
        self.stage = stage.into();
        self.fraction = fraction;
        self.file_units_done = 0;
        self.file_units_total = 0;
        self.artifact_units_done = 0;
        self.artifact_units_total = 0;
    }

    fn apply_conversion_event(&mut self, event: &ConversionProgress) {
        match event {
            ConversionProgress::AnalysisStarted => self.begin("Analyzing selected rounds", None),
            ConversionProgress::AnalysisFinished {
                selected_rounds,
                estimated_files,
                ..
            } => {
                self.stage = format!("Exporting {selected_rounds} rounds");
                self.file_units_total = (*estimated_files).max(1);
                self.file_units_done = 0;
                self.fraction = Some(0.08);
            }
            ConversionProgress::RoundStarted { round, .. } => {
                self.stage = format!("Round {round:02}");
            }
            ConversionProgress::PlayerWritten { .. } => {
                self.file_units_done += 1;
                let ratio = self.file_units_done as f32 / self.file_units_total.max(1) as f32;
                self.fraction = Some(0.10 + 0.72 * ratio.min(1.0));
            }
            ConversionProgress::ArtifactsWritingStarted { artifacts, .. } => {
                self.stage = "Writing output files".to_string();
                self.artifact_units_total = (*artifacts).max(1);
                self.artifact_units_done = 0;
                self.fraction = Some(0.84);
            }
            ConversionProgress::ArtifactWritten { .. } => {
                self.artifact_units_done += 1;
                let ratio =
                    self.artifact_units_done as f32 / self.artifact_units_total.max(1) as f32;
                self.fraction = Some(0.84 + 0.10 * ratio.min(1.0));
            }
            ConversionProgress::Finished { .. } => {
                self.stage = "Validating output".to_string();
                self.fraction = Some(0.96);
            }
            ConversionProgress::RoundSkipped { .. }
            | ConversionProgress::PlayerSkipped { .. }
            | ConversionProgress::RoundFinished { .. } => {}
        }
    }

    fn finish(&mut self, stage: impl Into<String>) {
        self.stage = stage.into();
        self.fraction = Some(1.0);
    }

    fn fail(&mut self, stage: impl Into<String>) {
        self.stage = stage.into();
        self.fraction = Some(0.0);
    }
}

#[derive(Clone)]
struct PlayerSummary {
    side: String,
    steam_id: u64,
    name: String,
    rounds: usize,
    files: usize,
}

struct PlayerAccumulator {
    side: String,
    name: String,
    rounds: BTreeSet<u32>,
    files: usize,
}

struct ConversionResultView {
    root: PathBuf,
    manifest_path: PathBuf,
    files_written: usize,
    validated: usize,
    output_bytes: u64,
    rounds_exported: usize,
    files_by_round: BTreeMap<u32, usize>,
    players: Vec<PlayerSummary>,
    cosmetic_files: usize,
    sticker_files: usize,
}

impl ConversionResultView {
    fn from_report(report: ConversionReport, validated: usize) -> Self {
        let output_bytes = directory_size_bytes(&report.root).unwrap_or(0);
        let rounds_exported = report.manifest.rounds.len();
        let files_by_round = report
            .manifest
            .rounds
            .iter()
            .map(|round| (round.round, round.files))
            .collect();
        let players = summarize_exported_players(&report.manifest.files);
        let cosmetic_files = report
            .manifest
            .files
            .iter()
            .filter(|file| {
                file.cosmetics
                    .as_ref()
                    .is_some_and(|cosmetics| !cosmetics.is_empty())
            })
            .count();
        let sticker_files = report
            .manifest
            .files
            .iter()
            .filter(|file| {
                file.cosmetics.as_ref().is_some_and(|cosmetics| {
                    cosmetics
                        .weapons
                        .iter()
                        .any(|weapon| !weapon.stickers.is_empty())
                })
            })
            .count();
        Self {
            root: report.root,
            manifest_path: report.manifest_path,
            files_written: report.files_written,
            validated,
            output_bytes,
            rounds_exported,
            files_by_round,
            players,
            cosmetic_files,
            sticker_files,
        }
    }

    fn console_command(&self, first_round: Option<u32>) -> String {
        let round = first_round.unwrap_or(0);
        let manifest = console_quote_path(&self.manifest_path);
        format!("dtr_go round \"{manifest}\" {round}\r\ndtr_go seq \"{manifest}\" {round}")
    }
}

fn analyze_worker(demo_path: PathBuf, tx: Sender<WorkerMessage>) {
    let _ = tx.send(WorkerMessage::Log(format!(
        "reading {}",
        demo_path.display()
    )));
    let result = read_demo(&demo_path).map(|parsed| {
        let analysis = analyze_demo(&parsed, AnalysisOptions::default());
        (Arc::new(parsed), analysis)
    });
    match result {
        Ok((parsed, analysis)) => {
            let _ = tx.send(WorkerMessage::Log(format!(
                "analysis complete: {} rounds",
                analysis.rounds.len()
            )));
            let _ = tx.send(WorkerMessage::AnalysisComplete { parsed, analysis });
        }
        Err(err) => {
            let _ = tx.send(WorkerMessage::Failed(err.to_string()));
        }
    }
}

fn convert_worker(pending: PendingConversion, clear_existing: bool, tx: Sender<WorkerMessage>) {
    let result = (|| -> crate::Result<(ConversionReport, usize)> {
        if clear_existing && pending.overwrite_root.exists() {
            fs::remove_dir_all(&pending.overwrite_root)
                .map_err(|err| crate::io_error(&pending.overwrite_root, err))?;
        }
        let progress_tx = tx.clone();
        let report = export_demo_with_progress(&pending.parsed, &pending.options, move |event| {
            let _ = progress_tx.send(WorkerMessage::ConversionProgress(event));
        })?;
        let _ = tx.send(WorkerMessage::Log("validating output".to_string()));
        let validated = validate_dtr_path(&report.root)?;
        Ok((report, validated))
    })();

    match result {
        Ok((report, validated)) => {
            let _ = tx.send(WorkerMessage::ConversionComplete { report, validated });
        }
        Err(err) => {
            let _ = tx.send(WorkerMessage::Failed(err.to_string()));
        }
    }
}

fn apply_visuals(ctx: &egui::Context) {
    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = Color32::from_rgb(18, 22, 28);
    visuals.window_fill = Color32::from_rgb(25, 31, 39);
    visuals.extreme_bg_color = Color32::from_rgb(11, 14, 18);
    visuals.faint_bg_color = Color32::from_rgb(31, 38, 48);
    visuals.widgets.active.bg_fill = Color32::from_rgb(42, 94, 126);
    visuals.widgets.hovered.bg_fill = Color32::from_rgb(57, 72, 88);
    visuals.selection.bg_fill = Color32::from_rgb(47, 119, 152);
    ctx.set_visuals(visuals);

    let mut style = (*ctx.style()).clone();
    style
        .text_styles
        .insert(TextStyle::Heading, FontId::proportional(25.0));
    style
        .text_styles
        .insert(TextStyle::Body, FontId::proportional(16.0));
    style
        .text_styles
        .insert(TextStyle::Button, FontId::proportional(16.0));
    style
        .text_styles
        .insert(TextStyle::Monospace, FontId::monospace(15.0));
    style.spacing.item_spacing = egui::vec2(9.0, 8.0);
    style.spacing.button_padding = egui::vec2(12.0, 7.0);
    style.spacing.interact_size = egui::vec2(44.0, 30.0);
    ctx.set_style(style);
}

fn default_round_selection(analysis: &DemoAnalysis) -> BTreeMap<u32, bool> {
    analysis
        .rounds
        .iter()
        .map(|round| (round.round, round.status == RoundStatus::Recommended))
        .collect()
}

fn cosmetic_confirmation_matches(input: &str) -> bool {
    input.trim() == COSMETIC_CONFIRMATION_PHRASE
}

fn cosmetic_export_ready(settings: &GuiSettings, acknowledged: bool) -> bool {
    !settings.export_cosmetics || acknowledged
}

fn workflow_step(
    ui: &mut egui::Ui,
    index: &str,
    title: &str,
    done: bool,
    running: bool,
    detail: &str,
) {
    let (fill, stroke, accent) = if done {
        (
            Color32::from_rgb(16, 54, 39),
            egui::Stroke::new(1.5, GOOD),
            GOOD,
        )
    } else if running {
        (
            Color32::from_rgb(18, 46, 66),
            egui::Stroke::new(1.5, INFO),
            INFO,
        )
    } else {
        (
            PANEL,
            egui::Stroke::new(1.0, Color32::from_rgb(48, 59, 74)),
            MUTED,
        )
    };
    egui::Frame::new()
        .fill(fill)
        .stroke(stroke)
        .corner_radius(6)
        .inner_margin(egui::Margin::symmetric(10, 8))
        .show(ui, |ui| {
            ui.set_min_width(170.0);
            ui.horizontal(|ui| {
                ui.label(RichText::new(index).strong().size(20.0).color(accent));
                ui.vertical(|ui| {
                    ui.label(RichText::new(title).strong().color(Color32::WHITE));
                    ui.label(RichText::new(detail).size(13.0).color(MUTED));
                });
            });
        });
}

fn metric_chip(ui: &mut egui::Ui, label: &str, value: &str, accent: Color32) {
    egui::Frame::new()
        .fill(PANEL_DEEP)
        .stroke(egui::Stroke::new(1.0, Color32::from_rgb(48, 59, 74)))
        .corner_radius(5)
        .inner_margin(egui::Margin::symmetric(8, 5))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(RichText::new(label).color(MUTED));
                ui.label(RichText::new(value).strong().color(accent));
            });
        });
}

fn summary_tile(ui: &mut egui::Ui, label: &str, value: &str, accent: Color32) {
    egui::Frame::new()
        .fill(PANEL)
        .stroke(egui::Stroke::new(1.0, Color32::from_rgb(50, 62, 78)))
        .corner_radius(6)
        .inner_margin(egui::Margin::symmetric(10, 8))
        .show(ui, |ui| {
            ui.set_min_width(104.0);
            ui.label(RichText::new(label).size(13.0).color(MUTED));
            ui.label(RichText::new(value).strong().size(20.0).color(accent));
        });
}

fn warning_strip(ui: &mut egui::Ui, title: &str, body: &str) {
    egui::Frame::new()
        .fill(Color32::from_rgb(58, 36, 18))
        .stroke(egui::Stroke::new(1.0, WARN))
        .corner_radius(6)
        .inner_margin(egui::Margin::symmetric(10, 8))
        .show(ui, |ui| {
            ui.label(
                RichText::new(title)
                    .strong()
                    .color(Color32::from_rgb(255, 217, 145)),
            );
            ui.label(RichText::new(body).color(Color32::from_rgb(238, 211, 174)));
        });
}

fn empty_panel(ui: &mut egui::Ui, title: &str, body: &str) {
    egui::Frame::new()
        .fill(PANEL)
        .stroke(egui::Stroke::new(1.0, Color32::from_rgb(44, 55, 69)))
        .corner_radius(6)
        .inner_margin(egui::Margin::symmetric(12, 10))
        .show(ui, |ui| {
            ui.label(RichText::new(title).strong().color(Color32::WHITE));
            ui.label(RichText::new(body).color(MUTED));
        });
}

fn path_block(ui: &mut egui::Ui, label: &str, value: &str) {
    ui.label(RichText::new(label).strong().color(Color32::WHITE));
    let mut text = value.to_string();
    ui.add(
        egui::TextEdit::singleline(&mut text)
            .code_editor()
            .interactive(false)
            .desired_width(ui.available_width()),
    );
}

fn table_header_text(ui: &mut egui::Ui, text: &str) {
    ui.label(RichText::new(text).strong().color(MUTED));
}

fn table_text(ui: &mut egui::Ui, text: impl Into<String>, color: Color32, strong: bool) {
    let mut rich = RichText::new(text.into()).color(color);
    if strong {
        rich = rich.strong();
    }
    ui.label(rich);
}

fn summarize_exported_players(files: &[crate::model::ConvertedFile]) -> Vec<PlayerSummary> {
    let mut players: BTreeMap<(u8, u64), PlayerAccumulator> = BTreeMap::new();
    for file in files {
        let key = (side_rank(&file.side), file.steam_id);
        let player = players.entry(key).or_insert_with(|| PlayerAccumulator {
            side: file.side.clone(),
            name: if file.player_name.is_empty() {
                file.steam_id.to_string()
            } else {
                file.player_name.clone()
            },
            rounds: BTreeSet::new(),
            files: 0,
        });
        player.rounds.insert(file.round);
        player.files += 1;
    }

    players
        .into_iter()
        .map(|((_, steam_id), player)| PlayerSummary {
            side: player.side,
            steam_id,
            name: player.name,
            rounds: player.rounds.len(),
            files: player.files,
        })
        .collect()
}

fn side_rank(side: &str) -> u8 {
    if side.eq_ignore_ascii_case("t") {
        0
    } else if side.eq_ignore_ascii_case("ct") {
        1
    } else {
        2
    }
}

fn directory_size_bytes(path: &Path) -> std::io::Result<u64> {
    let metadata = fs::metadata(path)?;
    if metadata.is_file() {
        return Ok(metadata.len());
    }

    let mut total = 0_u64;
    for entry in fs::read_dir(path)? {
        let entry = entry?;
        total += directory_size_bytes(&entry.path()).unwrap_or(0);
    }
    Ok(total)
}

fn format_bytes(bytes: u64) -> String {
    const KB: f64 = 1024.0;
    const MB: f64 = 1024.0 * KB;
    const GB: f64 = 1024.0 * MB;
    let bytes = bytes as f64;
    if bytes >= GB {
        format!("{:.2} GB", bytes / GB)
    } else if bytes >= MB {
        format!("{:.1} MB", bytes / MB)
    } else if bytes >= KB {
        format!("{:.0} KB", bytes / KB)
    } else {
        format!("{bytes:.0} B")
    }
}

fn format_progress_event(event: &ConversionProgress) -> String {
    match event {
        ConversionProgress::AnalysisStarted => "analysis started".to_string(),
        ConversionProgress::AnalysisFinished {
            rounds,
            selected_rounds,
            estimated_files,
        } => format!(
            "analysis rounds={rounds} selected={selected_rounds} estimated_files={estimated_files}"
        ),
        ConversionProgress::RoundSkipped { round, reason } => {
            format!("skip round {round}: {reason}")
        }
        ConversionProgress::RoundStarted {
            round,
            estimated_players,
        } => format!("round {round} players={estimated_players}"),
        ConversionProgress::PlayerSkipped {
            round,
            steam_id,
            reason,
        } => format!("skip round {round} player {steam_id}: {reason}"),
        ConversionProgress::PlayerWritten {
            round,
            steam_id,
            path,
            ticks,
            ..
        } => format!("wrote round {round} player {steam_id} ticks={ticks} path={path}"),
        ConversionProgress::RoundFinished { round, files } => {
            format!("round {round} files={files}")
        }
        ConversionProgress::ArtifactsWritingStarted { root, artifacts } => {
            format!("writing {artifacts} artifacts under {root}")
        }
        ConversionProgress::ArtifactWritten { path, kind } => {
            format!("wrote {:?} {path}", kind)
        }
        ConversionProgress::Finished {
            manifest_path,
            files_written,
            ..
        } => format!("finished files={files_written} manifest={manifest_path}"),
    }
}

fn is_demo_file(path: &Path) -> bool {
    path.is_file()
        && path
            .extension()
            .and_then(|ext| ext.to_str())
            .is_some_and(|ext| ext.eq_ignore_ascii_case("dem"))
}

fn console_quote_path(path: &Path) -> String {
    path.display().to_string().replace('"', "\\\"")
}

fn settings_path() -> Option<PathBuf> {
    std::env::var_os("APPDATA")
        .map(PathBuf::from)
        .map(|root| root.join("CS2 DemoTracer").join("gui-settings.json"))
}

fn load_settings() -> GuiSettings {
    let Some(path) = settings_path() else {
        return GuiSettings::default();
    };
    let Ok(text) = fs::read_to_string(path) else {
        return GuiSettings::default();
    };
    let mut settings: GuiSettings = serde_json::from_str(&text).unwrap_or_default();
    if !settings.export_cosmetics {
        settings.export_stickers = false;
    }
    settings
}

fn save_settings(settings: &GuiSettings) -> std::io::Result<()> {
    let Some(path) = settings_path() else {
        return Ok(());
    };
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let text = serde_json::to_string_pretty(settings).map_err(std::io::Error::other)?;
    fs::write(path, text)
}

fn reveal_path(path: &Path) -> std::io::Result<()> {
    #[cfg(windows)]
    {
        Command::new("explorer")
            .arg(format!("/select,{}", path.display()))
            .spawn()?;
        return Ok(());
    }

    #[cfg(not(windows))]
    {
        let target = path.parent().unwrap_or(path);
        Command::new("xdg-open").arg(target).spawn()?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn progress_reducer_tracks_export_units() {
        let mut progress = GuiProgress::default();
        progress.apply_conversion_event(&ConversionProgress::AnalysisFinished {
            rounds: 3,
            selected_rounds: 2,
            estimated_files: 4,
        });
        assert_eq!(progress.file_units_total, 4);
        assert_eq!(progress.fraction, Some(0.08));

        progress.apply_conversion_event(&ConversionProgress::PlayerWritten {
            round: 1,
            steam_id: 1,
            player_name: "alpha".to_string(),
            side: "t".to_string(),
            path: "round01/t/1_alpha.dtr".to_string(),
            ticks: 64,
            subticks: 0,
        });
        assert_eq!(progress.file_units_done, 1);
        assert!(progress.fraction.unwrap() > 0.1);

        progress.apply_conversion_event(&ConversionProgress::ArtifactsWritingStarted {
            root: "out/demo".to_string(),
            artifacts: 6,
        });
        progress.apply_conversion_event(&ConversionProgress::ArtifactWritten {
            path: "manifest.json".to_string(),
            kind: crate::export::ConversionArtifactKind::Manifest,
        });
        assert_eq!(progress.artifact_units_done, 1);
        assert!(progress.fraction.unwrap() > 0.84);
    }

    #[test]
    fn default_selection_uses_recommended_rounds_only() {
        let analysis = DemoAnalysis {
            demo_path: "demo.dem".to_string(),
            demo_stem: "demo".to_string(),
            map: "de_mirage".to_string(),
            tick_rate: 64.0,
            row_count: 10,
            rounds: vec![
                crate::model::RoundSummary {
                    round: 1,
                    start_tick: 0,
                    end_tick: 64,
                    duration_seconds: 1.0,
                    t_players: 5,
                    ct_players: 5,
                    total_players: 10,
                    valid_rows: 10,
                    status: RoundStatus::Recommended,
                    problems: Vec::new(),
                },
                crate::model::RoundSummary {
                    round: 2,
                    start_tick: 0,
                    end_tick: 64,
                    duration_seconds: 1.0,
                    t_players: 4,
                    ct_players: 5,
                    total_players: 9,
                    valid_rows: 9,
                    status: RoundStatus::Suspicious,
                    problems: vec!["available players 9 != 10".to_string()],
                },
            ],
        };

        let selection = default_round_selection(&analysis);

        assert_eq!(selection.get(&1), Some(&true));
        assert_eq!(selection.get(&2), Some(&false));
    }

    #[test]
    fn cosmetic_confirmation_requires_exact_phrase() {
        assert!(cosmetic_confirmation_matches(COSMETIC_CONFIRMATION_PHRASE));
        assert!(cosmetic_confirmation_matches(&format!(
            "  {COSMETIC_CONFIRMATION_PHRASE}  "
        )));
        assert!(!cosmetic_confirmation_matches(
            "I accept cosmetic export risk"
        ));
    }

    #[test]
    fn cosmetic_export_requires_acknowledgement() {
        let mut settings = GuiSettings::default();
        assert!(cosmetic_export_ready(&settings, false));

        settings.export_cosmetics = true;
        assert!(!cosmetic_export_ready(&settings, false));
        assert!(cosmetic_export_ready(&settings, true));
    }

    #[test]
    fn output_size_uses_human_units() {
        assert_eq!(format_bytes(0), "0 B");
        assert_eq!(format_bytes(1536), "2 KB");
        assert_eq!(format_bytes(2 * 1024 * 1024 + 512 * 1024), "2.5 MB");
    }

    #[test]
    fn exported_players_are_grouped_by_side_and_steam_id() {
        let files = vec![
            converted_file(1, "t", 11, "alpha"),
            converted_file(2, "t", 11, "alpha"),
            converted_file(1, "ct", 22, "bravo"),
        ];

        let players = summarize_exported_players(&files);

        assert_eq!(players.len(), 2);
        assert_eq!(players[0].side, "t");
        assert_eq!(players[0].steam_id, 11);
        assert_eq!(players[0].rounds, 2);
        assert_eq!(players[0].files, 2);
        assert_eq!(players[1].side, "ct");
        assert_eq!(players[1].steam_id, 22);
    }

    fn converted_file(
        round: u32,
        side: &str,
        steam_id: u64,
        player_name: &str,
    ) -> crate::model::ConvertedFile {
        crate::model::ConvertedFile {
            path: format!("round{round:02}/{side}/{steam_id}_{player_name}.dtr"),
            round,
            side: side.to_string(),
            steam_id,
            player_name: player_name.to_string(),
            ticks: 64,
            subticks: 0,
            play_start_tick_index: 0,
            first_weapon_def_index: 0,
            preload_weapon_def_indices: Vec::new(),
            hifi_event_count: 0,
            inventory_snapshot_count: 0,
            loadout: crate::model::ReplayLoadout::default(),
            cosmetics: None,
            view: None,
            scoreboard: None,
        }
    }
}
