/// Graphics options live in `ascnet-graphics.ini` beside PGR.exe, so they work however the game is started
/// (launcher, CrossOver shortcut, script). One `key = value` per line; `#` or `;` starts a comment.
/// Absent file = feature off. Every malformed line or unknown key is reported, never silently dropped.
///
/// Keys:
/// - `dump = 1`: log every render-pipeline / quality value, then each change.
/// - `fps = 240`: unlock the frame rate (`Application.targetFrameRate`, vsync off while set); a positive whole number.
/// - `nofade = on`: disable the camera / character proximity fade.
/// - `ultrawide = on`: run fullscreen at the monitor's own resolution (21:9, 32:9, ...) instead of the game's
///   2560x2048 cap (see `graphics_lua.rs`); off restores the game's own window fallback.
/// - `renderscale = 2.0`: internal render scale in every scene (0.25-4.0).
/// - `<Setting>.<Field> = value`: override one field of a render-pipeline quality setting object, e.g.
///   `ShadowSetting.CascadeSize = 4096`. Names are the game's own (case-sensitive) and are checked in-game;
///   the value is typed there too (bool, number, enum name or number, comma list for arrays).
pub(crate) const CONFIG_FILE: &str = "ascnet-graphics.ini";
pub(crate) const RENDER_SCALE_RANGE: (f32, f32) = (0.25, 4.0);

use super::graphics_live::Desired;

#[derive(Clone, Debug, PartialEq)]
pub(crate) struct Override {
    pub setting: String,
    pub field: String,
    pub value: String,
}

#[derive(Debug, Default, PartialEq)]
pub(crate) struct Config {
    pub dump: bool,
    pub overlay: bool,
    pub fps: Option<i32>,
    pub nofade: bool,
    pub ultrawide: bool,
    pub render_scale: Option<f32>,
    /// In file order; a repeated key keeps its first position and takes the last value.
    pub overrides: Vec<Override>,
    pub problems: Vec<String>,
}

impl From<Config> for Desired {
    fn from(config: Config) -> Self {
        Desired { render_scale: config.render_scale, overrides: config.overrides, dump: config.dump, overlay: config.overlay, fps: config.fps, nofade: config.nofade, ultrawide: config.ultrawide }
    }
}

/// The ini text for `desired`; `Config::parse` reads it back to the same `Desired`.
pub(crate) fn render(desired: &Desired) -> String {
    let mut text = String::from("# AscNet graphics options (written by the in-game overlay)\n");
    if let Some(scale) = desired.render_scale {
        text.push_str(&format!("renderscale = {scale}\n"));
    }
    for (key, on) in [("dump", desired.dump), ("overlay", desired.overlay), ("ultrawide", desired.ultrawide)] {
        if on {
            text.push_str(&format!("{key} = on\n"));
        }
    }
    if let Some(fps) = desired.fps {
        text.push_str(&format!("fps = {fps}\n"));
    }
    if desired.nofade {
        text.push_str("nofade = on\n");
    }
    for o in &desired.overrides {
        text.push_str(&format!("{}.{} = {}\n", o.setting, o.field, o.value));
    }
    text
}

impl Config {
    pub(crate) fn parse(text: &str) -> Self {
        let mut config = Config::default();
        for (index, raw) in text.lines().enumerate() {
            let line = raw.split(['#', ';']).next().unwrap_or("").trim();
            if line.is_empty() {
                continue;
            }
            let number = index + 1;
            let Some((key, value)) = line.split_once('=') else {
                config.problems.push(format!("line {number}: expected `key = value`, got `{line}`"));
                continue;
            };
            let (key, value) = (key.trim(), value.trim());
            match key.to_ascii_lowercase().as_str() {
                "dump" => match flag(value) {
                    Some(on) => config.dump = on,
                    None => config.problems.push(format!("line {number}: `dump` must be 1/0, true/false or on/off, got `{value}`")),
                },
                "overlay" => match flag(value) {
                    Some(on) => config.overlay = on,
                    None => config.problems.push(format!("line {number}: `overlay` must be 1/0, true/false or on/off, got `{value}`")),
                },
                "fps" => match fps(value) {
                    Some(fps) => config.fps = Some(fps),
                    None => config.problems.push(format!("line {number}: `fps` must be a positive whole number, got `{value}`")),
                },
                "nofade" => match flag(value) {
                    Some(on) => config.nofade = on,
                    None => config.problems.push(format!("line {number}: `nofade` must be 1/0, true/false or on/off, got `{value}`")),
                },
                "ultrawide" => match flag(value) {
                    Some(on) => config.ultrawide = on,
                    None => config.problems.push(format!("line {number}: `ultrawide` must be 1/0, true/false or on/off, got `{value}`")),
                },
                "renderscale" => match value.parse::<f32>().ok().filter(|s| (RENDER_SCALE_RANGE.0..=RENDER_SCALE_RANGE.1).contains(s)) {
                    Some(scale) => config.render_scale = Some(scale),
                    None => config.problems.push(format!(
                        "line {number}: `renderscale` must be a number from {} to {}, got `{value}`", RENDER_SCALE_RANGE.0, RENDER_SCALE_RANGE.1)),
                },
                _ => match key.split_once('.') {
                    Some((setting, field)) if identifier(setting) && identifier(field) && !value.is_empty() => {
                        match config.overrides.iter_mut().find(|o| o.setting == setting && o.field == field) {
                            Some(existing) => existing.value = value.to_string(),
                            None => config.overrides.push(Override { setting: setting.into(), field: field.into(), value: value.into() }),
                        }
                    }
                    Some(_) if value.is_empty() => config.problems.push(format!("line {number}: `{key}` has no value")),
                    _ => config.problems.push(format!("line {number}: unknown key `{key}` (expected dump, overlay, renderscale, fps, nofade, ultrawide or Setting.Field)")),
                },
            }
        }
        config
    }

    pub(crate) fn active(&self) -> bool {
        self.dump || self.overlay || self.nofade || self.ultrawide || self.fps.is_some() || self.render_scale.is_some() || !self.overrides.is_empty()
    }
}

/// A plain positive decimal integer; signs, spaces, `0`, `-1` ("platform default") and overflow are rejected.
fn fps(value: &str) -> Option<i32> {
    if !value.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    value.parse().ok().filter(|&fps| fps > 0)
}

fn flag(value: &str) -> Option<bool> {
    match value.to_ascii_lowercase().as_str() {
        "1" | "true" | "on" => Some(true),
        "0" | "false" | "off" => Some(false),
        _ => None,
    }
}

fn identifier(name: &str) -> bool {
    !name.is_empty() && !name.starts_with(|c: char| c.is_ascii_digit()) && name.chars().all(|c| c.is_ascii_alphanumeric() || c == '_')
}

/// A value from the file, typed by the in-game field it targets. Enum names are resolved in-game, so they stay text.
#[derive(Debug, PartialEq)]
pub(crate) enum Typed {
    Bool(bool),
    Int(i32),
    Float(f32),
    Ints(Vec<i32>),
    Floats(Vec<f32>),
}

pub(crate) fn bool_value(value: &str) -> Option<Typed> {
    flag(value).map(Typed::Bool)
}

pub(crate) fn int_value(value: &str) -> Option<Typed> {
    value.parse().ok().map(Typed::Int)
}

pub(crate) fn float_value(value: &str) -> Option<Typed> {
    value.parse::<f32>().ok().filter(|v| v.is_finite()).map(Typed::Float)
}

pub(crate) fn ints_value(value: &str) -> Option<Typed> {
    value.split(',').map(|v| v.trim().parse().ok()).collect::<Option<Vec<i32>>>().map(Typed::Ints)
}

pub(crate) fn floats_value(value: &str) -> Option<Typed> {
    value.split(',').map(|v| v.trim().parse::<f32>().ok().filter(|v| v.is_finite())).collect::<Option<Vec<f32>>>().map(Typed::Floats)
}

/// `*Downsample*` fields are render-target divisors (1 = full resolution). Verified for SSAO: its pass does
/// `width / SSAOSetting.Downsample` (`idiv`), so 0 crashed the game entering Babylonia. Other downsample fields are
/// held to the same rule.
pub(crate) fn check_divisor(field: &str, value: &Typed) -> Result<(), String> {
    match value {
        Typed::Int(n) if *n < 1 && field.to_ascii_lowercase().contains("downsample") => {
            Err(format!("`{field}` is a divisor (1 = full resolution, 2 = half); {n} would crash the renderer"))
        }
        _ => Ok(()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dump_flag_and_comments() {
        let config = Config::parse("# graphics\n\n  DUMP = On ; diagnostics\n");
        assert!(config.dump && config.problems.is_empty());
        assert!(!Config::parse("dump=0").dump);
        assert!(!Config::parse("").active());
    }

    #[test]
    fn later_lines_override_earlier_ones() {
        let config = Config::parse("dump = 1\ndump = off\nShadowSetting.CascadeSize = 2048\nrenderscale = 1.5\nShadowSetting.CascadeSize = 4096\nrenderscale=2");
        assert!(!config.dump);
        assert_eq!(config.render_scale, Some(2.0));
        assert_eq!(config.overrides, [Override { setting: "ShadowSetting".into(), field: "CascadeSize".into(), value: "4096".into() }]);
    }

    #[test]
    fn render_scale_is_bounded() {
        assert_eq!(Config::parse("RenderScale = 0.25").render_scale, Some(0.25));
        assert_eq!(Config::parse("renderscale = 4").render_scale, Some(4.0));
        for bad in ["0.2", "4.01", "NaN", "inf", "2x", ""] {
            let config = Config::parse(&format!("renderscale = {bad}"));
            assert_eq!(config.render_scale, None, "{bad}");
            assert_eq!(config.problems.len(), 1, "{bad}");
        }
    }

    #[test]
    fn overrides_keep_order_case_and_raw_value() {
        let config = Config::parse("SSAOSetting.Enable = on\nAntiAliasingSetting.ForwardAAMethod = SubpixelMorphologicalAntiAliasing\nShadowSetting.CascadeFrameUpdate = 0, 1, 2, 4");
        assert!(config.active() && config.problems.is_empty());
        let keys: Vec<_> = config.overrides.iter().map(|o| (o.setting.as_str(), o.field.as_str(), o.value.as_str())).collect();
        assert_eq!(keys, [
            ("SSAOSetting", "Enable", "on"),
            ("AntiAliasingSetting", "ForwardAAMethod", "SubpixelMorphologicalAntiAliasing"),
            ("ShadowSetting", "CascadeFrameUpdate", "0, 1, 2, 4"),
        ]);
    }

    #[test]
    fn problems_are_reported_with_line_numbers() {
        let config = Config::parse("dump = yes\nrenderscale 2\nshadows = 4\nA.B.C = 1\nShadowSetting.CascadeSize =\n9a.b = 1");
        assert!(!config.active());
        assert_eq!(config.problems, [
            "line 1: `dump` must be 1/0, true/false or on/off, got `yes`",
            "line 2: expected `key = value`, got `renderscale 2`",
            "line 3: unknown key `shadows` (expected dump, overlay, renderscale, fps, nofade, ultrawide or Setting.Field)",
            "line 4: unknown key `A.B.C` (expected dump, overlay, renderscale, fps, nofade, ultrawide or Setting.Field)",
            "line 5: `ShadowSetting.CascadeSize` has no value",
            "line 6: unknown key `9a.b` (expected dump, overlay, renderscale, fps, nofade, ultrawide or Setting.Field)",
        ]);
    }

    #[test]
    fn downsample_fields_reject_divisors_below_one() {
        assert!(check_divisor("Downsample", &Typed::Int(0)).is_err());
        assert!(check_divisor("DownsampleFactor", &Typed::Int(-1)).is_err());
        assert!(check_divisor("ScreenSpaceShadowDownSample", &Typed::Int(0)).is_err());
        assert!(check_divisor("Downsample", &Typed::Int(1)).is_ok());
        assert!(check_divisor("CascadeSize", &Typed::Int(0)).is_ok());
    }

    #[test]
    fn typed_values() {
        assert_eq!(bool_value("TRUE"), Some(Typed::Bool(true)));
        assert_eq!(bool_value("2"), None);
        assert_eq!(int_value("-3"), Some(Typed::Int(-3)));
        assert_eq!(int_value("1.5"), None);
        assert_eq!(float_value("1e2"), Some(Typed::Float(100.0)));
        assert_eq!(float_value("inf"), None);
        assert_eq!(ints_value("0, 1,2 ,4"), Some(Typed::Ints(vec![0, 1, 2, 4])));
        assert_eq!(ints_value("0,,1"), None);
        assert_eq!(floats_value("0.15,0.4"), Some(Typed::Floats(vec![0.15, 0.4])));
    }

    #[test]
    fn overlay_flag() {
        let config = Config::parse("overlay = on");
        assert!(config.overlay && config.active() && config.problems.is_empty());
        assert!(!Config::parse("overlay = 0").overlay);
        assert_eq!(Config::parse("overlay = maybe").problems, ["line 1: `overlay` must be 1/0, true/false or on/off, got `maybe`"]);
    }

    #[test]
    fn fps_accepts_plain_positive_integers() {
        for (text, want) in [("240", 240), ("1", 1), ("2147483647", i32::MAX)] {
            let config = Config::parse(&format!("FPS = {text}"));
            assert_eq!(config.fps, Some(want), "{text}");
            assert!(config.active() && config.problems.is_empty());
        }
    }

    #[test]
    fn fps_rejects_everything_else() {
        for bad in ["", "0", "000", "-1", "+60", "6.0", "abc", "2147483648"] {
            let config = Config::parse(&format!("fps = {bad}"));
            assert_eq!(config.fps, None, "{bad:?}");
            assert!(!config.active());
            assert_eq!(config.problems, [format!("line 1: `fps` must be a positive whole number, got `{bad}`")], "{bad:?}");
        }
    }

    #[test]
    fn ultrawide_flag() {
        let config = Config::parse("Ultrawide = on");
        assert!(config.ultrawide && config.active() && config.problems.is_empty());
        assert!(!Config::parse("ultrawide = off").ultrawide);
        assert_eq!(Config::parse("ultrawide = wide").problems, ["line 1: `ultrawide` must be 1/0, true/false or on/off, got `wide`"]);
    }

    #[test]
    fn nofade_flag() {
        let config = Config::parse("nofade = On");
        assert!(config.nofade && config.active() && config.problems.is_empty());
        assert!(!Config::parse("nofade = 0").nofade);
        assert!(!Config::parse("").nofade);
        assert_eq!(Config::parse("nofade = maybe").problems, ["line 1: `nofade` must be 1/0, true/false or on/off, got `maybe`"]);
    }

    #[test]
    fn render_round_trips() {
        let o = |setting: &str, field: &str, value: &str| Override { setting: setting.into(), field: field.into(), value: value.into() };
        let desired = Desired {
            render_scale: Some(1.333),
            overrides: vec![
                o("ShadowSetting", "CascadeFrameUpdate", "0,1,2,4"),
                o("AntiAliasingSetting", "ForwardAAMethod", "SubpixelMorphologicalAntiAliasing"),
                o("SSAOSetting", "Enable", "off"),
            ],
            dump: true,
            overlay: true,
            fps: Some(240),
            nofade: true,
            ultrawide: true,
        };
        let config = Config::parse(&render(&desired));
        assert!(config.problems.is_empty());
        assert_eq!(Desired::from(config), desired);
        assert_eq!(Desired::from(Config::parse(&render(&Desired::default()))), Desired::default());
    }
}
