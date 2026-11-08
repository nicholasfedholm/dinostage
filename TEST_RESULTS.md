# DinoStage 1.0.0 headless load test results (2026-11-07)

Final run: `selftest` (87 PASS / 0 FAIL) then `ext` (93 PASS / 0 FAIL), one server session, zip out\dinostage_1.0.0.zip (sha256 35afc9a74c8c..., the one the final run loaded).
Raw logs: test\logs\server-main.log, test\logs\selftest-20261107-204427.txt, test\logs\ext-20261107-204545.txt (copies of test\srvdata).

## Setup (isolated)
- Server: Roaming\Vintagestory\VintagestoryServer.exe, --dataPath test\srvdata, --port 43397, --ip 127.0.0.1, --addModPath test\mods, fresh world seed dstest7731, creativebuilding. Launcher: test\tools\launch.py (stdin pipe, own pid only; process exited by itself via the autorun `exit` word, no kill needed).
- test\mods: dinostage_1.0.0.zip, LoP-Core-1.2.0.zip (lopcore), LoP-Dromaeo-1.0.19.zip (dromaeosauridaepatched-utahraptor-adult-male = theropod), LoP-Plesiosauria-1.0.1.zip (plesiosauria-plesiosaurus-adult-male). Vanilla pig-eurasian-adult-male and bear-black-adult-male. Mod load order in log: game, dinostage, lopcore, creative, survival, dromaeosauridaepatched, lopplesiosauria.
- Trigger: no player and no stdin, so env DINOSTAGE_AUTORUN=test\autorun.txt (words: selftest ext exit) is read in StartServerSide and runs 8 s after RunGame phase. Console `/stage selftest` and `/stage selftest2` also exist.
- Harmony: log line `[DinoStage] Mover patch applied.` and `Mover guard: applied (Start prefix+postfix, Stop postfix)`. PASS "mover grab marks entity held" in every suite.
- Live data check: file listing (relative path, size, mtime) of VintagestoryData before/after: Mods 320 files identical, whole data folder 36231 files identical (test\live_*_before/after.txt). Foreign VintagestoryServer pid 19500 was not present at launch and was never touched.

## Headless facts that shaped the test (not mod bugs)
1. A server with no player never ticks entity physics (entity IsTracked=0; ControlledPhysics.Ticking stays false). Even a never-frozen control pig hung in the air. Entities also go Inactive without a player.
2. Test-only fixes: Ops.TestAlwaysActive sets AlwaysActive on test spawns; Ops.StartPump drives the REAL EntityBehaviorControlledPhysics.OnPhysicsTick/AfterPhysicsTick (2 x 1/30 s per 50 ms tick) for test entities. So every "no fall" result below is the game's own gravity code held off by the freeze, and every "gravity resumes" result is the same code. A never-frozen control pig fell 8 blocks to ground (Y 113, onGround) in the same run.
3. WorldManager.DefaultSpawnPosition throws NRE headless with no player (WorldAPI.cs:242). Selftest used it. Fixed with Ops.SpawnXZ (test code only, not shipped behavior).

## Results

### (i) Theropod frozen airborne, pitched, anim held, moved via real mover, 10 s (selftest suite lop, A = utahraptor)
Frozen: idle, 60 frames, frame 29.5 (progress 0.5), speed 0, pitch 20, roll 15, heading 90. Real `new ItemEntityMoverTool()` StartDisableEntityMovement, 20 SetPos/SetState/StopTasks steps (+5 X, +2 Y), Ctrl+wheel yaw +0.5 rad at step 10, StopDisableEntityMovement. Deltas after 200 ticks (10 s):
| quantity | delta | tol | result |
|---|---|---|---|
| X (+5.0 expected) | 0 | 0.02 | PASS |
| Y (+2.0, no fall) | 0 (max over 200 ticks 0) | 0.02 | PASS |
| Z | 0 | 0.02 | PASS |
| frame 29.5 | 0 (max 0) | 0.01 | PASS |
| pitch 20 / roll 15 | 0 / 0 | 0.001 rad | PASS |
| yaw (90 + wheel, adopted on release) 118.648 deg | 0 | 0.001 rad | PASS |
| ctrl physics modules off, passive n/a, not on ground | as expected | - | PASS |
| held during grab (guard), yaw not re-asserted while held | PASS | - | PASS |
Release call: ctrlModules stayed false in the same call (Harmony postfix). Caveat: the server re-asserts frame every 50 ms, so "frame held" proves no stray reset, not an independent hold. Client display is manual.
The same suite ran for pig/bear (vanilla suite) with identical results.

### (ii) Chunk reload re-apply (ext phase R)
What was done: real `UnloadChunkColumn(16000,16000)` (entity left the world, sys.Staged dropped it) then real `LoadChunkColumnPriority` (OnLoaded fired). UnloadChunkColumn does NOT persist entities (0 entities after reload), so persistence was replicated by hand: Entity.ToBytes before unload, then CreateEntity + Entity.FromBytes + SpawnEntity after. This enters via Event.OnEntitySpawn, not OnEntityLoaded (same handler OnEntityIn serves both); SpawnEntity re-ids (11 -> 12). A true chunk-load OnEntityLoaded was NOT exercised: UNVERIFIED.
Result, new entity object, measured 2 s after reload vs baseline: X 0, Y 0, Z 0 (tol 0.02), frame 29.5 delta 0 (0.01), pitch/roll/yaw 0 (0.001), ctrl off, AI hooked, should-handler count = before-freeze + 1 (no duplicates), still airborne. PASS (12 checks).
BUG FOUND AND FIXED here: first run showed Y -0.072 after reload (entity fell one physics tick before the 50 ms ticker re-asserted). Fix: DinoStageSystem.OnEntityIn now calls Freeze.Assert immediately on load/spawn of a staged entity. Re-run: Y delta 0.

### (iii) Plesiosaur + pig two-actor precision (ext phase P)
P frozen pitch 10 roll -8 yaw 45 frame 29.5; G frozen pitch -12 roll 6 yaw 270 frame 14.75, both airborne. Pig moved with the real mover (+3, +1.5, 0, 20 steps): G X/Y/Z delta 0 (tol 0.001); P pos/frame/pitch/roll/yaw all delta 0 (tol 0.001). Plesiosaur nudged (0,+1,+2) with the /stage nudge writes: P delta 0, G delta 0. Pig re-nudged (-1,0,+1) and 5 s wait (pump on): G final pos delta 0, frame/pitch/roll/yaw delta 0; P still at nudged Y 118.59 (no fall), frame/angles delta 0, ctrl/passive off for both. 30 PASS. Control pig fell to ground. Note nudge here is the same writes as NudgeCmd (no player headless to run the command).

### (iv) Unstage both (ext phase U)
Immediately after Ops.ThawEntity: ctrl modules true, passive n/a (neither entity has PassivePhysics; mirrors ctrl), AI hooked false, OnShouldExecuteTask subscriber count back to the before-freeze value (P 1, G 1, delta 0), tree cleared, not in sys.Staged, held anim speed not stuck at 0, pitch/roll reset to 0 (P and G). After 3 s: both gravity resumed (P Y 118.59 -> 116 onGround, G 119.09 -> 113 onGround), G AI task active (1) and animation active (1). Animations restored: G yes.
INCONCLUSIVE for plesiosaur AI/animation: P had 0 tasks and 0 animations 23 s after unstage, but a fresh never-frozen plesiosaur spawned at the same spot also got none (and the one at another spot had anims but no task). Out-of-water plesiosaur AI behaviour, not attributable to DinoStage; check recorded as outcome-equals-control (pOk=False, control=False), which is weak. Verify in game with a water spot.
Selftest suite lop B (plesiosaur thaw) also: ctrl on, passive on, hook cleared, handlers equal baseline, task check logged as info only.

### (v) Bear + LoP species scene round trip (ext phase S)
Bear (frame 8.7 = progress 0.3, pitch 8 roll -4 yaw 135) and utahraptor (frame 41.3 = progress 0.7, pitch -6 roll 9 yaw 310). Both held anim speed 0 (checked on the live AnimationMetaData). SceneIO.Save, thaw + Die(Removed) both, SceneIO.Load (both respawned as new ids 20, 21), 4 s: all of X/Y/Z (tol 0.02), frame (0.01), pitch/roll/heading (0.001) deltas 0, staged true, held anim speed still 0. PASS. (Load also set hour 8 and weather altocumulus in the throwaway world, as designed.) LoP species loaded headless fine, no substitution.

## Code changes made during the test (src\DinoStage)
- DinoStageSystem.cs: OnEntityIn asserts the freeze immediately (real fix, see ii); TestAutorun hook (env var, no effect unless set).
- Ops.cs: SpawnXZ, TestAlwaysActive, physics pump (test only; default off).
- SelfTest.cs: use Ops.SpawnXZ, Done callback, handler baseline instead of 0, passive n/a handling, Y check relative to freeze moment, plesiosaur task check info-only.
- ExtTest.cs (new) + Commands.cs `/stage selftest2`.
- Rebuilt 0 errors, re-packed with pack.py (modinfo.json + DinoStage.dll).

## Not verified
- Client frame hold (ClientHold), camera, shadows, pitch/roll render: needs a client.
- OnEntityLoaded path from a genuine chunk load; entity id preservation.
- Plesiosaur AI/animation resume (needs water).
- Mover behavior with a real player, UDP position packets; real-time physics thread (pump emulates it, same code, different thread timing).
- Selftest creative-world only; weather set touches loaded regions only.
