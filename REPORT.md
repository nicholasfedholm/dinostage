# DinoStage 1.0.0 REPORT

## Deliverable
- out/dinostage_1.0.0.zip, 45122 B, SHA256 35afc9a74c8ca45ef633e66bc8cb2e1a3a363164a87551c018bf77cc3821c058 (modinfo.json 579 B, DinoStage.dll 112640 B). Same zip the final test run loaded.
- Docs: README.md, TEST.md, DESIGN.md, API_EVIDENCE.md, BUILD_NOTES.md, TEST_RESULTS.md, SHOTLIST.md, PROGRESS.md. Source: src/DinoStage/.
- GitHub: https://github.com/nicholasfedholm/dinostage (public), source commit 721586c57f4a6a47dbf16e57d0f75a27933b0e03 pushed to main, author Nick. Release v1.0.0 with the zip: https://github.com/nicholasfedholm/dinostage/releases/tag/v1.0.0. Scrub: 0 hits for both private-identifier patterns in the staged tree (6 absolute user paths replaced with %USERPROFILE% in the staged copy only). REPORT.md was added to the repo in a follow-up commit.

## What works (headless, verified)
- Freeze by pausing one animation at a stored frame; AI blocked (handlers + StopTasks); physics held via ControlledPhysics.EnableModulesApplication=false and PassivePhysics.Ticking=false; state in entity WatchedAttributes tree `dinostage`; 50 ms re-assert plus immediate assert on entity load. Pitch and roll supported (renderer applies them, EntityShapeRenderer.cs:784-786, synced ServerPackets.cs:328-330).
- Harmony guard on ItemEntityMoverTool Start/StopDisableEntityMovement applied ("Mover patch applied." in log).
- Scene save/load/list, time, weather, noon light, camera loom (angles inferred), spawn/select for any entity code.

## Engine refused / reduced
- Per-entity scale NOT shipped: no per-entity scale attribute; Properties.Client.Size is a client render value, hitbox/selection/shadow would not follow (EntityShapeRenderer.cs:795). /stage scale replies with the evidence.
- No NoGravity flag exists; the two physics switches above are the lever.
- StopTasks does not stop pathing (EntityBehaviorTaskAI.cs:113); handled separately (BUILD_NOTES.md).
- AnimationPacket carries no frame (AnimationPacket.cs:36-50); a small client part applies the held frame.
- ReloadPatterns is internal, so weather reload is skipped.

## Load test (isolated headless server, own dataPath, port 43397, throwaway world)
selftest 87 PASS / 0 FAIL; ext test 93 PASS / 0 FAIL. Tolerances: position 0.02, frame 0.01, angles 0.001 rad.
- (i) Utahraptor frozen airborne (pitch 20, roll 15, frame 29.5), moved +5 X +2 Y via the real mover methods (reflection), 10 s: X/Y/Z/frame/pitch/roll/yaw deltas all 0. Never-frozen control pig fell 8 blocks. Same for pig and bear.
- (ii) Chunk reload: real UnloadChunkColumn/LoadChunkColumnPriority, entity re-created via ToBytes/FromBytes/SpawnEntity: deltas 0. Bug found: Y drop -0.072 (ticker ran after one physics tick); fixed with immediate assert in OnEntityIn; rerun delta 0. Caveat: the OnEntityLoaded path itself was not exercised.
- (iii) Plesiosaur + pig, 30 checks: after pig mover move, plesiosaur nudge, pig re-nudge, all deltas 0, plesiosaur stayed frozen, no fall.
- (iv) Unstage: flags true, AI handlers back to baseline, trees cleared, pitch/roll 0, gravity resumed (plesiosaur Y 118.59 to 116, pig 119.09 to 113). Pig AI task and animation resumed. Plesiosaur AI/animation resume INCONCLUSIVE: an unfrozen control plesiosaur out of water also had no tasks. Needs a water spot.
- (v) Bear + utahraptor scene, anim speed 0 at progress 0.3/0.7, save, clear, reload as new ids: position/heading/frame/pitch/roll deltas 0, speed still 0. LoP species loaded headless, no substitution.
- Live Mods folder unchanged: 320 files in Mods, 36231 in VintagestoryData, listings (path, size, mtime) identical before/after.

## Unverified (need a client)
Client frame-hold display, pitch/roll render, camera loom angles, plesiosaur resume in water, genuine OnEntityLoaded chunk load, mover with a real player. Listed as manual steps in TEST.md. The server re-asserts the frame every 50 ms, so "frame held" shows no stray reset, not an independent hold. build.ps1 is untested (zip made with pack.py). README example entity codes are placeholders.
