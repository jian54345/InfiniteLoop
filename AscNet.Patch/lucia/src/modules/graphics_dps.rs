//! Read-only normal-fight recorded damage, attributed by the game's result accumulator.
//! Clock: XFight.NonPausedTime (dump.cs:23230; XBossInshotModel.lua:184).
//! This is the shipped non-paused fight clock, not wall time; skill time-stop semantics
//! are engine-defined. Peak5 is a sampled trailing five-second average, never a hit peak.
//! Result totals remain authoritative; native hits only supply counts and damage-id sums.
//! Hits wait for the next runtime-id poll join and are fenced at epoch/reset boundaries.
//! On-field time assigns each non-paused clock delta to the previous controlled character.
//! Double crits also count as crits; damage-id rankings use recorded hit damage, not counts.
use std::collections::{BTreeMap, VecDeque};
use super::graphics_lua::LuaBridge;
use super::graphics_dps_hits::{self, Hit};

const CHUNK: &str = "lucia_dps";
const POLL: &str = r#"
local s = rawget(_G, '__lucia_dps')
if not s then s = {epoch=0, names={}}; rawset(_G, '__lucia_dps', s) end
local fight = CS.XFight.Instance
local result = fight and fight.Result
local data = result and result.Data
local state = 0
if fight then
    for i, value in ipairs({CS.XFightState.Load, CS.XFightState.QuickRun,
        CS.XFightState.Fight, CS.XFightState.Wait, CS.XFightState.Exit, CS.XFightState.Restart}) do
        if fight.State == value then state = i; break end
    end
end
if fight ~= s.fight or result ~= s.result or data ~= s.data or
    ((state == 1 or state == 6) and state ~= s.state) then
    s.epoch = s.epoch + 1
    s.fight, s.result, s.data = fight, result, data
end
s.state = state
local status = 'retained'
if state == 1 or state == 6 then status = 'loading'
elseif state == 3 and data and not fight.IsSettled then status = 'active' end
if status ~= 'active' and CS.StatusSyncFight.XFightClient.FightInstance then
    status = 'unsupported DLC'
end
local clock = fight and fight.NonPausedTime or 0
local role = fight and fight:GetClientRole()
local controlled = role and role.Npc and role.Npc.Id or 0
local out = {table.concat({'DPS2', s.epoch, status, string.format('%.6f', clock), controlled}, '\t')}
if (state == 3 or state == 4 or state == 5) and data then
    if not role then return table.concat({'DPS2', s.epoch, 'waiting local role', clock, 0}, '\t') end
    if data.NpcDpsTable then
        XTool.LoopMap(data.NpcDpsTable, function(key, v)
            if v.RoleId == role.Id and v.CharacterId > 0 then
                local id = tonumber(v.CharacterId)
                local name = s.names[id]
                if not name then
                    -- Verified accessor: XCharacterAgency.lua:3027, not NPC template names.
                    name = XMVCA.XCharacter:GetCharacterName(id)
                    if not name or name == '' then name = tostring(id) end
                    name = name:gsub('.', function(c) return string.format('%02x', string.byte(c)) end)
                    s.names[id] = name
                end
                local elemental = 0
                if v.DamageMagic then
                    for i = 0, v.DamageMagic.Length - 1 do elemental = elemental + v.DamageMagic[i] end
                end
                out[#out+1] = table.concat({id, name, tostring(v.DamageTotal),
                    tostring(v.DamageNormal), tostring(elemental), tostring(v.Cure),
                    tostring(v.Hurt), tostring(v.BreakEndure), tostring(key)}, '\t')
            end
        end)
    end
end
return table.concat(out, '\n')
"#;

#[derive(Clone, Debug, Default)]
pub(super) struct DpsRow {
    pub character_id: u32,
    pub name: String,
    pub total: u64,
    pub share: f64,
    pub dps: f64,
    pub peak5: f64,
    pub physical: u64,
    pub elemental: u64,
    pub cure: u64,
    pub hurt: u64,
    pub break_endure: u64,
    pub hits: u64,
    pub crits: u64,
    pub double_crits: u64,
    pub biggest_hit: u64,
    pub damage_ids: Vec<(i32, u64)>,
    pub on_field_seconds: f64,
    pub on_field_dps: f64,
}

#[derive(Clone, Debug, Default)]
pub(super) struct DpsSnapshot {
    pub status: String,
    pub elapsed: f64,
    pub total: u64,
    pub dps: f64,
    pub peak5: f64,
    pub rows: Vec<DpsRow>,
    pub dropped_hits: u64,
}

struct Sample {
    epoch: u64, status: String, clock: f64, controlled: i32,
    runtime_ids: BTreeMap<i32, u32>, rows: BTreeMap<u32, DpsRow>,
}

fn parse(source: &str) -> Result<Sample, String> {
    let bad = || "invalid DPS polling response".to_owned();
    let mut lines = source.lines();
    let h: Vec<_> = lines.next().ok_or_else(bad)?.split('\t').collect();
    if h.len() != 5 || h[0] != "DPS2" { return Err(bad()); }
    let clock: f64 = h[3].parse().map_err(|_| bad())?;
    if !clock.is_finite() || clock < 0.0 { return Err(bad()); }
    let mut rows: BTreeMap<u32, DpsRow> = BTreeMap::new();
    let mut runtime_ids = BTreeMap::new();
    for line in lines {
        let f: Vec<_> = line.split('\t').collect();
        if f.len() != 9 { return Err(bad()); }
        let id: u32 = f[0].parse().map_err(|_| bad())?;
        if id == 0 || f[1].len() % 2 != 0 || !f[1].is_ascii() { return Err(bad()); }
        let bytes = (0..f[1].len()).step_by(2).map(|i| u8::from_str_radix(&f[1][i..i+2], 16).map_err(|_| bad())).collect::<Result<Vec<_>, _>>()?;
        let name = String::from_utf8(bytes).map_err(|_| bad())?;
        let values = f[2..8].iter().map(|s| s.parse::<u64>().map_err(|_| bad())).collect::<Result<Vec<_>, _>>()?;
        runtime_ids.insert(f[8].parse().map_err(|_| bad())?, id);
        let row = rows.entry(id).or_insert_with(|| DpsRow { character_id: id, name, ..Default::default() });
        for (dst, value) in [&mut row.total, &mut row.physical, &mut row.elemental, &mut row.cure, &mut row.hurt, &mut row.break_endure].into_iter().zip(values) {
            *dst = dst.checked_add(value).ok_or_else(bad)?;
        }
    }
    Ok(Sample { epoch: h[1].parse().map_err(|_| bad())?, status: h[2].to_owned(), clock,
        controlled: h[4].parse().map_err(|_| bad())?, runtime_ids, rows })
}

#[derive(Default)]
struct Window { points: VecDeque<(f64, u64)>, peak: f64 }
impl Window {
    fn push(&mut self, time: f64, total: u64) -> f64 {
        if self.points.back().is_some_and(|p| p.0 == time) { self.points.pop_back(); }
        self.points.push_back((time, total));
        let edge = time - 5.0;
        while self.points.len() > 2 && self.points[1].0 <= edge { self.points.pop_front(); }
        if let (Some(a), Some(b)) = (self.points.front(), self.points.get(1)) {
            if a.0 <= edge && b.0 > a.0 {
                // Linear interpolation between polls; no fabricated per-hit timestamps.
                let fraction = ((edge - a.0) / (b.0 - a.0)).clamp(0.0, 1.0);
                let baseline = a.1 as f64 + (b.1.saturating_sub(a.1)) as f64 * fraction;
                self.peak = self.peak.max((total as f64 - baseline).max(0.0) / 5.0);
            }
        }
        self.peak
    }
}

#[derive(Default)]
struct HitStats {
    hits: u64, crits: u64, double_crits: u64, biggest: u64,
    damage_ids: BTreeMap<i32, u64>, on_field: f64,
}

#[derive(Default)]
pub(super) struct DpsMeter {
    seq: u64,
    pending: bool,
    enabled: bool,
    reset: u64,
    reset_pending: bool,
    epoch: Option<u64>,
    origin: f64,
    last_clock: f64,
    baseline: BTreeMap<u32, DpsRow>,
    windows: BTreeMap<u32, Window>,
    total_window: Window,
    snapshot: DpsSnapshot,
    last_error: String,
    hits: BTreeMap<u32, HitStats>,
    pending_hits: Vec<Hit>,
    controlled: Option<u32>,
    dropped_origin: u64,
}

impl DpsMeter {
    pub(super) fn update(&mut self, bridge: &Result<LuaBridge, String>, enabled: bool, reset: u64, errors: &mut Vec<(String, String)>) -> Option<DpsSnapshot> {
        // Never join against the previous poll: NPC ids may be reused across fights.
        self.pending_hits.extend(graphics_dps_hits::drain());
        if self.reset != reset {
            self.reset = reset;
            self.reset_pending = true;
            self.clear_hits();
            self.snapshot = DpsSnapshot::default();
            self.snapshot.status = "reset; waiting for fight sample".into();
        }
        if !enabled {
            self.enabled = false;
            self.reset_pending = true;
            self.clear_hits();
            return None;
        }
        if !self.enabled {
            self.enabled = true;
            self.pending = false;
            if self.snapshot.status.is_empty() { self.snapshot.status = "waiting for fight sample".into(); }
        }
        let result = match bridge {
            Err(e) => Err(e.clone()),
            Ok(bridge) => {
                let mut result = if self.last_error.is_empty() { Ok(()) } else { Err(self.last_error.clone()) };
                if let Some((seq, outcome)) = bridge.outcome(CHUNK) {
                    if seq != self.seq {
                        self.seq = seq;
                        self.pending = false;
                        result = outcome.and_then(|text| parse(&text)).map(|sample| self.apply(sample));
                    }
                }
                if !self.pending { bridge.queue(CHUNK, POLL.to_owned()); self.pending = true; }
                result
            }
        };
        if let Err(error) = result {
            if self.last_error != error { super::failed("dps meter", &error); self.last_error = error.clone(); }
            errors.push(("dps meter".into(), error));
            self.snapshot.status = "polling error (summary retained)".into();
        } else { self.last_error.clear(); }
        Some(self.snapshot.clone())
    }

    fn apply(&mut self, sample: Sample) {
        let new_fight = self.epoch != Some(sample.epoch) || sample.clock < self.last_clock;
        if new_fight || self.reset_pending { self.clear_hits(); }
        let finishing = self.snapshot.status == "active" && !new_fight && !sample.rows.is_empty()
            && sample.status == "retained";
        if sample.status != "active" && !finishing {
            self.pending_hits.clear();
            self.controlled = None;
        }
        let active = sample.status == "active";
        if !active && self.snapshot.status != "active" && !self.reset_pending {
            self.snapshot.status = sample.status;
            return;
        }
        if !active && sample.rows.is_empty() {
            if self.snapshot.status == "active" { self.log_end(); }
            self.snapshot.status = sample.status;
            return;
        }
        let previous_clock = self.last_clock;
        if new_fight || self.reset_pending {
            if new_fight {
                if self.epoch.is_some() { self.log_end(); }
                super::ok("dps meter", "started normal fight recording");
            }
            self.epoch = Some(sample.epoch);
            self.origin = if self.reset_pending { sample.clock } else { 0.0 };
            self.baseline = if self.reset_pending { sample.rows.clone() } else { BTreeMap::new() };
            self.windows.clear();
            self.total_window = Window::default();
            self.snapshot = DpsSnapshot::default();
            self.reset_pending = false;
            // An initial zero point allows complete fights to have a five-second window.
            self.total_window.push(0.0, 0);
        }
        if active || finishing {
            if !new_fight {
                if let Some(id) = self.controlled {
                    self.hits.entry(id).or_default().on_field += (sample.clock - previous_clock).max(0.0);
                }
            }
            for hit in self.pending_hits.drain(..) {
                if let Some(id) = sample.runtime_ids.get(&hit.launcher_id) {
                    let stats = self.hits.entry(*id).or_default();
                    stats.hits = stats.hits.saturating_add(1);
                    stats.crits = stats.crits.saturating_add(u64::from(hit.crit || hit.double_crit));
                    stats.double_crits = stats.double_crits.saturating_add(u64::from(hit.double_crit));
                    stats.biggest = stats.biggest.max(u64::from(hit.damage));
                    let count = stats.damage_ids.entry(hit.damage_id).or_default();
                    *count = count.saturating_add(u64::from(hit.damage));
                }
            }
            self.controlled = if active { sample.runtime_ids.get(&sample.controlled).copied() } else { None };
        }
        self.last_clock = sample.clock;
        let elapsed = (sample.clock - self.origin).max(0.0);
        let mut rows = Vec::with_capacity(sample.rows.len());
        let mut total = 0u64;
        for (id, mut row) in sample.rows {
            if let Some(base) = self.baseline.get(&id) {
                row.total = row.total.saturating_sub(base.total);
                row.physical = row.physical.saturating_sub(base.physical);
                row.elemental = row.elemental.saturating_sub(base.elemental);
                row.cure = row.cure.saturating_sub(base.cure);
                row.hurt = row.hurt.saturating_sub(base.hurt);
                row.break_endure = row.break_endure.saturating_sub(base.break_endure);
            }
            row.dps = if elapsed > 0.0 { row.total as f64 / elapsed } else { 0.0 };
            if let Some(stats) = self.hits.get(&id) {
                row.hits = stats.hits;
                row.crits = stats.crits;
                row.double_crits = stats.double_crits;
                row.biggest_hit = stats.biggest;
                row.damage_ids = stats.damage_ids.iter().map(|(&id, &count)| (id, count)).collect();
                row.damage_ids.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(&b.0)));
                row.damage_ids.truncate(5);
                row.on_field_seconds = stats.on_field;
                row.on_field_dps = if stats.on_field > 0.0 { row.total as f64 / stats.on_field } else { 0.0 };
            }
            let window = self.windows.entry(id).or_insert_with(|| { let mut w = Window::default(); w.push(0.0, 0); w });
            row.peak5 = window.push(elapsed, row.total);
            total = total.saturating_add(row.total);
            rows.push(row);
        }
        for row in &mut rows { row.share = if total > 0 { row.total as f64 / total as f64 } else { 0.0 }; }
        rows.sort_by(|a, b| b.total.cmp(&a.total).then(a.character_id.cmp(&b.character_id)));
        let ended = self.snapshot.status == "active" && !active;
        self.snapshot = DpsSnapshot { status: sample.status, elapsed, total,
            dps: if elapsed > 0.0 { total as f64 / elapsed } else { 0.0 },
            peak5: self.total_window.push(elapsed, total), rows,
            dropped_hits: graphics_dps_hits::dropped().saturating_sub(self.dropped_origin) };
        if ended { self.log_end(); }
    }

    fn clear_hits(&mut self) {
        self.pending_hits.clear();
        self.hits.clear();
        self.controlled = None;
        self.dropped_origin = graphics_dps_hits::dropped();
    }

    fn log_end(&self) {
        super::ok("dps meter", &format!("summary: {} recorded damage / {:.2}s = {:.2} DPS", self.snapshot.total, self.snapshot.elapsed, self.snapshot.dps));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample(epoch: u64, clock: f64, controlled: i32) -> Sample {
        parse(&format!("DPS2\t{epoch}\tactive\t{clock}\t{controlled}\n101\t416c706861\t100\t40\t60\t2\t3\t4\t11\n101\t416c706861\t50\t20\t30\t1\t2\t3\t12\n202\t42\t20\t20\t0\t0\t0\t0\t22")).unwrap()
    }

    fn hit(launcher_id: i32, damage: u32, damage_id: i32) -> Hit {
        Hit { launcher_id, target_id: 99, damage, damage_id, crit: true, double_crit: false }
    }

    #[test]
    fn joins_counts_clock_and_fences() {
        let mut meter = DpsMeter::default();
        meter.apply(sample(1, 5.0, 11));
        assert_eq!(meter.snapshot.total, 170);
        assert_eq!(meter.snapshot.rows[0].name, "Alpha");
        meter.pending_hits.extend([hit(12, 300, 7), hit(11, 40, 8), hit(999, 999, 9)]);
        meter.apply(sample(1, 7.0, 22));
        let row = &meter.snapshot.rows[0];
        assert_eq!((row.hits, row.crits, row.double_crits, row.biggest_hit), (2, 2, 0, 300));
        assert_eq!(row.damage_ids, vec![(7, 300), (8, 40)]);
        assert_eq!(row.on_field_seconds, 2.0);
        assert_eq!(row.on_field_dps, 75.0);
        assert_eq!(row.total, 150); // Native hits never replace result totals.
        meter.apply(sample(1, 7.0, 22)); // Paused clock contributes zero.
        assert_eq!(meter.hits[&202].on_field, 0.0);
        meter.apply(sample(1, 8.0, 11));
        assert_eq!(meter.hits[&202].on_field, 1.0);
        for id in 0..7 { meter.pending_hits.push(hit(11, 1, id)); }
        meter.apply(sample(1, 9.0, 11));
        assert_eq!(meter.snapshot.rows[0].damage_ids.len(), 5);
        meter.pending_hits.push(hit(11, 1000, 1));
        meter.apply(sample(2, 1.0, 11)); // Reused NPC ids cannot credit a previous fight.
        assert_eq!(meter.snapshot.rows[0].hits, 0);
        assert_eq!(meter.snapshot.rows[0].on_field_seconds, 0.0);
        meter.pending_hits.push(hit(11, 1000, 1));
        meter.reset_pending = true;
        meter.apply(sample(2, 2.0, 11));
        assert_eq!(meter.snapshot.total, 0);
        assert_eq!(meter.snapshot.rows[0].hits, 0);
        assert_eq!(meter.snapshot.rows[0].on_field_seconds, 0.0);
    }

    #[test]
    fn parser_retention_and_window() {
        let mut meter = DpsMeter::default();
        meter.apply(sample(1, 5.0, 11));
        assert_eq!(meter.snapshot.peak5, 34.0);
        let mut final_hit = hit(11, 20, 3);
        final_hit.crit = false;
        final_hit.double_crit = true;
        meter.pending_hits.push(final_hit);
        let mut final_sample = sample(1, 6.0, 11);
        final_sample.status = "retained".into();
        meter.apply(final_sample);
        assert_eq!(meter.snapshot.rows[0].hits, 1);
        assert_eq!(meter.snapshot.rows[0].crits, 1);
        assert_eq!(meter.snapshot.rows[0].double_crits, 1);
        assert_eq!(meter.snapshot.rows[0].on_field_seconds, 1.0);
        meter.apply(parse("DPS2\t1\tretained\t5\t0").unwrap());
        assert_eq!(meter.snapshot.rows.len(), 2);
        assert!(parse("DPS2\t1\tactive\tNaN\t0").is_err());
        assert!(parse("DPS2\t1\tactive\t1\t0\n1\tff\t0\t0\t0\t0\t0\t0\t1").is_err());
        assert!(parse("DPS2\t1\tactive\t1\t0\n1\t41\t-1\t0\t0\t0\t0\t0\t1").is_err());
        let mut window = Window::default();
        for i in 0..=20 { window.push(i as f64, i * 10); }
        assert_eq!(window.peak, 10.0);
        assert!(window.points.len() <= 7);
    }
}
