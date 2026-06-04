# Udon Combat System (UCS)

A modular combat system for VRChat built with UdonSharp. Supports two gun types, physical magazine reloading, pooled projectiles, and networked hit detection.

For support and updates, join the Discord: https://discord.gg/cvF8JEhrq7

---

## Gun types

**Arcade Gun** — pick up and shoot. Reloads automatically, no physical interaction needed. Good for simple combat worlds or players new to the system.

**Complex Gun** — physical VR reload. Players manually eject and insert magazines and rack the slide. Prefabs are included for both one-handed (pistol-style) and two-handed (rifle-style) setups. The root of each gun prefab is where all the configurable variables live.

Some guns support a two-handed grip via `UCS_TwoHandedManager`. Anything that should follow the gun's rotation as it moves should be placed on the designated rotate point within that setup.

---

## Hitboxes

Hitboxes are managed locally and pooled inside the combat system prefab. If you make changes to a hitbox, duplicate those changes across the pool to keep all instances consistent.

---

## Projectiles

Projectile weapons store their projectile objects as disabled children of the gun prefab, which get cloned at runtime. This lets you preconfigure them — things like a physical bullet with its own rigidbody and collision behaviour should be set up this way.

---

## Docs

- **Setting up a complex gun** — `DOCS/SetupDocs/ComplexGunSetup.md`
- **Technical reference** (architecture, script API) — `DOCS/TechnicalDocs/BaseGun.md`
