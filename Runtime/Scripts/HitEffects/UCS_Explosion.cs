using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// Pooled explosion. Put it on a prefab in a UCS_HitEffectsPool and call pool.SpawnAt(...) from anything
// (gun hit effect slot, projectile, grenade). Runs on every client: visuals, audio, rigidbody force and local
// player knockback happen everywhere; hitbox damage is only sent from the blamed player's client, same as bullets.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_Explosion : UCS_HitEffectBase
{
    [Header("Damage")]
    [SerializeField] public float Radius = 6f;
    [SerializeField] public float MaxDamage = 100f;
    [Tooltip("Damage at the edge of Radius, falls off linearly from MaxDamage at the centre")]
    [SerializeField] public float MinDamage = 10f;
    [Tooltip("Layers holding hitboxes and rigidbodies the blast affects")]
    [SerializeField] public LayerMask HitLayers = ~0;
    [Tooltip("Layers that block the blast (walls, cover). Targets behind these are untouched.")]
    [SerializeField] public LayerMask OcclusionLayers = 1;

    [Header("Force")]
    [Tooltip("Speed (m/s) given to rigidbodies at the centre, falling off to 0 at Radius. Mass-independent, so a crate and a grenade get thrown the same.")]
    [SerializeField] public float RigidbodyPushSpeed = 8f;
    [SerializeField] public float UpwardsModifier = 1f;
    [Tooltip("Speed (m/s) added to the local player at the centre, falling off to 0 at Radius. 0 disables.")]
    [SerializeField] public float PlayerLaunchSpeed = 6f;

    [Header("Audio")]
    [SerializeField] public AudioSource Audio;
    [SerializeField] public AudioClip CloseClip;
    [SerializeField] public AudioClip MidClip;
    [SerializeField] public AudioClip FarClip;
    [SerializeField] public float MidDistance = 50f;
    [SerializeField] public float FarDistance = 250f;

    public override void OnSpawned(int blamedPlayerID, int attackerTeamID)
    {
        Vector3 center = transform.position;
        VRCPlayerApi local = Networking.LocalPlayer;
        bool isBlamed = local != null && local.playerId == blamedPlayerID;

        PlaySound(center, local);
        LaunchLocalPlayer(center, local);

        Collider[] cols = Physics.OverlapSphere(center, Radius, HitLayers, QueryTriggerInteraction.Collide);
        int n = cols.Length;
        Rigidbody[] pushed = new Rigidbody[n];
        int pushedCount = 0;
        // one damage event per target: players keyed by playerId, other hitboxes by component
        UCS_Hitbox[] targets = new UCS_Hitbox[n];
        int[] targetKeys = new int[n];
        float[] targetDists = new float[n];
        int targetCount = 0;

        for (int i = 0; i < n; i++)
        {
            Collider col = cols[i];
            if (col == null) continue;
            Rigidbody rb = col.attachedRigidbody;
            UCS_Hitbox hb = isBlamed ? col.GetComponent<UCS_Hitbox>() : null;
            if (rb == null && hb == null) continue;

            // ClosestPoint is only valid on primitives / convex meshes, which hitboxes and dynamic rigidbodies are
            Vector3 point = col.ClosestPoint(center);
            if (IsOccluded(center, point, col)) continue;

            if (rb != null && !rb.isKinematic && !Contains(pushed, pushedCount, rb))
            {
                pushed[pushedCount++] = rb;
                if (isBlamed && !Networking.IsOwner(rb.gameObject)) Networking.SetOwner(local, rb.gameObject);
                // ponytail: VelocityChange ignores mass, so a vehicle gets shoved like a crate; switch to Impulse if heavy props show up
                rb.AddExplosionForce(RigidbodyPushSpeed, center, Radius, UpwardsModifier, ForceMode.VelocityChange);
            }

            if (hb == null) continue;
            int key = -1;
            UCS_PlayerHitbox ph = col.GetComponent<UCS_PlayerHitbox>();
            if (ph != null)
            {
                VRCPlayerApi tracked = ph.GetTrackedPlayer();
                if (Utilities.IsValid(tracked)) key = tracked.playerId;
            }
            float dist = Vector3.Distance(center, point);

            int slot = -1;
            for (int t = 0; t < targetCount; t++)
            {
                if (key >= 0 ? targetKeys[t] == key : targets[t] == hb) { slot = t; break; }
            }
            if (slot < 0)
            {
                slot = targetCount++;
                targetDists[slot] = float.MaxValue;
            }
            // ponytail: closest collider wins, so its DamageMultiplier applies (a blast at head height hits the head box)
            if (dist < targetDists[slot])
            {
                targets[slot] = hb;
                targetKeys[slot] = key;
                targetDists[slot] = dist;
            }
        }

        for (int t = 0; t < targetCount; t++)
        {
            float damage = Mathf.Lerp(MaxDamage, MinDamage, targetDists[t] / Radius);
            targets[t].HitEvent(Mathf.RoundToInt(damage), blamedPlayerID, attackerTeamID, (int)DamageType.explosion);
        }
    }

    private bool IsOccluded(Vector3 from, Vector3 to, Collider target)
    {
        RaycastHit hit;
        if (!Physics.Linecast(from, to, out hit, OcclusionLayers, QueryTriggerInteraction.Ignore)) return false;
        return hit.collider != target;
    }

    private bool Contains(Rigidbody[] arr, int count, Rigidbody rb)
    {
        for (int i = 0; i < count; i++) if (arr[i] == rb) return true;
        return false;
    }

    private void PlaySound(Vector3 center, VRCPlayerApi local)
    {
        if (Audio == null) return;
        float d = local != null ? Vector3.Distance(local.GetPosition(), center) : 0f;
        AudioClip clip = CloseClip;
        if (d >= FarDistance && FarClip != null) clip = FarClip;
        else if (d >= MidDistance && MidClip != null) clip = MidClip;
        if (clip != null) Audio.PlayOneShot(clip);
    }

    private void LaunchLocalPlayer(Vector3 center, VRCPlayerApi local)
    {
        if (PlayerLaunchSpeed <= 0f || local == null) return;
        Vector3 offset = local.GetPosition() - center;
        float d = offset.magnitude;
        if (d >= Radius) return;
        if (Physics.Linecast(center, local.GetPosition() + Vector3.up, OcclusionLayers, QueryTriggerInteraction.Ignore)) return;
        // up bias so a blast at the feet still lifts a grounded player
        Vector3 dir = ((d > 0.01f ? offset / d : Vector3.zero) + Vector3.up).normalized;
        local.SetVelocity(local.GetVelocity() + dir * PlayerLaunchSpeed * (1f - d / Radius));
    }
}
