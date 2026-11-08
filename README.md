# DinoStage 1.0.0

Creative-mode pose and stage tool for Vintage Story 1.22.x screenshots. Freeze any entity (LoP dinosaurs, vanilla bear, pig, anything with an entity code) at a chosen animation frame, pitch and roll it, hold it in the air with no AI and no gravity, move it with the vanilla Entity mover without thawing it, and save or load whole scenes. Works on mixed groups (predator, prey, vanilla animals) in one arrangement.

## Install
1. Put `dinostage_1.0.0.zip` in your Mods folder. Do not unpack it.
2. Join a creative world (or any world where you hold the `controlserver` privilege). Mod is Universal: the server part does everything; the client part is optional and only makes the held frame visible on a client.
3. Nothing else is required. No dependency on Test Toolbox: `/stage spawn` spawns by entity code.

## Pairing with other tools
- Screenshots: the vanilla key F12. F4 hides the HUD. DinoStage never takes the shot itself.
- Moving a posed animal: the vanilla creative Entity mover tool. The freeze survives grab, move, Ctrl+wheel yaw, and release (a Harmony guard re-asserts the freeze in the release call; the 50 ms server ticker is the fallback).
- Optional VS Director: use it for the camera path and framing; DinoStage supplies the frozen actors, noon light and loom camera start point.

## Command reference
All commands are `/stage <sub> ...` and need the `controlserver` privilege. Actors are a per-player list; commands act on the current actor (marked `>` in `/stage actors`).

| Command | Meaning |
|---|---|
| `/stage select [code]` | Add the entity you look at (or the nearest of `code` within 30 blocks) as an actor |
| `/stage actors` | List actors |
| `/stage actor <n>` | Choose current actor (1 based) |
| `/stage clear` | Deselect and fully thaw every actor (unstage) |
| `/stage spawn <entitycode> [x y z]` | Spawn at look point (or x y z) and select |
| `/stage list-anims [actor#]` | List animation codes and frame counts |
| `/stage freeze [all] [phys]` | Hold the actor: AI off, physics off, animation paused |
| `/stage thaw [all]` | Restore AI, physics, animation |
| `/stage anim <code> [speed] [progress]` | Choose the pose animation; giving a progress holds it (speed 0) |
| `/stage speed <v>` | 0 holds the current frame |
| `/stage progress <frame or 0.0..1.0 or n%>` | Hold at a frame. A decimal with a dot is a fraction, `50%` is percent, a plain integer is a frame |
| `/stage place <dx> <dy> <dz>` | Move to an offset from the player's feet |
| `/stage placeabs <x> <y> <z>` | Move to absolute coordinates |
| `/stage heading <deg>` | Set heading |
| `/stage pitch <deg>` / `/stage roll <deg>` | Set whole-body pitch or roll |
| `/stage nudge <fwd\|strafe\|up> <amount>` | Move in 0.1 block steps |
| `/stage nudgepitch <deg>` / `/stage nudgeroll <deg>` | Adjust pitch or roll by degrees |
| `/stage scene save\|load\|list\|delete <name>` | Save all actors plus hour and weather; load respawns missing actors and re-freezes |
| `/stage time <0..24>` | Set hour (use vanilla `/time stop` to stop the clock) |
| `/stage weather <code\|clear>` | Set weather pattern (`clear` = `clearsky`) |
| `/stage light noon` | Hour 12 and clearsky (hard shadows) |
| `/stage camera loom [back] [up]` | Place the player behind actor 2 (default 3.5 back, 1 up) looking at actor 1 |
| `/stage hints on\|off` | Chat hints |
| `/stage scale` | Not shipped, replies with the reason |
| `/stage selftest`, `/stage selftest2` | Scripted self tests (developer use, console friendly) |

## Camera loom and noon light
The "Prey POV" composition: actor 1 is the looming animal, actor 2 is the small foreground animal.
1. `/stage light noon` for a short hard shadow between the two.
2. Pose both, then `/stage camera loom` (or `/stage camera loom 3 0.8` to go closer and lower).
3. If the view angle does not snap onto actor 1, aim by hand from the placed spot. Position is exact, angle is inferred (see Reduced features).
4. Widen FOV in settings, F4 for no HUD, F12.

## Mover flow
1. `/stage freeze` the posed actor.
2. Pick it up with the vanilla Entity mover, move it, Ctrl+wheel for yaw, release.
3. Frame, pitch, roll stay as stored; the new yaw is adopted. It does not fall and does not walk off.
4. Fine pitch and roll adjustments: `/stage nudgepitch`, `/stage nudgeroll`.

## Reduced features
- Scale: NOT shipped. The visual size is per entity (`Properties.Client.Size`, EntityShapeRenderer.cs:795) but the hitbox, selection box and shadow would not follow, and no per-entity scale attribute exists in the engine. `/stage scale` replies with this. Evidence is in API_EVIDENCE.md and REPORT.md.
- Camera angles are inferred: yaw = atan2(-dx, -dz), pitch = asin(dy/len). The server asks the client to take them by bumping `positionVersionNumber`; this was not verified with a real client. Manual aim is the documented fallback.
- Client frame hold needs the client part; without it a client shows the default pose for the paused animation.

## Worked examples
### (a) LoP rex vs herbivore
```
/stage light noon
/stage spawn <rex code>
/stage list-anims
/stage anim <roar or bite code> 0 0.5
/stage pitch 15
/stage freeze
/stage spawn <herbivore code>
/stage anim <graze or alert code> 0 0.3
/stage freeze
/stage place 6 0 0
/stage actor 1
/stage place 6 1.5 8
/stage camera loom
```
Entity codes vary by pack. Use `/entity spawn` autocompletion or the code printed by your LoP pack; `list-anims` shows the animation codes.

### (b) Mixed scene: predator, prey, vanilla bear and pig
```
/stage spawn <carnivore code>
/stage anim <run code> 0 0.4
/stage pitch 10
/stage freeze
/stage spawn <herbivore code>
/stage anim <run code> 0 0.6
/stage freeze
/stage spawn bear-black-adult-male
/stage anim <rear or stand code> 0 0.7
/stage freeze
/stage spawn pig-eurasian-adult-male
/stage freeze
/stage scene save mixed
/stage clear
/stage scene load mixed
```
All four actors come back at their stored frame, pitch, roll and heading, still frozen. Verified headless for bear plus a LoP theropod (TEST_RESULTS.md).

### (c) Hero shot: rex vs Triceratops (SHOTLIST, Tyrannosauridae A)
```
/stage light noon
/stage spawn <rex code>
/stage anim <bite code> 0 0.55
/stage pitch 20
/stage freeze
/stage spawn <triceratops code>
/stage anim <head-down or idle code> 0 0.2
/stage freeze
/stage actors
/stage camera loom 3.5 1
```
Actor 1 must be the rex and actor 2 the Triceratops (check `/stage actors`; actors are numbered in the order they were added, so spawn the rex first). Rex head out of the top of the frame, Triceratops low in front, flat dry plain. Full shot list: SHOTLIST.md.

## Unstage
`/stage thaw` (one actor) or `/stage clear` restores physics, re-enables AI and stops the held animation.
