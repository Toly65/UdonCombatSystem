using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

// Interact button that hands the local player one item from a BulkPreOrderPool and pops it out at SpawnPoint.
// Same reserve -> fulfill -> place flow as GunSpawnManager, for one item. Pool state is synced, so it polls
// that rather than listening for result events.
// When the pool is empty it recycles an item nobody is holding or has stowed (grenades that are live or spent are
// left alone, they return themselves after detonating).
// Lives on the prefab root: set the pool and Size here. Rotate the SpawnPoint child to orient the item (guns lie
// across the tray, their forward along the root's X).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_PoolDispenser : UdonSharpBehaviour
{
    [SerializeField] private BulkPreOrderPool pool;
    [Tooltip("Body width, height, depth in metres. Grenade 0.16,0.3,0.16  Pistol 0.32,0.32,0.2  Rifle 1.0,0.42,0.28")]
    [SerializeField] private Vector3 size = new Vector3(0.16f, 0.3f, 0.16f);
    [Tooltip("Where items appear. Its forward is the pop-out direction.")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float popSpeed = 0.6f;

    [Header("Button")]
    [Tooltip("Optional: nudged along its local -Z while a dispense is in progress")]
    [SerializeField] private Transform buttonVisual;
    [SerializeField] private float buttonTravel = 0.006f;
    [SerializeField] private AudioSource dispenseAudio;

    [Header("Polling")]
    [SerializeField] private float pollSeconds = 0.1f;
    [SerializeField] private int maxPolls = 30;

    private bool busy;
    private int polls;
    private int myId;
    private GameObject[] activeBefore;
    private GameObject placedItem;
    private Vector3 buttonRestPos;

    // Authored local pose of every synced child grip, baked in the editor (see GunSpawnManager): players move the
    // grips, not the gun root, so a reused gun would otherwise come back in pieces.
    [SerializeField, HideInInspector] private Transform[] poseKeys = new Transform[0];
    [SerializeField, HideInInspector] private Vector3[] posePos = new Vector3[0];
    [SerializeField, HideInInspector] private Quaternion[] poseRot = new Quaternion[0];

    // Authored kinematic/gravity of every synced rigidbody, baked in the editor. Pooled items start inactive, so a
    // requester who isn't the pool owner first sees an item while someone else owns it; VRCObjectSync holds remote
    // rigidbodies kinematic and the item can stay frozen after ownership arrives. The new owner reapplies these.
    [SerializeField, HideInInspector] private VRCObjectSync[] physKeys = new VRCObjectSync[0];
    [SerializeField, HideInInspector] private bool[] physKinematic = new bool[0];
    [SerializeField, HideInInspector] private bool[] physGravity = new bool[0];

    private void Start()
    {
        if (buttonVisual != null) buttonRestPos = buttonVisual.localPosition;
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    // ponytail: rebaked on any edit to this component, script reload or scene load; touch it after adding items to the pool.
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        Layout();
        BakeGripPoses();
    }

    // Fits the primitive body to Size. Numbers are the original grenade layout with the fixed trims kept.
    private void Layout()
    {
        float w = size.x, h = size.y, d = size.z;
        float zc = -0.025f; // tray centre: midway between the column front and the base front, for any depth
        float headY = h - 0.035f;
        Place("Base", new Vector3(0, 0.01f, 0), new Vector3(w, 0.02f, d));
        Place("Column", new Vector3(0, h / 2f, d / 2f - 0.025f), new Vector3(w, h, 0.05f));
        Place("Head", new Vector3(0, headY, -0.005f), new Vector3(w, 0.07f, d + 0.01f));
        Place("Nozzle", new Vector3(0, h - 0.078f, -0.03f), null);
        Place("SpawnPoint", new Vector3(0, h / 2f, -0.03f), null);
        Place("DispenseAudio", new Vector3(0, h / 2f, -0.03f), null);
        Place("Drip Tray", new Vector3(0, 0.0215f, zc), new Vector3(w - 0.032f, 0.003f, d - 0.07f));
        Place("Tray Lip Left", new Vector3(-(w - 0.032f) / 2f, 0.03f, zc), new Vector3(0.004f, 0.018f, d - 0.061f));
        Place("Tray Lip Right", new Vector3((w - 0.032f) / 2f, 0.03f, zc), new Vector3(0.004f, 0.018f, d - 0.061f));
        Place("Tray Lip Front", new Vector3(0, 0.03f, zc - (d - 0.07f) / 2f - 0.0025f), new Vector3(w - 0.032f, 0.018f, 0.004f));
        Place("Button", new Vector3(w / 2f - 0.035f, headY, -d / 2f - 0.013f), null);
        RectTransform label = transform.Find("Label") as RectTransform;
        if (label != null)
        {
            label.anchoredPosition = new Vector2(-0.025f, headY);
            label.localPosition = new Vector3(label.localPosition.x, label.localPosition.y, -d / 2f - 0.0105f);
        }
    }

    private void Place(string child, Vector3 pos, Vector3? scale)
    {
        Transform t = transform.Find(child);
        if (t == null) return;
        t.localPosition = pos;
        if (scale.HasValue) t.localScale = scale.Value;
    }

    private void BakeGripPoses()
    {
        var keys = new System.Collections.Generic.List<Transform>();
        var pos = new System.Collections.Generic.List<Vector3>();
        var rot = new System.Collections.Generic.List<Quaternion>();
        var phys = new System.Collections.Generic.List<VRCObjectSync>();
        var kin = new System.Collections.Generic.List<bool>();
        var grav = new System.Collections.Generic.List<bool>();
        if (pool != null)
            for (int o = 0; o < pool.GetObjectCount(); o++)
            {
                GameObject item = pool.GetObjectAt(o);
                if (item == null) continue;
                foreach (VRCObjectSync sync in item.GetComponentsInChildren<VRCObjectSync>(true))
                {
                    Rigidbody rb = sync.GetComponent<Rigidbody>();
                    if (rb != null) { phys.Add(sync); kin.Add(rb.isKinematic); grav.Add(rb.useGravity); }
                    if (sync.transform == item.transform) continue; // single-pickup items: the root itself is placed
                    keys.Add(sync.transform);
                    pos.Add(sync.transform.localPosition);
                    rot.Add(sync.transform.localRotation);
                }
            }
        poseKeys = keys.ToArray();
        posePos = pos.ToArray();
        poseRot = rot.ToArray();
        physKeys = phys.ToArray();
        physKinematic = kin.ToArray();
        physGravity = grav.ToArray();
    }
#endif

    // Called by UCS_DispenserButton on the Button child, which is the interact target.
    public void _Dispense()
    {
        if (busy || pool == null) return;
        myId = Networking.LocalPlayer.playerId;
        GameObject recycled = null;
        if (pool.GetFreeCount() == 0)
        {
            recycled = RecycleOne();
            if (recycled == null) return;
        }

        busy = true;
        SetButtonPressed(true);
        activeBefore = pool.GetActiveObjectsForPlayer(myId);
        // a recycled item of ours can still read as active until the pool owner's update lands; it may come straight back
        for (int k = 0; k < activeBefore.Length; k++) if (activeBefore[k] == recycled) activeBefore[k] = null;
        // reservations top up to the amount of not-yet-fulfilled claims, so ask for one more than we're holding
        pool._RequestItems(myId, pool.GetReservedCountForPlayer(myId) + 1);
        polls = 0;
        SendCustomEventDelayedSeconds(nameof(_PollReserved), pollSeconds);
    }

    public void _PollReserved()
    {
        polls++;
        if (pool.GetReservedCountForPlayer(myId) == 0 && polls <= maxPolls)
        {
            SendCustomEventDelayedSeconds(nameof(_PollReserved), pollSeconds);
            return;
        }
        if (polls > maxPolls) { Finish(); return; }
        pool._FulfillAllPreOrders();
        polls = 0;
        SendCustomEventDelayedSeconds(nameof(_PollActive), pollSeconds);
    }

    public void _PollActive()
    {
        polls++;
        GameObject item = FindNewItem();
        if (item == null && polls <= maxPolls)
        {
            SendCustomEventDelayedSeconds(nameof(_PollActive), pollSeconds);
            return;
        }
        if (item != null) Place(item);
        Finish();
    }

    private GameObject FindNewItem()
    {
        GameObject[] now = pool.GetActiveObjectsForPlayer(myId);
        for (int i = 0; i < now.Length; i++)
        {
            bool seen = false;
            for (int k = 0; k < activeBefore.Length; k++) if (activeBefore[k] == now[i]) { seen = true; break; }
            if (!seen) return now[i];
        }
        return null;
    }

    private void Place(GameObject item)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Networking.IsOwner(item)) Networking.SetOwner(local, item);

        // an item stowed when it was last used is still locked to that player's stow point
        StowableManager stow = item.GetComponent<StowableManager>();
        if (stow != null) stow.ForceUnstowEverywhere();
        UCS_Grenade grenade = item.GetComponent<UCS_Grenade>();
        if (grenade != null) grenade.ResetGrenade();

        item.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        // Parents come before children here, so each grip lands relative to an already-placed parent.
        VRCObjectSync[] syncs = item.GetComponentsInChildren<VRCObjectSync>(true);
        for (int s = 0; s < syncs.Length; s++)
        {
            GameObject sg = syncs[s].gameObject;
            if (!Networking.IsOwner(sg)) Networking.SetOwner(local, sg);
            RestoreLocalPose(sg.transform);
            Rigidbody rb = sg.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // an interpolated rigidbody overwrites a transform-only teleport on the next physics step
                rb.position = sg.transform.position;
                rb.rotation = sg.transform.rotation;
                rb.velocity = spawnPoint.forward * popSpeed;
                rb.angularVelocity = Vector3.zero;
            }
            syncs[s].FlagDiscontinuity();
        }
        RestorePhysics(item);
        // ownership can settle after this frame; apply once more in case object sync froze it again
        placedItem = item;
        SendCustomEventDelayedSeconds(nameof(_RestorePlacedPhysics), 0.5f);
        if (dispenseAudio != null) dispenseAudio.Play();
    }

    public void _RestorePlacedPhysics()
    {
        if (placedItem == null || !placedItem.activeInHierarchy || IsHeld(placedItem)) return;
        RestorePhysics(placedItem);
    }

    private void RestorePhysics(GameObject item)
    {
        VRCObjectSync[] syncs = item.GetComponentsInChildren<VRCObjectSync>(true);
        for (int s = 0; s < syncs.Length; s++)
        {
            if (!Networking.IsOwner(syncs[s].gameObject)) continue;
            for (int i = 0; i < physKeys.Length; i++)
            {
                if (physKeys[i] != syncs[s]) continue;
                syncs[s].SetKinematic(physKinematic[i]);
                syncs[s].SetGravity(physGravity[i]);
                break;
            }
        }
    }

    private void RestoreLocalPose(Transform grip)
    {
        for (int i = 0; i < poseKeys.Length; i++)
        {
            if (poseKeys[i] != grip) continue;
            grip.localPosition = posePos[i];
            grip.localRotation = poseRot[i];
            return;
        }
    }

    // Frees one fulfilled slot nobody is holding. Live or spent grenades are skipped: they return themselves.
    private GameObject RecycleOne()
    {
        int count = pool.GetObjectCount();
        for (int i = 0; i < count; i++)
        {
            if (pool.IsSlotFree(i)) continue;
            GameObject obj = pool.GetObjectAt(i);
            if (obj == null || !obj.activeSelf) continue;
            UCS_Grenade grenade = obj.GetComponent<UCS_Grenade>();
            if (grenade != null && grenade.GetState() >= UCS_Grenade.StateLive) continue;
            if (IsHeld(obj)) continue;
            StowableManager stow = obj.GetComponent<StowableManager>();
            if (stow != null && stow.IsStowedSynced) continue; // in someone's pocket

            pool._ReturnObject(pool.GetClaimantAt(i), i);
            return obj;
        }
        return null;
    }

    private bool IsHeld(GameObject obj)
    {
        VRC_Pickup[] pickups = obj.GetComponentsInChildren<VRC_Pickup>(true);
        for (int i = 0; i < pickups.Length; i++) if (pickups[i].IsHeld) return true;
        return false;
    }

    private void Finish()
    {
        busy = false;
        SetButtonPressed(false);
    }

    private void SetButtonPressed(bool pressed)
    {
        if (buttonVisual != null) buttonVisual.localPosition = buttonRestPos + (pressed ? Vector3.back * buttonTravel : Vector3.zero);
    }
}
