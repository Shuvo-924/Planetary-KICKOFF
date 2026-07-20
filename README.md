# GameJam IUT — Script Guide

Unity space kick-journey game. Scene: **SceneP**. Player tag: `Player`.

## Controls
- **RMB / MMB** — orbit camera
- **Force slider (right side)** — set kick strength before launch
- **Hover asteroid** — see distance in meters
- **LMB** — lock that aim, play kick anim; at **frame 30/43** snap local-up toward it, shake, and launch
- **Space** — jet while airborne

## Kick timing (`CharacterMovement`)
1. Click locks the hovered asteroid (or camera forward).
2. `KickTrigger` plays (`Armature|ArmatureAction`, 43 frames).
3. At **frame 30** (`kickOffFrame`): instant `SnapUpToward` (local up = aim), camera shake, `linearVelocity` = aim × slider force.
4. Planet gravity keeps pulling; body slowly re-aligns to anti-gravity while flying.

| Field | Role |
|-------|------|
| `kickTotalFrames` / `kickOffFrame` | 43 / 30 — when physics launch happens |
| `minKickForce` / `maxKickForce` / `kickForce` | Slider range + default |
| `SelectedKickForce` | Value from `KickAimUI` slider |

## `KickAimUI.cs` — force bar + distance
Created at runtime (no scene setup needed).

| Member | Role |
|--------|------|
| `SelectedForce` | Current slider value used by the next kick |
| `ShowHoverDistance` | Label above hovered asteroid (`XX m`) |
| `SetLocked` / `ClearLock` | Shows `LOCK  XX m` during wind-up |

---

## How the systems connect

```
PlanetGenerator (Awake)
  → spawns start planet + asteroids + Earth
  → PlacePlayer → CharacterMovement.SpawnOnSurface

PlanetGravity (FixedUpdate, on each planet)
  → pulls player Rigidbody (useGravity = false)
  → NotifyGravityPull → CharacterMovement

CharacterMovement (FixedUpdate)
  → 6-way SphereCasts
  → Planet hit → spring stand force + upright align
  → Debris hit → damage + recoil
  → LMB → KickAimUI force + anim → launch at frame 30

KickAimUI
  → side force slider + hover/lock distance labels

DebrisProximityColliders
  → turns asteroid MeshColliders on near the player

CameraMovement + CameraShake
  → orbit around player up; shake on kick/hazard
```

The player **Rigidbody is never kinematic**. Gravity always pulls; standing is an **opposite support force**, not disabling attraction.

---

## `CharacterMovement.cs` — player

### Public fields (Inspector)

| Field | What it does |
|-------|----------------|
| `rb`, `anim`, `cam`, `feet` | References (auto-found if empty where possible) |
| `kickForce` / `minKickForce` / `maxKickForce` | Default + slider range |
| `energy` / `kickEnergyCost` | Resource for kicks |
| `kickTotalFrames` / `kickOffFrame` | Anim sync (43 / 30) |
| `clickAimMaxDistance` | Max mouse-aim ray distance for asteroids |
| `kickAirLock` | Seconds after kick where stand forces / re-ground are disabled |
| `planetLayer` | Only these surfaces count as ground (default: Planet) |
| `hazardLayer` | Nearby hits here damage you (default: Debris) |
| `bodyProbeRadius` | SphereCast radius for 6-way probes |
| `planetProbeDistance` / `hazardProbeDistance` | How far each probe looks |
| `surfacePad` | Extra height above the hit point when standing (important at large scale) |
| `standHeight` | Desired foot clearance (0 = derive from scale) |
| `groundStickOffset` | Extra stick height |
| `uprightRotateSpeed` | How fast the body aligns to surface / anti-gravity |
| `hazardDamage` / `hazardRecoil` / `hazardCooldown` | Non-planet contact response |
| `standSpring` / `standDamping` | Support force = spring error + damper + gravity counter |
| `gravityCounterScale` | Multiplier on countering planet pull while standing |
| `jetCharges` / `jetImpulse` / `jetEnergyCost` / `jetKey` | Air boost |

### Important methods

| Method | Role |
|--------|------|
| `NotifyGravityPull(dir, strength)` | Called by `PlanetGravity`; stores pull for stand + upright |
| `SpawnOnSurface(planet, outward, clearance)` | Places player outside the planet shell (called by generator) |
| `Probe()` | 6 SphereCasts (±X/Y/Z); picks Planet or Hazard |
| `Stand(gravity)` | Spring support + kill sink velocity + align upright |
| `Align(up)` | Slerp rotation so local up matches surface / anti-gravity |
| `SnapUpToward(dir)` | **Instant** local-up = aim (used at frame 30) |
| `BeginKick()` / `KickAtFrame()` / `ExecuteLaunch()` | Lock → wait for frame 30 → snap + shake + fly |
| `Jet()` | Space air boost along camera look |
| `Hazard(hit)` | Damage energy + bounce off debris |
| `PickAsteroid(...)` | Mouse ray vs asteroid bounds (for hover + kick aim) |

### Properties
- `IsGrounded` — currently standing on Planet layer  
- `InAirLock` — kick/hazard lock active  
- `IsKickPending` — wind-up before frame 30  
- `GravityDown` — strongest recent pull direction  
- `SelectedKickForce` — slider force for next kick  

---

## `PlanetGravity.cs` — attraction (one per planet)

| Field / method | Role |
|----------------|------|
| `maxGravity` | Surface acceleration (m/s²) |
| `atmosphereHeight` | Pull fades to 0 this far past the surface |
| `minDistance` | Prevents 1/r² singularity near center |
| `FixedUpdate` | `a = maxGravity * (R/r)² * atmosphereFade`, then `AddForce` |

Does **not** care if you are grounded. Standing is handled by `CharacterMovement.Stand`.

---

## `PlanetGenerator.cs` — level build

Runs in `Awake` (before physics) so the player is not buried in the planet.

### Inspector groups

| Group | Fields | Role |
|-------|--------|------|
| Prefabs | `startingPlanetPrefab`, `nearbyPlanetPrefabs`, `debrisPrefabs`, `earthPrefab` | What to spawn |
| Planet size | `startingPlanetWorldRadius`, nearby min/max, `planetMeshLocalRadius` | World scale; **0.9875** matches SphereSmooth mesh |
| Journey | `journeyDirection`, `earthDistance`, `earthAnchor` | Arc path toward Earth |
| Phases | `phaseCount`, stones/side counts, spike chances | Difficulty along the path |
| Kick spacing | `minKickGap` / `maxKickGap`, `pathWidth`, scales, `debrisAltitude` | Asteroid placement |
| Asteroid drift | min/max drift & spin, spike multiplier | Passed into `AsteroidMotion.Init` |
| Layers | `debrisLayerIndex` (8) | Debris must **not** use Planet layer |

### Key methods

| Method | Role |
|--------|------|
| `Build()` | Full world generation |
| `OnArc(arcDist, lateral, altitude)` | Point above crust along surface arc |
| `Lift(pos, altitude)` | Push a point outward so it is never inside the planet |
| `SpawnCorridor(...)` | Stepping-stone + side debris for one phase |
| `SpawnRock(...)` | Instantiate debris, add motion/visual, set Debris layer |
| `PlacePlayer(...)` | Calls `CharacterMovement.SpawnOnSurface` |
| `SetPlanetWorldRadius(...)` | Scale + SphereCollider so world radius matches mesh |
| `GetPlanetCenter` / `GetPlanetRadius` | Shared helpers used by gravity & player spawn |

### Properties
- `Earth` — Earth transform  
- `PhaseCenters` — arc midpoints per phase  

---

## `AsteroidMotion.cs` — debris movement

| Member | Role |
|--------|------|
| `All` | Static list of live asteroids (aim + proximity) |
| `isSpike` | Spike vs normal debris |
| `recoilDamping` | How fast kick recoil fades |
| `Init(...)` | Set drift, spin, mass, spike flag |
| `ApplyKickRecoil(impulse)` | Newton recoil from player kick |
| `SetColliderActive(on)` | Lazy MeshCollider on/off |
| `WorldBounds` | For mouse hover / click aim |
| `Update` | Move: drift + recoil; rotate on local axis |

---

## `AsteroidVisual.cs` — look / highlight

| Member | Role |
|--------|------|
| `rimColor` / `emissionColor` / `emissionIntensity` | Brighten dark rock textures |
| `outlineScale` | Inverted-hull outline size |
| `Setup(isSpike)` | Spike = red tint; build emission + outline |
| `SetHighlighted(on)` | Stronger glow when mouse-aimed |

---

## `DebrisProximityColliders.cs` — performance

| Field | Role |
|-------|------|
| `enableRadius` | Turn MeshColliders **on** inside this distance |
| `disableRadius` | Turn **off** outside (hysteresis so they don’t flicker) |
| `refreshInterval` | Throttle checks (~0.12s) |

Attached automatically by `PlanetGenerator`.

---

## `CameraMovement.cs` — orbit cam

| Field | Role |
|-------|------|
| `carTransform` | Follow target (player) |
| `distance` / `minDistance` / `heightOffset` / `lookAtHeight` | Orbit framing |
| `mouseSensitivity`, `minPitch` / `maxPitch` | Look limits |
| `requireMouseButton` | If true, only orbit while RMB/MMB held |
| `orbitSmoothTime` / `positionSmoothTime` | Feel (lower = snappier) |
| `collisionLayers` | Pull camera in if blocked |
| `urp` | Optional: disables heavy FullScreenPass feature |

Uses **player `transform.up`** so the camera stays sensible on a planet surface.

---

## `CameraShake.cs` — kick feel

| Member | Role |
|--------|------|
| `AddTrauma` / `ShakeFromKick` / `Shake` | Add shake energy |
| `Offset` / `EulerOffset` | Read by `CameraMovement` each LateUpdate |
| `traumaDecay`, `maxOffset`, `maxAngle` | Fade and strength |

Put this on the **Camera** object (duplicates on empty parents are destroyed).

---

## `NewCar.cs`

Legacy car controller from another prototype. **Not used** by SceneP’s stickman loop. Safe to ignore for this jam.

---

## Tuning tips

1. **Player floats / jitters** — planet radius too large; keep `startingPlanetWorldRadius` around **500**, not 10000.  
2. **Spawn buried** — raise `spawnClearance` / `surfacePad`; keep `planetMeshLocalRadius` at **0.9875**.  
3. **Kick feels weak** — raise `kickForce` / `minLeaveSpeed`; ensure `kickAirLock` is ~1s so stand forces don’t cancel launch.  
4. **Landing on rocks hurts** — intentional: Debris layer ≠ Planet. Only Planet layer stands.  
5. **Invisible asteroids** — ambient is set in `PlanetGenerator`; tweak `AsteroidVisual` emission if still dark.
