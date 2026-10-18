# Changelog

All notable changes to the Udon Combat System package.

## [2.3.0] - 2026-10-18

Changes since 2.2.0.

### Added

#### Pooled explosions
- New `UCS_Explosion` hit effect: radius damage with linear falloff (**Max Damage** to **Min Damage**), occlusion by **Occlusion Layers**, a mass-independent rigidbody push (**Rigidbody Push Speed**, 8 m/s at the centre), local player knockback (**Player Launch Speed**, 6 m/s at the centre, 0 disables), and close / mid / far audio picked by distance.
  - Runs on every client like other hit effects. Hitbox damage is sent with `DamageType.explosion` and only applied from the blamed player's client, same as bullets. Each player takes one hit per explosion (the closest of their hitboxes).
- `UCS_HitEffectsPool.SpawnAt(position, rotation, blamedPlayerID, attackerTeamID)`: positions the instance before enabling it and passes blame to the effect via the new virtual `UCS_HitEffectBase.OnSpawned`. Guns, projectiles and grenades can all spawn explosions through a pool reference.
- `ExplosionPoolExample.prefab`: a ready-to-use explosion pool with the explosion audio clips assigned.

#### Grenades
- `UCS_Grenade`: one generic grenade script for frag, impact and flashbang types. It lives on an always-active parent with Manual sync. `UCS_GrenadePickup` on the pickup forwards pickup, drop, use and collision events to it, so the pickup can be stowed or disabled freely.
  - Pins are pulled with a physbone in VR (polled from a delayed-frame loop, no poller objects). Any number of pins is supported, and all must be pulled to arm (the M84 has two). Pin physbone chains end at the pin's pull ring with a 3 cm grab radius, so the ring is easy to grab.
  - Releasing an armed grenade ejects the safety lever and starts the fuse. Pins and the lever detach as local physics objects on every client, then hide after **Loose Part Lifetime**. Attached parts keep their rigidbodies kinematic with interpolation off, so they stay put when the grenade is teleported (dispensers, respawns).
  - **Detonate On Impact** turns it into an impact grenade, with **Impact Arm Delay** so it doesn't go off in the thrower's hand. **Fuse Time** acts as a fallback (0 = none). Impact grenades also go off when their flight path crosses a UCS hitbox (**Impact Hitbox Layers**, Interactive by default), since pickups don't physically collide with players.
  - The fuse is timed in server time, so it keeps counting if the thrower leaves or someone picks up a live grenade.
  - Detonation spawns whatever pool is in **Detonation Pool** via `SpawnAt`, blaming the thrower. **Respawn Delay** resets the grenade at its spawn point, or returns it to **Return Pool** (defaults to a `BulkPreOrderPool` on a parent). `ResetGrenade()` is public for pools and spawners.
  - Held with **Gun** orientation and an exact gun point (same as the guns): fuze up, lever against the palm, pins on the off-hand side, so they're held the same way every time. Left-handed pickups flip the grenade upside down about the grip (**Hand Flip Root**), matching the real left-handed grip: thumb on the lever, pins on the inside at the bottom. Synced, so everyone sees it.
  - VR throws get a pickup **Throw Velocity Boost** scaled to the holder's eye height (x2 at 0.93 m, divided by eye height, clamped x1 to x3), set on every pickup, so throw distance stays the same across avatar sizes. The boost starts at 1 m/s, so gentle drops and hand-offs aren't boosted.
  - Desktop: click to pull every pin, click again to throw. Looking level or up throws overhand along the look direction plus **Click Throw Lift** at **Click Throw Speed**. Looking at the floor blends (between **Click Underhand Pitch Start** and **Full**) into a fast underhand toss at **Click Underhand Speed** that leaves the hand no steeper than **Click Underhand Max Down Degrees**, so it skims in and rolls on. Also applies in VR to grenades with no pin physbones.
  - Stowable: grenade prefabs carry a `StowableManager` (size **ExtraSmall**) and `StowablePickup`, so they fit the hip pockets. While a stow manager is present, a spent grenade despawns through it (`SetItemSpawned`) instead of toggling the pickup itself, and grenades force an unstow before returning to their pool.
- `UCS_Flashbang` hit effect: each client works out its own player's exposure (distance, line of sight to the head, facing) and whites out the view with the new FOV-agnostic `UCS/FlashOverlay` shader, with a fade and optional ringing. Skipped in mirrors and the photo camera.
- Low-poly grenade models in `Runtime/Models/Grenades`: M67 (frag), T13 Beano (impact) and M84 (flashbang), all sharing one palette-atlas material. Pins, safety levers (M67, M84) and the M84's second pin are separate child meshes, with each lever's pivot at its hinge.
- Prefabs:
  - Grenade-only prefabs `Prefabs/Grenades/M67 Grenade`, `T13 Impact Grenade` and `M84 Flashbang` for pooling, with no detonation pool attached: point **Detonation Pool** at a shared pool in the scene.
  - `... setup` versions of each, nesting the grenade with its own detonation pool.
  - `FlashPoolExample.prefab`.

#### Pooling and dispensers
- `BulkPreOrderPool` (`Runtime/Scripts/Pooling`): a networked object pool for guns and grenades. Players reserve items in bulk, then the reservations are fulfilled (activated and handed over) in one go; all claims are resolved by the pool's owner, so concurrent requests can't double-claim a slot. `GetClaimantAt(slot)` reports who holds a slot.
- `UCS_PoolDispenser` + `Pool Dispenser.prefab`: a coffee-machine style dispenser that reserves, fulfills and pops one item out of a `BulkPreOrderPool` for the local player.
  - When the pool is empty it recycles an item nobody is holding or has stowed (live or spent grenades are left to return themselves). Items are unstowed before being placed.
  - **Size** (width, height, depth): the body, tray, button, label and spawn point lay themselves out to fit, in the editor only. Tooltip presets: grenade 0.16 x 0.3 x 0.16, pistol 0.32 x 0.32 x 0.2, rifle 1.0 x 0.42 x 0.28. Rotate the `SpawnPoint` child to orient the item (rifles lie across the tray).
  - Handles multi-grip guns: the authored local pose of every synced child grip is baked in the editor and restored on dispense, so a reused gun comes out in one piece instead of every grip landing on the spawn point. Rebaked whenever the dispenser is edited; touch it after adding items to its pool.
  - `UCS_DispenserButton`: the Interact target on the dispenser's Button child, forwarding presses to the public `UCS_PoolDispenser._Dispense()`. The dispenser itself lives on the prefab root, where the pool and size are set.
- `Combat System Setup.prefab` gained a `Gun Dispensers` group: a G17 dispenser and an M4 dispenser, each with a 4-gun `BulkPreOrderPool`. The pooled guns share the setup's existing `G17Magpool` / `M4 Magpool`.

### Changed
- `UCS_BaseGun` spawns its hit effects and bullet sparks through `SpawnAt`, passing the shooter as blame, so an explosion pool in either slot gives explosive rounds.
- Package assembly now references `com.toly65.udon-stow-system` (already a package dependency).

### Fixed
- Mag belt sat in the wrong place on avatars with backwards or upside-down hip bones. It now measures the hip bone against the player rotation per avatar (re-measured on avatar change), the same fix the stow system's stow points use.
- Items dispensed by a player who wasn't the pool owner could float with no gravity on their first spawn: VRCObjectSync held them kinematic from when another player owned them. The dispenser now bakes each synced rigidbody's authored kinematic and gravity settings and reapplies them through `SetKinematic` / `SetGravity` after taking ownership, and again 0.5 s later unless the item has been picked up.

### Setup notes
- Existing `Pool Dispenser` instances keep their pool, name and label; the component move to the prefab root is handled inside the prefab.
- After updating, run UdonSharp **Compile All** before building.

## [2.2.0] - 2026-10-10

Changes since 2.1.1.

### Added

#### Segmented player hitboxes with distance LODs
Player hitboxes are no longer a single full-body capsule. Each player now has up to three hitbox tiers, and each remote player switches between them based on distance from the local player:

| LOD | Name | Hitboxes | Used |
|---|---|---|---|
| 0 | Pill | One full-body capsule (the previous behaviour) | Beyond **Segmented Pill Max Distance** (default 40 m) |
| 1 | Segmented Pill | Separate **head**, **torso** and **legs** boxes | Within 40 m |
| 2 | Articulated | Per-bone hitboxes | Within **Articulated Max Distance** (default 15 m) |

- `UCS_HitboxUpdater` re-evaluates one tracked player per frame (round robin) and swaps the active hitbox root, only subscribing the colliders of the active tier for position updates, so far players cost the same as before.
- **Max LOD** on `UCS_PlayerHealthObjectBase` caps the tier per setup. The shipped `HealthSystem.prefab` uses **Segmented Pill**; the Articulated tier is supported but ships with no hitboxes, so add per-bone hitboxes and raise Max LOD to use it.
- **Per-avatar head sizing**: the head box is measured from each avatar's head bone to eye distance and eye span, relative to eye height, so stylised and big-headed avatars get a fitting head box. Tunables on the updater: Head To Eye Ratio, Eye Span Ratio, Max Head Scale Ratio (guards against misplaced eye bones), Head Size Multiplier, and Log Head Sizing for tuning.
- **Torso box** width and depth follow the avatar's upper-arm span (**Torso Width / Depth Multiplier**).
- **Bone Offset** on `UCS_PlayerHitbox`: offset from the tracked bone in bone-local space, in multiples of eye height (e.g. lift a head box).
- Leg box and full-body pill now extend down to the soles of the feet instead of stopping at the ankles.
- Existing per-hitbox **Damage Multiplier** applies per segment, so headshot multipliers can be set on the head box.

#### Simulated projectile bullet mode
- New **Simulated Projectile** bullet mode on `UCS_BaseGun`, sitting between hitscan raycasts and Rigidbody projectiles.
  - Bullets have travel time and bullet drop (gravity multiplier), with no GameObject, Rigidbody or collider per bullet.
  - Each bullet is stepped once per frame with a single raycast covering that frame's travel, so fast bullets cannot tunnel through thin walls.
  - The per-frame loop is self-scheduling and stops when nothing is in flight, so idle guns cost nothing. It does not use `Update`, so it cannot clash with subclasses such as `UCS_ComplexGun`.
  - Damage and impact effects reuse the raycast path; damage stays shooter-authoritative.
  - Settings: Muzzle Velocity, Gravity Multiplier, Lifetime, Max In Flight (oldest bullet is recycled when full).
- **Tracers** via a LineRenderer template cloned per in-flight slot, with **Tracer Every Nth Round** (default 3, 0 = none).
- Shotgun pellet spread works in Simulated mode.

#### Bullet audio
- **Surface impact sounds** at the hit point (Raycast and Simulated modes), only on Effect Layers so player hits do not ricochet.
- **Bullet whizz-by sounds** for other players' simulated bullets passing within Whizz Radius of your head, played once at the point of closest approach.
- **Local player hit sound** on `UCS_PlayerHealthObjectBase`, played only for the player who took bullet damage (2D, never spatialized in VRChat, top priority).
- **Speed of sound** for gunshots: distant listeners hear the shot after distance / Speed Of Sound (default 343 m/s, 0 = instant). Supersonic bullets now arrive before their own bang.
- Bullet audio uses a small round-robin voice pool cloned from a 3D AudioSource template (**Voices**, default 6).
- Audio clips: `Runtime/Audio/BulletWhizzes`, `Runtime/Audio/HitEffects` (including compressed `_loud` variants of the body hit clips) and placeholder impacts in `Runtime/Audio/Placeholder Bullet SFX`.

#### Damage vignette
- Fullscreen low-health vignette (`UCS/DamageVignette` shader, `DamageVignette.mat`) using the legacy VRChat combat system damage texture from the SDK.
  - Works in VR and desktop without post-processing, mapped per eye so it holds up on wide-FOV headsets.
  - Skipped in mirrors and the VRChat photo camera.
  - Flashes to a strength based on missing health on each hit, holds, then fades out (**Vignette Hold Seconds**, **Vignette Fade Seconds**). Renderer is disabled when not visible.

#### Desktop HUD
- **Ammo display** in the bottom-right of the desktop HUD, styled after VRChat's desktop HUD, with a **Q Reload** key prompt that lights up when the gun is empty. Low ammo turns red; infinite ammo shows INF.
  - Driven from `UCS_HealthBarsManager` by polling the local player's held pickup, so guns need no HUD wiring.
- New read-only gun accessors: `GetAmmoCount()`, `GetAmmoCapacity()`, `HasInfiniteAmmo()`.
- Desktop HUD canvas is now **fitted to the screen** by `UCS_HeadTracker` (resized when FOV or aspect changes), so HUD elements can use normal corner anchors. VR keeps the authored canvas size.

### Changed
- `UCS_BaseGun` inspector: the separate Raycast and Projectile sections are replaced by one **Bullet Settings** section with a **Bullet Mode** dropdown (None / Raycast / Simulated Projectile / Rigidbody Projectile). It is backed by the existing bools, so existing prefabs keep their settings.
- Firing code consolidated into a single `FireFromMuzzle` path for all modes and barrels.
- Desktop health bar is anchored to the bottom-left corner of the fitted canvas instead of a hand-tuned offset from center.
- Gun **Mechanics Audio** (reloads, slide, mag) is now 3D with a linear falloff to silence at 12 m, plus a VRChat Spatial Audio Source following the same curve.
- Gun **Barrel Audio** (gunshots) is now 3D with a logarithmic falloff (full volume to 2 m, near silent by 400 m), plus a VRChat Spatial Audio Source.
- Package assembly now references `Unity.TextMeshPro` (already required by the VRChat SDK).

### Fixed
- Hitbox boxes could flip their width and depth axes as players turned, because only the up direction was constrained; boxes now keep a stable orientation.
- Reload and gunshot sounds were 2D and audible at full volume anywhere in the world.
- Desktop health bar drifted off the corner or off screen at non-default FOV or window aspect ratios.
- Local hit sound could be quiet in VRChat because the health object does not follow the player and VRChat auto-spatialized it.
- `HealthSystem.prefab`: the health bar manager's `desktopHealthBar` reference was empty in some setups, so the VR hide logic could not reach the bar.

### Setup notes
- Hitboxes: `HealthSystem.prefab` now ships the Pill and Segmented Pill tiers with Max LOD set to Segmented Pill. Setups that override Max LOD to Pill keep the old single-capsule behaviour. Damage multipliers on the new head/torso/leg boxes default to 1, so set a head multiplier if you want headshots to count extra.
- `HealthSystem.prefab` gained `DamageVignette`, `LocalHitAudio` and `AmmoPanel`; scenes using the prefab pick them up automatically.
- To use Simulated Projectiles on other guns: set Bullet Mode, add a `BulletTracer` (LineRenderer, world space, 2 points) and a `BulletAudio` (3D AudioSource) child, and assign them. The M4 and G17 in the demo scene are already set up.
- After updating, run UdonSharp **Compile All** before building.
