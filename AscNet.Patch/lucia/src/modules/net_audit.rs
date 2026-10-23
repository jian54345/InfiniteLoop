//! Native outbound calls: DNS lookups of known telemetry / public-IP-lookup hosts are always redirected to an
//! unresolvable name (`blocked.invalid`), so the lookup fails like an offline host; with `ASCNET_PATCH_NETAUDIT=1`
//! every outbound call is also logged once per destination and calling module. Arguments are otherwise untouched.
use std::{cell::Cell, collections::HashSet, ffi::c_void, net::{Ipv4Addr, Ipv6Addr}, sync::{LazyLock, Mutex, mpsc::{sync_channel, SyncSender}, atomic::{AtomicBool, Ordering}}};
use ilhook::x64::Registers;
use windows::Win32::System::Diagnostics::Debug::{CONTEXT, CONTEXT_CONTROL_AMD64, CONTEXT_INTEGER_AMD64, RtlLookupFunctionEntry, RtlVirtualUnwind, UNW_FLAG_NHANDLER};
use crate::{diag, interceptor::Interceptor};

// Minimal kernel32 ABI keeps this observer independent of networking feature flags.
#[link(name = "kernel32")]
extern "system" {
    fn LoadLibraryW(name: *const u16) -> *mut c_void;
    fn GetProcAddress(module: *mut c_void, name: *const u8) -> *mut c_void;
    fn GetModuleHandleExW(flags: u32, address: *const u16, module: *mut *mut c_void) -> i32;
    fn GetModuleFileNameW(module: *mut c_void, name: *mut u16, size: u32) -> u32;
    fn RtlCaptureStackBackTrace(skip: u32, count: u32, frames: *mut *mut c_void, hash: *mut u32) -> u16;
    fn ReadProcessMemory(process: *mut c_void, address: *const c_void, buffer: *mut c_void, size: usize, read: *mut usize) -> i32;
    fn GetLastError() -> u32;
    fn SetLastError(error: u32);
}

const LIMIT: usize = 512;
static AUDIT: AtomicBool = AtomicBool::new(false);
static BLOCKED_A: &[u8] = b"blocked.invalid\0";
static BLOCKED_W: [u16; 16] = {
    let mut wide = [0u16; 16];
    let mut i = 0;
    while i < BLOCKED_A.len() {
        wide[i] = BLOCKED_A[i] as u16;
        i += 1;
    }
    wide
};
/// Seen by the 4.8 client (network audit, 2026-10-23): CrashSight's upload host (`pc.crashsight.wetest.net`)
/// and the public-IP lookup `XNoticeManager.RequestIp` runs at start-up (icanhazip, then ipify / ifconfig /
/// inet-ip as fallbacks). The client has no consumer of that IP; blocking leaves it empty.
const BLOCKED_SUFFIXES: [&str; 6] = ["wetest.net", "icanhazip.com", "ipify.org", "ifconfig.me", "ifconfig.co", "inet-ip.info"];

fn blocked_host(host: &str) -> bool {
    let host = host.trim_end_matches('.').to_ascii_lowercase();
    BLOCKED_SUFFIXES.iter().any(|suffix| host == *suffix || host.ends_with(&format!(".{suffix}")))
}
static OWNER: LazyLock<Mutex<Interceptor>> = LazyLock::new(|| unsafe { install_hooks() });
static EVENTS: LazyLock<SyncSender<Event>> = LazyLock::new(|| {
    let (sender, receiver) = sync_channel::<Event>(512);
    std::thread::spawn(move || {
        ACTIVE.with(|flag| flag.set(true));
        let mut seen = HashSet::with_capacity(LIMIT);
        let mut full = false;
        while let Ok(event) = receiver.recv() {
            if OVERFLOW.swap(false, Ordering::Relaxed) {
                diag::log(&diag::failed_line("lucia", "net audit", "audit queue full; some calls were not audited"));
            }
            if seen.contains(&event.key) { continue; }
            if seen.len() == LIMIT {
                if !full {
                    full = true;
                    diag::log(&diag::ok_line("lucia", "net audit", "dedup table full; further new destinations are not logged"));
                }
                continue;
            }
            let line = if event.blocked {
                format!("{} ({}) from {}; the lookup fails as if offline", event.key.destination, event.key.api, event.source)
            } else {
                format!("{} {} from {} (caller {})", event.key.api, event.key.destination, event.source, event.caller)
            };
            let what = if event.blocked { "telemetry blocked" } else { "net audit" };
            seen.insert(event.key);
            diag::log(&diag::ok_line("lucia", what, &line));
        }
    });
    sender
});
static OVERFLOW: AtomicBool = AtomicBool::new(false);
struct Event { key: AuditKey, source: String, caller: String, blocked: bool }
thread_local! { static ACTIVE: Cell<bool> = const { Cell::new(false) }; }

#[derive(Hash, PartialEq, Eq)]
struct AuditKey { api: &'static str, destination: String, module: String }
fn dedup_key(api: &'static str, destination: String, module: &str) -> AuditKey {
    AuditKey { api, destination, module: module.to_ascii_lowercase() }
}

/// Always installs the DNS hooks (telemetry block); the connect / WinHTTP / WinINet observers only with the audit.
pub unsafe fn install() {
    AUDIT.store(std::env::var("ASCNET_PATCH_NETAUDIT").as_deref() == Ok("1"), Ordering::Relaxed);
    LazyLock::force(&EVENTS);
    LazyLock::force(&OWNER);
}

unsafe fn install_hooks() -> Mutex<Interceptor> {
        let mut owner = Interceptor::new();
        let dns: &[(&str, ilhook::x64::JmpBackRoutine)] =
            &[("getaddrinfo", dns_a), ("GetAddrInfoW", dns_w), ("GetAddrInfoExW", dns_ex_w), ("gethostbyname", host_a)];
        let mut libraries: Vec<(&str, &[(&str, ilhook::x64::JmpBackRoutine)])> = vec![("ws2_32.dll", dns)];
        if AUDIT.load(Ordering::Relaxed) {
            libraries.push(("ws2_32.dll", &[("connect", connect)]));
            libraries.push(("winhttp.dll", &[("WinHttpConnect", http_connect)]));
            libraries.push(("wininet.dll", &[("InternetConnectW", internet_connect_w), ("InternetConnectA", internet_connect_a), ("InternetOpenUrlW", url_w), ("InternetOpenUrlA", url_a)]));
        }
        for (dll, targets) in libraries {
            let wide: Vec<u16> = dll.encode_utf16().chain(Some(0)).collect();
            // Retain LoadLibrary's reference for the lifetime of the installed hooks.
            let module = LoadLibraryW(wide.as_ptr());
            for &(api, routine) in targets {
                let name: Vec<u8> = api.bytes().chain(Some(0)).collect();
                let address = if module.is_null() { std::ptr::null_mut() } else { GetProcAddress(module, name.as_ptr()) };
                if address.is_null() {
                    diag::log(&diag::failed_line("lucia", "net hook", &format!("{dll}!{api} unavailable; calls through this API are neither blocked nor audited")));
                } else if let Err(error) = owner.attach(address as usize, routine) {
                    diag::log(&diag::failed_line("lucia", "net hook", &format!("{dll}!{api}: {error}; calls through this API are neither blocked nor audited")));
                } else if AUDIT.load(Ordering::Relaxed) {
                    diag::log(&diag::ok_line("lucia", "net audit hook", &format!("{dll}!{api}")));
                }
            }
        }
        Mutex::new(owner)
}

unsafe fn copy_memory(address: usize, bytes: &mut [u8]) -> bool {
    if address == 0 { return false; }
    let mut copied = 0;
    ReadProcessMemory(-1isize as *mut c_void, address as *const c_void, bytes.as_mut_ptr().cast(), bytes.len(), &mut copied) != 0 && copied == bytes.len()
}

unsafe fn text(address: usize, wide: bool) -> String {
    if address == 0 { return "<null>".into(); }
    let mut buffer = [0u8; 2048];
    let mut length = 0;
    let unit = if wide { 2 } else { 1 };
    while length + unit <= buffer.len() {
        let Some(pointer) = address.checked_add(length) else { return "<unreadable>".into(); };
        // Read only as far as the next page boundary, including strings ending there.
        let count = (4096 - pointer % 4096).min(buffer.len() - length);
        let count = count - count % unit;
        if count == 0 || !copy_memory(pointer, &mut buffer[length..length + count]) { return "<unreadable>".into(); }
        for offset in (length..length + count).step_by(unit) {
            if buffer[offset] == 0 && (!wide || buffer[offset + 1] == 0) {
                return decode_text(&buffer[..offset], wide);
            }
        }
        length += count;
    }
    let mut value = decode_text(&buffer, wide);
    value.push_str("<truncated>");
    value
}

fn decode_text(bytes: &[u8], wide: bool) -> String {
    let value = if wide {
        char::decode_utf16(bytes.chunks_exact(2).map(|b| u16::from_le_bytes([b[0], b[1]]))).map(|c| c.unwrap_or(char::REPLACEMENT_CHARACTER)).collect::<String>()
    } else { String::from_utf8_lossy(bytes).into_owned() };
    // Keep untrusted URLs/hostnames on a single diagnostic line.
    value.chars().map(|c| if c.is_control() { ' ' } else { c }).collect()
}

fn sockaddr(bytes: &[u8]) -> Option<String> {
    let family = u16::from_le_bytes(bytes.get(..2)?.try_into().ok()?);
    let port = u16::from_be_bytes(bytes.get(2..4)?.try_into().ok()?);
    match family {
        2 => { let ip: [u8; 4] = bytes.get(4..8)?.try_into().ok()?; Some(format!("{}:{port}", Ipv4Addr::from(ip))) }
        23 => {
            let ip: [u8; 16] = bytes.get(8..24)?.try_into().ok()?;
            let scope = u32::from_le_bytes(bytes.get(24..28)?.try_into().ok()?);
            let ip = Ipv6Addr::from(ip);
            Some(if scope == 0 { format!("[{ip}]:{port}") } else { format!("[{ip}%{scope}]:{port}") })
        }
        _ => None,
    }
}

fn system_module(name: &str) -> bool {
    let basename = name.rsplit(['/', '\\']).next().unwrap_or(name).to_ascii_lowercase();
    matches!(basename.as_str(), "ntdll.dll" | "kernel32.dll" | "kernelbase.dll" | "ws2_32.dll" | "winhttp.dll" | "wininet.dll" | "mswsock.dll" | "dnsapi.dll" | "iphlpapi.dll" | "nsi.dll" | "secur32.dll" | "crypt32.dll" | "rpcrt4.dll" | "sechost.dll" | "ucrtbase.dll" | "msvcrt.dll" | "lucia.dll")
        || basename.starts_with("wine") || basename.starts_with("api-ms-win-") || basename.starts_with("ext-ms-win-")
}

unsafe fn module_at(address: usize) -> Option<String> {
    if address == 0 { return None; }
    let mut module = std::ptr::null_mut();
    if GetModuleHandleExW(0x4 | 0x2, address as *const u16, &mut module) == 0 { return None; }
    if crate::LUCIA_MODULE.get().copied() == Some(module as usize) { return Some("lucia.dll".into()); }
    let mut path = [0u16; 1024];
    let length = GetModuleFileNameW(module, path.as_mut_ptr(), path.len() as u32) as usize;
    if length == 0 || length >= path.len() { return None; }
    let start = path[..length].iter().rposition(|&c| c == b'\\' as u16 || c == b'/' as u16).map_or(0, |i| i + 1);
    Some(String::from_utf16_lossy(&path[start..length]))
}

unsafe fn original_source(regs: &Registers, caller: usize) -> Option<String> {
    // ilhook's generated trampoline has no registered unwind metadata. Capturing
    // the callback stack cannot reliably cross it; resume from the original
    // caller's post-call context instead, without touching the live registers.
    let mut context = CONTEXT {
        ContextFlags: CONTEXT_CONTROL_AMD64 | CONTEXT_INTEGER_AMD64,
        Rip: caller as u64,
        Rsp: regs.rsp.checked_add(8)?,
        Rbx: regs.rbx,
        Rbp: regs.rbp,
        Rsi: regs.rsi,
        Rdi: regs.rdi,
        R12: regs.r12,
        R13: regs.r13,
        R14: regs.r14,
        R15: regs.r15,
        ..Default::default()
    };
    for _ in 0..16 {
        if context.Rip == 0 || context.Rsp == 0 { break; }
        let Some(name) = module_at(context.Rip as usize) else { break; };
        if !system_module(&name) { return Some(name); }
        let (rip, rsp) = (context.Rip, context.Rsp);
        let mut return_address = [0u8; 8];
        if !copy_memory(rsp as usize, &mut return_address) { break; }
        let mut image_base = 0;
        let entry = RtlLookupFunctionEntry(rip, &mut image_base, None);
        if entry.is_null() {
            // Leaf functions have no unwind entry: emulate their return safely.
            context.Rip = u64::from_le_bytes(return_address);
            let Some(next_rsp) = rsp.checked_add(8) else { break; };
            context.Rsp = next_rsp;
        } else {
            let mut handler_data = std::ptr::null_mut();
            let mut establisher_frame = 0;
            RtlVirtualUnwind(UNW_FLAG_NHANDLER, image_base, rip, entry, &mut context, &mut handler_data, &mut establisher_frame, None);
        }
        if context.Rip == 0 || context.Rsp <= rsp { break; }
    }
    None
}

struct Guard(u32);
impl Drop for Guard {
    fn drop(&mut self) { ACTIVE.with(|flag| flag.set(false)); unsafe { SetLastError(self.0); } }
}

/// Logs `destination` once (always for blocks, otherwise only with the audit on).
unsafe fn observe(reg: *mut Registers, api: &'static str, blocked: bool, destination: impl FnOnce(&Registers) -> String) {
    if !blocked && !AUDIT.load(Ordering::Relaxed) { return; }
    let error = GetLastError();
    if ACTIVE.with(|flag| flag.replace(true)) { return; }
    let _guard = Guard(error);
    let regs = &*reg;
    let destination = destination(regs);
    let mut return_address = [0u8; 8];
    let caller_address = if copy_memory(regs.rsp as usize, &mut return_address) { usize::from_le_bytes(return_address) } else { 0 };
    let caller = module_at(caller_address).unwrap_or_else(|| "<unknown>".into());
    let mut source = if !system_module(&caller) && caller != "<unknown>" { Some(caller.clone()) } else { None };
    let mut frames = [std::ptr::null_mut(); 16];
    // Required bounded capture is diagnostic only: never attribute through the
    // unregistered trampoline using these callback-stack frames.
    let _count = RtlCaptureStackBackTrace(0, frames.len() as u32, frames.as_mut_ptr(), std::ptr::null_mut());
    if source.is_none() { source = original_source(regs, caller_address); }
    let source = source.unwrap_or_else(|| "<unknown>".into());
    let key = dedup_key(api, destination, &source);
    // ponytail: bounded handoff; logging and dedup never hold up a network thread.
    if EVENTS.try_send(Event { key, source, caller, blocked }).is_err() {
        OVERFLOW.store(true, Ordering::Relaxed);
    }
}

macro_rules! host_hook {
    ($name:ident, $api:literal, $register:ident, $wide:expr, $port:expr) => {
        unsafe extern "win64" fn $name(reg: *mut Registers, _: usize) {
            observe(reg, $api, false, |r| {
                let host = text(r.$register as usize, $wide);
                if $port { format!("{host}:{}", r.r8 as u16) } else { host }
            });
        }
    };
}

/// DNS entry points: the host is the first argument (rcx). A deny-listed host is swapped for `blocked.invalid`
/// (a reserved TLD that never resolves) before the original runs, so the caller sees an ordinary lookup failure.
macro_rules! dns_hook {
    ($name:ident, $api:literal, $wide:expr) => {
        unsafe extern "win64" fn $name(reg: *mut Registers, _: usize) {
            if ACTIVE.with(|flag| flag.get()) { return; }
            let host = text((*reg).rcx as usize, $wide);
            let blocked = blocked_host(&host);
            if blocked {
                (*reg).rcx = if $wide { BLOCKED_W.as_ptr() as u64 } else { BLOCKED_A.as_ptr() as u64 };
            }
            observe(reg, $api, blocked, |_| host);
        }
    };
}
dns_hook!(dns_a, "getaddrinfo", false);
dns_hook!(dns_w, "GetAddrInfoW", true);
dns_hook!(dns_ex_w, "GetAddrInfoExW", true);
dns_hook!(host_a, "gethostbyname", false);
host_hook!(http_connect, "WinHttpConnect", rdx, true, true);
host_hook!(internet_connect_w, "InternetConnectW", rdx, true, true);
host_hook!(internet_connect_a, "InternetConnectA", rdx, false, true);
host_hook!(url_w, "InternetOpenUrlW", rdx, true, false);
host_hook!(url_a, "InternetOpenUrlA", rdx, false, false);
unsafe extern "win64" fn connect(reg: *mut Registers, _: usize) {
    observe(reg, "connect", false, |r| {
        let mut address = [0u8; 28];
        let size = (r.r8 as i32).max(0) as usize;
        let size = size.min(address.len());
        if size == 0 || !copy_memory(r.rdx as usize, &mut address[..size]) { return "<unreadable sockaddr>".into(); }
        sockaddr(&address[..size]).unwrap_or_else(|| "<unsupported sockaddr>".into())
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn socket_addresses() {
        assert_eq!(sockaddr(&[2, 0, 1, 187, 127, 0, 0, 1]).as_deref(), Some("127.0.0.1:443"));
        let mut ipv6 = [0u8; 28];
        ipv6[0] = 23; ipv6[3] = 80; ipv6[23] = 1;
        assert_eq!(sockaddr(&ipv6).as_deref(), Some("[::1]:80"));
        ipv6[24] = 3;
        assert_eq!(sockaddr(&ipv6).as_deref(), Some("[::1%3]:80"));
        assert_eq!(sockaddr(&ipv6[..20]), None);
        assert_eq!(sockaddr(&[0; 8]), None);
    }
    #[test]
    fn attribution_and_dedup() {
        assert!(system_module("C:\\Windows\\System32\\WS2_32.DLL"));
        assert!(system_module("lucia.dll"));
        assert!(system_module("winepulse.drv"));
        assert!(!system_module("CrashSight64.dll"));
        assert!(!system_module("GameAssembly.dll"));
        let key = dedup_key("connect", "127.0.0.1:80".into(), "PGR.exe");
        assert!(key == dedup_key("connect", "127.0.0.1:80".into(), "pgr.EXE"));
        assert!(key != dedup_key("connect", "127.0.0.1:81".into(), "PGR.exe"));
        assert!(key != dedup_key("WinHttpConnect", "127.0.0.1:80".into(), "PGR.exe"));
        assert!(key != dedup_key("connect", "127.0.0.1:80".into(), "PerfSight.dll"));
    }
}

#[cfg(test)]
mod block_tests {
    use super::*;
    #[test]
    fn deny_list_matches_hosts_and_subdomains_only() {
        for host in ["pc.crashsight.wetest.net", "WETEST.NET.", "ipv4.icanhazip.com", "icanhazip.com", "api.ipify.org", "ifconfig.me", "inet-ip.info"] {
            assert!(blocked_host(host), "{host}");
        }
        for host in ["notwetest.net", "icanhazip.com.evil.example", "prod-encdn-ak.pgr-game.com", "127.0.0.1", ""] {
            assert!(!blocked_host(host), "{host}");
        }
        assert_eq!(&BLOCKED_W[..BLOCKED_A.len()], BLOCKED_A.iter().map(|&b| u16::from(b)).collect::<Vec<_>>().as_slice());
    }
}
