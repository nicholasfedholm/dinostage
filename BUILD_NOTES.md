# DinoStage 1.0.0 build notes

Build: `dotnet build src/DinoStage/DinoStage.csproj -c Release` succeeded (0 errors) against the Optimum install
(%USERPROFILE%\AppData\Local\Programs\Optimum; the brief's Roaming\Vintagestory has no DLLs).
Zip: out\dinostage_1.0.0.zip (modinfo.json + DinoStage.dll, no pdb), made by pack.py (build.ps1 does the same;
the harness blocked running powershell from this session, PERMISSION class, so pack.py was used. build.ps1 is untested).
Not run in game: nothing here was executed against a live server. Selftest is compiled, not run.

## Verified members (file:line, from game source / decompiled API)
Compile success confirms existence and signature of every member used (the 4 earlier-unverified ones too:
EntityProperties in Vintagestory.API.Common, IAnimationManager.Animator, IClassRegistryAPI.CreateEntity, BlockPos(x,y,z,dim)).
Line evidence where read:

VERIFIED PathTraverserBase.Stop() public abstract PathTraverserBase.cs:78
VERIFIED WaypointsTraverser.Stop() Essentials\WaypointsTraverser.cs:503
VERIFIED EntityBehaviorTaskAI.PathTraverser public WaypointsTraverser? EntityBehaviorTaskAI.cs:13
VERIFIED EntityAgent.BodyYaw EntityAgent.cs:107
VERIFIED EntityAgent.BodyYawServer EntityAgent.cs:112
VERIFIED server position packet sends agent.BodyYaw ServerPackets.cs:349
VERIFIED ServerMain entity.Initialize(type.Clone()) ServerMain.cs:2889,2944
VERIFIED ClientSystemEntities entityType.Clone() ClientSystemEntities.cs:351
VERIFIED EntityProperties.Clone deep-clones Client EntityProperties.cs:155-218
VERIFIED EntityClientProperties.Clone clones animations EntityClientProperties.cs:172-206
VERIFIED AnimationMetaData.Clone shallow AnimationMetaData.cs:276
VERIFIED AnimNowActive sets CurrentFrame=StartFrameOnce AnimatorBase.cs:248-261
VERIFIED RunningAnimation.Progress returns early at speed 0; CalcBlendedWeight skips at speed 0 (RunningAnimation)
VERIFIED Privilege.controlserver API\Server\Privilege.cs:108
VERIFIED IGameCalendar.Add(float) :213, HoursPerDay :57; SetDayTime not on interface (GameCalendar.cs:372-376 replicated)
VERIFIED WeatherSimulationRegion.SetWeatherPattern :397, TickEvery25ms :225; WeatherSystemServer.ReloadConfigs :70
VERIFIED SyncedTreeAttribute.GetIntAndIncrement :95
VERIFIED AiTaskManager.OnShouldExecuteTask / OnExecuteTask events, StopTasks (compile + source)
VERIFIED ItemEntityMoverTool.StartDisableEntityMovement/StopDisableEntityMovement protected virtual (Entity) ItemEntityMoverTool.cs:164-194
VERIFIED ItemEntityMoverTool per-step writes ItemEntityMoverTool.cs:145-147, wheel yaw :219
VERIFIED IWorldManagerAPI.LoadChunkColumnPriority(ChunkLoadOptions), DefaultSpawnPosition int[] (compile)
VERIFIED IBlockAccessor.GetTerrainMapheightAt, Entity.Die(EnumDespawnReason.Removed), EntityTypes list (compile)

## Open items
(a) Stopping the traverser: PathTraverserBase.Stop() public abstract; WaypointsTraverser.Stop() sets Active=false, clears
    Controls/ServerControls.Forward, zeroes WalkVector, resets counters. Freeze calls it every assert.
(b) Properties is per-entity: Initialize(type.Clone()) on server and entityType.Clone() on client, deep clone of Client and
    animations. So Size/AnimationMetaData are per-entity. A client-side scale would be per entity, but hitbox, selection box
    and shadow would not follow.
(c) Yaw: EntityAgent.BodyYaw and BodyYawServer are settable; EntityPos has no BodyYaw. Set Pos.Yaw, BodyYaw, BodyYawServer.

## Reduced / not shipped
- `/stage scale`: not shipped, replies with an error carrying the evidence in (b). Upgrade path: client-side Properties.Client.Size
  per entity plus server collision/selection box rewrite; needs in-game testing.
- Selftest drives `new ItemEntityMoverTool()` protected methods by reflection; if construction fails it simulates the same writes
  (then Harmony hooks cannot fire and the guard checks are expected FAIL/skipped, which the log states).
- Client frame hold (ClientHold) cannot be tested headless; manual in-game check. Animation codes not in the entity JSON client
  animations are not recognised by the client.
- Camera angles inferred: yaw=atan2(-dx,-dz), pitch=asin(dy/len).
- WeatherSystemServer.ReloadPatterns is internal; not used.
- Selftest LoP pair: used only if a non-game-domain tyranno/dromaeo/plesio-like type exists in EntityTypes; otherwise logged and skipped.

## Risks
- Nothing executed in game: all behavior (freeze under mover use, thaw, scene round trip, client frame hold) is unproven until `/stage selftest` runs.
- Harmony postfix on a protected virtual: if the target method is overridden elsewhere the patch may miss; ticker fallback re-asserts within 50 ms.
- Weather set touches loaded regions only; clear maps to pattern code `clearsky` (may not exist in modded sets).
- Server AnimationPacket carries no frame; client display depends on the optional client part being installed.
