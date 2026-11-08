# DinoStage 10 minute in-game photo session

Setup: creative world with LoP packs (a tyrannosaur, a herbivore, a Triceratops if available) and Test Toolbox. Mod zip in Mods, you have `controlserver`. Replace `<...>` with real entity codes (use `/entity spawn` autocomplete). F4 hides HUD, F12 takes screenshots.

## A. Texture workflow (about 3 min)
1. Spawn two dinos with Test Toolbox (`/spawnGen` etc.) near each other on flat ground. Check: both appear.
2. `/stage light noon` then `/stage time 18` (golden hour). Check: chat confirms hour; sky is low and warm.
3. `/stage select <rex code>`, `/stage list-anims`, `/stage anim <roar code> 0 0.5`, `/stage freeze`. Check: rex stops walking, jaws open mid-roar and hold.
4. `/stage select <herbivore code>`, `/stage anim <idle or graze code> 0 0.3`, `/stage freeze`, then `/stage nudge fwd 20` to stand it beside the rex. Check: pose holds after nudge.
5. Wait 10 seconds. Check: frame does not drift (frame hold display, MANUAL, see section G).
6. `/stage scene save texA`. Check: chat reports 2 actors saved. F12 screenshot, note file name.
7. `/stage clear`, then `/stage scene load texA`. Check: both reappear at the same pose and spot, frozen. F12 screenshot.
8. Compare the two screenshots. Success: same poses, same lighting.

## B. Mixed all-creatures scene (about 3 min)
1. `/stage clear`. Spawn a carnivore: `/stage spawn <carnivore code>`, `/stage anim <run code> 0 0.4`, `/stage pitch 10`, `/stage freeze`.
2. `/stage spawn <herbivore code>`, `/stage anim <run code> 0 0.6`, `/stage freeze`, `/stage nudge fwd 40`. Check: herbivore is ahead of the carnivore, both mid-stride, off AI.
3. `/stage spawn bear-black-adult-male`, `/stage anim <rear or stand code> 0 0.7`, `/stage freeze`, `/stage pitch -5`. Check: bear reared up and still.
4. `/stage actors`. Check: three actors listed.
5. `/stage scene save mixed`, `/stage clear`, `/stage scene load mixed`. Success: all three return in one scene, same poses, frozen. Screenshot.

## C. Hero shot with camera loom (about 2 min)
1. `/stage clear`, `/stage light noon`.
2. Spawn the rex first (actor 1): `/stage spawn <rex code>`, `/stage anim <bite code> 0 0.55`, `/stage pitch 20`, `/stage freeze`, `/stage nudge up 10`.
3. Spawn the Triceratops (actor 2): `/stage spawn <triceratops code>`, `/stage anim <idle code> 0 0.2`, `/stage freeze`, place it about 8 blocks in front of the rex.
4. `/stage camera loom 3.5 1`. Check: you are low behind actor 2. Success: rex looms top of frame; if the view angle did not apply, aim by hand (MANUAL, section G). F4, F12.

## D. Mover flow (about 1 min)
1. Pose and freeze any actor (for example the rex at pitch 20).
2. Pick it up with the vanilla Entity mover, move it a few blocks and up 2, Ctrl+wheel to turn it, release.
3. Success: same frame, same pitch, still airborne, does not fall, does not walk.
4. `/stage nudgepitch 5` and check it tilts further.

## E. Unstage check (about 1 min)
1. `/stage clear` (or `/stage thaw all`).
2. Success: actors fall to the ground if airborne, then walk and animate again within a few seconds (AI resumes). Pitch and roll return to level.
3. Do this for a land animal. Plesiosaur needs water (section G).

## F. Quick fail checks
- `/stage scale` replies "not shipped" with the reason (expected).
- `/stage spawn nonsense` reports an unknown code (expected).

## G. Client-only items NOT verified headless (manual checks)
1. Frame hold display: with the client part loaded, the frozen frame is visibly the same after 10 s and after leaving and re-entering view. Fail if the model snaps to a default pose.
2. Pitch and roll render: `/stage pitch 30`, `/stage roll 20`, the body visibly tilts and the tilt survives a relog. Source says it renders (EntityShapeRenderer.cs:784-786); not seen in game.
3. Camera angles: after `/stage camera loom`, check the view faces actor 1. Inferred only; if not, aim by hand and note it.
4. Plesiosaur AI resume in water: spawn a plesiosaur in water, freeze, `/stage clear`, check it swims again. The headless run was inconclusive (a never-frozen control plesiosaur out of water also had no tasks).
5. Chunk reload: freeze a dino, walk 200+ blocks away and back. It should still hold its stored pose.
