# Complex Gun Setup Guide

> **No coding required.** This guide walks you through dropping a working physical-reload gun into your VRChat world using the included prefabs.

The Complex Gun (`UCS_ComplexGun`) gives players a VR-physical experience: they manually eject and insert magazines, and rack the slide to chamber a round. Two prefabs are included:

| Prefab | Use case |
|---|---|
| **Example one handed gun setup** | Pistols, one-handed weapons |
| **M4 stub** | Rifles, two-handed weapons |

---

## Step 1 — Find and duplicate the prefab

In your Project window, locate the prefab you want to use. Drag it into your scene, or duplicate an existing instance.

**Do not edit the original prefab directly** — duplicate it (Ctrl+D in the Hierarchy) so your changes stay local to your scene or variant.

---

## Step 2 — Understand the hierarchy

### One-handed gun

```
One-Handed Gun               ← VRC_Pickup + UCS_ComplexGun live here (root)
  └── Primary Grip
        ├── Visual            ← your 3D gun mesh goes here
        ├── Grip Point        ← where the player's hand attaches
        ├── Bullet Casing     ← particle for ejecting casings
        ├── MuzzlePoint       ← empty Transform pointing out the barrel
        ├── Mechanics Audio   ← AudioSource for slide/mechanical sounds
        ├── SliderPhysbone    ← VRCPhysBone driving the slide
        ├── MagazineSocket    ← trigger volume for mag insertion
        └── MagazinePickupAnchor  ← where the mag snaps inside the gun
MagPool                      ← VRCObjectPool + UCS_MagPool (keep in scene)
```

### Two-handed gun

```
Gun Root                     ← VRC_Pickup + UCS_ComplexGun live here (root)
  ├── Primary Grip
  │     └── Grip Point        ← primary hand attachment
  ├── Secondary Grip          ← second hand attachment point
  │     ├── Front Target
  │     └── Exact Gun
  └── Gun+animator            ← gun mesh, audio, and all components
        ├── Primary Grip point
        ├── EmptyCasingDischargeParticle
        ├── Visual             ← your 3D gun mesh goes here
        ├── MuzzlePoint
        ├── Mechanics Audio
        ├── SliderPhysbone
        ├── MagazineSocket
        ├── MagazinePickupAnchor
        └── TwoHandedManager   ← handles the second-hand grip logic
MagPool                       ← keep in scene alongside the gun
```

> `UCS_ComplexGun` and `VRC_Pickup` are on the **root object** of the gun — not on a child. This keeps it easy to select without expanding the hierarchy, and avoids some edge-case sync issues with VRChat's ownership model.

> **Keep the MagPool in the scene.** It manages the magazines that players grab to reload — if you delete it, reloading breaks.

---

## Step 3 — Swap in your gun mesh

1. Expand the hierarchy to find the **Visual** child.
2. Replace or add your 3D model as a child of Visual (or replace its existing mesh).
3. Make sure **MuzzlePoint** still points forward out of the barrel — rotate it if needed.
4. **Grip Point** controls where the player's hand snaps. Move it to sit naturally in the hand.

---

## Step 4 — Configure UCS_ComplexGun in the Inspector

Select the **root object** of the gun — that's where `UCS_ComplexGun` lives on both prefabs.

### The fields you'll actually need to change

**Fire settings**

| Field | What it does | Common values |
|---|---|---|
| Fire Mode | Semi, Auto, Burst, or Safe | Semi for pistols, Auto for rifles |
| Rounds Per Minute | Fire rate | 400–600 for pistols, 700–900 for rifles |
| Magazine Size | Rounds per mag | 17 for pistol, 30 for rifle |
| Damage | Damage per bullet | 25 |
| Range | Max bullet range in meters | 100 |

**Ammo type**

| Field | What it does |
|---|---|
| Required Mag Pool | Drag in the MagPool from your scene. When the player picks up the gun, this tells the MagBelt which type of magazine to present at the hip — so the right ammo appears automatically. |

**Hit effects** — without these the gun fires silently with no visual feedback. Two pools are needed:

| Field | What it does |
|---|---|
| Hit Effect Pool | A `UCS_HitEffectsPool` containing your hit decal prefab — appears where bullets land on surfaces |
| Bullet Spark Pool | A `UCS_HitEffectsPool` containing your bullet spark prefab — the flash on impact |

Add a `UCS_HitEffectsPool` component to a GameObject in your scene (or on the gun itself), assign your prefab and an initial pool size of 4–8, then drag that component into both fields. You can use the same pool for both if you only have one effect prefab.

**Audio** — drag your AudioClip assets into FireSound, EmptyFireSound, magpullSound, maginsertSound, slideBackSound, slideForwardSound.

**Haptic Feedback** — turn on for VR controller rumble on each shot.

### Fields that are already wired in the prefab

These are pre-configured and you usually don't need to touch them:

- Mag Socket → MagazineSocket child
- Slide Phys Bone → SliderPhysbone child
- Slider Handler → UCS_SliderHandler on SliderPhysbone
- Magazine Visual Root → Visual child
- Magazine Pickup Anchor → MagazinePickupAnchor child

---

## Step 5 — Configure the MagPool

Select the **MagPool** object in your scene.

| Field | What to set |
|---|---|
| Mag Type ID | Must match the gun's type ID — default is already set correctly in the prefab |
| Max Ammo | Match this to the gun's Magazine Size |
| Pool Size | How many loose magazines can exist at once — 4 is usually enough |

If you change Pool Size, click **Generate Pool** in the Inspector to rebuild the child magazine objects.

---

## Step 6 — Set up animations

The gun animator needs several clips to drive the slide and fire cycle. Setting this up by hand in Unity's Animator window is tedious, so we built a generator tool that wires everything up automatically.

**The generator tool is a separate optional download.** It depends on [AnimatorAsCode](https://github.com/hai-vr/av3-animator-as-code) — a third-party library that has to be installed first. Because not everyone needs it (you can wire the animator by hand if you prefer), we kept the tool and its dependency out of the main package to avoid bloating installs that don't need it.

To use it: install AnimatorAsCode, then import the generator tool package. It will appear at **Tools → Gun Slide → Animator Generator**. Open it, assign your Gun Animator and the clips listed below, then hit **Generate**. The tool builds all the animator layers and transitions for you.

### Slide tracking clips

These control what the slide looks like as the player physically pulls it back and releases it.

| Clip | What it should be |
|---|---|
| **Slide Moving Back** | The slide travelling from fully forward to fully rearward. This is driven by the PhysBone stretch value, so it acts as a scrub — the animation position matches how far back the player is pulling. **Turn off looping.** If the slide looks jittery when you pull it back, this is why. |
| **Slide Fully Rear** | A single frame of the slide at its rearmost position. |
| **Slide Locked Back** | A single frame of the slide held open (same as Slide Fully Rear unless your gun has a separate visual for the locked state, like a bolt catch). |
| **Slide Returning** | The slide travelling back forward. Also scrubbed by PhysBone stretch — the same note about looping applies. **Turn off looping.** |
| **Slide Forward** | The slide at rest in its forward/closed position. |

**Optional — Charge Handle Motion**: if your gun has a separate charging handle (e.g. an AR-style bolt), enable the charge handle layer in the tool and supply this clip. Like the slide clips, it's scrubbed by a PhysBone value — **turn off looping on it**. If the charging handle looks jittery when fully pulled back, that's why.

### Fire cycle clips

These play automatically when the gun fires. They should play **once and stop** — do not loop them.

| Clip | What it should be |
|---|---|
| **Fire Cycle** | The slide snapping back and returning forward — the normal fire animation when ammo remains. |
| **Fire Cycle Lock** | The slide snapping back and staying open — plays on the last round in the magazine. |

### Bullet visibility clips

Simple one-frame clips that show or hide the round in the chamber.

| Clip | What it should be |
|---|---|
| **Enable Bullet** | A single frame with the bullet/chambered round visible. |
| **Disable Bullet** | A single frame with the bullet/chambered round hidden. |

---

## Step 7 — Optional: add a MagBelt

A `UCS_MagBelt` is a hip holster that presents a spare magazine for the player to grab. Without it, players must pick up magazines dropped on the ground.

1. Add a `UCS_MagBelt` GameObject to your scene (or drag in the included belt prefab).
2. Drag it into the **Mag Belt** field on `UCS_ComplexGun`.
3. The belt tracks the player's hip bone automatically — no further setup needed.

---

## How reloading works (for reference)

1. Player fires the last round → slide locks back automatically.
2. Player grabs the magazine inside the gun and pulls it out.
3. Player reaches to their hip belt (or the ground), grabs a fresh magazine.
4. Player brings the magazine into the **MagazineSocket** trigger volume — it snaps in.
5. Player grabs and releases the **SliderPhysbone** to rack the slide.
6. Gun is loaded and ready to fire.

---

## Common tweaks

| I want… | What to change |
|---|---|
| Faster fire rate | Increase Rounds Per Minute |
| More damage | Increase Damage |
| Longer range | Increase Range |
| Full auto | Fire Mode → Auto |
| 3-round burst | Fire Mode → Burst, Burst Count = 3 |
| Shotgun spread | Enable Shotgun Mode, set Pellets Per Shot |
| Projectile bullets instead of hitscan | Disable Raycast, enable Use Projectiles, assign a Projectile Manager |
| VR controller rumble | Enable Haptic Feedback |
| Gun resets ammo when dropped | Enable Refill Ammo On Disable |
