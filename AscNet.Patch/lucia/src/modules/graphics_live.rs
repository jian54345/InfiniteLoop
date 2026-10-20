//! State shared between the graphics worker (IL2CPP side, `graphics.rs`/`graphics_tweaks.rs`) and the in-game
//! overlay (render thread, `graphics_overlay.rs`). The overlay never touches IL2CPP: it edits `desired` and reads
//! `catalogue`/`status`; the worker applies `desired` every tick and republishes the rest.
use std::sync::{LazyLock, Mutex};

use super::graphics_params::Override;

/// What the player wants; the same data as `ascnet-graphics.ini`. Override values stay text in the ini syntax
/// (`on`/`off`, numbers, enum names, comma lists); the worker types them against the live field.
#[derive(Clone, Debug, Default, PartialEq)]
pub(crate) struct Desired {
    pub render_scale: Option<f32>,
    pub overrides: Vec<Override>,
    pub dump: bool,
    pub overlay: bool,
    /// `fps = <n>`: `Application.targetFrameRate = n` (and `vSyncCount = 0`) while set; `None` = the game's own.
    pub fps: Option<i32>,
    /// `nofade = on`: camera/character proximity fade disabled while true.
    pub nofade: bool,
}

#[derive(Clone, Debug, PartialEq)]
pub(crate) enum FieldKind {
    Bool,
    Int,
    Float,
    /// Constant name and value, in declaration order.
    Enum(Vec<(String, i32)>),
    Ints,
    Floats,
    /// Shown, not editable (structs, objects, other types).
    ReadOnly,
}

#[derive(Clone, Debug, PartialEq)]
pub(crate) struct FieldInfo {
    pub name: String,
    pub kind: FieldKind,
    /// Current live value, formatted in ini syntax (enum = constant name, arrays = comma list).
    pub value: String,
    /// The game's own value while this field is overridden (what removing the override restores).
    pub game_value: Option<String>,
}

#[derive(Clone, Debug, PartialEq)]
pub(crate) struct SettingInfo {
    /// Field name on `RenderPipelineQualityRuntimeData`, e.g. `ShadowSetting` (the `<Setting>` in an override key).
    pub name: String,
    pub fields: Vec<FieldInfo>,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub(crate) struct Status {
    pub scene: String,
    /// Last scale the game itself asked for through `SetupExtraRenderScale`.
    pub game_render_scale: Option<f32>,
    /// `GetRenderScale()` as the pipeline reports it.
    pub effective_render_scale: Option<f32>,
    /// Latest problem per override key (`Setting.Field`), `renderscale`, `fps` or `nofade`; cleared when the key
    /// applies cleanly.
    pub errors: Vec<(String, String)>,
    /// `Application.targetFrameRate` and `QualitySettings.vSyncCount` as last read (None until read).
    pub target_frame_rate: Option<i32>,
    pub vsync_count: Option<i32>,
}

#[derive(Debug, Default)]
pub(crate) struct Live {
    pub desired: Desired,
    /// Empty until IL2CPP and `GetRuntimeData()` are ready; refreshed by the worker every tick.
    pub catalogue: Vec<SettingInfo>,
    pub status: Status,
}

pub(crate) static LIVE: LazyLock<Mutex<Live>> = LazyLock::new(|| Mutex::new(Live::default()));
