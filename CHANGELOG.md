# Changelog

All notable changes to the Udon Combat System package.

## [2.2.0] - Unreleased

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
- To use Simulated Projectiles on other guns: set Bullet Mode, add a `BulletTracer` (LineRenderer, world space, 2 points) and a `BulletAudio` (3D AudioSource) child, and assign them. The M4 and G17 in the demo scene and the matchmaking gun pools are already set up.
- After updating, run UdonSharp **Compile All** before building.
