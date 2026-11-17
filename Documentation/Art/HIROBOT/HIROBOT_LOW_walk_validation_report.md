# HIROBOT_LOW – Walk-in-Place Loop: Validation Report

Scene: `HaroldHiro _ForAnimationClaude RETOPO.blend` (saved; previous save is kept as `.blend1`).

## Setup
- **Mesh:** `HIROBOT_LOW` is unchanged. Geometry, UVs, material and baked textures are untouched. Backup mesh: `HIROBOT_LOW_mesh_backup` (fake user).
- **Rig:** `HIROBOT_LOW_Rig`, 27 bones, quaternion rotation.
  - Non-deforming: `root` (never keyed) and `pelvis` (vertical bob only).
  - Deforming: 25 bones, one per rigid piece. They match the existing vertex groups 1:1, every vertex is weighted 1.0 to a single bone, and no automatic weights were used. Pieces can only move rigidly.
- **Joint pivots:**
  - Hip: centre of the thigh capsule's top sphere.
  - Knee: centre of the capsule's bottom sphere, so the shin turns around the ball instead of sliding off it.
  - Ankle: at the boot collar height.
  - Shoulder, elbow and wrist: inside the overlapping shells.
- **Action:** `HIROBOT_LOW_Walk_Loop` (fake user).
  - 30 fps; keys on frames 1–31, with frame 31 equal to frame 1.
  - Cycles modifier on all 55 F-curves; the action is set to cyclic. Playback range is 1–30.

## Motion
- **Legs:** analytic 2-bone IK on a computed treadmill foot path.
  - Stride ±45 mm, lift 28 mm; each foot is on the floor for 22 of 31 frames.
  - The sole stays level, and the knee bends forward.
- **Pelvis:** bobs 5.9 mm vertically. It is set as high as possible while the knees stay slightly bent, so legs never snap straight.
- **Torso:** twists ±3°, rolls ±1.5° and leans 2.5° forward. The head cancels 85 % of the torso rotation to stay stable.
- **Arms:** swing ±12° against the opposite leg, with a 4–8° elbow bend.

## Per-frame checks (frames 1–31, every frame)
| Check | Result |
|---|---|
| Rest pose vs original mesh | 0 m deviation |
| Piece rigidity (max edge-length change) | 0.0002 mm, i.e. float noise |
| New intersections between pieces that don't touch at rest | none |
| Joints that touch at rest losing contact (gaps) | none (35 joint contacts kept) |
| Lowest vertex in any frame | 0.000 mm (no floor penetration) |
| Lateral foot drift | 0 mm |
| Root / rig / mesh object world position | fixed at (0, 0, 0) |
| Frame 31 vs frame 1 | 0 m difference |
| Largest vertex move per frame: loop boundary vs median vs max | 9.2 / 22.2 / 34.6 mm (no pop at the boundary) |

Minimum clearances over the cycle:
| Pair | Clearance |
|---|---|
| shin ↔ torso | 5.1 mm |
| boot ↔ boot | 49 mm |
| shin ↔ shin | 45.6 mm |
| forearm ↔ torso | 58.9 mm |
| upper arm ↔ head | 29 mm |
| palm ↔ thigh | 157 mm |

Visual check in Material Preview: front, side, back and perspective views at frames 1, 4, 8, 12 and 20.

## Problems found and fixed
1. **Shin top clipped into the belly at mid-swing** (frames 8–11 / 23–26) with the first settings (45 mm lift, 1.5° lean). Fixed by lowering lift to 28 mm, stride to 45 mm and setting lean to 2.5°, which leaves 5.1 mm clearance.
2. **The original high-poly `HIROBOT` sits in the same spot**, so it would overlap the animated robot. It is hidden in viewport and render, not deleted.

## Revision 2 – arms closer to the body, more sway
- **Arm position:** shoulder lowered 15° and elbow 25°, applied in the animation only (the rest pose is unchanged). The hands moved from (0.38, 0.50) to about (0.27, 0.38) in x/z, about 11 cm closer to the body and 11 cm lower.
- **Arm motion:**
  - Forward/back swing increased from ±12° to ±18°.
  - The forearm follows ±6°, lagging the upper arm by 8 % of the cycle.
  - Both arms drift ±2° to the same side, slightly behind the body's weight shift.
- **Rejected options:** larger drops (shoulder 20–40°, or 25° shoulder with 25° elbow) made the forearm or fingers touch the thigh capsule, shin or torso.
- **Re-check, all 31 frames:**
  - New intersections: none. Joint contacts lost: none. Rigidity change: 0.0002 mm.
  - Floor: 0.000 mm minimum. Loop: frame 31 equals frame 1. Root: fixed.
  - Minimum clearances: arm ↔ thigh 10.3 mm, arm ↔ torso 16.6 mm, arm ↔ shin 31.4 mm, arm ↔ boot 82.7 mm, upper arm ↔ head 39.2 mm, shin ↔ torso 5.1 mm.

## Revision 3 – more natural legs and arms, head bounce
**Legs**
- Heel strike: the boot lands 6° toe-up and rolls flat around the heel edge.
- Toe-off: it rolls 8° onto the toe edge before lifting.
- The swing path keeps the same speed as the planted foot at lift-off and touchdown, so there's no jerk. Peak boot clearance is 18.7 mm.
- The foot can never go below the floor, which is enforced against the real boot geometry.

**Pelvis**
- Turns ±2° with the stepping leg and tilts ±1°.
- Bobs 4.2 mm, with the hip heights from the turn and tilt included in the leg calculation.

**Torso and arms**
- The torso counter-turns against the pelvis (70 %) and leans 2.5° forward.
- Arms are pendulum-like: 20° forward and 13° back, lagging the legs by 6 % of the cycle.
- The elbow bends 6–18° (more on the forward swing), and the wrist trails the forearm by ±5°.

**Head**
- Bounces 5 mm along the neck and nods ±2.5° once per step, lagging the body by 9 % of the cycle.
- Tilts ±1.5° with the weight shift and keeps 80 % of its world orientation steady.
- Total head bounce: 13.8 mm.

**Rejected settings:** stronger roll (16° push-off), a bigger pelvis turn (4°) or more foot lift made the shin clip into the belly. The settings above were picked by a parameter sweep.

**Re-check, all 31 frames**
- New intersections: none. Joint contacts lost: none (head–neck included).
- Rigidity change: 0.0003 mm. Floor: 0.000 mm minimum.
- Lateral foot drift: 0.07 mm. Root: fixed. Frame 31 equals frame 1.
- Loop boundary step: 16.4 mm (median step 27.8 mm), so no pop.
- Minimum clearances: shin ↔ torso 8.3 mm, arm ↔ thigh 13.4 mm, arm ↔ torso 14.8 mm, arm ↔ shin 33.6 mm, upper arm ↔ head 36.8 mm, boot ↔ boot 48.9 mm.

## Revision 4 – weight shift and step timing
**Weight shift**
- The hips slide ±8 mm sideways over the planted foot.
- Each leg also tilts sideways in an exact calculation, so the ankle stays at its rest X (lateral foot drift 0 mm).

**Bob timing**
- The body dips just after heel strike and is highest at the passing pose: 4 mm amplitude, 8 mm total.
- It is fitted under the legs' reach limit, so knees never lock or snap.

**Torso:** dips forward an extra 0.8° on each landing.

**Head bounce**
- Now only settles down onto the neck (0–6 mm, lagging the landing) and never rises above its rest seat. Total head travel: 8 mm.
- Defect found and fixed: the up-and-down version opened a gap between head and torso at the passing pose (frames 9–13 and 24–28).

**Re-check, all 31 frames**
- New intersections: none. Joint contacts lost: none.
- Rigidity change: 0.0003 mm. Floor: 0.000 mm minimum. Root: fixed. Frame 31 equals frame 1.
- Loop boundary step: 16.2 mm (median 25.0 mm).
- Minimum clearances: shin ↔ torso 7.1 mm, arm ↔ thigh 13.3 mm, arm ↔ torso 14.8 mm, arm ↔ shin 38.9 mm, upper arm ↔ head 37.1 mm, boot ↔ boot 49.1 mm, shin ↔ shin 45.5 mm.

## Revision 5 – arms closer to the body
**Change**
- Shoulder drop stays at 15°. Elbow drop goes from 25° to 30°, and a 30° forward elbow bend is added.
- Swing is now 16° forward and 10° back (was 20° and 13°).
- The hands sit beside and slightly in front of the hips. Average palm X went from 0.292 to 0.265, about 2.7 cm closer to the body.

**Search:** about 14 combinations of shoulder drop, elbow drop, elbow bend, swing size and torso twist were tried. Every pose closer than this clipped the forearm into the thigh capsule or the fingers into the shin. That geometry is the physical limit for a clip-free swing.

**Re-check, all 31 frames**
- New intersections: none. Joint contacts lost: none. Rigidity change: 0.0003 mm.
- Floor: 0.000 mm minimum. Foot drift: 0 mm. Root: fixed. Frame 31 equals frame 1.
- Minimum clearances: arm ↔ thigh 5.7 mm, arm ↔ torso 10.2 mm, arm ↔ shin 15.0 mm, arm ↔ boot 70.9 mm, upper arm ↔ head 37.0 mm, shin ↔ torso 7.1 mm.

## Revision 6 – more humanoid gait (slight overlap allowed)
Small rigid-shell overlaps were allowed on request. The mesh still cannot distort: every vertex stays 100 % bound to one bone, and the edge-length check confirms it.

**Legs**
- Stride ±65 mm (was ±50); 12° toe push-off and 10° heel strike (were 8° and 6°).
- Deeper knee bend (reach 0.97), peak boot clearance 32 mm.

**Pelvis and torso**
- Hip turn ±4° and tilt ±2°; weight shift ±12 mm; body bob 11.9 mm.
- The torso counter-turns against the hips (80 %), leans 3° and dips 1.2° on each landing.

**Arms**
- Hang lower along the sides: shoulder 25°, elbow 20° down and 28° forward.
- Larger humanlike swing, 24° forward and 15° back, with 18° extra elbow bend on the forward swing.
- Average palm X: 0.249.

**Head:** steadied at 85 %, with 2° nod and a 6 mm settle per step.

**Overlap check**
- Method: maximum vertex penetration depth, using nearest-surface plus a ray-parity inside test, on all 30 frames.
- Only piece pairs that do not touch at rest were checked.

| Pair | Max depth | Frame |
|---|---|---|
| forearm ↔ thigh capsule (L/R) | 9.3 mm | 10 / 25 |
| forearm ↔ torso (L/R) | 7.0 mm | 18 / 3 |
| shin ↔ torso (L/R) | 6.4 mm | 19 / 4 |
| index finger ↔ shin (L/R) | 2.7 mm | 26 / 11 |

Rejected: a 35° arm drop sank the hands about 30 mm into the shins.

**Re-check, all 31 frames**
- Rigidity change: 0.0003 mm (no distortion). Rest pose deviation: 0.
- Joint contacts lost: none. Floor: 0.000 mm minimum. Foot drift: 0 mm.
- Root: fixed. Frame 31 equals frame 1. Loop boundary step: 19.6 mm (median 31.8 mm).

## Revision 7 – human gait cues
**Legs**
- Swing-foot timing: the foot peaks early in the swing, about 37 % of the way, as the knee flexes after push-off. Then the leg reaches forward and descends.
- The toe stays pointed (+8°) after push-off and pulls up before heel strike.
- The foot lands 8 mm ahead of the hips, as a human foot does.
- Feet are on the floor for 24 of 31 frames; peak swing clearance is 28.6 mm.

**Upper body**
- Shoulders counter-tilt (−1°) against the hip tilt.
- Hands drift 4° toward the centre line as they swing forward.
- Elbow at rest is 22° forward, with 10° extra bend on the forward swing, reduced from 28° + 18° to keep the forearm off the belly.

**Rejected:** a 6–10° inward hand swing with the old elbow bend pushed the forearms 14–20 mm into the torso.

**Overlap depth, all 30 frames (max)**
| Pair | Depth |
|---|---|
| shin ↔ torso | 6.9 mm |
| index finger ↔ shin | 6.6 mm |
| forearm ↔ torso | 6.3 mm |
| forearm ↔ thigh capsule | 6.2 mm |
| middle finger ↔ shin | 3.6 mm |

All are lower than revision 6 (whose maximum was 9.3 mm).

**Re-check, all 31 frames**
- Rigidity change: 0.0003 mm (no distortion). Rest pose deviation: 0.
- Joint contacts lost: none. Floor: 0.000 mm minimum. Foot drift: 0 mm.
- Root: fixed. Frame 31 equals frame 1. Loop boundary step: 18.6 mm (median 30.1 mm).

## Revision 8 – legs and steps
**Stride**
- Stride ±85 mm (was ±65), about 30 % longer.
- The planted foot now moves back at 8.4 mm per frame (0.25 m/s walk-in-place speed).

**Body height**
- The stance knee is straighter (reach 0.993), as in a human midstance.
- This raises the body and was the key to fitting the longer stride without the shin hitting the belly.

**Foot roll**
- Heel strike 12° toe-up (was 10°), toe push-off 11°.
- The swing foot peaks earlier (skew 0.6); peak clearance 32 mm.

**Hips:** turn ±6° (was ±4°), so each leg visibly reaches with its hip.

**Sweep results**
- Rejected: strides of 70–80 mm with the old crouch (reach 0.97–0.975) put the shin 10–14 mm into the belly.
- Rejected: moving the landing point further forward pushed the forearm into the thigh capsule.
- 95 mm separated an internal capsule/boot contact; 105 mm gave 8 mm of shin into the belly.

**Overlap depth, all 30 frames (max)**
| Pair | Depth |
|---|---|
| forearm ↔ torso | 6.3 mm |
| forearm ↔ thigh capsule | 6.0 mm |
| index finger ↔ shin | 5.5 mm |
| shin ↔ torso | 3.0 mm (was 6.9) |
| middle finger ↔ shin | 1.9 mm |

**Re-check, all 31 frames**
- Rigidity change: 0.0003 mm (no distortion). Rest pose deviation: 0.
- Joint contacts lost: none. Floor: 0.000 mm minimum. Foot drift: 0 mm.
- Root: fixed. Frame 31 equals frame 1. Loop boundary step: 16.2 mm (median 35.1 mm).

## Revision 9 – smoothness pass
**Method:** each part's acceleration and jerk were sampled at half-frame steps around the loop. "Spiky" means peak acceleration divided by its RMS; 1.4 is a pure sine.

**Causes found and fixed**
1. **Swing foot joins (C1, not C2):** the swing path met the planted foot with matching position and speed but not acceleration. Replaced with a quintic Hermite that matches position, velocity and acceleration at toe-off and heel strike.
2. **Swing lift profile:** the early-peak curve `w^0.6` had infinite curvature at lift-off. Replaced with a smooth time-warp and a sin⁴ bump.
3. **Floor clamp:** the hard per-sample floor clamp kicked in and out. Replaced with a soft maximum whose width fades to zero at lift-off and touchdown.
4. **Foot-roll blends:** cubic smoothstep was replaced with quintic smootherstep (no acceleration jump at the start or end of the heel and toe roll).
5. **Contact-vertex jumps:** the low-poly sole switched contact vertices during the roll, and the very short thigh amplified this about 20×. Replaced with an ideal heel/toe-edge rolling model plus a quadratic correction fitted to the real sole. Result: never below the floor (0.000 mm), at most 0.35 mm hover while rolling, exact when flat.
6. **Arm switches:** `max(0, …)` elbow bend, inward drift, and the kinked forward/back swing were replaced with smooth polynomial versions.
7. **Heel-to-flat landing:** now takes 16 % of the cycle instead of 10 %, with 10° toe-up at heel strike (was 12°). A real foot settles over about that long.

**Spikiness and jerk peak/RMS (before → after)**
| Part | Spikiness | Jerk |
|---|---|---|
| shin | 3.92 → 2.43 | 3.68 → 2.29 |
| boot | 2.62 → 1.80 | 3.88 → 2.18 |
| upper arm | 2.35 → 1.82 | 3.74 → 1.68 |
| hand | 2.08 → 1.82 | 3.34 → 1.68 |
| torso | 1.38 (unchanged) | 1.41 |
| head | 1.39 (unchanged) | 1.39 |

**Overlap depth, all 30 frames (max)**
| Pair | Depth |
|---|---|
| index finger ↔ shin | 8.5 mm |
| forearm ↔ thigh capsule | 6.8 mm |
| forearm ↔ torso | 6.2 mm |
| middle finger ↔ shin | 3.2 mm |
| shin ↔ torso | 1.6 mm |

**Re-check, all 31 frames**
- Rigidity change: 0.0003 mm (no distortion). Rest pose deviation: 0.
- Joint contacts lost: none. Floor: 0.000 mm minimum. Foot drift: 0 mm.
- Root: fixed. Frame 31 equals frame 1.

## Limitations
- **Treadmill-style walk:** the planted foot moves backward under the body at 5 mm/frame (0.15 m/s) while flat on the floor. That is how a walk-in-place cycle works; with forward root motion added in an engine, the planted foot holds still.
- **Small steps:** the robot's legs are very short (hip pivot 0.36 m above the floor), so steps are small by design. Bigger steps clip the shin into the belly.
- **No IK controls in the rig:** the IK is solved by a script and baked into keys. The rig has no IK/pole controllers for hand-tweaking; edit the keys or ask for a control rig.
- **Feet stay flat:** there is no heel-toe roll. Toe pitch made the big boots touch the floor during swing.
