//! The two launcher-owned keys (`fps`, `nofade`) of `ascnet-graphics.ini` beside PGR.exe.
//! lucia owns the rest of the file; every other line is preserved verbatim.
use anyhow::{Context, Result};
use std::{fs, io, path::Path};

pub const FILE: &str = "ascnet-graphics.ini";

#[derive(Debug, Default, PartialEq, Eq)]
pub struct Options {
    pub fps: Option<i32>,
    pub nofade: bool,
}

/// `(key, value)` of a setting line; same comment/case rules as lucia's parser.
fn setting(line: &str) -> Option<(String, &str)> {
    let (key, value) = line.split(['#', ';']).next()?.split_once('=')?;
    Some((key.trim().to_ascii_lowercase(), value.trim()))
}

fn flag(value: &str) -> Option<bool> {
    match value.to_ascii_lowercase().as_str() {
        "1" | "true" | "on" => Some(true),
        "0" | "false" | "off" => Some(false),
        _ => None,
    }
}

/// Last valid occurrence wins; invalid values are ignored (lucia reports them).
pub fn parse(text: &str) -> Options {
    let mut options = Options::default();
    for (key, value) in text.lines().filter_map(setting) {
        match key.as_str() {
            "fps" if !value.is_empty() && value.bytes().all(|b| b.is_ascii_digit()) => {
                options.fps = value.parse().ok().filter(|n| *n > 0).or(options.fps)
            }
            "nofade" => options.nofade = flag(value).unwrap_or(options.nofade),
            _ => {}
        }
    }
    options
}

/// Sets (`Some`) or removes (`None`) `key`: the last occurrence is replaced in place,
/// earlier duplicates are dropped, a missing key is appended.
fn edit(text: &str, key: &str, value: Option<&str>) -> String {
    let lines: Vec<&str> = text.lines().collect();
    let is_key = |l: &&str| setting(l).is_some_and(|(k, _)| k == key);
    let last = lines.iter().rposition(|l| is_key(l));
    let mut out = String::with_capacity(text.len() + 16);
    for (i, line) in lines.iter().enumerate() {
        if Some(i) == last {
            if let Some(value) = value {
                out.push_str(&format!("{key} = {value}\n"));
            }
        } else if !is_key(line) {
            out.push_str(line);
            out.push('\n');
        }
    }
    if last.is_none() {
        if let Some(value) = value {
            out.push_str(&format!("{key} = {value}\n"));
        }
    }
    out
}

fn has_key(text: &str, key: &str) -> bool {
    text.lines().filter_map(setting).any(|(k, _)| k == key)
}

fn read_text(client: &Path) -> Result<String> {
    match fs::read_to_string(client.join(FILE)) {
        Ok(text) => Ok(text),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(String::new()),
        Err(e) => Err(e).with_context(|| format!("read {FILE}")),
    }
}

fn write_text(client: &Path, text: &str) -> Result<()> {
    let path = client.join(FILE);
    let temp = client.join(format!("{FILE}.tmp"));
    fs::write(&temp, text).with_context(|| format!("write {FILE}"))?;
    fs::rename(&temp, &path).with_context(|| format!("replace {FILE}"))
}

pub fn read(client: &Path) -> Result<Options> {
    Ok(parse(&read_text(client)?))
}

/// `Some(n)` writes `fps = n`; `None` removes the line (the game keeps its own frame rate).
pub fn set_fps(client: &Path, fps: Option<i32>) -> Result<()> {
    write_text(client, &edit(&read_text(client)?, "fps", fps.map(|n| n.to_string()).as_deref()))
}

/// On writes `nofade = on`; off removes the line (absent means off).
pub fn set_nofade(client: &Path, on: bool) -> Result<()> {
    write_text(client, &edit(&read_text(client)?, "nofade", on.then_some("on")))
}

/// Writes legacy launcher-JSON values only for keys the ini does not have yet.
/// Returns the keys written (for the launcher log).
pub fn migrate(client: &Path, fps: Option<i32>, nofade: bool) -> Result<Vec<&'static str>> {
    let mut text = read_text(client)?;
    let mut written = Vec::new();
    if let Some(fps) = fps.filter(|_| !has_key(&text, "fps")) {
        text = edit(&text, "fps", Some(&fps.to_string()));
        written.push("fps");
    }
    if nofade && !has_key(&text, "nofade") {
        text = edit(&text, "nofade", Some("on"));
        written.push("nofade");
    }
    if !written.is_empty() {
        write_text(client, &text)?;
    }
    Ok(written)
}

#[cfg(test)]
mod tests {
    use super::*;

    const SAMPLE: &str = "# header\nrenderscale = 1.5\nfps = 60 # old\n\n; note\nOverlay = on\nfps = 120\nGfx.Field = 3\n";

    #[test]
    fn last_occurrence_wins_when_reading() {
        assert_eq!(parse(SAMPLE), Options { fps: Some(120), nofade: false });
        assert_eq!(parse("nofade = on\nnofade = off\n").nofade, false);
        assert_eq!(parse("fps = 60\nfps = abc\nfps = -5\n").fps, Some(60));
        assert_eq!(parse("NoFade = TRUE").nofade, true);
    }

    #[test]
    fn edit_replaces_last_drops_duplicates_keeps_the_rest() {
        let out = edit(SAMPLE, "fps", Some("144"));
        assert_eq!(out, "# header\nrenderscale = 1.5\n\n; note\nOverlay = on\nfps = 144\nGfx.Field = 3\n");
    }

    #[test]
    fn edit_appends_and_removes() {
        assert_eq!(edit("# c\nfps = 60\n", "nofade", Some("on")), "# c\nfps = 60\nnofade = on\n");
        assert_eq!(edit("", "fps", Some("60")), "fps = 60\n");
        assert_eq!(edit(SAMPLE, "fps", None), "# header\nrenderscale = 1.5\n\n; note\nOverlay = on\nGfx.Field = 3\n");
        assert_eq!(edit("a = 1\n", "fps", None), "a = 1\n");
    }

    #[test]
    fn files_round_trip_and_migration_only_fills_absent_keys() {
        let dir = std::env::temp_dir().join(format!("ascnet-ini-test-{}", std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        let _ = fs::remove_file(dir.join(FILE));
        assert_eq!(read(&dir).unwrap(), Options::default());

        assert_eq!(migrate(&dir, Some(90), true).unwrap(), ["fps", "nofade"]);
        assert_eq!(read(&dir).unwrap(), Options { fps: Some(90), nofade: true });
        // Present keys are never overwritten.
        assert!(migrate(&dir, Some(30), true).unwrap().is_empty());
        assert_eq!(read(&dir).unwrap().fps, Some(90));

        fs::write(dir.join(FILE), "# keep\nnofade = off\n").unwrap();
        assert_eq!(migrate(&dir, Some(75), true).unwrap(), ["fps"]);
        assert_eq!(fs::read_to_string(dir.join(FILE)).unwrap(), "# keep\nnofade = off\nfps = 75\n");

        set_fps(&dir, None).unwrap();
        set_nofade(&dir, true).unwrap();
        assert_eq!(fs::read_to_string(dir.join(FILE)).unwrap(), "# keep\nnofade = on\n");
        set_nofade(&dir, false).unwrap();
        assert_eq!(fs::read_to_string(dir.join(FILE)).unwrap(), "# keep\n");
        fs::remove_dir_all(dir).unwrap();
    }
}
