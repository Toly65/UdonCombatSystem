# UCS Technical Reference

> For developers extending the system, adding new gun types, or interfacing with UCS from other scripts.

---

## Class Hierarchy

```
UdonSharpBehaviour
  └── UCS_BaseGun                  (core firing, ammo, reload logic)
        ├── UCS_ArcadeGun          (adds reload animation override)
        └── UCS_ComplexGun         (adds physical slide + mag socket)

UdonSharpBehaviour
  ├── UCS_SliderHandler            (polls PhysBone stretch, fires slide events)
  ├── UCS_MagSocket                (trigger-based mag insertion/ejection)
  ├── UCS_Mag                      (magazine data — ammo count, state, pooling)
  ├── UCS_MagPool                  (VRCObjectPool wrapper for mag spawning)
  ├── UCS_MagBelt                  (hip-attached mag holster, avatar-scale aware)
  ├── UCS_MagPickup                (pickup event relay on the mag)
  ├── UCS_AmmoInventory            (persistent ammo counts by type ID)
  ├── UCS_HitEffectsPool           (generic object pool for hit/spark VFX)
  ├── UCS_ProjectileManager        (object pool for physical projectile GOs)
  ├── UCS_Hitbox                   (abstract base — override HitEvent for damage)
  ├── UCS_TwoHandedManager         (two-handed grip support)
  └── UCS_PickupEventTransferer    (routes VRC_Pickup events to child scripts)
```

---

## Firing Pipeline

### Hitscan (Raycast mode)

```
TriggerPull()
  → checks bulletChambered, isReloading, slideLockedBack (ComplexGun)
  → switch FireMode:
      Semi  → SendCustomNetworkEvent(All, "FireGun")
      Auto  → SendCustomNetworkEvent(All, "StartAuto") → FireAuto() loop
      Burst → SendCustomNetworkEvent(All, "FireBurst") → FireGun() × BurstCount
      Safe  → nothing

FireGun()  [runs on ALL clients]
  → BulletRaycast(muzzle)
      → Physics.Raycast(HitLayers)    → UCS_Hitbox.HitEvent(damage) + Rigidbody impulse
      → Physics.Raycast(EffectLayers) → HitEffectsPool.AcquireInstance()
      → BulletSparkPool.AcquireInstance()
  → GunAnimator.SetBool("IsFiring", true)
  → FireSound.PlayOneShot()
  → MuzzleFlash.Play()
  → BulletEject.Play()
  → haptic feedback (local player only)
  → [owner only] ConsumeFiredRound()
      → CurrentAmmo--
      → if empty: SetChamberEmpty (network event) + auto-reload
```

### Projectile mode

When `UseProjectiles` is enabled and `Raycast` is disabled, `FireGun` calls `ProjectileManager.SpawnProjectile(position, direction, speed)` instead of raycasting. The projectile GameObject handles its own hit detection.

When both `UseProjectiles` and `Raycast` are enabled, the projectile spawns visually but `BulletRaycast` still handles damage — useful for tracer-style visuals with instant-hit damage.

### Auto fire loop

```
StartAuto() → isAutoFiring = true → FireAuto()
FireAuto()  → FireGun() → SendCustomEventDelayedSeconds("FireAuto", CycleTime)
StopAuto()  → isAutoFiring = false  (next FireAuto() call exits silently)
```

`StartAuto` and `StopAuto` are sent as network events so all clients stay in sync.

---

## Magazine System

The magazine system is modular and type-safe. Each gun declares a `magTypeId` string (e.g. `"Pistol"`, `"Rifle"`). Magazines and pools sharing that ID are compatible; mismatched IDs are rejected at insertion.

```
UCS_MagPool  (VRCObjectPool)
  │  magTypeId = "Pistol"
  │  maxAmmo   = 17
  │
  ├─ spawns ──► UCS_Mag  (one per pool slot)
  │               currentAmmo (synced)
  │               isHeld / isSocketed state
  │               lifetimeAfterNonInteraction (auto-despawn)
  │
  └─ consumed by ──► UCS_MagBelt  (presents preview mag at hip holster)
                      UCS_MagSocket  (accepts mag into gun)
                        └─ notifies ──► UCS_ComplexGun.OnMagazineInserted()
                                          CurrentAmmo = mag.GetCurrentAmmo()
                                          slideCycleNeedsChamber = true
```

When a magazine is ejected (`UCS_MagSocket.EjectMag`), the gun's remaining `CurrentAmmo` is transferred back into the mag via `TransferAmmoToMag`. Dropped mags auto-return to the pool after `lifetimeAfterNonInteraction` seconds (default 30s).

### Ammo Inventory

`UCS_AmmoInventory` is an optional persistent ammo store keyed by type ID. When a `UCS_MagPool` has an inventory reference, spawned magazines draw ammo from it rather than spawning full. When a mag returns to the pool its remaining ammo is deposited back.

---

## Networking Model

UCS uses **Manual sync** (`BehaviourSyncMode.Manual`) on guns and magazines. Key points:

- **Synced variables on UCS_BaseGun**: `CurrentAmmo`, `isReloading`, `bulletChambered`
- **Synced variable on UCS_Mag**: `currentAmmo`
- Damage and effects are triggered via `SendCustomNetworkEvent(All, ...)` so every client sees the same muzzle flash, hit effect, and sound without needing late-join sync.
- Ammo deduction only runs on the **owner** of the gun GameObject. Non-owners see chamber state changes via `SetChamberEmpty` / `SetChamberLoaded` network events.
- Ownership transfers automatically when a player picks up the gun (`GunDropped()` → `RequestSerialization()`).
- `UCS_MagPool` uses `VRCObjectPool` (no variable sync) — pool state is managed by VRChat's built-in object pool networking.

---

## Script Reference

---

### UCS_BaseGun

**File**: `UCS_BaseGun.cs` | **Sync**: `Manual` | **Extended by**: `UCS_ArcadeGun`, `UCS_ComplexGun`

Core gun script. Handles all firing logic, ammo management, fire modes, audio, haptics, animations, and particles. Subclass this to create new gun types.

#### Inspector Fields

**Muzzle Settings**

| Field | Type | Description |
|---|---|---|
| `multipleBarrels` | bool | Fire from all transforms in `MuzzlePoints` simultaneously |
| `MuzzlePoints` | Transform[] | Used when `multipleBarrels` is true |
| `MuzzlePoint` | Transform | Used when `multipleBarrels` is false |
| `DesktopFaceFiring` | bool | On desktop, aims toward head position rather than muzzle forward |

**Fire Mode Settings**

| Field | Type | Description |
|---|---|---|
| `FireMode` | FireSelection | `Safe`, `Semi`, `Auto`, or `Burst` |
| `BurstCount` | int | Shots per burst (Burst mode only) |
| `BurstDelay` | float | Seconds between burst shots |
| `shotgunMode` | bool | Fire multiple pellets per shot |
| `PelletsPerShot` | int | Number of pellets when `shotgunMode` is true |

**Raycast Settings**

| Field | Type | Description |
|---|---|---|
| `Raycast` | bool | Enable hitscan bullet detection |
| `Range` | float | Max raycast distance in meters |
| `HitLayers` | LayerMask | Layers that register damage hits |
| `EffectLayers` | LayerMask | Layers that spawn hit effects / sparks |
| `Damage` | float | Damage passed to `UCS_Hitbox.HitEvent` |
| `ImpactForce` | float | Impulse applied to hit rigidbodies |
| `HitEffectsPool` | UCS_HitEffectsPool | Pool for hit decal effects |
| `BulletSparkPool` | UCS_HitEffectsPool | Pool for bullet spark effects |

**Projectile Settings**

| Field | Type | Description |
|---|---|---|
| `UseProjectiles` | bool | Spawn physical projectile GameObjects on fire |
| `ProjectileManager` | UCS_ProjectileManager | Pool managing projectile instances |
| `ProjectileSpeed` | float | Velocity applied to spawned projectiles |
| `ProjectileSpreadAngle` | float | Max random spread angle in degrees (shotgun mode) |

**Gun Settings**

| Field | Type | Description |
|---|---|---|
| `RoundsPerMinute` | float | Determines `CycleTime = 60 / RPM` |
| `MagazineSize` | int | Bullets per magazine |
| `ReloadTime` | float | Total reload duration in seconds |
| `refillAmmoOnDisable` | bool | Reset ammo to full when the gun is disabled/dropped |
| `InfiniteAmmo` | bool | Never consume ammo; always fire |
| `InfiniteMagazine` | bool | No reload required; ammo isn't tracked |
| `AutoReload` | bool | Automatically call `StartReload()` when empty |
| `bulletInChamberAddsToMag` | bool | Whether the chambered round counts toward `MagazineSize + 1` |

**Audio**

| Field | Type | Description |
|---|---|---|
| `barrelAudioSource` | AudioSource | For fire and empty-fire sounds |
| `magazineAudioSource` | AudioSource | For mag pull/insert sounds |
| `firemechanicsAudioSource` | AudioSource | For slide sounds |
| `FireSound` | AudioClip | Played on each shot |
| `EmptyFireSound` | AudioClip | Played when trigger pulled with empty chamber |
| `magpullSound` | AudioClip | Played when pulling the magazine |
| `maginsertSound` | AudioClip | Played when inserting the magazine |
| `slideBackSound` | AudioClip | Played when slide is pulled back |
| `slideForwardSound` | AudioClip | Played when slide goes forward |

**Haptic Feedback**

| Field | Type | Description |
|---|---|---|
| `hapticFeedback` | bool | Enable VR controller haptics on fire |
| `hapticFeedbackDuration` | float | Duration in seconds |
| `hapticFeedbackAmplitude` | float | Strength 0–1 |
| `hapticFeedbackFrequency` | float | Frequency 0–1 |
| `hapticFeedbackOnManualReload` | bool | Also trigger haptics during manual reload |

**Animations**

| Field | Type | Description |
|---|---|---|
| `GunAnimator` | Animator | Animator controller on the gun |
| `CycleAnimation` | AnimationClip | The fire-cycle animation clip |

Expected animator parameters: `IsFiring` (bool), `IsFiringLock` (bool — last round). The animator should have a `FireCycleLayer` with states `FireIdle`, `FireCycle`, `FireCycleLock`.

**Particles**

| Field | Type | Description |
|---|---|---|
| `MuzzleFlashParticleSystem` | ParticleSystem | Plays on each shot |
| `BulletEjectParticleSystem` | ParticleSystem | Ejects spent casing on fire |
| `UnfiredBulletEjectParticleSystem` | ParticleSystem | Ejects unfired round when slide cycled manually |

#### Public Methods

| Method | Description |
|---|---|
| `TriggerPull()` | Call when player presses fire. Routes to correct fire mode. |
| `TriggerRelease()` | Call when player releases fire. Stops auto fire. |
| `FireGun()` | Network event — fires one shot on all clients. |
| `StartAuto()` | Network event — begins auto-fire loop. |
| `StopAuto()` | Network event — ends auto-fire loop. |
| `FireBurst()` | Network event — fires a burst. |
| `StartReload()` | Begins reload sequence (guards against double-reload). |
| `CompleteReload()` | Called after `ReloadTime` — refills ammo, clears reload flag. Virtual. |
| `ReloadGun()` | Plays reload audio/animation, schedules `CompleteReload`. Virtual. |
| `SetChamberLoaded()` | Network event — marks chamber as loaded; shows bullet visual. |
| `SetChamberEmpty()` | Network event — marks chamber as empty; hides bullet visual. |
| `GunDropped()` | Call on drop to serialize ownership and ammo state. |

#### Protected / Virtual Members

| Member | Description |
|---|---|
| `CycleTime` | Seconds per shot, computed from `RoundsPerMinute` in `Start()`. |
| `CurrentAmmo` | Synced ammo count. |
| `isReloading` | Synced reload flag. |
| `bulletChambered` | Synced chamber state. |
| `ConsumeFiredRound()` | Decrements `CurrentAmmo`. Override for custom ammo logic. |
| `UpdateBulletVisibility(bool)` | Called when chamber state changes. Override to drive animator parameters. |
| `EjectChamberedRound()` | Removes the chambered round and decrements ammo. |
| `InsertMagazine()` | Plays mag-insert audio. |
| `ConsumeMagazineRound()` | Feeds a round from magazine into chamber without changing total ammo count. |

---

### UCS_ArcadeGun

**File**: `UCS_ArcadeGun.cs` | **Inherits**: `UCS_BaseGun`

Minimal subclass. Adds a `reloadAnimation` field and plays it via `GunAnimator.Play()` at the start of reload. Otherwise inherits all behaviour from `UCS_BaseGun`.

| Field | Type | Description |
|---|---|---|
| `reloadAnimation` | AnimationClip | Animation to play when `ReloadGun()` is called |

---

### UCS_ComplexGun

**File**: `UCS_ComplexGun.cs` | **Inherits**: `UCS_BaseGun` | **Sync**: `Manual`

Full VR-physical gun. Overrides reload flow to require manual magazine insertion and slide cycling. Manages slide lock state and delegates magazine state to `UCS_MagSocket`.

#### Additional Inspector Fields

| Field | Type | Description |
|---|---|---|
| `reloadAnimation` | AnimationClip | Reload animation (if used) |
| `gunPickup` | VRC_Pickup | Reference to the pickup component (auto-found if null) |
| `magSocket` | UCS_MagSocket | The socket that accepts/ejects magazines |
| `slidePhysBone` | VRCPhysBone | The PhysBone on the slide |
| `sliderHandler` | UCS_SliderHandler | Detects slide position thresholds |
| `magBelt` | UCS_MagBelt | Optional hip belt to source magazines from |
| `requiredMagPool` | UCS_MagPool | Only mags from this pool (matching type ID) are accepted |
| `magazineVisualRoot` | GameObject | Shown/hidden based on whether a mag is inserted |
| `magazinePickupAnchor` | Transform | Where the magazine snaps when socketed in the gun |
| `prefillFromInventory` | bool | If true, the initial magazine draws ammo from `UCS_AmmoInventory` |

#### Public Methods

| Method | Description |
|---|---|
| `OnMagazineInserted(UCS_Mag)` | Called by `UCS_MagSocket` — adopts mag's ammo count, arms slide cycle. |
| `TransferAmmoToMag(UCS_Mag)` | Copies current `CurrentAmmo` into the mag and zeros the gun's count. Called on ejection. |
| `DumpMagazine()` | Immediately removes the magazine and zeros ammo (no ejection physics). |
| `SetMagazineInserted(bool)` | Updates internal magazine state flag. |
| `SetMagazineVisualVisible(bool)` | Shows or hides `magazineVisualRoot`. |
| `OnEjectionThresholdCrossed()` | Called by `UCS_SliderHandler` — ejects chambered round. |
| `OnSlidePulledBack()` | Called by `UCS_SliderHandler` — handles slide-lock, sounds. |
| `OnSlideForward()` | Called by `UCS_SliderHandler` — plays slide-forward sound. |
| `OnSlideInsertionThesholdCrossed()` | Called by `UCS_SliderHandler` — chambers next round if conditions are met. |
| `OnSlideReleasePressed()` | Call from a UI button or interaction — releases slide from locked-back state. |
| `GetAcceptedMagTypeId()` | Returns the mag type string this gun accepts. |
| `GetRequiredMagPool()` | Returns the `UCS_MagPool` reference. |
| `GetMagazinePickupAnchor()` | Returns the anchor Transform for mag positioning. |
| `GetPrefillFromInventory()` | Returns whether to prefill from inventory. |

#### Animator Parameters

In addition to the base gun parameters:

| Parameter | Type | Description |
|---|---|---|
| `IsFiring` | bool | True during a fire cycle |
| `IsFiringLock` | bool | True when the last round is fired (slide locks back) |
| `BulletVisible` | bool | Whether a round is visible in the chamber |
| `SlideStretch` | float | Current PhysBone stretch (0–1), set by `UCS_SliderHandler` |
| `SlideLocked` | bool | True when slide is locked back |

---

### UCS_SliderHandler

**File**: `UCS_SliderHandler.cs` | **Sync**: `None`

Polls a `VRCPhysBone`'s stretch value every `Update()` and fires threshold events on `UCS_ComplexGun`. Drives `SlideStretch` and `SlideLocked` animator parameters.

#### Inspector Fields

| Field | Type | Description |
|---|---|---|
| `physBone` | VRCPhysBone | The slide's PhysBone |
| `gunAnimator` | Animator | The gun's animator |
| `complexGun` | UCS_ComplexGun | The gun to notify |
| `ejectionThreshold` | float | Stretch at which spent casing is ejected (default 0.35) |
| `fullRearThreshold` | float | Stretch at which "fully back" is triggered (default 0.95) |

#### Events Fired on UCS_ComplexGun

| At stretch | Direction | Method called |
|---|---|---|
| ≥ ejectionThreshold | increasing | `OnEjectionThresholdCrossed()` |
| < ejectionThreshold | decreasing | `OnSlideInsertionThesholdCrossed()` |
| ≥ fullRearThreshold | increasing | `OnSlidePulledBack()` |
| ≤ 0.05 | decreasing | `OnSlideForward()` |

---

### UCS_MagSocket

**File**: `UCS_MagSocket.cs` | **Sync**: `None`

Trigger-volume component handling physical magazine insertion and ejection. Uses `OnTriggerEnter` to detect when a magazine is brought close.

#### Inspector Fields

| Field | Type | Description |
|---|---|---|
| `gun` | UCS_ComplexGun | The gun that owns this socket |
| `magBelt` | UCS_MagBelt | Optional belt to configure on gun pickup |
| `magazineVisualObject` | GameObject | Additional visual shown when a mag is present |

#### Public Methods

| Method | Description |
|---|---|
| `InsertMag(UCS_Mag)` | Inserts a magazine — snaps to anchor, notifies gun, ejects existing mag first. |
| `EjectMag()` | Ejects the current magazine — transfers ammo back to mag, re-enables physics. |
| `HasCurrentMag()` | Returns true if a magazine is currently socketed. |
| `RefreshSocketedMagPickupState()` | Updates pickupability of the socketed mag when gun hold state changes. |
| `SetSocketedMagGunHeld(bool)` | Propagates gun-held state to the socketed mag's pickup. |

---

### UCS_Mag

**File**: `UCS_Mag.cs` | **Sync**: `Manual`

Data and state for a single magazine. Tracks ammo count (synced), held/socketed state, and manages its own lifetime (auto-returns to pool when dropped and uncollected).

#### Inspector Fields

| Field | Type | Description |
|---|---|---|
| `magPool` | UCS_MagPool | The pool this mag belongs to |
| `lifetimeAfterNonInteraction` | float | Seconds before auto-despawn when dropped (default 30) |
| `ammoInventory` | UCS_AmmoInventory | Optional inventory to draw from |
| `magPickupRoot` | GameObject | Root GO with VRC_Pickup — enable/disable to control grabability |
| `magPickupVisual` | GameObject | The visual mesh — can be hidden independently of pickup root |

#### Public Methods

| Method | Description |
|---|---|
| `GetCurrentAmmo()` | Returns current ammo count. |
| `SetCurrentAmmo(int)` | Sets ammo, clamped to pool max. Serializes if owner. |
| `GetMaxAmmo()` | Returns max ammo from the pool definition. |
| `GetMagTypeId()` | Returns the type string from the pool. |
| `GetMagPool()` | Returns this mag's pool reference. |
| `SetHeld(bool)` | Updates held state. |
| `SetSocketed(bool)` | Updates socketed state. |
| `SetSocket(UCS_MagSocket)` | Sets which socket holds this mag. |
| `ClearSocket()` | Clears socket reference. |
| `ResetForPool()` | Resets all state for pool reuse. Called before returning to `VRCObjectPool`. |
| `MarkDropped()` | Records drop time, schedules `DespawnIfExpired`. |
| `SetReturnToPool(pool, holsterPoint, maxDistance)` | Register a pool + holster point to auto-return to when dropped near holster. |
| `ClearReturnToPool()` | Clears the return-to-pool registration. |

---

### UCS_MagPool

**File**: `UCS_MagPool.cs` | **Sync**: `NoVariableSync` | **Requires**: `VRCObjectPool`

Wraps `VRCObjectPool` with mag-specific logic: type ID gating, ammo initialization, and optional inventory integration. The Inspector has a **Generate Pool** button that populates the `VRCObjectPool` automatically from a prefab example.

#### Inspector Fields

| Field | Type | Description |
|---|---|---|
| `magTypeId` | string | Identifies compatible mag types (e.g. `"Pistol"`) |
| `maxAmmo` | int | Maximum ammo per magazine |
| `ammoInventory` | UCS_AmmoInventory | Optional inventory to draw/deposit ammo |
| `ammoInventoryTypeId` | string | Overrides `magTypeId` for inventory lookups if set |
| `poolExample` | GameObject | Template prefab for Generate Pool button |
| `poolSize` | int | Number of instances to generate |

#### Public Methods

| Method | Description |
|---|---|
| `GetMagTypeId()` | Returns the type ID string. |
| `GetMaxAmmo()` | Returns max ammo per mag. |
| `AcquireFullMag()` | Spawns a fully-loaded magazine from the pool. |
| `AcquireMagWithInventory()` | Spawns a magazine with ammo drawn from `UCS_AmmoInventory`. |
| `AcquireMagWithAmmo(int)` | Spawns a magazine with a specific ammo count. |
| `ReturnMagToPool(GameObject)` | Returns a mag to the `VRCObjectPool`. |
| `ReturnMagToPool(UCS_Mag)` | Deposits remaining ammo to inventory, resets mag, returns it to pool. |
| `ApplyDefinitionToMag(UCS_Mag)` | Sets the mag's pool reference (used after spawn). |

---

### UCS_MagBelt

**File**: `UCS_MagBelt.cs` | **Sync**: `NoVariableSync`

Hip-mounted holster that tracks the local player's hip bone and presents a preview magazine. Scales automatically with avatar eye height changes.

#### Inspector Fields

| Field | Type | Description |
|---|---|---|
| `BeltHipBone` | Transform | A bone driven to match the player's `HumanBodyBones.Hips` |
| `MagazineHolsterPoint` | Transform | Where the preview mag appears |
| `ammoInventory` | UCS_AmmoInventory | Fills the preview mag from inventory on pickup |
| `flipBelt` | bool | Mirror the belt for left-handed setup |
| `UseFullHipRotation` | bool | Match full hip rotation vs yaw-only (yaw-only is more stable for most avatars) |

#### Public Methods

| Method | Description |
|---|---|
| `SetRequestedMagSource(typeId, pool)` | Called by `UCS_ComplexGun.OnPickup()` — sets which pool/type to present. |
| `ClearRequestedMag()` | Called by `UCS_ComplexGun.OnDrop()` — removes preview mag, clears pool ref. |
| `OnMagPickedUpFromBelt(UCS_Mag)` | Called when player grabs the preview mag — fills it from inventory and spawns a replacement. |
| `ReturnMagToBelt(UCS_Mag)` | Returns a mag to the pool; respawns the preview. |
| `IsMagCloseToHolster(mag, maxDistance)` | Returns true if the mag is within `maxDistance` of the holster point. |
| `GetMagazineHolsterPoint()` | Returns the holster Transform. |

---

### UCS_HitEffectsPool

**File**: `UCS_HitEffectsPool.cs` | **Sync**: `None`

Self-contained object pool for any GameObject (hit decals, bullet sparks, etc.). Doubles pool size automatically when exhausted.

| Field | Type | Description |
|---|---|---|
| `Prefab` | GameObject | The prefab to pool |
| `PoolParent` | Transform | Parent Transform for pooled instances (uses own transform if null) |
| `InitialPoolSize` | int | Number of instances created on Start (default 4) |

**`AcquireInstance()`** — returns an inactive pooled instance and activates it. Grows pool if needed. The caller is responsible for deactivating the instance when the effect is done (e.g. via the particle system's `Stop Action → Disable`).

---

### UCS_ProjectileManager

**File**: `UCS_ProjectileManager.cs`

Object pool for physical projectile GameObjects. Called by `UCS_BaseGun.FireGun()` when `UseProjectiles` is true.

| Field | Type | Description |
|---|---|---|
| `ProjectilePrefab` | GameObject | The projectile prefab (should have a `Rigidbody`) |
| `ProjectilePoolParent` | Transform | Parent for pooled instances |
| `InitialPoolSize` | int | Starting pool size (default 8) |

| Method | Description |
|---|---|
| `AcquireProjectile()` | Returns an inactive pooled projectile and activates it. |
| `SpawnProjectile(position, direction, speed)` | Acquires a projectile, places it at position, applies `direction * speed` to its Rigidbody velocity. |

---

### UCS_Hitbox

**File**: `UCS_Hitbox.cs`

Abstract base class for damage receivers. Attach to any GameObject that should be damageable. Override `HitEvent` to implement your health/damage system.

| Field | Type | Description |
|---|---|---|
| `hitboxes` | Collider[] | Colliders to enable/disable as a group |

| Method | Description |
|---|---|
| `HitEvent(int damage)` | Called by `UCS_BaseGun` when a raycast hits this hitbox's collider. Override to apply damage. |
| `EnableHitboxes()` | Enables all colliders in `hitboxes`. |
| `DisableHitboxes()` | Disables all colliders in `hitboxes`. |

**Example override:**

```csharp
public class MyPlayerHealth : UCS_Hitbox
{
    [UdonSynced] private int health = 100;

    public override void HitEvent(int damage)
    {
        if (!Networking.IsOwner(gameObject)) return;
        health -= damage;
        health = Mathf.Max(0, health);
        RequestSerialization();
        if (health <= 0) OnDeath();
    }
}
```

---

### UCS_TwoHandedManager

**File**: `UCS_TwoHandedManager.cs`

Manages two-handed grip support. Attach to the gun and configure the secondary grip pickup. Inspect the component in Unity for full field details.

---

### UCS_PickupEventTransferer

**File**: `UCS_PickupEventTransferer.cs`

Routes VRC_Pickup events (`OnPickup`, `OnDrop`, `OnPickupUseDown`, `OnPickupUseUp`) from a parent pickup to child UdonBehaviours. Useful when gun logic lives on a child object rather than the same GameObject as `VRC_Pickup`. Wire the pickup events to this component's relay methods in the Inspector.

---

## FireSelection Enum

Defined in `UCS_BaseGun.cs`.

| Value | Description |
|---|---|
| `Safe` | Trigger does nothing |
| `Semi` | One shot per trigger pull |
| `Auto` | Fires continuously while trigger is held |
| `Burst` | Fires `BurstCount` shots then stops |
