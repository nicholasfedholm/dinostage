# DinoStage family shot list
Nick, 2026-11-07, updated same evening with Nick's picks. Reference composition: render3.png (in-game, not the model-editor render)
Status: Nick agreed the Abelisauridae, Carcharodontosauridae, Dromaeosauridae, Compsognathidae, Herrerasauridae, Oviraptorosauria, Ceratopsidae, Hadrosauroidea, Iguanodontia, Ankylosauria, Pachycephalosauria, Stegosauria, Diplodocoidea and Ichthyosauria shots as written. Changes and additions below for Tyrannosauridae, Spinosauridae, Macronaria and Plesiosauria. Allosauridae, Therizinosauridae, Ornithomimosauria, Plateosauridae, Pterosauria and Mosasauridae were not commented on, left as proposed.

## The reference composition, named: Prey POV / Loom

What makes that shot work, and what DinoStage has to be able to do to repeat it:

1. Low camera, just behind and above a small foreground animal, back of it to camera. The viewer is the prey.
2. Main animal centre-top, mid-action and airborne / rearing, mouth open, limbs spread, head partly out of the top of frame. Too big to fit, that is the point.
3. Its shadow falls on the flat ground between the two animals. The shadow is the threat arriving before contact.
4. Dead flat, empty stage (dry plain), hard noon light, no trees or clutter. Silhouette and scale do all the work.
5. Huge scale contrast: foreground animal small and low, main animal looming vertical.

DinoStage features this preset needs:
- Freeze any animation at a chosen frame (attack, jump, rear, swim stroke), and hold an entity in the air with no gravity and no AI
- Pitch / roll the whole entity, not just yaw (a diving or rearing pose is a pitch)
- A second (and third) staged entity as a foreground prop, with its own frozen pose (flee, alert, graze, look-up)
- Camera preset "Prey POV": low, ~1 block up, behind actor 2, slight wide FOV, main actor framed top-centre
- Flat stage + fixed noon sun so the shadow lands readable between them, HUD off, hi-res export named by species
- **Move while frozen (Nick, 2026-11-07): the vanilla creative Entity mover tool must work on a staged animal WITHOUT breaking its freeze.** See the dedicated section at the end. Posing a shot means nudge, look, nudge again, so a mover that thaws the animal, drops it, or resets it to a default pose makes the whole preset unusable.

Variants in the list below: Loom (direct copy), Invert (defender is low/foreground, attacker recoils up top), Scale (no attack, just size), Waterline (same framing half in / half out of water).

## Per family

### Land predators

- **Tyrannosauridae — Loom, do BOTH versions (Nick's pick).**
  - **A, the classic: rex vs Triceratops.** Adult rex frozen at the bite-down frame, pitched forward, head out of top of frame. Foreground: an adult Triceratops low in frame, back three-quarter to camera, head down, frill and horns up at the rex. The textbook face-off, and the trike's frill gives the foreground real weight instead of a small prey animal.
  - **B, the prey version (as originally proposed).** Same rex pose, foreground a juvenile hadrosaur, back to camera, head up. Flat plain, noon. More scale contrast, less of a duel.
- **Abelisauridae — Loom, charge version.** Carnotaurus-type at full charge, head low instead of high, horns to camera, one front foot off the ground. Foreground prey seen from behind, caught mid-turn to flee. Lower camera than the rex shot, head-on feels faster.
- **Allosauridae — Pack Loom.** One allosaur leaping onto the flank/neck of a sauropod (foreground is the sauropod's leg and belly filling the left edge, out of focus scale), a second allosaur rearing behind it with mouth open. The only family where the foreground prop is bigger than the star.
- **Carcharodontosauridae — Loom, slicing bite.** Head swung in from the side at a sauropod neck that runs diagonally across the top of frame, teeth in. Foreground: the sauropod's front leg and the predator's shadow on the ground under the neck.
- **Spinosauridae — Fishing like a bear (Nick's pick).** Not a dino as the foreground, a fish. Spino standing in shallow water the way a grizzly stands in a salmon run: reared up, weight back, one huge claw hand frozen mid-swipe / mid-scoop, a fish caught in or flying off the claw, more fish breaking the surface in the foreground. Sail fills the top of frame, water splash and shadow under it. The claw close-up is still in the shot, it is just holding breakfast now.
- **Dromaeosauridae — Loom, the closest match to the reference.** This is almost certainly the family the reference shot is staging: feathered arms spread like wings, sickle claw raised, pounce frozen mid-air over a small ornithomimosaur / baby in the foreground. Do it at dusk-ish low sun for a long shadow.
- **Compsognathidae — Scale gag / forced perspective.** Tiny predator cannot loom, so cheat: camera on the ground, compy mid-leap very close to camera so it fills the top of frame, attacking a grasshopper / lizard prop in the foreground that reads huge. Or a pack of 5 to 6 converging on one foreground prey, shadows all pointing in.
- **Herrerasauridae — Loom, Triassic plain.** Herrerasaurus / Staurikosaurus lunging at a foreground Plateosaurus juvenile on the same dry flat stage. Keep the stage emptier and harsher than the others, it is the oldest scene in the series.

### Odd theropods

- **Oviraptorosauria — Invert, nest defence.** Not a kill shot: parent rearing upright, feathered arms spread wide like the reference, mouth open hissing, frozen between camera and a foreground nest with eggs / babies. Attacker is a small dromaeosaur low in the foreground, back to camera. Same pose, opposite meaning.
- **Therizinosauridae — Invert, the best upright poser in the series.** Reared fully upright, huge claws spread, exactly the vertical silhouette of the reference. Foreground: a small tyrannosauroid low, backing off, head up at it. No contact, all threat display.
- **Ornithomimosauria — Chase, reversed Loom.** Foreground ostrich-mimic back to camera at a dead run, neck low, one foot off the ground; the thing looming top-centre behind it is the predator (tyrannosaur or dromaeosaur). Or solo version: rearing kick defence at a dromaeosaur in the foreground.

### Herbivores, defence and display

- **Ceratopsidae — Loom, head-on.** Frill and horns filling the top of frame, head down mid-charge, front feet off the ground. Foreground: a baby ceratopsian back to camera watching, or the predator flattened low. Low camera makes the frill read as a wall.
- **Hadrosauroidea — Rear and call.** One adult reared up on hind legs, front feet off the ground, head high out of frame, calling; foreground juveniles looking up at it, predator's shadow (only the shadow, no predator in frame) falling across the ground in front of them.
- **Iguanodontia — Invert, thumb-spike defence.** Reared up, both thumb spikes forward, at a foreground allosaur / raptor low in frame, back to camera, caught deciding whether to leave.
- **Ankylosauria — Invert, tail swing.** It cannot leap, so it stays low and foreground: club mid-swing in a wide arc, body braced, and the attacker top-centre is the one airborne, knocked recoiling off it, legs splayed like the reference animal in reverse. Shadow of the club on the ground between them.
- **Pachycephalosauria — Duel.** Two of them: foreground one back to camera leaping in, second one coming down head-first from top-centre, domes about to meet. No prey needed, the collision is the shot.
- **Stegosauria — Invert, thagomizer.** Stegosaur low in the foreground, plates in silhouette, tail frozen at the top of its swing; predator behind it rearing back mouth open, about to get spiked. Camera behind the stegosaur, looking along its back.
- **Diplodocoidea — Scale + tail whip.** Foreground predator tiny at its feet looking up (the small grey animal in the reference, at real scale); the sauropod's neck rears out of the top of frame and its tail is frozen mid-whip-crack across the sky. Nothing attacks, size is the whole shot.
- **Macronaria — Scale, extreme version, plus extras (Nick asked for 2 to 3 more).**
  - **A, headless loom.** Brachiosaurus-type head and neck gone out of the top of frame entirely, one front leg like a tree trunk at the frame edge, tiny foreground theropod / baby sauropod at its feet. The only family where cropping the head out completely works better than showing it.
  - **B, the neck comes down.** The opposite crop: neck frozen in a deep arc bringing the head all the way down to camera level for an eye-to-eye close-up in the foreground, body and legs looming huge and soft behind it. Small foreground dino face to face with it.
  - **C, column walk.** Camera on the ground between its front legs, legs as columns passing on both sides, a baby macronarian weaving between the adult's legs in the foreground, looking up. Movement implied by one adult foot frozen mid-step off the ground.
  - **D, waterhole browser.** Head high pulling at a tree top / tall cycad at the frame edge, neck diagonal across the sky, small predators waiting in the foreground shade watching it, not attacking. Calm scale instead of threat.
- **Plateosauridae — Rear defence.** Reared fully bipedal (the other natural upright poser), claws forward, mouth open, over a foreground Herrerasaurus low in frame. Pairs with the Herrerasauridae shot as the same scene from the other side.

### Flyers and swimmers (need air / water stages in DinoStage)

- **Pterosauria — Loom, the literal reference if you swap the animal.** Swoop frozen at the bottom of a dive: wings spread full width across the top, mouth open, feet reaching, shadow racing ahead of it on the flat ground, small foreground animal back to camera looking up. Also a grounded version: quadrupedal pounce / wing-walk looming over a baby.
- **Mosasauridae — Waterline Loom.** Camera half at the surface: mosasaur lunging up and out, mouth open, front half airborne and pitched down at a foreground plesiosaur / big fish back to camera at the surface. On a dry stage instead: suspended swim pose, same framing, mouth open over the foreground animal, shadow underneath standing in for depth.
- **Plesiosauria — Waterline strike, plus the pig shot (do both).**
  - **A, strike.** Long neck frozen in an S strike coming down from top-centre at fish / a baby in the foreground, flippers spread wide like the reference animal's arms. Head close to camera reads best, body stays back.
  - **B, pig in its mouth (Nick's addition).** Same waterline framing, neck swung over toward camera, a vanilla Vintage Story pig held in its jaws, legs dangling. It is the foreground animal from the reference shot, promoted to victim. Deliberately absurd, and exactly the kind of cross-over shot that reads instantly to a VS player scrolling ModDB. Needs DinoStage to be able to parent / hold one staged entity in another's mouth, or at minimum position the pig precisely with the mover while both stay frozen (see Move while frozen).
- **Ichthyosauria — Breach.** The airborne moment is natural here: full dolphin-style breach arc over the water, pitched down for re-entry, foreground a fish school / small marine reptile at the surface with its shadow under the falling animal. Dry-stage version: swim pose at the same pitch, foreground animal below it.

## Batch note

Every shot above is the same DinoStage preset with different actors, poses and one of four variants (Loom / Invert / Scale / Waterline), so a family page series would look consistent: same low camera, same flat stage, same noon shadow, species swapped. That consistency is what makes it usable as ModDB page art across all 24 packs.

## Move while frozen: Entity mover support (Nick, 2026-11-07)

Question: creative mode already has an Entity mover tool (vanilla 1.22: it holds an entity in front of you, mousewheel changes its distance). Can DinoStage let that tool move a staged dino while it stays in the same freeze?

Answer: yes, and it should reuse the vanilla mover instead of inventing a second mover. Position and pose are separate data in the engine, so this works PROVIDED the freeze is built the right way:

- **Freeze = paused animation, not stopped animation.** Store the staged state on the entity (WatchedAttributes, e.g. `dinostage/staged`, anim code, animation progress/frame, pitch, roll, yaw, scale). Pause by holding the RunningAnimation at that progress with speed 0 and suppressing the default animation. Do NOT freeze with StopAllAnimations: that drops the pose back toward the default/bind pose, and a move would then have nothing to preserve.
- **Freeze = AI and physics held separately.** TaskAI tasks cleared (the DinoShowcase method), plus no gravity / no passive-physics fall while staged, so an airborne or rearing animal does not drop when it is picked up or set down. The Showcase 1.0.3 lesson applies: a periodic re-freeze sweep already exists for chunk reloads, and it must re-apply the STORED pose, never respawn or reset the animal.
- **The mover only changes Pos.** While the mover is holding a staged entity, DinoStage should: keep AI cleared, keep physics/gravity off, keep the animator paused at the stored progress, and ignore the mover's normal release behaviour if that behaviour wakes the animal up (vanilla already had to special-case this once: in 1.22 fish no longer bolt away when the entity mover is used, so a held entity trying to resume AI on release is a real, known failure mode, not a hypothetical).
- **On release, re-assert, do not restart.** Write the new position, then re-apply the same anim code at the same stored progress, same pitch/roll, same scale. No animation restart, no progress reset, no AI restore.
- **Pitch/roll need their own nudge controls.** The vanilla mover moves and (with distance on the wheel) positions an animal, but a Loom pose also lives in its whole-body pitch. DinoStage still needs small pitch/roll/yaw nudge keys or commands that adjust the stored staged rotation while the freeze holds, ideally usable while the mover has the animal held.

Acceptance tests for a build:
1. Freeze a dromaeosaur mid-pounce (airborne, pitched), move it 5 blocks left and 2 up with the Entity mover, release: same frame, same pitch, still airborne, does not fall, does not start walking.
2. Same test after walking away and back (chunk reload): pose is the stored one, not a default standing pose.
3. Freeze two actors (plesiosaur + pig positioned in its mouth), move the plesiosaur: the pig arrangement can be re-nudged with the mover afterwards without the plesiosaur thawing.
4. Unstage restores normal AI, gravity and animations cleanly, animal behaves normally afterwards.
