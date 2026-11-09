# DinoStage 1.1.0 report

Clean animation reset by default, stacking as an opt-in layer. Headless tests pass; in-game behavior is Nick's pass (see TEST.md section E2 and G6).

## Reset mechanism chosen
Root cause of the stacking: `StopAnimation` only removes the dictionary key. The old `RunningAnimation` stays Running in the animator's `CurAnims`, because speed 0 returns early from `Progress` and the ease-out branch needs `Iterations != 0` (default OnAnimationEnd is Repeat). Every active animation sums into the element pose, so the old pose keeps contributing.

Fix (server `Freeze.AssertOne` and client `ClientHold`, shared in `AnimSet.cs`):
1. `AnimationManager.StopAnimation(key)` for every active key that is not in {base} + {layers}.
2. Then `RunningAnimation.Stop()` on every RunningAnimation not in that set. Order matters: reversed, `AnimatorBase` reactivates the stopped animation.
3. Then the existing target logic (start at stored progress, speed 0).

Evidence (file:line in decompiled 1.22.7; re-verified by the implementer, full list in API_EVIDENCE.md section "DinoStage 1.1.0 reset mechanism" and RESEARCH_ANIMRESET.md): AnimationManager.cs 232-257 (StopAnimation), RunningAnimation.cs 97-100 and 159-168 (speed-0 return, Stop), AnimatorBase.cs 118-128, 185-214 (reactivate, stuck case), ElementPose.cs 54-102 (per-frame clear and sum), EntityAgent.cs 583-652 (client-only default anim, restarts each tick), AnimationPacket.cs 44-52 (default anim skipped in sync). No animator-wide reset exists in the engine (NOT FOUND), so no element-level zeroing was needed.

Default anim: client.animations entry with `triggeredBy.defaultAnim == true` (runtime `Properties.Client.Animations`, `TriggeredBy?.DefaultAnim`). It is client-only, so ClientHold also carries a Harmony postfix on `EntityAgent.OnGameTick` for staged entities; the 50 ms client tick is the fallback.

Extra finding: a non-default Average-blend active animation makes the client stop the default idle each tick (EntityAgent.cs 610, 633-638), so base=default idle plus an Average layer needs the client to restart idle itself (handled).

Real bug found by the tests: a RunningAnimation kept from the AI's own trot still pointed at the AI's old meta (speed 1.2), so a speed-0 hold advanced between asserts. `Freeze.AssertOne` now points it at the held meta.

## Changes per file (src\DinoStage)
- AnimSet.cs (new): clean/stop logic. AnimOps.cs (new): `/stage anim`, `animlayer`, `resetpose` logic. AnimProbe.cs (new): test probes.
- Freeze.cs: clean on assert, default-animation capture at frame 0 (log line names it, "(default)" tag), clean thaw.
- ClientHold.cs: client clean, layers, Harmony postfix.
- StageState.cs, SceneIO.cs, Ops.cs: layers (cap 4, optional fields, 1.0.0 scenes load), commands wiring.
- Commands.cs: `/stage animlayer`, `/stage resetpose`, /help text; `anim` clears layers; 5th layer is an error message.
- SelfTest.cs, ExtTest.cs: new tests. DinoStage.csproj, modinfo.json: 1.1.0; csproj HintPath uses `$(USERPROFILE)`. ClientShot.cs: default shots dir is now Documents\DinoStage\shots (was a hard-coded personal path).
- pack.py, build.ps1: 1.1.0 zip. Docs: README.md, TEST.md (E2 a-d, G6), API_EVIDENCE.md (appended).

## Tests (isolated dedicated server, own data folder, `python -I test\tools\launch.py "selftest ext exit" 540`)
| | before (1.0.0) | after (1.1.0) |
|---|---|---|
| selftest | 87 pass / 0 fail | 191 pass / 0 fail |
| ext | 93 pass / 0 fail | 116 pass / 0 fail |
| total | 180 / 0 | 307 / 0 |

New coverage: stuck-state proof (StopAnimation alone leaves the speed-0 anim running; AnimSet.Clean ends it); clean-switch equals fresh freeze of the same animation on real engine element poses; layer stacking differs from base alone only on the layer's keyframed elements; layer cap (5th refused with message); mover keeps base and layers; resetpose leaves heading/pitch/roll untouched; freeze with no explicit anim holds the default at frame 0; hand-written 1.0.0 scene loads; scene with layers round-trips. Final run was repeated on the final zip after the scrub rebuild (run: selftest-20261108-223624, ext-20261108-223815). Live Mods folder and data folder listings were identical before and after the first full run.

## Release zip
`dinostage\out\dinostage_1.1.0.zip`, 60123 bytes, md5 5a48c3a7c6b0cc9d2f718e1da022a255, sha256 fe4f8c4c5e0793d45d2452a40673bfcece19095fd103a674a20340006ac2ad4e. Entries: modinfo.json, DinoStage.dll only. Built with DebugType=none and PathMap so the DLL carries no local paths (scan for the Windows username in UTF-8 and UTF-16: 0 hits). Not installed into the live Mods folder.

## Unverified (Nick's in-game pass)
- Client rendering, Harmony postfix ordering vs the DinoStage tick; a LoP subclass overriding `EntityAgent.OnGameTick` could bypass the patch (50 ms tick fallback).
- Real chunk unload/reload with layers (existing chunk-reload phase covers the base only; layers covered via the scene respawn path).
- The ext test used the Utahraptor because no Pycnonemosaurus adult male is installed in the test set; in-game case E2(a) needs the Pycnonemosaurus.
- Note: the earlier 1.0.0 zip was built with default debug settings and its DLL may carry the local PDB path; not changed here.

## Publish
See PROGRESS.md for the repo, commit and release link.
