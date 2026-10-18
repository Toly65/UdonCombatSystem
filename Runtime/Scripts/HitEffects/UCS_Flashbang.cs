using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// Pooled flashbang detonation. Spawn it through UCS_HitEffectsPool.SpawnAt like any other hit effect.
// Each client works out how exposed its own player is (distance, line of sight to the head, facing) and
// whites out its view with a UCS/FlashOverlay renderer, then fades back, with an optional ringing sound.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_Flashbang : UCS_HitEffectBase
{
    [Header("Exposure")]
    [SerializeField] public float Radius = 15f;
    [Tooltip("Layers that block the flash (walls, cover)")]
    [SerializeField] public LayerMask OcclusionLayers = 1;
    [Tooltip("Exposure multiplier when looking directly away from the flash (1 when looking straight at it)")]
    [Range(0f, 1f)]
    [SerializeField] public float LookingAwayExposure = 0.3f;

    [Header("Blind")]
    [Tooltip("Seconds of full whiteout at full exposure, scaled down by exposure")]
    [SerializeField] public float BlindSeconds = 3f;
    [SerializeField] public float FadeSeconds = 2.5f;
    [Tooltip("Renderer using UCS/FlashOverlay (huge Cube, no collider)")]
    [SerializeField] public Renderer Overlay;

    [Header("Audio")]
    [Tooltip("3D bang everyone hears")]
    [SerializeField] public AudioSource BangAudio;
    [SerializeField] public AudioClip BangClip;
    [Tooltip("2D ringing for the exposed local player, volume follows the whiteout")]
    [SerializeField] public AudioSource RingAudio;

    private float peak = 0f;
    private float fadeStart = 0f;
    private bool fading = false;

    public override void OnSpawned(int blamedPlayerID, int attackerTeamID)
    {
        if (BangAudio != null && BangClip != null) BangAudio.PlayOneShot(BangClip);

        float exposure = GetLocalExposure(transform.position);
        if (exposure <= 0.01f) return;
        // a second flash can only make it worse
        if (fading && exposure <= GetFaded()) return;

        peak = exposure;
        fadeStart = Time.time + BlindSeconds * exposure;
        SetBlind(exposure);
        if (RingAudio != null)
        {
            RingAudio.volume = exposure;
            if (!RingAudio.isPlaying) RingAudio.Play();
        }
        if (!fading)
        {
            fading = true;
            SendCustomEventDelayedFrames(nameof(_Fade), 1);
        }
    }

    private float GetLocalExposure(Vector3 center)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return 0f;
        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 toFlash = center - head.position;
        float d = toFlash.magnitude;
        if (d >= Radius) return 0f;
        if (Physics.Linecast(center, head.position, OcclusionLayers, QueryTriggerInteraction.Ignore)) return 0f;

        float facing = d > 0.01f ? (Vector3.Dot(head.rotation * Vector3.forward, toFlash / d) + 1f) * 0.5f : 1f;
        return (1f - d / Radius) * Mathf.Lerp(LookingAwayExposure, 1f, facing);
    }

    private float GetFaded()
    {
        float t = (Time.time - fadeStart) / Mathf.Max(0.01f, FadeSeconds);
        return peak * (1f - Mathf.Clamp01(t));
    }

    public void _Fade()
    {
        float v = GetFaded();
        SetBlind(v);
        if (RingAudio != null) RingAudio.volume = v;
        if (v > 0f && gameObject.activeInHierarchy)
        {
            SendCustomEventDelayedFrames(nameof(_Fade), 1);
            return;
        }
        fading = false;
        SetBlind(0f);
        if (RingAudio != null) RingAudio.Stop();
    }

    private void OnDisable()
    {
        // returned to the pool mid-fade: never leave a player blind
        fading = false;
        SetBlind(0f);
    }

    private void SetBlind(float intensity)
    {
        if (Overlay == null) return;
        // material (not sharedMaterial) so editor play mode never writes into the asset
        Overlay.material.SetFloat("_Intensity", intensity);
        Overlay.enabled = intensity > 0.001f;
    }
}
