# HIROBOT_LOW – Retopology / Bake Validation Report

Source: `HIROBOT` (28,384 faces / 56,704 tris, 29 separate rigid pieces), left untouched in `Collection`.
Result: `HIROBOT_LOW` in collection `HIROBOT_LOW`. Saved copy: `HIROBOT_LOW_4k_retopo.blend`.

## 1. Triangle budget
| Item | Value |
|---|---|
| Final triangles (after triangulation) | **3,864** (limit 4,000) |
| Faces | 1,980 (1,884 quads, 96 tris, 0 n-gons) |
| Vertices | 1,998 |
| Reduction | 56,704 → 3,864 (−93.2 %) |

Per-piece method (left side built, mirrored to right for exact symmetry):
- Ring-based loops projected on the original surface: shins, boots.
- Cube-sphere quad cages: head, hip joints, upper arms, palms.
- QuadriFlow quad remesh + shrinkwrap: torso, neck, forearms, fingers, thumbs.
- Eyes, mouth and chest light were not modelled as geometry; they are baked into BaseColor/Emission on the head and torso.

## 2. Geometry checks
| Check | Result |
|---|---|
| Non-manifold edges / verts | 0 / 0 |
| Duplicate verts (1e-5 m) | 0 |
| Degenerate faces | 0 |
| Inconsistent winding | 0 |
| Normals outward (all 25 pieces) | yes |
| Open boundary edges | 32 – intentional open bottom ring of each shin, hidden inside the boot (same as the source, which is also open there) |
| Quad aspect ratio p95 / max | 5.6 / 28 (worst ones are at the sole bevel and shin top, hidden inside joints) |
| Poles | 4 × valence-14 and 2 × valence-16 fans at boot sole / shin top (hidden or flat areas) |
| Transforms | applied (loc 0, rot 0, scale 1); source had scale −0.0254 and a rotation |

## 3. Shape fidelity
| Metric | Value |
|---|---|
| Outer-surface deviation, original → low (4,731 samples) | mean 4.0 mm, median 3.3 mm, p95 10.0 mm, max 17.3 mm (model is 1.24 m tall) |
| World bounding box | original X ±0.488, Y −0.297…0.295, Z 0…1.2405 · low X ±0.485, Y −0.297…0.295, Z 0…1.2395 |
| Visual front/side comparison | silhouettes match; checked in Material Preview |

## 4. UVs (single `UVMap`, 0–1 tile)
| Metric | Value |
|---|---|
| Overlap (1K raster) | 2 texels (negligible) |
| Faces outside 0–1 | 0 |
| Flipped faces | 0 |
| Angle distortion | mean 2.9°, p95 10.5° |
| Area ratio (texel-density) p5 / p95 | 0.88 / 1.53 |
| Coverage | 49.6 % |
| Island margin | 0.006 (~12 px at 2K) + 16 px bake padding |
| Seams | shins/boots: back seam + sole/top-cap rings. Other pieces: angle-based charts, so they have more seams than hand-placed ones would |

## 5. Bakes (2048×2048, Cycles, each low piece baked only from its own high piece)
| Map | File | Colour space | Notes |
|---|---|---|---|
| Base Color | HIROBOT_LOW_BaseColor.png | sRGB | includes eyes/mouth/visor |
| Normal | HIROBOT_LOW_Normal.png | Non-Color | tangent space, OpenGL (+Y), MikkTSpace – Unity/Blender convention. DirectX engines (Unreal): flip G |
| Roughness | HIROBOT_LOW_Roughness.png | Non-Color | |
| Metallic | HIROBOT_LOW_Metallic.png | Non-Color | |
| AO | HIROBOT_LOW_AO.png | Non-Color | 32 spp, 3 cm distance; for the engine's occlusion slot (not wired into the Blender BSDF) |
| Emission | HIROBOT_LOW_Emission.png | sRGB | colour × (strength/9); use Emission Strength 9 |

Bake QA (≈10,000 texel samples, 5 per face):
- Inverted normals: 0. Normal tilt median 4.5°, p95 36° (the large tilts are real details: grooves, slots and visor edges).
- Missed-ray texels: 86 samples (0.9 %), all on faces hidden inside other pieces (shin top inside hip, boot collar inside shin, neck). Filled from neighbouring texels.
- Fixes made during the bake:
  - The source mesh has inside-out normals on the right side, head, torso and legs. Corrected on temporary bake copies only; the original was not changed.
  - Holes in the source (shoulder sockets, neck, tube ends) were capped on the bake copies to avoid black texels.

## 6. Material
`HIROBOT_LOW_MAT` (Principled BSDF): BaseColor → Base Color, Roughness, Metallic, Normal → Normal Map (tangent, UVMap) → Normal, Emission → Emission Color (strength 9). AO texture node present for export. Textures are linked relatively (`//HIROBOT_LOW_textures/`).

## 7. Animation readiness
- Rigid robot pieces stay separate shells inside one object, so each can be rigidly skinned.
- 25 vertex groups, one per piece (head, torso, neck, upperarm/forearm/palm/finger/thumb, hipjoint/leg/foot, L/R). No skeleton was invented.
- Edge loops run around the length of every limb piece (rings on shins/boots/hips/arms).
- **Deformation test: not run.** No rig was supplied. Because the design is rigid segments, the joints are at piece boundaries and need no bending geometry.

## 8. Known limitations
- Pieces built with QuadriFlow (torso, forearms, fingers) have good quad flow but are not hand-placed loops.
- UV seams on the non-ring pieces come from angle-based charts, so there are more seams than an artist would place; no seam artifacts were visible in Material Preview.
- The source's empty material slot (hip joints) was baked as Blender's default surface (grey 0.8, roughness 0.5).
- Emission is stored as LDR colour × 1/9; the very bright eye glow needs strength 9 in the engine.
