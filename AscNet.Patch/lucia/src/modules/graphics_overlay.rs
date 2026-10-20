//! In-game Dear ImGui overlay (hudhook, D3D11 Present hook) over the shared `LIVE` state. It only edits
//! `desired` and reads `catalogue`/`status`; applying is the graphics worker's job. Ctrl+Shift+G toggles.
use std::collections::HashMap;

use hudhook::hooks::dx11::ImguiDx11Hooks;
use hudhook::imgui::{Condition, Io, Key, TreeNodeFlags, Ui};
use hudhook::windows::Win32::Foundation::HINSTANCE;
use hudhook::{Hudhook, ImguiRenderLoop, MessageFilter};

use super::graphics_live::{Desired, FieldInfo, FieldKind, Live, Status, LIVE};
use super::graphics_params::{render, Override, CONFIG_FILE, RENDER_SCALE_RANGE};
use super::il2cpp;

const RED: [f32; 4] = [1.0, 0.35, 0.35, 1.0];
const AMBER: [f32; 4] = [1.0, 0.8, 0.3, 1.0];

pub(super) fn start() {
    let mut builder = Hudhook::builder().with::<ImguiDx11Hooks>(Overlay::default());
    if let Some(module) = crate::LUCIA_MODULE.get() {
        builder = builder.with_hmodule(HINSTANCE(*module as *mut _));
    }
    match builder.build().apply() {
        Ok(()) => il2cpp::ok("overlay", "D3D11 Present hook installed; Ctrl+Shift+G toggles"),
        Err(error) => il2cpp::failed("overlay", &format!("{error:?}"), "the game runs without the overlay"),
    }
}

#[derive(Default)]
struct Overlay {
    visible: bool,
    /// Text being edited for array fields, keyed `Setting.Field`; committed on Enter.
    buffers: HashMap<String, String>,
    save_result: String,
    /// Last FPS value before the unlock was switched off (re-enabling restores it; 0 = never set, uses 240).
    fps_last: i32,
    /// The last typed FPS value was below 1 and was not applied.
    fps_invalid: bool,
}

impl ImguiRenderLoop for Overlay {
    fn message_filter(&self, _io: &Io) -> MessageFilter {
        if self.visible { MessageFilter::InputAll } else { MessageFilter::empty() }
    }

    fn render(&mut self, ui: &mut Ui) {
        let io = ui.io();
        if io.key_ctrl && io.key_shift && ui.is_key_pressed_no_repeat(Key::G) {
            self.visible = !self.visible;
        }
        if !self.visible {
            return;
        }
        let mut save = None;
        let mut open = true;
        ui.window("AscNet graphics")
            .size([520.0, 640.0], Condition::FirstUseEver)
            .opened(&mut open)
            .build(|| {
                let mut live = LIVE.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                save = self.draw(ui, &mut live);
            });
        self.visible = open;
        if let Some(text) = save {
            self.save_result = save_ini(&text);
        }
    }
}

impl Overlay {
    /// Returns the ini text when "Save" was pressed; the file write happens outside the `LIVE` lock.
    fn draw(&mut self, ui: &Ui, live: &mut Live) -> Option<String> {
        let Live { desired, catalogue, status } = live;
        if catalogue.is_empty() {
            ui.text("waiting for the game…");
            return None;
        }
        let scale = |v: Option<f32>| v.map_or("-".to_owned(), |v| format!("{v:.2}"));
        ui.text(format!("scene: {}", status.scene));
        ui.text(format!(
            "render scale: game asked {}, effective {}",
            scale(status.game_render_scale),
            scale(status.effective_render_scale)
        ));
        draw_render_scale(ui, desired, status.game_render_scale.or(status.effective_render_scale));
        if let Some((_, message)) = status.errors.iter().find(|(key, _)| key == "renderscale") {
            ui.text_colored(RED, message);
        }
        self.draw_extras(ui, desired, status);
        ui.separator();
        let mut save = None;
        if ui.button("Save to ascnet-graphics.ini") {
            save = Some(render(desired));
        }
        ui.same_line();
        if ui.button("Reset all") {
            desired.overrides.clear();
            desired.render_scale = None;
            self.buffers.clear();
            desired.fps = None;
            desired.nofade = false;
        }
        if !self.save_result.is_empty() {
            ui.text_wrapped(&self.save_result);
        }
        ui.separator();
        for setting in catalogue.iter() {
            if !ui.collapsing_header(&setting.name, TreeNodeFlags::empty()) {
                continue;
            }
            let _setting_id = ui.push_id(&setting.name);
            for field in &setting.fields {
                let key = format!("{}.{}", setting.name, field.name);
                let _field_id = ui.push_id(&field.name);
                if self.draw_field(ui, desired, &setting.name, field, &key) {
                    self.buffers.remove(&key);
                }
                if let Some((_, message)) = status.errors.iter().find(|(k, _)| *k == key) {
                    ui.text_colored(RED, message);
                }
            }
        }
        save
    }

    /// Returns true when the override was removed (the caller drops its text buffer).
    fn draw_field(&mut self, ui: &Ui, desired: &mut Desired, setting: &str, field: &FieldInfo, key: &str) -> bool {
        let index = desired.overrides.iter().position(|o| o.setting == setting && o.field == field.name);
        let current = index.map_or(field.value.as_str(), |i| desired.overrides[i].value.as_str());
        let mut edited = None;
        match &field.kind {
            FieldKind::Bool => {
                let mut on = current == "on";
                if ui.checkbox(&field.name, &mut on) {
                    edited = Some(if on { "on" } else { "off" }.to_owned());
                }
            }
            FieldKind::Int => {
                let mut v = current.parse::<i32>().unwrap_or_default();
                if ui.input_int(&field.name, &mut v).build() {
                    edited = Some(v.to_string());
                }
            }
            FieldKind::Float => {
                let mut v = current.parse::<f32>().unwrap_or_default();
                if ui.input_float(&field.name, &mut v).build() {
                    edited = Some(v.to_string());
                }
            }
            FieldKind::Enum(items) => {
                let names: Vec<&str> = items.iter().map(|(name, _)| name.as_str()).collect();
                let mut selected = names.iter().position(|name| *name == current).unwrap_or(0);
                if ui.combo_simple_string(&field.name, &mut selected, &names) {
                    edited = Some(names[selected].to_owned());
                }
            }
            FieldKind::Ints | FieldKind::Floats => {
                let buffer = self.buffers.entry(key.to_owned()).or_insert_with(|| current.to_owned());
                if ui.input_text(&field.name, buffer).enter_returns_true(true).build() {
                    edited = Some(buffer.trim().to_owned());
                }
            }
            FieldKind::ReadOnly => ui.text(format!("{}: {}", field.name, current)),
        }
        if let Some(value) = edited {
            match index {
                Some(i) => desired.overrides[i].value = value,
                None => desired.overrides.push(Override { setting: setting.to_owned(), field: field.name.clone(), value }),
            }
        }
        let Some(i) = index.or_else(|| desired.overrides.iter().position(|o| o.setting == setting && o.field == field.name)) else {
            return false;
        };
        ui.same_line();
        ui.text_colored(AMBER, format!("game: {}", field.game_value.as_deref().unwrap_or("?")));
        ui.same_line();
        if ui.button("Reset") {
            desired.overrides.remove(i);
            return true;
        }
        false
    }

    /// Frame rate unlock and camera fade rows.
    fn draw_extras(&mut self, ui: &Ui, desired: &mut Desired, status: &Status) {
        let mut on = desired.fps.is_some();
        if ui.checkbox("Unlock FPS", &mut on) {
            if on {
                desired.fps = Some(if self.fps_last > 0 { self.fps_last } else { 240 });
            } else {
                self.fps_last = desired.fps.take().unwrap_or(0);
            }
            self.fps_invalid = false;
        }
        if let Some(fps) = desired.fps {
            let mut value = fps;
            ui.same_line();
            if ui.input_int("target FPS", &mut value).build() {
                self.fps_invalid = value < 1;
                if !self.fps_invalid {
                    desired.fps = Some(value);
                }
            }
        }
        let show = |v: Option<i32>| v.map_or("-".to_owned(), |v| v.to_string());
        ui.text(format!("frame rate: target {}, vsync {}", show(status.target_frame_rate), show(status.vsync_count)));
        if self.fps_invalid {
            ui.text_colored(RED, "target FPS must be at least 1");
        }
        if let Some((_, message)) = status.errors.iter().find(|(key, _)| key == "fps") {
            ui.text_colored(RED, message);
        }
        ui.checkbox("No camera fade", &mut desired.nofade);
        if let Some((_, message)) = status.errors.iter().find(|(key, _)| key == "nofade") {
            ui.text_colored(RED, message);
        }
    }
}

fn draw_render_scale(ui: &Ui, desired: &mut Desired, fallback: Option<f32>) {
    let mut on = desired.render_scale.is_some();
    if ui.checkbox("Override render scale", &mut on) {
        desired.render_scale = on.then(|| fallback.unwrap_or(1.0));
    }
    let Some(scale) = desired.render_scale.as_mut() else {
        return;
    };
    ui.slider("Render scale", RENDER_SCALE_RANGE.0, RENDER_SCALE_RANGE.1, scale);
    for preset in [1.0_f32, 1.5, 2.0, 2.5] {
        ui.same_line();
        if ui.button(format!("{preset:.1}")) {
            *scale = preset;
        }
    }
}

fn save_ini(text: &str) -> String {
    let path = std::env::current_exe().ok().and_then(|exe| exe.parent().map(|dir| dir.join(CONFIG_FILE)));
    let Some(path) = path else {
        let reason = "cannot locate PGR.exe's folder";
        il2cpp::failed("overlay save", reason, "the settings stay live but are not saved");
        return format!("save failed: {reason}");
    };
    match std::fs::write(&path, text) {
        Ok(()) => {
            il2cpp::ok("overlay save", &path.display().to_string());
            format!("saved {}", path.display())
        }
        Err(error) => {
            il2cpp::failed("overlay save", &format!("{}: {error}", path.display()), "the settings stay live but are not saved");
            format!("save failed: {error}")
        }
    }
}
