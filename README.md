# Toly's Udon Combat System (UCS)

Tired of point-and-click gunfights? Want guns that feel like actual guns in VRChat?

This package adds a modular, VR-physical combat system for VRChat worlds built with Udon/UdonSharp. Inspired by Boneworks and Bonelab, it supports fully interactive weapons with manual magazine reloading, slide-racking, hit detection, health systems, and hit effects — all networked and synced across players.

## Documentation

Full setup guides and script reference live on the **[GitHub Wiki](https://github.com/Toly65/UdonCombatSystem/wiki)**. Start at [Getting Started](https://github.com/Toly65/UdonCombatSystem/wiki/Getting-Started) if you're setting up a world, or jump straight to the [Script Reference](https://github.com/Toly65/UdonCombatSystem/wiki/Home#script-reference) pages if you're extending the system. The wiki is the authoritative, up-to-date source — this README is a high-level overview only.

## How to Install

- Add the VPM listing to Creator Companion: [Add Repo](https://toly65.github.io/vpm/add-repo.html)

## Demo Videos

*Coming soon — check back for VR and desktop gameplay demos.*

## What It Does

The Udon Combat System lets players:

- Pick up and fire guns with semi-auto, full-auto, burst, and safe fire modes.
- Reload physically — eject spent mags, grab fresh ones from a hip belt, insert them, and rack the slide.
- Fight with hitscan (raycast) or physical projectile weapons.
- Take and receive damage through a team-aware player health and hitbox system.
- See impact effects — bullet sparks, hit decals, muzzle flash, and shell casings.

## Gun Types

**Arcade Gun** (`UCS_ArcadeGun`) — *stub, not yet functional.* Will be a simpler pick-up-and-shoot option with automatic reload.

**Complex Gun** (`UCS_ComplexGun`) — Boneworks/Bonelab-style VR-physical reload. Players manually:
- Eject the magazine from the gun.
- Grab a fresh magazine from their hip belt (or the ground).
- Insert the magazine into the gun's magazine socket.
- Rack the slide (via PhysBone) to chamber a round.
- Fire until empty, then repeat.

Each Complex Gun supports one-handed (pistol) and two-handed (rifle) setups, with prefabs included for both.

## Core Features

- **Modular gun system** — base gun class with arcade and complex subclasses. Easy to extend with custom gun types.
- **Physical magazine reloading** — Boneworks/Bonelab-inspired manual reload. Magazines are physical objects with synced ammo counts, pooling, and auto-despawn.
- **MagBelt** — a hip-mounted holster that tracks the player's avatar, presents spare magazines, and auto-scales to any avatar size.
- **Two-handed grip** — full support for rifles and other two-handed weapons via `UCS_TwoHandedManager`.
- **Multiple fire modes** — Semi, Auto, Burst, and Safe. Fire modes can be swapped per gun.
- **Shotgun mode** — fires multiple pellets per shot with configurable spread.
- **Projectile support** — fire physical projectiles (with Rigidbody physics) instead of hitscan raycasts, or use both together for tracer visuals with instant-hit damage.
- **Player health & hitbox system** — team-aware damage via `UCS_PlayerHitbox` and `UCS_PlayerHealthObjectBase`, with an extensible `UCS_Hitbox` base class for custom damageable objects. Hitboxes support multiple levels of detail (currently: a single full-body capsule, with segmented and per-limb tiers planned).
- **Health UI** — head-tracked and wrist-tracked health bar displays.
- **Hit effects** — pooled hit decals and bullet sparks that spawn on impact and auto-recycle.
- **Death handlers** — configurable death behaviours; a teleport-based handler is included and recommended, with a VRChat-legacy-Combat-API-based ragdoll handler also available (not recommended — that platform API is unreliable).
- **Ammo inventory** — optional persistent ammo store keyed by magazine type. Magazines draw from and deposit back into shared inventory pools.
- **Networking** — manual sync with network events for firing, chamber state, and hit effects. Works across all clients including late-joiners.
- **Animator integration** — gun animator drives fire cycles, slide position, bullet visibility, and slide-lock state.
- **Haptic feedback** — VR controller rumble on fire and reload.
- **Desktop support** — desktop-friendly firing with head-aim alignment and auto-fire handling.
- **Infinite ammo / infinite magazine** — toggle for testing or arcade-style gameplay.

## Typical Use Cases

- **Combat/arena worlds** — players gear up with physical-reload weapons, manage ammo, and fight with real mechanical interaction.
- **Shooting ranges** — target practice with score tracking via custom hitbox overrides.
- **Survival or exploration maps** — limited ammo, magazine management, and lootable weapon pickups.
- **FPS-style game worlds** — team deathmatch, co-op horde modes, or PvE encounters with hitbox-driven enemy health.

## Scripting & Extensibility

UCS is built to be extended. The full class hierarchy:

```
UCS_BaseGun                  (core firing, ammo, reload, fire modes)
  └── UCS_MagFedGun          (adds magazine size, refill, and feed logic)
        ├── UCS_ArcadeGun    (stub — not yet functional)
        └── UCS_ComplexGun   (physical mag socket + slide cycling)

UCS_Hitbox                   (abstract — override HitEvent for custom damage receivers)
  └── UCS_PlayerHitbox       (team-aware player hitbox, reports to a health object)
UCS_PlayerHealthObjectBase   (player health, hitbox LOD switching, death handoff)
UCS_HitboxUpdater            (per-frame hitbox tracking and LOD switching, shared per scene)
UCS_DeathHandlerBase         (abstract — override for custom death behaviour)
UCS_HitEffectsPool           (pooled hit decals and bullet sparks)
UCS_ProjectileManager        (pooled physical projectiles)
UCS_AmmoInventory            (persistent ammo by type ID)
UCS_MagPool / UCS_Mag        (magazine pooling and state)
UCS_MagBelt / UCS_MagSocket  (hip holster and magazine insertion)
UCS_SliderHandler            (PhysBone-driven slide tracking)
UCS_TwoHandedManager         (second-hand grip support)
```

For full field-by-field detail, see the [Script Reference](https://github.com/Toly65/UdonCombatSystem/wiki/Home#script-reference) on the wiki.

## Notes

- This system is designed for creators who want mechanical depth in their VRChat worlds.
- For support and updates, join the Discord: [https://discord.gg/cvF8JEhrq7](https://discord.gg/cvF8JEhrq7)
