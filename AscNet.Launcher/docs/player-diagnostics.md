# Player diagnostics (lucia / KRSDK)

Both native patches (`lucia.dll`, `PGR_Data/Plugins/KRSDK.dll`) append every diagnostic line to one file
beside `PGR.exe`:

```
<client folder>\ascnet-patch.log
```

(The same lines are also printed to stdout, which only exists when `PGR.exe` is started from a console or
Wine.) The file is cut when it grows past 1 MiB. Each launch appends, so send the **last launch**: everything
from the last `[lucia] OK KRSDK.bin identity` / `[lucia] FAILED KRSDK.bin identity` line to the end.

## What to send back

1. The region you play (EN / TW / KR / JP / CN).
2. All lines of `ascnet-patch.log` starting with `[lucia]` or `[KRSDK]` that contain `OK` or `FAILED`
   (or just the whole file).
3. If a line says `FAILED`, nothing else is needed: the reason and the looked-up name are in that line.

## Healthy launch (KR example)

```
[lucia] OK KRSDK.bin identity: package=com.herogame.pc.punishing.grayraven.kr game=G286 app=A1794 channel=240 channel_name=PC channel_op=null source=PGR_Data/Plugins/KRSDKRes/KRSDK.bin
[lucia] OK GameAssembly.dll module lookup: base=0x7FF8A1230000
[lucia] OK il2cpp exports: 12 il2cpp_* exports resolved from GameAssembly.dll
[lucia] OK il2cpp domain ready: assemblies=187 waited_ms=1200
[lucia] OK lookup assembly+class: UnityEngine.UnityWebRequestModule.dll!UnityEngine.Networking.UnityWebRequest
[lucia] OK lookup UnityEngine.UnityWebRequestModule.dll!UnityEngine.Networking.UnityWebRequest::InternalSetUrl(System.String)->System.Void: method at 0x7FF8A2345678
[lucia] OK routing origin: http://127.0.0.1:8080
[lucia] OK hook install: UnityEngine.Networking.UnityWebRequest::InternalSetUrl detour at 0x7FF8A2345678
[KRSDK] OK KRSDK.bin identity: package=com.herogame.pc.punishing.grayraven.kr game=G286 app=A1794 channel=240 channel_name=PC channel_op=null source=PGR_Data/Plugins/KRSDKRes/KRSDK.bin
[KRSDK] OK lucia.dll routing initialization
[lucia] OK first client config request: package=com.kurogame.punishing.grayraven.kr cdn_key=jqlCmYRizwT76uvX
```

What each region must show (from the client's own files, not from the server):

| Region | `KRSDK.bin` `game` / `app` | `first client config request` package |
| ------ | -------------------------- | ------------------------------------- |
| KR | `G286` / `A1794`, channel `240` | `com.kurogame.punishing.grayraven.kr` |
| JP | `G282` / `A1778`, channel `240` | `com.kurogame.punishing.grayraven.jp` |

If `first client config request` is missing, the game never asked for `client/config/...`: the hook is
not active, or the client talks to a host the patch does not route.

## Failure lines

Format: `[lucia|KRSDK] FAILED <what was looked up>: <reason> | send: ascnet-patch.log (beside PGR.exe) and the client region`

| Line starts with | Meaning |
| ---------------- | ------- |
| `[lucia] FAILED KRSDK.bin identity` | `PGR_Data/Plugins/KRSDKRes/KRSDK.bin` is missing/unreadable or lacks a `KR_*` key (the key is named). Client install incomplete. |
| `[lucia] FAILED GameAssembly.dll module lookup` | `lucia.dll` was loaded outside `PGR.exe`'s IL2CPP process. |
| `[lucia] FAILED native routing ...: ... GameAssembly.dll does not export `il2cpp_...`` | The named IL2CPP export is missing: not the 4.8.0 client, or `GameAssembly.dll` was modified. |
| `[lucia] FAILED native routing ...: il2cpp_domain_assembly_open could not open assembly ...` | The assembly named in the line is not in this client's metadata. |
| `[lucia] FAILED native routing ...: il2cpp_class_from_name: type ...` / `method ...` | The class or method named in the line is missing or has another signature: Unity build differs from 4.8.0. |
| `[lucia] FAILED native routing ...: installing the InternalSetUrl detour failed` | Another injector/overlay hooked the same function. Send the list of overlays/injectors you run. |
| `[lucia] FAILED il2cpp domain ready` | IL2CPP still had no assemblies after 60 s (lucia keeps waiting). |
| `[KRSDK] FAILED lucia.dll routing initialization` | `lucia.dll` is missing next to `PGR.exe`, blocked (antivirus), is an old build, or its own `[lucia] FAILED` line (above in the same file) explains why. |
| `[KRSDK] FAILED KRSDK.bin identity` / `kurosdk_getConfigInfo packaged config` | Same as the `[lucia] FAILED KRSDK.bin identity` row; login is refused without a channel. |
| `[KRSDK] FAILED login channel from KRSDK.bin` | `KR_ChannelID` could not be read at login. |

When `lucia` fails, native routing stays off and the client talks to the retail servers.

## launcher.log (beside `AscNet.Launcher.exe`)

Setup, Restore, Play and the official-client download/update each write one line naming the selected game folder, the
detected region (or why detection failed), the patch state before the action, and the outcome or the full error:

```
Setup: game=D:\Games\PGR region=CN patch=Unpatched
Play: failed: refusing installation: unsupported client: ...
```

The region comes from the SDK files: `KRSDKEx.dll` + `KRSDKRes/KRSDKConfig.json` without `KRSDKRes/KRSDK.bin` is CN, even
if a `KRSDK.dll` is present.

## Local server logs (`%LOCALAPPDATA%\AscNetLauncher\local\logs\`)

`server.log` and `mongod.log` hold the local backend's output. Each start moves the previous run to `server.previous.log`
(`mongod.previous.log`), and a file that reaches 32 MiB during a run is moved there too, so each log keeps at most two
files and the newest lines are always in `server.log`. Send both server files when reporting a bug.

How much the server writes is set by `VerboseLevel` in `%LOCALAPPDATA%\AscNetLauncher\local\config.json`; the change
applies the next time the launcher starts the server:

| `VerboseLevel` | Logged |
|---|---|
| `Silent` | No packet lines; other server messages (startup, warnings, errors) still appear. |
| `Normal` | One line per request (sizes only), response and push (names only). |
| `Debug` (default) | Same lines, with the full response/push content that bug reports need. |
| `SuperDebug` | Also the heartbeats, which every lower level skips. |

Leave the `GameServer` and `Database` entries unchanged; the launcher refuses to start the server if they no longer match Setup.

## China (CN) client

The CN client (战双帕弥什) has no `KRSDK.dll` and no `KRSDK.bin`, and a CN folder must not contain `KRSDK.dll`: Setup moves a
leftover one (an old patch or a manual copy) into `.ascnet-launcher/backups/` and Restore puts it back; it ships the official SDK
(`PGR_Data/Plugins/KRSDKEx.dll`, `libkrsdkcurl.dll`, `KRSDKRes/KRSDKConfig.json`). The Launcher installs only
`version.dll`, `lucia.dll`, `libraries.txt` and the generated `PGRBase.dll`; `PGR.exe`, `GameAssembly.dll`, `KRSDKEx.dll`
and `libkrsdkcurl.dll` are only hash-checked against `supported-client.json` (never modified or backed up), and
an unknown hash is reported as unsupported with the observed SHA-256. `lucia` redirects the SDK's `/sdkcom/*` requests in
process (a hook on `libkrsdkcurl.dll!kr_sdk_curl_easy_setopt`), so the official login UI stays in use.

Healthy CN lines in `ascnet-patch.log` (the same `routed` line appears per redirected SDK request, query strings are never logged):

```
[lucia] OK KRSDKConfig.json identity: package=com.kurogame.haru.hero game=G148 app=A1393 channel=19 channel_name=国内PC source=PGR_Data/Plugins/KRSDKRes/KRSDKConfig.json
[lucia] OK CN SDK hook install: PGR_Data/Plugins/libkrsdkcurl.dll!kr_sdk_curl_easy_setopt detour at 0x..., routing /sdkcom/* to http://127.0.0.1:8080
[lucia] routed https://sdkapi.kurogame.com/sdkcom/v2/sys/conf.lg -> http://127.0.0.1:8080/sdkcom/v2/sys/conf.lg
```

| Line starts with | Meaning |
| ---------------- | ------- |
| `[lucia] FAILED KRSDKConfig.json identity` | `KRSDKConfig.json` is missing/unreadable. |
| `[lucia] FAILED CN SDK routing (login stays on retail servers)` | `libkrsdkcurl.dll` could not be loaded or lacks `kr_sdk_curl_easy_setopt` (not the verified 4.8.0 CN SDK), or another injector hooked the same function. |

No `routed ... /sdkcom/...` line after the login window opens means the SDK never reached the hook. The SDK also keeps
its own cache under `%APPDATA%\KR_G148\A1393\` (agreement, `KRSDKCache.json`) and logs/cache under
`%USERPROFILE%\AppData\LocalLow\kurogame\战双帕弥什` (per the real player log); include them when reporting a CN login problem.

## FPS unlock and no camera fade

Both are plain keys of `ascnet-graphics.ini` (see the table below): `fps = <n>` and `nofade = on`. There are no
environment variables and no extra threads; the graphics worker applies them. The launcher has no controls for them.
Two ways to set them, both editing the same file:

- The in-game overlay (`overlay = on`, Ctrl+Shift+G): **Unlock FPS** + target field (enabling uses the last value, else
  240; values below 1 are refused inline) and **No camera fade** apply live within 0.5 s; **Save to ascnet-graphics.ini**
  rewrites the whole file.
- Editing the file by hand (read at game start).

One-time migration: values saved by older launchers (FPS unlock, frame-rate field, no camera fade) are copied once into
the selected client's `ascnet-graphics.ini`, only for keys the file does not have yet, and then dropped from the
launcher's settings.

Same on EN/TW/KR/JP.

**Migration from the old bundle patch.** Earlier launchers injected a Lua hook (`PgrNativeFpsTimer`) into
`matrix.ab`'s `XUiMain.lua`. On every Check/startup the launcher looks for that hook; if found, and PGR.exe is not
running, it removes it (the usual same-size rewrite, original kept under `.ascnet-launcher/fps-backups/`), writes
`fps = <the hook's old value>` to `ascnet-graphics.ini`, and logs `Removed the old bundle FPS patch ...`. While PGR.exe
is running it logs `the old bundle FPS patch (N FPS) is still installed; close PGR.exe and press Check to remove it`
(the hook keeps working until then). Afterwards only the native path exists; the launcher never writes the bundle again.

Healthy lines (`fps = 240`, `nofade = on`):

```
[lucia] OK graphics options requested: fps = 240, nofade = on in ascnet-graphics.ini; applying once IL2CPP is ready
[lucia] OK lookup UnityEngine.Application: assembly=UnityEngine.CoreModule.dll
[lucia] OK lookup Application.set_targetFrameRate(Int32)/get_targetFrameRate(): static property accessors
[lucia] OK lookup UnityEngine.QualitySettings.vSyncCount: assembly=UnityEngine.CoreModule.dll
[lucia] OK lookup XCameraDitherDetector: assembly=Assembly-CSharp.dll
[lucia] OK lookup XCameraDitherDetector.UseCameraDither: static System.Boolean
[lucia] OK lookup XNpcDither: assembly=Assembly-CSharp.dll
[lucia] OK lookup XNpcDither.SetPlayerSelfDitherParameter(...PlayerSelfDitherParameter): static, 1 parameter; game value read from playerSelfDitherParameter
[lucia] OK FPS unlock: targetFrameRate=240, vSyncCount=0; the game's own values are targetFrameRate=-1, vSyncCount=1
[lucia] OK apply FPS unlock: targetFrameRate=240, vSyncCount=0 held every 0.5 s
[lucia] OK no camera fade: applied; the game's own values are UseCameraDither=true, playerSelfDitherParameter=PlayerSelfDither { ... }
[lucia] OK FPS unlock: removed, restored targetFrameRate=-1, vSyncCount=1        (option switched off in the overlay)
[lucia] OK no camera fade: removed, restored UseCameraDither=true, playerSelfDitherParameter=...
```

Failures only affect their own option; the game keeps its own value and routing is unaffected:
`FAILED ascnet-graphics.ini: line N: ...` (`fps` not a positive whole number, `nofade` not a flag),
`FAILED lookup FPS unlock` / `lookup QualitySettings.vSyncCount` (the unlock may then be capped by vsync) /
`FAILED lookup no camera fade` / `lookup XNpcDither...` (only the camera fade is disabled), and
`FAILED apply FPS unlock` / `write XCameraDitherDetector.UseCameraDither=false` / `apply XNpcDither...` (call threw, or
the value read back differently). The overlay shows the same problems in red under the option. Lines are logged on the
first result and on every later change, not every 0.5 s.

## Graphics options (`ascnet-graphics.ini`)

`ascnet-graphics.ini` beside `PGR.exe` is read by `lucia` at game start, however the game is launched (launcher,
CrossOver shortcut, script). One `key = value` per line, `#`/`;` comments. Absent file = off, no lines.

| Key | Effect |
|---|---|
| `renderscale = 2.0` | Internal render scale in every scene (0.25–4.0). The game itself sets 1.333 in menus/Babylonia and 1.0 in fights through `RenderPIplineQualityManger.SetupExtraRenderScale`; `lucia` detours that call (and `EnableExtraRenderScale`, which Babylonia switches off) so the chosen scale holds everywhere with no reset window. |
| `fps = 240` | Frame-rate unlock: a plain positive whole number (anything else is a `FAILED ascnet-graphics.ini: line N` and ignored; absent = the game's own frame rate). `lucia` holds `UnityEngine.Application.targetFrameRate = n` and `QualitySettings.vSyncCount = 0` (Unity ignores `targetFrameRate` while vsync paces the frame, and the game's own VSync option sets it) every 0.5 s. Removing the key restores the game's `targetFrameRate` and `vSyncCount`, recorded before the first write and again whenever the game changes them. The overlay shows `target <n>, vsync <v>` as read from the game. |
| `nofade = on` | Disables the character/camera proximity fade natively (`on`/`off`, `1`/`0`, `true`/`false`; absent = off): `XCameraDitherDetector.UseCameraDither = false` and `XNpcDither.SetPlayerSelfDitherParameter` with opaque values, re-applied every 0.5 s. Dying-enemy dissolves (`XDitherHelper`) are untouched. Turning it off restores `UseCameraDither` and the game's own `XNpcDither.playerSelfDitherParameter`, both read before the first write. Supersedes `tools/apply_pgr_nofade.py`. |
| `<Setting>.<Field> = value` | Overrides one field of the render pipeline's runtime quality objects (`UniversalRenderPipelineAsset.RenderPipelineDefaultQualityData`, read as managed memory, never through Unity calls), e.g. `ShadowSetting.CascadeSize = 4096`. Names are the game's own and case-sensitive (see the overlay or dump). Values: `on`/`off`, numbers, enum names or numbers (`AntiAliasingSetting.DeferredAAMethod = SubpixelMorphologicalAntiAliasing`), comma lists for arrays (`ShadowSetting.CascadeFrameUpdate = 0,0,1,2`). Re-applied every 0.5 s; the game resets them on scene and quality changes. Removing an override restores the game's value. |
| `overlay = on` | In-game Dear ImGui window (hudhook D3D11 `Present` hook), toggled with **Ctrl+Shift+G**; the game gets no keyboard/mouse input while it is open. Shows scene, game-requested and effective render scale; edits render scale and every quality field live (changes apply within 0.5 s), marks overridden fields with the game's value and a Reset, shows each key's error in red, and **Save to ascnet-graphics.ini** writes the current options. Not all fields have a visible effect: `ForwardAAMethod` is ignored by deferred-rendered cameras (`DeferredAAMethod` applies), `ActiveRealTimeAreaLight` only matters in scenes with area lights, and no renderer reader was found for `AdditionalCharacterLighting`. |
| `dump = 1` | Read-only: every render-pipeline quality object (`RenderPIplineQualityManger` statics, `GetRuntimeData()` and its `Quality_*` settings, `Is*()` flags), `XQualityManager.Instance`, `XResolutionManager` statics and the managed `UnityEngine.QualitySettings` getters, every 2 s; all values once, then only changes, tagged with the active Unity scene (`XGame` = menus, `FightScene`, `NormalScene` = Babylonia). |

Healthy lines:

```
[lucia] OK graphics options requested: renderscale = 2, 5 setting override(s) in ascnet-graphics.ini; applying once IL2CPP is ready
[lucia] OK render scale detours: RenderPIplineQualityManger.SetupExtraRenderScale at 0x..., EnableExtraRenderScale at 0x...
[lucia] OK render scale: RenderPIplineQualityManger.GetRenderScale() = 2
[lucia] OK graphics override ShadowSetting.CascadeSize: 2048 -> 4096 (scene="XGame"; re-applied whenever the game resets it)
[lucia] OK render scale: the game asked for 1 (scene="FightScene"); using 2
[lucia] OK render scale: the game switched the extra render scale off (scene="NormalScene"); kept on
[lucia] OK overlay: D3D11 Present hook installed; Ctrl+Shift+G toggles
[lucia] OK graphics options: the game is quitting; detached from IL2CPP and stopped touching the game
```

Every problem is a `FAILED` line and only that line is skipped; the game keeps its own value:
`FAILED ascnet-graphics.ini: line N: ...` (syntax, unknown key, `renderscale` out of range),
`FAILED graphics override X.Y: ... has no field ...; it has: ...` (lists the real names), `... is not a valid ...`,
`... is a divisor (1 = full resolution, 2 = half); 0 would crash the renderer` (any `*Downsample*` field below 1: the
SSAO pass divides the screen width by `SSAOSetting.Downsample`), `FAILED render scale ...` (detour or method missing),
`FAILED overlay ...` (the game runs without the overlay), `FAILED ... exit detection unavailable ...`. On quit `lucia`
catches `UnityEngine.Application.Internal_ApplicationQuit`, stops touching the game and detaches its thread from
IL2CPP; `il2cpp_shutdown` waits for attached threads, so without the detach the game would hang on exit.
Overrides change what the renderer is asked to do; a value the game never ships (e.g. huge shadow maps) can still cost
GPU time or render oddly, so change one key at a time. In the dump, `<threw>` means a getter is not ready yet (early
passes) and `<Name>` is an object that is named, not walked. Unity's `lodBias`, `maximumLODLevel`, texture limit,
anisotropic filtering and `maxQueuedFrames` accessors are stripped from the 4.8 build and cannot be read or set.

## Optional trace (developers)

`ASCNET_PATCH_TRACE=1` (the Launcher clears it, so set it only when running `PGR.exe` by hand) adds
`[lucia] OK lookup XRemoteConfig: assembly=... class=XRemoteConfig` / `[lucia] FAILED lookup XRemoteConfig ...`
(`XRemoteConfig` is a metadata dump only; no hook is installed on it and routing does not depend on it),
`remote-config ...` field/method dumps and the first 64 `InternalSetUrl call url=...` lines.
