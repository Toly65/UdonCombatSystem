# LLM Gun Porting Pipeline (Blender -> Unity -> UCS)

Notes for an LLM agent porting a new gun model into the Udon Combat System. Written from the
HK416A5 port (`Assets/Toly65/InDevGuns/HK416/`), which is the worked example for everything below.
Read this before touching anything; it will save you most of the measuring and dead ends.

## 0. Prerequisites (install before starting)

| What | How | Check |
|---|---|---|
| **MCP for Blender** (Blender side addon + MCP server) | `uvx mcp-for-blender install-addon`, then in Blender: Preferences > Add-ons > enable "Blender MCP", Start MCP Server. Add the `blender` MCP server to the agent. | `get_addon_status` returns `up_to_date: true` |
| **MCP for Unity** (CoplayDev) | Unity package `com.coplaydev.unity-mcp` (`https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`, already in this project's manifest). Window > MCP for Unity, start the bridge, add the `UnityMCP` server to the agent. | `execute_code` with `return Application.unityVersion;` works |
| Animator As Code v1 | Only needed if you regenerate animators with the UCS Animator Creator Tool. Not needed for the clip-retarget route below. | |

Tool notes:
- `manage_prefabs` and some other UnityMCP tools force an `Assets/` prefix, so they cannot open
  package prefabs (`Packages/com.toly65.udon-combat-system/...`). Use `execute_code` (C#) with
  `AssetDatabase` / `PrefabUtility.LoadPrefabContents` instead. It is the most reliable tool anyway.
- `execute_code` blocks `AssetDatabase.DeleteAsset` unless `safety_checks=false`.
- In `execute_code`, write `UnityEngine.Object`, not `Object` (ambiguous with `object`).
- Never edit the user's source `.blend`. First thing: `bpy.ops.wm.save_as_mainfile()` to a `_UCS.blend` copy.

## 1. Conventions UCS expects (the "gun space")

All gameplay points live in the `Gun+animator` transform space of the M4 template:

- **+Z = muzzle direction, +Y = up, +X = the gun's right side (ejection port side).**
- **Origin = the primary hand grip point** (on the pistol grip, ~35 mm behind and ~12 mm below the
  trigger blade centre; that is where the M4's `Grip Point` sits).
- Units are metres.

Export the model so that **every mesh's origin is this same gun origin** and every transform is
identity. Then every moving part's rest `localPosition` is `(0,0,0)`, and animation clips are pure
offsets. This keeps retargeting trivial.

**Exception: parts that rotate** (folding/pivoting charging handles, hammers, triggers, dust covers)
get their origin **on their pivot axis**, and the round (`Bullet`) gets its origin at its own centre so
the feed tilt rotates about it. Their clips then key absolute values (`rest + delta`), not pure offsets.

## 2. Blender stage

Do it in small `execute_blender_code` steps and `look` after each destructive step.

1. **Inventory**: list objects, tri counts, materials, modifiers, world bounds (`matrix_world @ bound_box`).
2. **Bake**: for every mesh, `bpy.data.meshes.new_from_object(evaluated)` then `me.transform(matrix_world)`,
   clear modifiers, unparent, identity matrix. (Booleans and geometry-node smoothing get baked.)
   Don't use `bpy.data.libraries.load` to re-read source objects later: unparented, their transforms come out wrong.
3. **Delete internals** that are never visible: hammer, springs, firing pin, mag follower, locking lugs, etc.
4. **Merge into the parts UCS animates or toggles**, everything else into `Body`:

   | Object | Why separate |
   |---|---|
   | `Body` | static |
   | `Bolt` (carrier + cam pin, etc.) | animated by slide / fire cycle |
   | `ChargingHandle` | animated by the charge-handle layer (AR style) |
   | `Magazine` | `magazineVisualRoot` toggle + the loose pool mag mesh |
   | `IronSights` | so sights can be hidden when an optic is mounted |

   Pistol: `Slide` instead of `Bolt`/`ChargingHandle`. Add a hammer/trigger only if you will animate it.
5. **Optimise, but do not decimate a model that is already game-ready.** This is VR: players hold the
   receiver ~30 cm from their face and read the roll marks. Optimisation means deleting hidden internals,
   merging objects (draw calls) and atlasing materials, not cutting visible detail.
   - Lossless only by default: `remove_doubles(0.0005)` then `shade_smooth_by_angle(30 deg, keep_sharp_edges=True)`.
     Skip `dissolve_limit` too (it only saved ~8% and creates ngons the exporter re-triangulates).
   - A held VR rifle at ~20k tris is fine. Only reduce when the source is genuinely heavy
     (rough guide: rifle > 50k, pistol > 25k), and then retopo/bake or decimate the least-looked-at
     parts first (stock interior, barrel under the handguard), never the receiver, markings or sights.
   - First HK416 attempt decimated 22k -> 9.7k and the user rejected it: engraved "HK 416" markings,
     selector markings and handguard slots were destroyed.
6. **Atlas** (flat-colour models, the common case): one palette texture, one material.
   Each source material becomes an 8x8 px cell in a 32x32 PNG; every face's UVs collapse to its
   cell centre. A second PNG holds metallic (RGB) and smoothness (A = 1 - roughness) for Unity Standard.
   Write albedo pixels already sRGB-encoded (Blender base colour is linear) and save the image as Non-Color.
   If the model has real textures, bake them to one atlas instead (not covered by the HK example).
   ```python
   N,CELL=32,8; per=N//CELL
   def lin2srgb(c): return c*12.92 if c<=0.0031308 else 1.055*c**(1/2.4)-0.055
   # for material i: cell (i%per, i//per); fill albedo with lin2srgb(base), ms with (met,met,met,1-rough)
   # uv centre = ((cx+.5)*CELL/N, (cy+.5)*CELL/N); assign to every loop of faces using that material
   # then me.materials.clear(); me.materials.append(atlas)
   ```
7. **Scale**: calibrate from standard parts, not overall length (stocks and barrels vary).
   AR family: bolt carrier ~0.17 m, charging handle ~0.175 m, 30 rd STANAG mag ~0.19 m tall.
   The HK416 model needed `0.051`. Some sources are already real-scale metres (the MP5 was): check
   overall length against the real gun first and skip scaling if it matches.
8. **Gun space transform** (bake into mesh data, not object transforms):
   `M = Rot(whatever turns the barrel to -Y) @ Scale(s) @ Translate(-grip_point)`, i.e. **the barrel must
   point Blender -Y, up = +Z**. HK416 (barrel +X): `RotZ(-90)`. MP5 (barrel +Y): `RotZ(180)`.
   (Barrel on +Y puts the muzzle on Unity -Z. Learned the hard way.) Set pivot origins (see section 1)
   *after* this transform, by transforming the pivot point with the same `M`.
9. **Export FBX** (MESH only):
   ```python
   bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'},
       apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
       bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
   ```
   Export straight into `Assets/.../<Gun>/`. Keep the `_UCS.blend` beside the source model, not in Assets.

Useful measurements to print before transforming (in source units, convert after): muzzle tip centre
(bore axis height), trigger blade centre, bolt front face, magazine top centre and bottom,
charging-handle rear tab centre, handguard extents, ejection port extents.

## 3. Unity stage

1. **Import settings** (C#): FBX `materialImportMode = None`, no animation/rig/cameras/lights.
   Palette PNGs: Point filter, no mipmaps, uncompressed, sRGB only on albedo.
   Material: Standard, `_MainTex` = albedo, `_MetallicGlossMap` = MS map, keyword `_METALLICGLOSSMAP`,
   `_GlossMapScale` 1.
2. **Verify axes**: every FBX child should have identity transform, and `Body` mesh bounds centre z > 0.
   A vertex probe near the ejection port should land on +X.
3. **Clone the template**: `AssetDatabase.CopyAsset` of `Packages/com.toly65.udon-combat-system/Prefabs/Guns/M4 setup.prefab`
   (rifles) or `G17 setup.prefab` (pistols) into the gun folder. Internal references (mag pool, socket,
   pickups, two-handed manager) remap to the copy automatically.
4. **Edit with `PrefabUtility.LoadPrefabContents`**:
   - Delete `Visual/Mk18_mod0` and `Visual/RedDot`; `PrefabUtility.InstantiatePrefab(fbx, Visual)`, name it after the gun,
     assign the atlas material.
   - `UCS_ComplexGun.magazineVisualRoot` and `UCS_MagSocket.magazineVisualObject` -> the new `Magazine` child.
   - Animator `runtimeAnimatorController` -> the retargeted controller (step 5).
   - Reposition from measurements (HK416 values, gun space):

     | Transform | M4 | HK416 | Rule |
     |---|---|---|---|
     | `MuzzlePoint` | (0, .060, .377) | (0, .0717, .4687) | bore height, barrel tip |
     | `Visual/Bullet` | (0, .060, .140) | (0, .0717, .156) | just ahead of bolt face, on bore |
     | `EmptyCasingDischargeParticle` | (.014, .069, .068) | (.016, .075, .095) | ejection port, +X side |
     | `Slide stuff/PhysbonePoller` + `Slider handler and physbone` | (0, .083, -.020) | (0, .0935, -.025) | charging-handle rear tab (pistol: slide rear serrations) |
     | `MagazineSocket` | (0, .043, .090) | (0, .052, .092) | magwell, shift with mag top |
     | `MagazineSocket/MagazinePickupAnchor` (gun space) | (0, -.076, .097) | (0, -.067, .100) | shift with mag top |
     | `Secondary grip` | (0, .028, .196) | (0, 0, .28) | **y = 0**, see below |
     | `Secondary grip/Exact Gun` | (0, .038, -.004) | (0, .07, 0) | handguard centre height |
     | `Secondary grip` BoxCollider | c(0,-.03,.02) s(.04,.1,.03) | c(0,.075,.02) s(.05,.07,.16) | front part of the handguard only, see below |
     | `Secondary grip` VRCPickup proximity | 2 | 0.1 | **never leave the template's 2 m** |
     | `Primary Grip` 2nd BoxCollider | c(0,.075,.147) s(.02,.021,.395) | c(0,.075,.11) s(.03,.04,.70) | whole-gun physics body |

     **Secondary grip must sit on the grip's Z axis (local y = 0).** `UCS_TwoHandedManager.ApplyTwoHandedRotation`
     aims the visual's +Z straight at the secondary pickup's *origin*, so any height there becomes a
     permanent pitch offset when two-handing. Put the hand height on `Exact Gun` instead.

     **Keep the front grip's grab zone tight.** The templates ship the secondary VRCPickup with
     `proximity = 2` (VR laser-grab reach), so reaching for the charging handle grabs the front grip by
     accident. Set proximity to 0.05-0.1 (hand contact only; use 0.05 when the charging handle is near the handguard) and size the trigger box to the handguard's real
     cross-section (HK416: 5 x 7 cm), covering only where the support hand actually sits, with a clear gap
     (HK416: ~25 cm) back to the charging handle / slide PhysBone. This matters more on guns whose charging
     handle sits *on* the handguard (G3, MP5, UMP, SCAR): there, end the box ahead of the handle's full
     travel, or the two grabs overlap.
   - Mag pool: rename, and in every pooled mag swap `MagPickup/mag_waffle (1)` (keep the GameObject so
     `magPickupVisual` stays wired) to the `Magazine` mesh + atlas material, `localPosition = -anchorInGunSpace`,
     identity rotation/scale. Set the `MagPickup` BoxCollider around the mag (`magCentre - anchor`).
     Keep `magTypeId` the same as the M4 (`m4Rifle`) for STANAG guns so mags interchange; give it a new id
     for a different magazine family.
   - **Re-read positions after `SaveAsPrefabAsset`.** One edit (`Bullet`) silently didn't persist on the first save.
5. **Animations: retarget the template *clips*, then build the controller with the UCS Animator Creator Tool.**
   **Never copy a shipped `.controller`; always generate.** Controllers drift from the generator: until
   2026-10-11 `M4.controller` was stale output (`IsFiring` as a Bool while the scripts call `SetTrigger`,
   so the FireCycle layer never returned to idle and pinned the bolt forward: no lock-back, no racking).
   The first HK416 port copied it and shipped broken. M4 has since been regenerated with the tool.
   To regenerate an existing controller in place (keeping its GUID so prefabs stay wired): generate into a
   temp controller, copy the temp file's contents over the old `.controller` file (keep the old `.meta`),
   fix `m_Name`, `ImportAsset(ForceUpdate)`, delete the temp.
   The tool lives at `Assets/Toly65/UCS animator creator Tool/Editor/GunSlideAnimatorGeneratorEditor.cs`
   (`Tools > Gun Slide > Animator Generator`, needs Animator As Code v1).

   5a. Copy the clips that reference moved parts (`Charging handle`, `Fire`, `SlideMovingBack`,
   `SlideMovingForward`, `SlideRear`, `slideForward`). Reuse `EnableBullet`/`DisableBullet` as-is.
   For each curve binding:
   - `Visual/Mk18_mod0/bolt_group` -> `Visual/<Gun>/Bolt`; position keys `v' = (-0.8x, 0.8y, -0.8z)`
     (Mk18_mod0 is rotated 180 deg Y and scaled 0.8; rest is 0).
   - `Visual/Mk18_mod0/charging_handle` -> `Visual/<Gun>/ChargingHandle`; `v' = flip * (v - (0, .023852462, .08744008))`.
   - `Visual/Bullet` position keys: add `(newBullet - oldBullet)`; rotation keys unchanged.
   - Scale `inTangent`/`outTangent` by the same factor as the value.
   Result: bolt and charging handle travel 64 mm back along -Z (same as the M4).

   5b. Create a fresh empty controller (`AnimatorController.CreateAnimatorControllerAtPath`) **in its own
   `execute_code` call**, then assign it to the `Gun+animator` Animator in the prefab in a *second* call.
   Doing both in one call saves a null reference. If the instance still reads null, `ImportAsset(prefab, ForceUpdate)`.

   5c. Run the tool headless via reflection. `Generate(bool showDoneDialog = true)` takes `false` so the
   "Done" modal doesn't block the editor:
   ```csharp
   var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GunSlideAnimatorGeneratorEditor")).First(x => x != null);
   var tool = ScriptableObject.CreateInstance(t);
   void F(string n, object v) => t.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(tool, v);
   F("gunAnimator", anim); F("animatorRoot", anim.transform); F("assetContainer", ctrl);
   F("clipSlideMovingBack", movingBack); F("clipSlideFullyRear", rear); F("clipSlideLockedBack", rear);
   F("clipSlideReturning", movingForward);   // M4 "SlideMovingForward": forward->rear + round feeding up from the mag
   F("clipSlideForward", forward);
   F("includeChargeHandleLayer", true); F("clipChargeHandleMotion", chargingHandle);   // AR style
   F("clipFireCycle", fire); F("clipFireCycleLock", rear);
   F("clipBulletEnable", enableBullet); F("clipBulletDisable", disableBullet);
   t.GetMethod("Generate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tool, new object[]{ false });
   UnityEngine.Object.DestroyImmediate(tool);
   ```
   Expect params `SlideStretch:Float, SlideLocked:Bool, IsFiring:Trigger, IsFiringLock:Bool, BulletVisible:Bool`.

   5d. **Check every clip's stop time.** A clip whose `AnimationClipSettings.stopTime` is 0 while its curves
   run to 0.083 s plays as zero-length at runtime: the state reports `length 1.000` and only frame 0 is ever
   shown (bolt never cycles, slide pull doesn't follow the hand). The shipped M4 `Fire.anim` and
   `SlideMovingBack.anim` had this until 2026-10-11. Scan and fix:
   ```csharp
   var s = AnimationUtility.GetAnimationClipSettings(clip);
   if (Mathf.Abs(s.stopTime - clip.length) > 0.001f) { s.startTime = 0; s.stopTime = clip.length; AnimationUtility.SetAnimationClipSettings(clip, s); }
   ```
   Clips rebuilt with `new AnimationClip()` + `SetEditorCurve` (step 5a) get a correct stop time automatically;
   clips you reuse untouched do not.
   A gun with different mechanics (G3 roller lock, bullpup, pistol) needs its own clips and options
   (`manualLockOnly`, `includeChargeHandleGrabLayer`): see Gun-Setup wiki Step 6.
6. **Bullet mode: pick by what the weapon fires.** Since 2026-10-12 the M4 and G17 templates ship in
   Simulated mode with tracer/audio templates, so a firearm cloned from them only needs its muzzle
   velocity set. Older prefabs (and anything cloned before then) were Raycast.

   | Bullet Mode | Use for | Settings |
   |---|---|---|
   | **Raycast** | Things with no travel time or drop: lasers, energy beams, hitscan arcade guns | `Raycast` on |
   | **Simulated Projectile** (default for firearms) | Real ballistics: rifles, pistols, SMGs, shotguns, MGs. Travel time, drop, tracers, whizz-by | `SimulatedProjectiles` on, `Raycast`/`UseProjectiles` off; `SimulatedProjectileSpeed` = real muzzle velocity |
   | **Rigidbody Projectile** | Slow, physical, visible projectiles: grenade launchers, rockets, missiles, bolts/arrows | `UseProjectiles` on + a `UCS_ProjectileManager` |

   Muzzle velocities used: 5.56 rifle (HK416 14.5") 880, M4 900, 9 mm SMG (MP5) 400, 9 mm pistol 375.
   Simulated mode also needs two children on the gun root, cloned from a gun that already has them
   (`Assets/Toly65/Udon Matchmaking Framework/Prefabs/MatchmakingSystemTest.prefab` -> `M4 (1)`):
   `BulletTracer` (LineRenderer, -> `TracerTemplate`) and `BulletAudio` (AudioSource + VRC Spatial Audio,
   -> `BulletAudioTemplate`). Copy `SurfaceImpactSounds` (3) and `WhizzSounds` (4), gravity 1,
   lifetime 3, max in flight 32, tracer every 3rd round, 6 voices, whizz radius 2.5 from the same gun.
   If you earlier copied scene refs from a Raycast gun, revert the instance's `SurfaceImpactSounds` /
   `WhizzSounds` overrides, or they stay empty. Verify: after a burst the gun root has 32 + 1
   `BulletTracer` and 6 + 1 `BulletAudio` objects.
7. **Scene references** (not in the prefab): copy `magBelt`, `HitEffectsPool`, `BulletSparkPool`,
   `SurfaceImpactSounds`, `WhizzSounds` from an existing gun in the scene, and `UCS_MagSocket.magBelt`.
8. **UdonSharp note**: `UdonBehaviour.publicVariables` looks empty in the editor; that is normal for
   U# 1.x (the proxy component is the source of truth and is copied at play/build). Set fields through
   `new SerializedObject(proxyComponent)`.

## 4. Verify

- Screenshot (`manage_camera screenshot` with `view_position`/`view_target`, `camera="Main Camera"`)
  side-on next to the template gun, to check scale and colours.
- Sample clips in edit mode: `AnimationMode.StartAnimationMode(); BeginSampling(); SampleAnimationClip(go, clip, t); EndSampling(); ... StopAnimationMode();`
  and print part positions at t = 0, 0.5, 1.
- Play mode (ClientSim) smoke test on the gun's `UdonBehaviour`:
  `SendCustomEvent("Pickup")`, `SendCustomEvent("PerformDesktopReload")`, wait a frame or two,
  check `GetProgramVariable("CurrentAmmo") == MagazineSize` and `bulletChambered == true`,
  then `TriggerPull` -> ammo decrements.
- **Ammo going down is not proof the animator works.** Log part positions every frame with an
  `EditorApplication.update` callback (`Debug.Log` bolt/charging-handle z, current clip per layer,
  `SlideLocked`, `CurrentAmmo`), fire until empty, then `PerformDesktopReload`, and read the log back.
  Expected: bolt cycles 0 -> -0.064 -> 0 per shot, FireCycleLayer returns to `FireIdle` between shots,
  last round holds the bolt back (`FireCycleLock`), and the reload ends in `SlideReturning` -> `SlideForward`
  with `bulletChambered` true.
- Known pre-existing console noise (also on the shipped M4, not caused by a port):
  `Parameter 'Hash 901101572' does not exist` (`SlideGrabbed` is not in the generated controllers) and
  `UnassignedReferenceException ... VRCObjectPool` from ClientSim.
- Still needs a human in VR: grip feel (`Grip Point`, `Exact Gun`), slide PhysBone pull reaching
  stretch ~1.0, mag insertion feel. Ask the user to playtest.

## 5. HK416 port record (reference numbers)

- Source: `HK416A5.blend` (30 objects, 7 flat materials, 22.4k tris, X-forward, Z-up, unit scale ~19.6x real).
- Output: `Assets/Toly65/InDevGuns/HK416/` - `HK416.fbx` (Body 16788, Bolt 1122, ChargingHandle 664,
  IronSights 915, Magazine 792 = 20.3k tris, no decimation, 5 renderers, 1 material), `HK416_Atlas.mat` + two 32x32 palette PNGs,
  `Anim/` (controller + 6 retargeted clips), `HK416 setup.prefab` (gun + 20-mag pool).
- Scale 0.051, grip point in source units (-1.23, 0, -1.03), bore height 0.0717 m above grip.
- An instance sits on the gun table in `Gun Demo World.unity` next to the M4.

## 6. Non-AR mechanics: HK roller-delayed (MP5 / G3) worked example

The M4 clips can't be retargeted onto a gun that works differently: **author the clips in code**
(`new AnimationClip()`, `clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", curve)`,
`"localEulerAnglesRaw.z"` for rotations, `typeof(GameObject), "m_IsActive"` for visibility, linear
tangents, 5 frames @ 60 fps). Clips built this way get correct stop times automatically.

**Map the real mechanism onto the framework first.** Read `UCS_ComplexGun` / `UCS_SliderHandler`;
the knobs that matter:

| Real gun behaviour | Framework setting |
|---|---|
| Bolt locks open on the last round (AR, Glock) | `autoLockSlideOnEmpty` on; tool: Fire Cycle Lock clip |
| Bolt closes on empty, player latches it (MP5, G3) | `autoLockSlideOnEmpty` off, `manualSlideLockOnly` on; tool: **Manual Lock Only** |
| Charging handle moves when pulled but not when firing (AR, MP5, G3) | tool: **Include Charge Handle Layer** (motion clip driven by `SlideStretch`) |
| Handle pivots out the moment it is grabbed | tool: **Include Charge Handle Grab Layer** (`SlideGrabbed`). Its Pivoted and Locked states share one clip, so it rotates on grab |
| Handle only rotates when it latches into the notch (MP5) | **no** grab layer: key the handle's rotation in the slide clips instead, `-35` in `SlideLockedBack`, `0` in every other slide clip (WD is off, so every state must key it) |
| Handle springs home while the bolt stays locked (AR) | `UCS_SliderHandler.separateChargingHandle` on |
| Handle *is* what holds the bolt back (MP5, G3) | `separateChargingHandle` off |
| VR player latches by holding the handle back | slide PhysBone **`allowPosing` on** (posing is the only VR path into the lock; `OnPhysBonePosed` at stretch >= 0.95). It's an `AdvancedBool` enum in C#, not a bool |
| HK slap release | built in: with `manualSlideLockOnly`, the next grab after letting go releases the lock |

**MP5 parts and clips** (`Assets/Toly65/InDevGuns/MP5/`):
- Parts: `Body`, `Bolt`, `ChargingHandle` (origin on the cocking-tube axis), `Magazine`, `Bullet` (the
  source's loose `cartridge_9mm`, moved into the chamber in Blender, origin at its centre).
- Bolt travel 65 mm, notch hold 60 mm. Handle motion clip (ChargeHandleLayer) moves position only;
  the slide clips key only its rotation (`localEulerAnglesRaw.z = -35` in `SlideLockedBack`, knob on -X
  swings up; 0 elsewhere). Two layers can drive the same transform as long as each clip keys different
  properties (WD is off). First attempt used the grab layer and the user rejected it: the handle
  snapped into the notch rotation as soon as it was grabbed.
- **Swap the casing particle.** The template's `EmptyCasingDischargeParticle` renders the M4's 5.56
  `M855` mesh. Pistol-calibre guns need their own: the MP5 uses `MP5_Casing9mm.fbx`, cut from the
  model's cartridge (case + primer faces only, picked by palette-UV cell, origin at centre, real metres;
  the particle's scale 10 x startSize 0.1 = 1:1).
- `SlideReturning` and `Fire` include the round: extracted with the bolt, then a fresh one rising
  nose-up (`x = -15 deg`) from the mag lips and feeding into the chamber.
- Charging handle on the handguard: the front-grip box is a thin strip under the *front* of the
  handguard only (c(0,.055,.03) s(.05,.02,.07) relative to the grip at z .24, so z .235-.305, y .045-.065;
  knob at y .113), proximity **0.05**, `Exact Gun` moved forward to match. The first, larger box
  (underside of the whole handguard, proximity 0.1) still stole the handle grab. Start this small.
- Own mag pool, `magTypeId = mp5_9mm`. 30 + 1 rounds after a reload.

**Verified in ClientSim** (frame log): desktop reload grabs -> handle swings out -> bolt follows the
handle back -> latches in the notch -> mag swap -> handle drops out of the notch and rides forward with
the bolt while the round feeds; firing cycles the bolt while the handle stays put; bolt closes on empty.

**Known framework quirks seen on this gun:**
- `UCS_BaseGun.FireGun` sets `IsFiringLock` on the last round without checking `autoLockSlideOnEmpty`,
  so on manual-lock guns remote clients skip the final bolt cycle and log `Parameter 'IsFiringLock' does not exist`.
- `UCS_ComplexGun`'s Play() fallback looks up layer `"FireCycleLayer"`, but the tool names it
  `GunSlide__FireCycleLayer`, so the fallback never runs (the trigger path works).
- `SlidePosed` / `SlideGrabbed` "does not exist" warnings on controllers without those layers are harmless.

Not covered yet (needs a human in VR): whether the -35 deg notch angle and 3.5 cm PhysBone child give a
natural pull, and whether posing at full rear feels right as the latch gesture.
