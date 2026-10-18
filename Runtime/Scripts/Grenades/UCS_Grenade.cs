using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.UdonNetworkCalling;

// Generic grenade. Lives on an always-active parent (the pickup child gets enabled/disabled by stowing and
// despawning), owns all synced state, and is driven by UCS_GrenadePickup on the pickup.
//
// Flow: Safe -> pull every pin (physbone in VR, Use on desktop) -> Armed -> release the pickup -> Live
// (lever ejects, fuse burns / impact arms) -> detonate -> Spent -> optional respawn.
// Grenade types are just settings: frag = fuse + explosion pool, impact = DetonateOnImpact + explosion pool,
// flashbang = fuse + flash pool. Whatever is in DetonationPool is spawned through SpawnAt with the thrower as blame.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_Grenade : UdonSharpBehaviour
{
    public const int StateSafe = 0;
    public const int StateArmed = 1;
    public const int StateLive = 2;
    public const int StateSpent = 3;

    [Header("Parts")]
    [SerializeField] private VRC_Pickup Pickup;
    [Tooltip("Pin meshes that detach when pulled. Every pin must be pulled to arm the grenade.")]
    [SerializeField] private Transform[] Pins;
    [Tooltip("One physbone per pin (same order). Pulling it past Pin Pull Stretch pulls that pin. Leave empty for desktop-style Use-to-pull only.")]
    [SerializeField] private VRCPhysBone[] PinPhysBones;
    [Tooltip("Safety lever that ejects on release. Optional (impact grenades like the T13 have none).")]
    [SerializeField] private Transform Lever;
    [Tooltip("Where the detonation spawns. Defaults to the pickup.")]
    [SerializeField] private Transform DetonationPoint;

    [Header("Detonation")]
    [SerializeField] private UCS_HitEffectsPool DetonationPool;
    [Tooltip("Seconds from release to detonation. For impact grenades this is a fallback, 0 = never.")]
    [SerializeField] private float FuseTime = 4f;
    [SerializeField] private bool DetonateOnImpact = false;
    [Tooltip("Impacts are ignored for this long after release, so it doesn't go off on the thrower's hand")]
    [SerializeField] private float ImpactArmDelay = 0.15f;
    [Tooltip("Layers holding UCS hitboxes. Pickups don't physically collide with players, so impact grenades sweep their flight path against these instead.")]
    [SerializeField] private LayerMask ImpactHitboxLayers = 1 << 8;
    [Tooltip("Seconds after detonation before the grenade resets: back into Return Pool if set, otherwise at its spawn point. Negative = stays spent until ResetGrenade is called.")]
    [SerializeField] private float RespawnDelay = -1f;
    [Tooltip("Pool this grenade is a managed object of (e.g. a dispenser's pool). Defaults to a BulkPreOrderPool on a parent.")]
    [SerializeField] private BulkPreOrderPool ReturnPool;

    [Header("Pin / Lever")]
    [Tooltip("Physbone stretch (0-1 of its max stretch) that counts as the pin being pulled out")]
    [SerializeField] private float PinPullStretch = 0.8f;
    [SerializeField] private float LeverEjectSpeed = 3f;
    [SerializeField] private float LeverEjectSpin = 25f;
    [Tooltip("Seconds a detached pin or lever stays in the world")]
    [SerializeField] private float LoosePartLifetime = 10f;

    [Header("Left Hand")]
    [Tooltip("VRChat uses the same ExactGun for both hands, so a left hand holds it like a right hand (pins on the outside). " +
             "When set, picking up left-handed turns this root upside down about the grip's forward axis: the real left-handed " +
             "grip, thumb on the lever, pins on the inside at the bottom.")]
    [SerializeField] private Transform HandFlipRoot;
    [Tooltip("The pickup's ExactGun transform (VRChat gun frame: +X forward)")]
    [SerializeField] private Transform GripPoint;

    [Header("VR Throw")]
    [Tooltip("Pickup throw boost at Reference Eye Height. Hand speed scales with avatar size, so the boost is divided by " +
             "eye height on every pickup to keep throw distance the same across avatar sizes. 0 leaves the pickup's own boost alone.")]
    [SerializeField] private float ThrowBoostAtReference = 2f;
    [SerializeField] private float ReferenceEyeHeight = 0.93f;
    [SerializeField] private float MinThrowBoost = 1f;
    [SerializeField] private float MaxThrowBoost = 3f;

    [Header("Desktop Throw")]
    [Tooltip("Speed of a click throw (desktop: click to pull the pin, click again to throw)")]
    [SerializeField] private float ClickThrowSpeed = 12f;
    [Tooltip("Upward bias added to the look direction, so a level throw still arcs")]
    [SerializeField] private float ClickThrowLift = 0.2f;
    [Tooltip("Looking at the floor turns the throw into a fast underhand toss along the line of sight (no lift) that skims in and rolls on")]
    [SerializeField] private float ClickUnderhandSpeed = 6f;
    [Tooltip("Degrees below the horizon where the overhand throw starts blending into the underhand toss")]
    [SerializeField] private float ClickUnderhandPitchStart = 10f;
    [Tooltip("Degrees below the horizon where the toss is fully underhand")]
    [SerializeField] private float ClickUnderhandPitchFull = 35f;
    [Tooltip("Steepest an underhand toss leaves the hand, in degrees below the horizon. Shallow skims land and roll on; steep ones bounce and stop.")]
    [SerializeField] private float ClickUnderhandMaxDownDegrees = 20f;

    [Header("Audio")]
    [SerializeField] private AudioSource MechanicsAudio;
    [SerializeField] private AudioClip PinPullSound;
    [SerializeField] private AudioClip LeverReleaseSound;

    [UdonSynced] private int state = StateSafe;
    [UdonSynced] private int pinsPulledMask = 0;
    [UdonSynced] private int throwerId = -1;
    // server time (ms) so a fuse survives ownership changes (thrower leaves, someone picks a live grenade back up)
    [UdonSynced] private int detonateAtServerMs = 0;
    [UdonSynced] private int impactArmAtServerMs = 0;
    [UdonSynced] private bool heldLeft = false;

    private Vector3 flipRootPos;
    private Quaternion flipRootRot;
    private Vector3 flipRootFlippedPos;
    private Quaternion flipRootFlippedRot;

    private bool heldLocally = false;
    private bool loopRunning = false;
    private Vector3 lastSweepPos;
    private int allPinsMask = 0;
    private Rigidbody pickupBody;
    private VRCObjectSync pickupSync;
    // optional: lets the grenade go in stow pockets; when present it owns hiding the pickup
    private StowableManager stowManager;

    // detached-part bookkeeping: pins at [0..n), lever at [n]
    private Transform[] parts;
    private Transform[] partParents;
    private Vector3[] partLocalPos;
    private Quaternion[] partLocalRot;
    private bool[] partDetached;
    private float[] partDetachTime;

    private void Start()
    {
        if (Pins == null) Pins = new Transform[0];
        if (PinPhysBones == null) PinPhysBones = new VRCPhysBone[0];
        allPinsMask = (1 << Pins.Length) - 1;
        if (Pickup != null)
        {
            pickupBody = Pickup.GetComponent<Rigidbody>();
            pickupSync = (VRCObjectSync)Pickup.GetComponent(typeof(VRCObjectSync));
        }
        if (DetonationPoint == null && Pickup != null) DetonationPoint = Pickup.transform;
        if (ReturnPool == null) ReturnPool = GetComponentInParent<BulkPreOrderPool>();
        stowManager = GetComponent<StowableManager>();

        if (HandFlipRoot != null && GripPoint != null && HandFlipRoot.parent != null)
        {
            Transform space = HandFlipRoot.parent;
            flipRootPos = HandFlipRoot.localPosition;
            flipRootRot = HandFlipRoot.localRotation;
            Vector3 pivot = space.InverseTransformPoint(GripPoint.position);
            Quaternion half = Quaternion.AngleAxis(180f, space.InverseTransformDirection(GripPoint.right));
            flipRootFlippedPos = pivot + half * (flipRootPos - pivot);
            flipRootFlippedRot = half * flipRootRot;
        }

        int n = Pins.Length + 1;
        parts = new Transform[n];
        partParents = new Transform[n];
        partLocalPos = new Vector3[n];
        partLocalRot = new Quaternion[n];
        partDetached = new bool[n];
        partDetachTime = new float[n];
        for (int i = 0; i < n; i++)
        {
            Transform p = i < Pins.Length ? Pins[i] : Lever;
            parts[i] = p;
            if (p == null) continue;
            partParents[i] = p.parent;
            partLocalPos[i] = p.localPosition;
            partLocalRot[i] = p.localRotation;
            // attached parts ride their parent; an interpolated body would keep its own pose when the grenade is
            // teleported (dispensers, respawns) and be left floating where it used to be
            Rigidbody partBody = p.GetComponent<Rigidbody>();
            if (partBody != null) partBody.interpolation = RigidbodyInterpolation.None;
            // loose parts must not shove the grenade they just came off
            Collider partCol = p.GetComponent<Collider>();
            Collider pickupCol = Pickup != null ? Pickup.GetComponent<Collider>() : null;
            if (partCol != null && pickupCol != null) Physics.IgnoreCollision(partCol, pickupCol);
        }

        ApplyState();
    }

    // ---- state queries ----
    public int GetState() => state;
    public bool IsLive() => state == StateLive;

    // ---- events from UCS_GrenadePickup ----
    public void OnPickedUp()
    {
        heldLocally = true;
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        if (ThrowBoostAtReference > 0f && Pickup != null)
        {
            // ponytail: assumes hand speed is linear in eye height (VRChat scales tracking with avatar size)
            float eye = Mathf.Max(0.1f, Networking.LocalPlayer.GetAvatarEyeHeightAsMeters());
            Pickup.ThrowVelocityBoostScale = Mathf.Clamp(ThrowBoostAtReference * ReferenceEyeHeight / eye, MinThrowBoost, MaxThrowBoost);
        }
        if (HandFlipRoot != null && Pickup != null)
        {
            // stays as-is after the drop so the grenade doesn't snap round mid-throw
            heldLeft = Pickup.currentHand == VRC_Pickup.PickupHand.Left;
            Commit();
        }
        StartLoop();
    }

    public void OnDropped()
    {
        heldLocally = false;
        if (!Networking.IsOwner(gameObject)) return;
        if (state == StateArmed) Release();
        else if (state == StateLive) ArmImpact(); // a live grenade picked up and thrown again
    }

    // Desktop (and VR grenades without pin physbones): first click pulls every pin, second click throws.
    // VR players pull pins with the physbone and throw by letting go.
    public void OnUseDown()
    {
        if (!Networking.IsOwner(gameObject)) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null || (local.IsUserInVR() && PinPhysBones.Length > 0)) return;

        if (state == StateSafe)
        {
            for (int i = 0; i < Pins.Length; i++)
            {
                if ((pinsPulledMask & (1 << i)) == 0) PullPin(i);
            }
        }
        else if (state == StateArmed && heldLocally && Pickup != null)
        {
            // dropping fires OnDropped -> Release; the throw velocity goes on next frame, after VRChat's own release
            Pickup.Drop();
            SendCustomEventDelayedFrames(nameof(_ApplyClickThrow), 1);
        }
    }

    public void _ApplyClickThrow()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (pickupBody == null || local == null || pickupBody.isKinematic) return;
        Quaternion head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
        Vector3 look = head * Vector3.forward;
        // 0 = overhand (level or up), 1 = underhand toss at the floor, blended between the two pitches
        float down = -Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg;
        float under = Mathf.Clamp01(Mathf.InverseLerp(ClickUnderhandPitchStart, ClickUnderhandPitchFull, down));
        Vector3 dir = (look + Vector3.up * ClickThrowLift * (1f - under)).normalized;
        // an underhand toss leaves the hand low and shallow even when looking at the floor, so it rolls on
        float minUp = -Mathf.Sin(Mathf.Lerp(90f, ClickUnderhandMaxDownDegrees, under) * Mathf.Deg2Rad);
        if (dir.y < minUp)
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 0.0001f) flat = head * Vector3.up; // looking straight down: toss toward the top of the view
            flat.y = 0f;
            dir = flat.normalized * Mathf.Sqrt(1f - minUp * minUp) + Vector3.up * minUp;
        }
        float speed = Mathf.Lerp(ClickThrowSpeed, ClickUnderhandSpeed, under);

        pickupBody.velocity = dir * speed + local.GetVelocity();
        // overarm end-over-end tumble blends into a forward roll for the underhand toss
        pickupBody.angularVelocity = head * new Vector3(Mathf.Lerp(-6f, 7f, under), 0f, 0f);
    }

    public void OnImpact()
    {
        if (!DetonateOnImpact || state != StateLive || heldLocally || !Networking.IsOwner(gameObject)) return;
        if (Networking.GetServerTimeInMilliseconds() - impactArmAtServerMs < 0) return;
        Detonate();
    }

    // ---- owner actions ----
    private void PullPin(int index)
    {
        pinsPulledMask |= 1 << index;
        if (index < PinPhysBones.Length && PinPhysBones[index] != null) PinPhysBones[index].ReleaseGrabs();
        if ((pinsPulledMask & allPinsMask) == allPinsMask) state = StateArmed;
        Commit();
    }

    private void Release()
    {
        int now = Networking.GetServerTimeInMilliseconds();
        state = StateLive;
        throwerId = Networking.LocalPlayer.playerId;
        detonateAtServerMs = now + Mathf.RoundToInt(FuseTime * 1000f);
        ArmImpact();
        Commit();
        StartLoop();
    }

    // owner-local: only the owner checks impacts, so this isn't synced
    private void ArmImpact()
    {
        impactArmAtServerMs = Networking.GetServerTimeInMilliseconds() + Mathf.RoundToInt(ImpactArmDelay * 1000f);
        if (Pickup != null) lastSweepPos = Pickup.transform.position;
    }

    // ponytail: first hit only, so a non-hitbox trigger on these layers in the path hides a hitbox behind it that frame
    private bool SweepHitsHitbox(Vector3 from, Vector3 to)
    {
        RaycastHit hit;
        if (!Physics.Linecast(from, to, out hit, ImpactHitboxLayers, QueryTriggerInteraction.Collide)) return false;
        return hit.collider.GetComponent<UCS_Hitbox>() != null;
    }

    private void Detonate()
    {
        Vector3 pos = DetonationPoint != null ? DetonationPoint.position : transform.position;
        state = StateSpent;
        Commit();
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetDetonate), pos, throwerId);
        if (RespawnDelay >= 0f) SendCustomEventDelayedSeconds(nameof(_Respawn), RespawnDelay);
    }

    [NetworkCallable]
    public void NetDetonate(Vector3 position, int blamedPlayerID)
    {
        if (DetonationPool != null) DetonationPool.SpawnAt(position, Quaternion.identity, blamedPlayerID, -1);
    }

    public void _Respawn()
    {
        if (!Networking.IsOwner(gameObject) || state != StateSpent) return;
        // a stow point would otherwise drag it straight back into the pocket
        if (stowManager != null) stowManager.ForceUnstowEverywhere();
        // reset first so the loose pin/lever get reparented under the root the pool is about to hide
        ResetGrenade();
        if (ReturnPool != null)
        {
            int slot = ReturnPool.GetSlotIndexOf(gameObject);
            if (slot >= 0)
            {
                ReturnPool._ReturnObject(ReturnPool.GetClaimantAt(slot), slot);
                return;
            }
        }
        if (pickupSync != null)
        {
            if (!Networking.IsOwner(pickupSync.gameObject)) Networking.SetOwner(Networking.LocalPlayer, pickupSync.gameObject);
            pickupSync.Respawn();
        }
    }

    // Puts the grenade back to Safe with pins and lever reattached. Owner only; pools / spawners call this.
    public void ResetGrenade()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        state = StateSafe;
        pinsPulledMask = 0;
        throwerId = -1;
        heldLeft = false;
        Commit();
    }

    private void Commit()
    {
        RequestSerialization();
        ApplyState();
    }

    // ---- owner frame loop (replaces physbone pollers) ----
    private void StartLoop()
    {
        if (loopRunning) return;
        loopRunning = true;
        SendCustomEventDelayedFrames(nameof(_Tick), 1);
    }

    public void _Tick()
    {
        bool keepGoing = false;
        if (Networking.IsOwner(gameObject))
        {
            if (state == StateSafe && heldLocally)
            {
                keepGoing = true;
                for (int i = 0; i < PinPhysBones.Length && i < Pins.Length; i++)
                {
                    if ((pinsPulledMask & (1 << i)) != 0) continue;
                    VRCPhysBone pb = PinPhysBones[i];
                    if (pb != null && pb.Stretch >= PinPullStretch) { PullPin(i); break; }
                }
            }
            else if (state == StateLive)
            {
                keepGoing = true;
                int now = Networking.GetServerTimeInMilliseconds();
                if (DetonateOnImpact && !heldLocally && Pickup != null)
                {
                    Vector3 pos = Pickup.transform.position;
                    bool hitPlayer = now - impactArmAtServerMs >= 0 && SweepHitsHitbox(lastSweepPos, pos);
                    lastSweepPos = pos;
                    if (hitPlayer)
                    {
                        Detonate();
                        keepGoing = false;
                    }
                }
                if (keepGoing && FuseTime > 0f && now - detonateAtServerMs >= 0)
                {
                    Detonate();
                    keepGoing = false;
                }
            }
        }

        if (keepGoing) SendCustomEventDelayedFrames(nameof(_Tick), 1);
        else loopRunning = false;
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // thrower left or someone grabbed a live grenade: the new owner keeps the (server-timed) fuse running
        if (player != null && player.isLocal && state == StateLive)
        {
            if (Pickup != null) lastSweepPos = Pickup.transform.position;
            StartLoop();
        }
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    // ---- visuals, run on every client from synced state ----
    private void ApplyState()
    {
        if (parts == null) return;
        if (HandFlipRoot != null && GripPoint != null)
        {
            HandFlipRoot.localPosition = heldLeft ? flipRootFlippedPos : flipRootPos;
            HandFlipRoot.localRotation = heldLeft ? flipRootFlippedRot : flipRootRot;
        }
        bool spent = state == StateSpent;
        if (spent && Pickup != null && Pickup.IsHeld) Pickup.Drop();
        if (stowManager != null)
        {
            // the stow manager also hides stowed items from other players; one owner of the pickup's visibility
            if (stowManager.IsItemSpawned() == spent) stowManager.SetItemSpawned(!spent);
        }
        else if (Pickup != null && Pickup.gameObject.activeSelf == spent)
        {
            Pickup.gameObject.SetActive(!spent);
        }

        for (int i = 0; i < Pins.Length; i++)
        {
            bool pulled = (pinsPulledMask & (1 << i)) != 0;
            SetPartDetached(i, pulled, Vector3.zero, Vector3.zero);
            if (i < PinPhysBones.Length && PinPhysBones[i] != null) PinPhysBones[i].enabled = !pulled;
        }

        int leverIndex = Pins.Length;
        Vector3 leverVel = Vector3.zero;
        Vector3 leverSpin = Vector3.zero;
        if (Lever != null && Pickup != null)
        {
            // lever mesh centre, not its pivot: the pivot sits at the hinge on top of the fuze
            Renderer leverRenderer = Lever.GetComponent<Renderer>();
            Vector3 outward = (leverRenderer != null ? leverRenderer.bounds.center : Lever.position) - Pickup.transform.position;
            leverVel = (outward.sqrMagnitude > 0f ? outward.normalized : Vector3.up) * LeverEjectSpeed;
            leverSpin = Pickup.transform.right * LeverEjectSpin;
        }
        SetPartDetached(leverIndex, state >= StateLive, leverVel, leverSpin);
    }

    private void SetPartDetached(int i, bool detached, Vector3 extraVelocity, Vector3 spin)
    {
        Transform p = parts[i];
        if (p == null || partDetached[i] == detached) return;
        partDetached[i] = detached;
        Rigidbody rb = p.GetComponent<Rigidbody>();
        Collider col = p.GetComponent<Collider>();

        if (detached)
        {
            Vector3 inherit = pickupBody != null && !pickupBody.isKinematic ? pickupBody.velocity : Vector3.zero;
            p.SetParent(null, true);
            if (col != null) col.enabled = true;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.velocity = inherit + extraVelocity;
                rb.angularVelocity = spin;
            }
            partDetachTime[i] = Time.time;
            PlayMechanicsSound(i < Pins.Length ? PinPullSound : LeverReleaseSound);
            SendCustomEventDelayedSeconds(nameof(_HideLooseParts), LoosePartLifetime);
        }
        else
        {
            if (col != null) col.enabled = false;
            p.SetParent(partParents[i], false);
            p.localPosition = partLocalPos[i];
            p.localRotation = partLocalRot[i];
            p.gameObject.SetActive(true);
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.position = p.position;
                rb.rotation = p.rotation;
            }
        }
    }

    public void _HideLooseParts()
    {
        float cutoff = Time.time - LoosePartLifetime + 0.05f;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] != null && partDetached[i] && partDetachTime[i] <= cutoff) parts[i].gameObject.SetActive(false);
        }
    }

    private void PlayMechanicsSound(AudioClip clip)
    {
        if (MechanicsAudio != null && clip != null) MechanicsAudio.PlayOneShot(clip);
    }
}
