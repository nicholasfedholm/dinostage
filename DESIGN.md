# DinoStage DESIGN

All citations are in API_EVIDENCE.md (dec_proj, 1.22.7). Short form: file:line there.

## Mod boundary
Separate mod, modid `dinostage`, Universal side (server logic + a small client part). No dependency on Test Toolbox: spawning is `/stage spawn <code>` (World.GetEntityType, ClassRegistry.CreateEntity, World.SpawnEntity, IWorldAccessor.cs). Override of the default not needed: nothing in the API evidence forces shared state with Test Toolbox.

## Why a client part is needed
Server AnimationPacket carries only CRC32 + AnimationSpeed per active animation (AnimationPacket.cs:36-50), not the frame. A client receiving speed 0 would hold frame 0, not the stored progress. So the client part reads the entity's WatchedAttributes tree `dinostage` and sets `GetAnimationState(code).CurrentFrame` (RunningAnimation.cs:11) with speed 0 (RunningAnimation.Progress returns early at speed 0, :99-102). Server also sets CurrentFrame so saves keep it (Entity.cs:2093).

## Freeze model (stored state, never respawn)
State tree `dinostage` on entity.WatchedAttributes (synced + persisted, Entity.cs:225, :2067): staged, anim, frame, yaw, pitch, roll, noPhys. Freeze = pause the one animation at a stored frame (AnimationSpeed 0), stop other animations individually (StopAnimation(code), AnimationManager.cs:232), never StopAllAnimations (:259, drops to default pose).
- AI: subscribe TaskManager.OnShouldExecuteTask/OnExecuteTask returning false + StopTasks (AiTaskManager.cs:35-37, :195, :360-376), same method the mover uses. Pathing is NOT stopped by StopTasks (EntityBehaviorTaskAI.cs:113): also stop PathTraverser (worker verifies member) and zero Motion.
- Physics: ControlledPhysics.EnableModulesApplication=false (:77) and PassivePhysics.Ticking=false (:91). Verified: no NoGravity flag exists (NOT FOUND), so these two switches are the levers.
- Handlers/flags are runtime-only, so the server re-asserts from the stored tree: on entity load (Event.OnEntityLoaded), after chunk reload, every 50 ms for staged ids.

## Mover (ItemEntityMoverTool) interplay
Verified writes: grab StartDisableEntityMovement :164-179 (AI handlers, StopTasks, EnableModulesApplication=false, Motion 0), each tick Pos.SetPos + SetState + StopTasks :145-147, release StopDisableEntityMovement :181-194 sets EnableModulesApplication=true (:192) and unsubscribes AI handlers. It never touches AnimManager, Pitch, Roll. Ctrl+wheel changes Pos.Yaw only (:219). Server-side only, so a server Harmony patch is enough.
Guard: Harmony prefix/postfix on StartDisableEntityMovement and StopDisableEntityMovement (protected virtual, :164/:181).
- Grab: mark entity as held (no angle re-assert while held, so Ctrl+wheel yaw works).
- Release postfix: adopt the entity's current yaw (mover may have changed it), keep stored frame/pitch/roll, then immediately re-assert freeze (EnableModulesApplication=false, AI handlers back, Motion 0). The 50 ms ticker is the fallback. Position is whatever the mover left; no fall happens because physics is re-disabled in the same call.
Fallback if the patch fails to apply: ticker only; DinoStage placement commands are the supported path. REPORT.md records which was tested.

## Features and API path
- select/actors/clear: IPlayer.CurrentEntitySelection (IPlayer.cs:37) or RayTraceForSelection (IWorldAccessor.cs:593), GetNearestEntity (:340). Any entity code.
- spawn: unknown code reported; GetEntityType(AssetLocation) (:255).
- place/rotate/nudge: Pos.SetPos, Pos.Yaw/Pitch/Roll (EntityPos.cs:110-140), plus BodyYaw for EntityAgent. Pitch and Roll are applied by the renderer (EntityShapeRenderer.cs:784-786) and synced (ServerPackets.cs:328-330): pitch/roll SHIP, verified in source, in-game visual check is a TEST.md step.
- scale: NOT SHIPPED (reduced). Visual scale is entity.Properties.Client.Size (EntityShapeRenderer.cs:795) and Properties is assigned by reference (Entity.cs:687); writing Size would resize every entity of that type (shared object, worker to confirm). No per-entity scale attribute exists (NOT FOUND). Flagged in REPORT.md.
- pose: animation list from Properties.Client.AnimationsByMetaCode.Keys (EntityClientProperties.cs:79) and LoadedShapeForEntity.Animations (:60); play, speed, progress, hold, release restores AI/physics/animation via StopAnimation + clearing stored tree.
- scene save/load: JSON in GetOrCreateDataPath("dinostage") (ICoreAPICommon.cs:112), weather via WeatherSimulationRegion.SetWeatherPattern (:397), time via GameCalendar.SetDayTime (GameCalendar.cs:372). Actors re-found by stored entity id, else nearest of code, else respawned.
- time/weather/noon: as above; `/stage light noon` = 12h + clearsky.
- camera loom: server computes player pose; teleport via Entity.TeleportTo(EntityPos) (Entity.cs:975-982). Player pitch/yaw: client position packets are authoritative unless positionVersionNumber is bumped (ServerUdpNetwork.cs:163-166, EntityPlayer.cs:1426), INFERRED; implementation bumps it. If angles do not take effect in game, README documents manual aim (REPORT.md flags).
- HUD hints toggle: chat feedback on/off.

## Gate 2 recovery
`/stage release` and `/stage clear` clear the tree, restore EnableModulesApplication/Ticking true, unsubscribe AI handlers, restart the held-off animations to defaults, and verify (selftest reports flags).

## Test strategy
Headless dedicated server in an isolated data dir with an isolated Mods copy; a `/stage selftest` driver exercises the real mover's protected methods via a subclass/reflection (same writes as vanilla), measures deltas, logs to file. Client-side frame display (CurrentFrame on the client) and camera angles cannot be checked headless: manual TEST.md steps.
