# DinoStage API evidence (VS 1.22.7 decompiled, dec_proj)

Read-only sweep 2026-11-07 (explore-audit). Short paths: dec_proj/... = %USERPROFILE%/vs_scratch/dec_proj/...
Line numbers are 1-based. VERIFIED = read in source. INFERRED = reasoned from read code. NOT FOUND = zero grep hits across VintagestoryAPI, VSEssentials, VSSurvivalMod, VSCreativeMod, Vintagestory, VintagestoryLib.

## Q1. Vanilla mover: ItemEntityMoverTool (VERIFIED, full file read, 237 lines)

- class ItemEntityMoverTool : Item, IHeldItemOnMouseWheel | dec_proj/VSSurvivalMod/Vintagestory/GameContent/ItemEntityMoverTool.cs:12 | public, non-sealed, protected virtual hooks.
- Registered as class: dec_proj/VSSurvivalMod/Vintagestory/GameContent/SurvivalCoreSystem.cs:828 | api.RegisterItemClass("ItemEntityMover", typeof(ItemEntityMoverTool)).
- Dispatch OnHeldInteractStart :28-38 | client -> OnHeldInteractStartClient (:95-101, only sets PreventDefaultAction); server -> OnHeldInteractStartServer.
- OnHeldInteractStep :40-51 | server work at :46-49 (OnHeldInteractStepServer). OnHeldInteractStop :53-59 | server -> OnHeldInteractStopServer.
- ON GRAB, OnHeldInteractStartServer :103-128:
  - StartDisableEntityMovement(target) :109 (first action).
  - Itemstack attrs: "distance" SetDouble :112; "offsetX"/"offsetY"/"offsetZ" SetDouble :114-116; "entity-id" SetLong :117.
  - affectedEntities (instance Dictionary<long,long>, :24, not persisted), Add :127.
- EACH TICK, OnHeldInteractStepServer :130-149:
  - SpawnParticles at target :142 (cosmetic).
  - entityById.Pos.SetPos(targetPosition) :145 (Pos only; Yaw/Pitch/Roll/Motion not written).
  - GetBehavior<EntityBehaviorControlledPhysics>()?.SetState(new EntityPos(x,y,z), 0f) :146 (history only, see Q2).
  - GetBehavior<EntityBehaviorTaskAI>()?.TaskManager?.StopTasks() :147 (every tick while held).
  - Not written per tick: AnimManager, WatchedAttributes, gravity flags, Pitch, Roll, Motion.
- ON RELEASE, OnHeldInteractStopServer :151-162 -> StopDisableEntityMovement(target) :158; affectedEntities.Remove :160.
- StartDisableEntityMovement :164-179 (protected virtual):
  - TaskManager.OnShouldExecuteTask += PreventAiTask :169; TaskManager.OnExecuteTask += PreventAiTaskExecution :170; TaskManager.StopTasks() :171.
  - EntityBehaviorControlledPhysics.EnableModulesApplication = false :176.
  - target.Pos.Motion.Set(0,0,0) :177.
- StopDisableEntityMovement :181-194 (protected virtual):
  - unsubscribe both handlers :186-187.
  - EnableModulesApplication = true :192 (this is what restores gravity/motion for Land entities, see Q2).
  - Does NOT touch AnimManager, Pitch, Roll, NoGravity, WatchedAttributes.
- PreventAiTask(IAiTask) :196-199 returns false; PreventAiTaskExecution(IAiTask, IAiTask?) :201-204 returns false. Return false = block (VERIFIED via AiTaskManager ShouldExecuteTask, Q2).
- GetViewPosition :206-209. Server mouse wheel :211-228: Ctrl+wheel -> Pos.Yaw += deltaPrecise/3f :219 (yaw only); plain wheel -> "distance" :224-226. Client wheel :230-236 only SetHandled while right button held.
- Server vs client: all grab/step/release logic is server-side (side checks :30, :46, :55). Client only sets PreventDefaultAction :99.
- Client-initiated via packets: NOT SHOWN in this file. Dispatch from dec_proj/VintagestoryAPI/Vintagestory/API/Common/CollectibleObject.cs:1385 and :1553. Packet transport not traced.
- Touches AnimManager: NO. Touches tasks: only TaskManager subscriptions and StopTasks (:147, :171).
- Gap: StopTasks does not stop PathTraverser (WaypointsTraverser). See EntityBehaviorTaskAI.cs:113 in Q2.

## Q2. AnimManager, speed, hold, sync, AI suspend, gravity

Animation API:
- IAnimationManager.ActiveAnimationsByAnimCode Dictionary<string,AnimationMetaData> | dec_proj/VintagestoryAPI/Vintagestory/API/Common/IAnimationManager.cs:32 (impl AnimationManager.cs:25).
- IAnimationManager: TryStartAnimation(meta) :58; StartAnimation(AnimationMetaData) :65; StartAnimation(string configCode) :72; StopAnimation(string) :78; StopAllAnimations() :83; GetAnimationState(string) :51 (returns RunningAnimation); ResetAnimation(string) :123; OnServerTick :115.
- AnimationManager.StartAnimation(AnimationMetaData) :164; stores ActiveAnimationsByAnimCode[animdata.Animation] = animdata :204 (key = Animation name, not Code).
- AnimationManager.StopAnimation(string) :232-257: Remove(code) :242; fallback loop matching .Code :244-249; AnimationsDirty = true on server :237-239.
- AnimationManager.StopAllAnimations :259 (drops to default pose; do not use for freeze).
- AnimationManager.ResetAnimation :133-141 sets CurrentFrame = 0.
- Entity.StartAnimation(string) dec_proj/VintagestoryAPI/Vintagestory/API/Common/Entities/Entity.cs:2421; Entity.StopAnimation(string) :2430; Entity.AnimManager property :364.
- AnimationMetaData.AnimationSpeed : float = 1f | dec_proj/VintagestoryAPI/Vintagestory/API/Common/AnimationMetaData.cs:101 (public field).
- GetCurrentAnimationSpeed(walk) = AnimationSpeed * (MulWithWalkSpeed ? walk : 1) * GlobalConstants.OverallSpeedMultiplier | AnimationMetaData.cs:198-200.
- Other AnimationMetaData fields: Code :59; Animation :78; Weight :86; ElementWeight Dictionary<string,float> :94; EaseInSpeed :126; EaseOutSpeed :133; SupressDefaultAnimation :162; CodeCrc32 :191; StartFrameOnce :187.
- RunningAnimation | dec_proj/VintagestoryAPI/Vintagestory/API/Common/RunningAnimation.cs:7. Public fields: CurrentFrame float :11; Active :18; Running :23; Iterations :25; NowPlaying :27; EasingFactor :33. AnimProgress property :44.
- RunningAnimation.Progress(dt, walkspeed) :97-104: if meta.AnimationSpeed == 0f -> return (:99-102). VERIFIED: speed 0 HOLDS the frame (no advance, no easing change).
- Nonzero speed: newFrame = CurrentFrame + 30*dt*(EaseAnimationSpeed ? EasingFactor : 1) :113. Called from AnimatorBase.cs:212 (ProgressRunningAnimation :77-104).
- Hold a chosen frame: set GetAnimationState(code).CurrentFrame = f (RunningAnimation.cs:11) and AnimationSpeed = 0. Start-at-frame path: AnimatorBase.cs:259 CurrentFrame = animData.StartFrameOnce; written back on serialize at AnimationManager.cs:400.
- Server animator tick: AnimationManager.OnServerTick :433-445 -> Animator.OnFrame(ActiveAnimationsByAnimCode, dt); CalculateMatrices = !Alive || requirePosesOnServer :435.
- Limits: ClientAnimator.MaxConcurrentAnimations = 16 (ClientAnimator.cs:30); AnimatorBase.CurAnims size 20 (AnimatorBase.cs:47).

Sync and persistence:
- Server -> client: AnimationPacket | dec_proj/VintagestoryLib/Vintagestory/Common/Network/Packets/AnimationPacket.cs:36-50: CodeCrc32 per active anim (:21) and AnimationSpeed via SerializeFloatPrecise (:20). Sent when AnimationsDirty (dec_proj/VintagestoryLib/Vintagestory/Server/Systems/ServerUdpNetwork.cs:195-199).
- Client receive: AnimationManager.OnReceivedServerAnimations :277-292 maps CRC32 via entity.Properties.Client.AnimationsByCrc32 (:281-282). Only codes in entity JSON client animations are recognized on the client. New entries get speed :289; existing entries updated in second block (:300-315, speed assign :314).
- ToAttributes writes "activeAnims" tree: AnimationManager.cs:349-357.
- Save/load: Entity.ToBytes writes AnimManager.ToAttributes (Entity.cs:2093) and WatchedAttributes.ToBytes (:2067); FromBytes reads WatchedAttributes (:1995) and AnimManager.FromAttributes (:2023). Anim state persists in saves.

Animation codes available:
- Shape.Animations : Animation[] | dec_proj/VintagestoryAPI/Vintagestory/API/Common/Shape.cs:33 (Animation.Code per shape).
- EntityClientProperties: LoadedShape :53; LoadedShapeForEntity :60; Animations AnimationMetaData[] :77 (entity JSON client.animations); AnimationsByMetaCode Dictionary<string,AnimationMetaData> :79 (key Code); AnimationsByCrc32 Dictionary<uint,AnimationMetaData> :81. Filled :151, :155. | dec_proj/VintagestoryAPI/Vintagestory/API/Common/Entities/EntityClientProperties.cs
- Runtime list: entity.Properties.Client.AnimationsByMetaCode.Keys (syncable codes) or entity.Properties.Client.LoadedShapeForEntity.Animations (all shape anims).

AI suspend and resume:
- AiTaskManager | dec_proj/VSEssentials/Vintagestory/GameContent/AiTaskManager.cs:11 (sealed). ActiveTasksSlotsNumber = 8 :15; ActiveTasksBySlot :27; AllTasks :29; events OnTaskStarted :31, OnTaskStopped :33, OnShouldExecuteTask :35 (ActionBoolReturn<IAiTask>), OnExecuteTask :37.
- StopTasks() :195-206: FinishExecute(cancelled: true) on each active task, OnTaskStopped, slot = null. StopTask(int slot) :183; StopTask(string id) :171.
- ShouldExecuteTask(task) :360-376: exec &= each OnShouldExecuteTask subscriber; any false blocks. VERIFIED.
- ExecuteTask(task, slot) :85-100: exec &= each OnExecuteTask subscriber; false returns early.
- AiTaskManager.OnGameTick(dt) :39-64: if global AiRuntimeConfig.RunAiTasks false, cancels running tasks (:43-52). AiRuntimeConfig.RunAiTasks static bool = true | dec_proj/VSEssentials/Vintagestory/GameContent/AiRuntimeConfig.cs:8 (global, not per entity).
- EntityBehaviorTaskAI | dec_proj/VSEssentials/Vintagestory/GameContent/EntityBehaviorTaskAI.cs:9; TaskManager public field :11; OnGameTick :108-118 runs TaskManager only if entity.State == Active && Alive (:110). PathTraverser?.OnGameTick :113 runs regardless of StopTasks.
- PauseAllTasks / ExecutingTask / ActiveTasks property: NOT FOUND.
- Resume: unsubscribe both events (mover does it at StopDisableEntityMovement :186-187). Tasks restart on next tick.
- Entity.GetBehavior<T>() Entity.cs:1817; GetBehavior(string) :1795.

Gravity and physics (Q2 cont.):
- NoGravity / ApplyPhysics / PhysicsEnabled flags: NOT FOUND.
- Entity.ApplyGravity virtual property | dec_proj/VintagestoryAPI/Vintagestory/API/Common/Entities/Entity.cs:444-460: Habitat != Land -> (Sea/Underwater ? !Swimming : false); Land -> true.
- Override precedent: EntityChunky.cs:26 `override bool ApplyGravity => false` (dec_proj/VintagestoryAPI/Vintagestory/API/Common/EntityChunky.cs); EntityProjectileBase.cs:85 `!Stuck`; EntityProjectile.cs:11.
- PModuleGravity.Applicable | dec_proj/VintagestoryAPI/Vintagestory/API/Common/Entities/PModuleGravity.cs:20-43: Air habitat -> false; IsFlying && !Gliding -> false; IsClimbing -> false; else returns entity.ApplyGravity (:40). DoApply :45.
- EntityBehaviorControlledPhysics | dec_proj/VSEssentials/Vintagestory/GameContent/EntityBehaviorControlledPhysics.cs: EnableModulesApplication {get;set;} = true :77. SetState(pos,dt) :85-101 (history only: prevPos, prevYMotion, onGround; no position write). SetModules :108-117 (Wind, OnGround, InLiquid, InAir, Gravity, MotionDrag, Knockback). OnPhysicsTick :550-568: returns if State != Active (:553-555); SetState :566; MotionAndCollision :567; ApplyTests :568. MotionAndCollision :260-266 returns early when EnableModulesApplication false (:262-263).
- EntityBehaviorPassivePhysics | dec_proj/VintagestoryAPI/Vintagestory/API/Common/EntityBehaviorPassivePhysics.cs:30: Ticking {get;set;} = true :91; OnPhysicsTick :366-370 returns if !Ticking (:369) or State != Active; gravity branch in MotionAndCollision :238 (if entity.ApplyGravity); OnPhysicsTickCallback :74.
- Physics driver (server) | dec_proj/VintagestoryLib/Vintagestory/Server/PhysicsManager.cs: ServerTick registered :238; 1/30 s steps (:292-293); calls IPhysicsTickable.OnPhysicsTick at :984-985 and :1132-1146.
- Entity tick: Entity.OnGameTick Entity.cs:1061 iterates behaviors (:1083, :1092, :1123, :1132). Behaviors tick only for loaded entities.
- Freeze levers (INFERRED from the above): (a) EnableModulesApplication=false blocks all ControlledPhysics modules incl. gravity; (b) EntityBehaviorPassivePhysics.Ticking=false blocks passive fall; (c) override or patch Entity.ApplyGravity (Entity.cs:444) or PModuleGravity.Applicable (:20). Vanilla release re-enables (a) at ItemEntityMoverTool.cs:192 but not (b) or (c).

## Q3. Position, rotation, scale

EntityPos | dec_proj/VintagestoryAPI/Vintagestory/API/Common/Entities/EntityPos.cs:
- protected roll :16, yaw :19, pitch :22; Dimension :13 (public int); HeadYaw :31; HeadPitch :37; Motion Vec3d :40 (public).
- Roll property :110; Yaw property :125; Pitch property :140. BodyYaw is NOT on EntityPos (body yaw is EntityAgent.BodyYawServer, used in ServerUdpNetwork.cs position packet path).
- SetPos(Vec3d) :228; SetPos(double,double,double) :302; SetPos(EntityPos) :313; SetAngles(EntityPos) :326 (copies roll, yaw, pitch, HeadPitch, HeadYaw :328-332); SetAngles(roll,yaw,pitch) :339; SetYaw :352.

Entity (dec_proj/VintagestoryAPI/Vintagestory/API/Common/Entities/Entity.cs):
- Pos {get; private set} :369; ServerPos => Pos :372; SidedPos => Pos :375; PreviousServerPos :126. VERIFIED: no separate server copy of entity pos.
- TeleportTo(EntityPos, Action) :975-982: copies Pos.Yaw/Pitch/Roll from argument, then TeleportToDouble :905-925 (IsTeleport = true :919; Pos.SetPos; Motion zeroed). Other overloads: TeleportTo(int,int,int) :947; TeleportTo(Vec3d) :956; TeleportTo(BlockPos) :965.

Client rendering | dec_proj/VSEssentials/Vintagestory/GameContent/EntityShapeRenderer.cs:
- bodyPitch = (entity is EntityPlayer) ? 0 : entity.Pos.Pitch :769.
- yaw = entity.Pos.Yaw + (rotY + 90deg) :770.
- RotateX(quat, bodyPitch + rotX ...) :784 (PITCH applied; skipped for EntityPlayer only).
- RotateY(quat, yaw) :785 (YAW applied).
- RotateZ(quat, Pos.Roll + stepPitch + rotZ ...) :786 (ROLL applied).
- So renderer applies Pitch, Yaw and Roll. VERIFIED.
- Scale: float scale = entity.Properties.Client.Size :795; Mat4f.Scale :797. Second path with size multiplier :812-813.

Sync to clients:
- Server position packet | dec_proj/VintagestoryLib/Vintagestory/Server/ServerPackets.cs:320-338: Yaw :328, Pitch :329, Roll :330 (SerializeFloatPrecise), Motion, PositionVersion :337 (WatchedAttributes "positionVersionNumber"). Sent per tick via PhysicsManager.cs:742 and :990.
- Pitch and Roll DO sync to clients via entity position packets. VERIFIED.

Scale:
- Visual scale = Properties.Client.Size (EntityClientProperties) :795. Properties assigned Entity.cs:687 (Properties = properties); whether instances share one EntityProperties object per type is UNVERIFIED.
- "scale" WatchedAttribute: NOT FOUND.
- Only runtime size path in API: Entity.cs:695-707 (client side only, when Properties.Client.SizeGrowthFactor != 0): WatchedAttributes "grow" listener sets Properties.Client.Size = client.Size + grow.age * SizeGrowthFactor.
- Runtime-safe server-side scale: NOT FOUND. Client-only visual, shared-object risk UNVERIFIED.

## Q4. Commands, selection, spawn, entity codes

- ICoreAPI.ChatCommands : IChatCommandApi | dec_proj/VintagestoryAPI/Vintagestory/API/Common/ICoreAPI.cs:21.
- IChatCommandApi | dec_proj/VintagestoryAPI/Vintagestory/API/Common/IChatCommandApi.cs:8: Create() :14; Create(name) :16; Get(name) :18; GetOrCreate(name) :20. Parsers via CommandArgumentParsers | dec_proj/VintagestoryAPI/Vintagestory/API/Common/CommandArgumentParsers.cs.
- Server example | dec_proj/VintagestoryLib/Vintagestory/Server/CmdTime.cs:23 cmdapi.Create("time").RequiresPrivilege(Privilege.time).HandleWith(cmdGetTime); BeginSub("set") :38 -> HandleWith(cmdTimeSet).
- Client example | dec_proj/VSEssentials/Vintagestory/GameContent/WeatherSystemCommands.cs:633 capi.ChatCommands.Create("weather").HandleWith(CmdWeatherClient). Also dec_proj/VintagestoryLib/Vintagestory/Client/NoObf/SystemCalendar.cs:18.
- Look-at entity: IPlayer.CurrentEntitySelection (EntitySelection) | dec_proj/VintagestoryAPI/Vintagestory/API/Common/IPlayer.cs:37. EntitySelection class | dec_proj/VintagestoryAPI/Vintagestory/API/Common/EntitySelection.cs:6 (.Entity). Usage: dec_proj/VintagestoryAPI/Vintagestory/API/Common/EntitiesArgParser.cs:197-202.
- Ray helper: IWorldAccessor.RayTraceForSelection(Vec3d from, float pitch, float yaw, float range, ref BlockSelection?, ref EntitySelection?, ...) | dec_proj/VintagestoryAPI/Vintagestory/API/Common/IWorldAccessor.cs:593.
- IWorldAccessor: GetEntitiesAround(Vec3d, horRange, vertRange, matcher) :311; GetNearestEntity :340; GetEntityById(long) :347; SpawnEntity(Entity) :280; immediate-send variant documented :283 (member name on :284 not captured); EntityTypes :162; GetEntityType(AssetLocation) :255. File: IWorldAccessor.cs (same folder as Q4 API).
- Entity creation: ClassRegistry.CreateEntity(Type) dec_proj/VintagestoryLib/Vintagestory/Common/ClassRegistry.cs:339; CreateEntity(string className) :355 (Lib, not API).

## Q5. Time and weather

Time (dec_proj/VintagestoryLib/Vintagestory/Server/CmdTime.cs; calendar dec_proj/VintagestoryLib/Vintagestory/Common/GameCalendar.cs):
- /time set: Create("time") :23; BeginSub("set") :38 -> HandleWith(cmdTimeSet). Hour set: server.GameWorldCalendar.SetDayTime(hour.Value / 24f * HoursPerDay) :239; SetDayTime(hours) :253.
- GameCalendar.SetDayTime(float wantHourOfDay) GameCalendar.cs:372 (public void).
- Freeze time: server.GameWorldCalendar.SetTimeSpeedModifier("baseline", 0f) CmdTime.cs:281. Interface IGameCalendar.SetTimeSpeedModifier(string, float) | dec_proj/VintagestoryAPI/Vintagestory/API/Common/IGameCalendar.cs:180.
- Read: GameCalendar.HourOfDay :104; TotalHours :114; TotalDays :116.

Weather (dec_proj/VSEssentials/Vintagestory/GameContent/):
- Root: WeatherSystemCommands.cs:84 sapi.ChatCommands.Create("weather").RequiresPrivilege(Privilege.controlserver).
- "set" (all loaded regions): CmdWeatherSet :211-224 -> ReloadPatterns(api.World.Seed); SetWeatherPattern(code, updateInstant: true). Registered via HandleWith(CmdWeatherSet).
- "setir" (current region): CmdWeatherSetir :169-186; SetWeatherPattern(code, updateInstant: true) at :183; TickEvery25ms(0.025f) :184.
- Wind: CmdWeatherSetw registered :133; wind pattern WordRange :130-132.
- WeatherSimulationRegion.SetWeatherPattern(string code, bool updateInstant) WeatherSimulationRegion.cs:397 (public bool).
- WeatherSystemServer.ReloadConfigs() WeatherSystemServer.cs:70; LoadConfigs(bool isReload) :81; SendWeatherStateUpdate(WeatherState) :180.
- ForceWeatherAndWind: NOT FOUND.

## Q6. Player teleport and look direction

- Server teleport: Entity.TeleportTo(EntityPos) Entity.cs:975-982 (copies Yaw/Pitch/Roll; IsTeleport :919).
- EntityPlayer: Onplrteleported(double x, double y, double z, Action, ICoreServerAPI) dec_proj/VintagestoryAPI/Vintagestory/API/Common/EntityPlayer.cs:1402; WatchedAttributes.GetIntAndIncrement("positionVersionNumber") :1426 (enclosing method ~1420-1427, name not captured).
- Client position ingest | dec_proj/VintagestoryLib/Vintagestory/Server/Systems/ServerUdpNetwork.cs:156-172 HandlePlayerPosition: drops packets with PositionVersion < WatchedAttributes "positionVersionNumber" (:163-166), then Pos.SetFromPacket (:172). So client pitch/yaw is authoritative unless the server bumps positionVersionNumber.
- SetCameraAngles: NOT FOUND. Server-side angle write: EntityPos.SetAngles(roll,yaw,pitch) EntityPos.cs:339; Pos.Yaw setter used by mouse wheel (ItemEntityMoverTool.cs:219).
- Creative fly: EntityControls.IsFlying (EntityPlayer.cs:503; PModuleGravity.cs:31 disables gravity while flying). Server fly toggle API: NOT FOUND.
- Spectator check: EntityPlayer.cs:207.
- INFERRED: a server-side Pos.Pitch on a player is overwritten by the next client position packet unless positionVersionNumber is bumped (EntityPlayer.cs:1426). Not tested.

## Q7. Persistence

- ICoreAPICommon | dec_proj/VintagestoryAPI/Vintagestory/API/Common/ICoreAPICommon.cs: DataBasePath :21; GetOrCreateDataPath(string) :112; StoreModConfig<T>(T, string) :122; StoreModConfig(JsonObject, string) :130; LoadModConfig<T>(string) :140; LoadModConfig(string) :149.
- GamePaths.ModConfig => Path.Combine(DataPath, "ModConfig") | dec_proj/VintagestoryAPI/Vintagestory/API/Config/GamePaths.cs:24. DataPath = %AppData%/VintagestoryData (GamePaths.cs:70).
- Entity WatchedAttributes: SyncedTreeAttribute | Entity.cs:225; persisted in Entity.ToBytes :2067, read in FromBytes :1995. Attributes (non-synced) persisted :2079.
- TreeAttribute setters: SetBool :267, SetInt :285, SetLong :302, SetDouble :319, SetFloat :337, SetString :349; getters GetBool :453, GetInt :468, GetDouble :560, GetFloat :575, GetString :590, GetTreeAttribute :653 | dec_proj/VintagestoryAPI/Vintagestory/API/Datastructures/TreeAttribute.cs.
- SyncedTreeAttribute.RegisterModifiedListener(path, Action) :27; MarkPathDirty(path) :66 | dec_proj/VintagestoryAPI/Vintagestory/API/Datastructures/SyncedTreeAttribute.cs.
- Vanilla usage pattern: Entity.cs:381-387 (onFire via GetBool/SetBool); Entity.cs:499 (entityDead via SetInt).

## Q8. Mod structure and csproj (own mods, VERIFIED)

Template: %USERPROFILE%/vs_scratch/.claude/worktrees/dinostage-build/sparkvs/src/Mod/ (SparkVS 0.2.0).
- SparkVS.csproj (1725 bytes): Microsoft.NET.Sdk; TargetFramework net10.0; AssemblyName SparkVS; Nullable enable; LangVersion latest; AppendTargetFrameworkToOutputPath false; TreatWarningsAsErrors true; CopyLocalLockFileAssemblies true; GenerateDependencyFile false.
- VSDir property default = %USERPROFILE%/AppData/Local/Programs/Optimum. NOTE: workspace CLAUDE.md says the game is at AppData/Roaming/Vintagestory. Verify before reuse.
- References (HintPath under VSDir, Private false): 0Harmony (Lib/0Harmony.dll), OpenTK.Graphics, OpenTK.Core, OpenTK.Mathematics, VintagestoryAPI (VintagestoryAPI.dll). No VSEssentials or VSSurvivalMod reference.
- modinfo.json (378 bytes, parsed by python): type code; modid sparkvs; name SparkVS; version 0.2.0; side Universal; requiredOnClient false; requiredOnServer false; dependencies game 1.22.0.
- ModSystem pattern: sparkvs/src/Mod/SparkVSSystem.cs:19 public sealed class SparkVSSystem : ModSystem, IRenderer; StartServerSide(ICoreServerAPI) override at :105.
- DinoStage code that subclasses ItemEntityMoverTool or uses AiTaskManager, EntityBehaviorControlledPhysics or EntityShapeRenderer would need VSSurvivalMod.dll and VSEssentials.dll (namespace Vintagestory.GameContent). INFERRED from source location, not built.
- DinoStage worktree: only dinostage/PROGRESS.md and sparkvs/ exist; no DinoStage csproj yet.

## Q9. Harmony availability and hook candidates

- 0Harmony / HarmonyLib used by own mods (under %USERPROFILE%/vs_scratch/): batch-apply-perf/betterruins-spawnchance-fix/src/BetterRuinsSpawnChanceFixModSystem.cs; chunklod-channelfix/src/ChannelFixModSystem.cs; collision-box-pass/codiag/src/CoDiagModSystem.cs and ServerHooks.cs; collision-box-pass/projreach/src/ProjectileReachModSystem.cs; knapster-crash/src/KnapsterChannelGuardSystem.cs; loadcrash-20261029/offthreadguard/src/OffThreadMipGuardMod.cs; loosestonesupportguard/src/Guard.cs. Also SparkVS (csproj reference 0Harmony).
- Hook candidates (names only):
  - ItemEntityMoverTool.StopDisableEntityMovement (ItemEntityMoverTool.cs:181, protected virtual) and OnHeldInteractStopServer (:151).
  - ItemEntityMoverTool.StartDisableEntityMovement (:164) and OnHeldInteractStepServer (:130).
  - EntityBehaviorControlledPhysics.EnableModulesApplication setter (ControlledPhysics.cs:77) and MotionAndCollision (:260).
  - EntityBehaviorPassivePhysics.Ticking setter (PassivePhysics.cs:91) and OnPhysicsTick (:366).
  - Entity.ApplyGravity getter (Entity.cs:444) and PModuleGravity.Applicable (PModuleGravity.cs:20).
  - AiTaskManager.ExecuteTask (AiTaskManager.cs:85) and ShouldExecuteTask (:360); EntityBehaviorTaskAI.OnGameTick (:108) and PathTraverser tick (:113), since StopTasks does not stop pathing.
  - AnimationManager.StopAnimation (AnimationManager.cs:232), StopAllAnimations (:259), OnReceivedServerAnimations (:277).
- Non-Harmony options: public events OnShouldExecuteTask and OnExecuteTask for AI; EnableModulesApplication and Ticking for physics; virtual ApplyGravity in an Entity subclass.

## Open items (not resolved in this sweep)
- Client-to-server transport for held-item interaction: not traced.
- Whether EntityProperties is shared per type (affects Properties.Client.Size writes): not traced.
- PathTraverser stop API (WaypointsTraverser): no public stop found in a name grep.
- Player pitch override behavior (positionVersionNumber): INFERRED only.


## DinoStage 1.1.0 reset mechanism

Added 2026-11-08 (1.1.0 implementation). DP = %USERPROFILE%/vs_scratch/dec_proj; API files under DP/VintagestoryAPI/Vintagestory/API/Common/. Line numbers 1-based. VERIFIED = re-read in source this session. Full reasoning with the 1.0.0 line numbers is in RESEARCH_ANIMRESET.md.

Root cause of stacked poses in 1.0.0 (VERIFIED)
- AnimationManager.StopAnimation :232-257 only removes the key from ActiveAnimationsByAnimCode (:242). It never stops the RunningAnimation.
- AnimatorBase.OnFrame :146-163 marks a RunningAnimation whose key is missing Active=false and applies OnActivityStopped (default Rewind, Animation.cs :47), so it stays Running.
- ProgressRunningAnimation :185-214 only stops it when Iterations < 0 and EasingFactor < 0.002 (:194-197). With speed 0, RunningAnimation.Progress returns early (RunningAnimation.cs :97-100), EasingFactor never decays, so the stop test is never reached.
- The stuck animation stays in CurAnims (:207); ClientAnimator.calculateMatrices :263-390 rebuilds every pose from CurAnims each frame and ElementPose.Add sums all of them, so the old pose is added to the new one for ever.
- Proven by test (selftest "stuck proof"): StopAnimation alone removes the dictionary key and the RunningAnimation is still running; AnimSet.Clean ends it.

The fix: two steps in this order (AnimSet.Clean, used by server Freeze.AssertAnim and client ClientHold.HoldEntity)
1. am.StopAnimation(key) for every dictionary key not in {base} U {layers}. Order matters: AnimatorBase.OnFrame :118-128 re-activates any RunningAnimation whose key is still in the dictionary, so a Stop() issued while the key is present is undone on the next frame.
2. RunningAnimation.Stop() (RunningAnimation.cs :159-168) on every Active or Running RunningAnimation (am.Animator.Animations, IAnimator.cs :23) not in the keep set. It sets Active/Running false, CurrentFrame 0, Iterations 0, EasingFactor 0 immediately, no easing. It is the only immediate hard stop; no animator-wide reset exists (IAnimator.cs :6-49, AnimatorBase.cs).

Client default idle (VERIFIED, the part the 1.0.0 research left open)
- EntityAgent.cs :583-652 restarts the client default animation (config entry with triggeredBy.defaultAnim, :608) on every client tick. A non-default Average-blend active animation makes the same block STOP the default idle (:633-638, unless alwaysRunIdle). SupressDefaultAnimation is read from the config entries.
- AnimationPacket.cs :46-47 skips DefaultAnim-triggered entries, so the server never sends it and cannot stop it. AnimationManager.OnReceivedServerAnimations :277-332 keeps client entries with TriggeredBy and WasStartedFromTrigger (:327) and mutates shared config metas (speed from the packet, :289 and :314).
- 1.1.0 therefore holds on the client from a Harmony postfix on EntityAgent.OnGameTick (id dinostage.client) plus the 50 ms listener: HoldEntity cleans to {base} U {layers}, starts the default anim itself when it is the base and not running, zeroes the speed of the held metas (remembering the idle speed), and Release restores the idle speed and hard-stops orphans on unstage.
- Client-side StopAnimation does not set AnimationsDirty (server only, AnimationManager.cs :238), so the client clean is local and idempotent.

Freeze default capture
- Default animation = Properties.Client.Animations entry with TriggeredBy.DefaultAnim == true (EntityAgent.cs :608; AnimationTrigger.cs :33); key = its Animation field lower-cased. Freeze.CaptureAnim holds it at frame 0, speed 0, Source "default"; with no default entry the old highest-Weight capture is kept (Source "captured"). Reason: the highest-Weight capture picked "alert" (weight 10, AnimationMetaData.cs :86) from an AI task.

Layers
- Layers are ordinary additional entries in ActiveAnimationsByAnimCode held at their stored frame (same Freeze.AssertOne path as the base). Blend is whatever the animation's own config meta says; poses of elements keyed by both animations combine in ClientAnimator.calculateMatrices per element weights. Cap 4 is a DinoStage rule, not an engine limit.

Test method note
- ServerAnimator derives from ClientAnimator but only computes matrices/poses when `!Alive || entity.requirePosesOnServer`. The tests set requirePosesOnServer before SpawnEntity (Ops.TestRequirePoses) and compare real ElementPose values (degX/Y/Z, scale, translate) from AnimatorBase.RootPoses.
