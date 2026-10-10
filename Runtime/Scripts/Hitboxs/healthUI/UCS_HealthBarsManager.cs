
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_HealthBarsManager : UdonSharpBehaviour
{
	[SerializeField] private GameObject desktopHealthSystem;
	[SerializeField] private GameObject vrHealthSystem;
	[SerializeField] private Image desktopHealthFill;
	[SerializeField] private Image vrHealthFill;

	[Tooltip("The desktop health bar inside the head-tracked root. Stays hidden for VR users even while the root is borrowed for heads-up messages, since they have the wrist bar.")]
	[SerializeField] private GameObject desktopHealthBar;

	[Header("Health Colors")]
	[SerializeField] private Color highHealthColor = new Color(0.2f, 0.85f, 0.3f, 1f);
	[SerializeField] private Color mediumHealthColor = new Color(0.95f, 0.8f, 0.2f, 1f);
	[SerializeField] private Color lowHealthColor = new Color(0.9f, 0.2f, 0.2f, 1f);

	[Header("Desktop Ammo Display")]
	[Tooltip("Bottom-right panel on the desktop HUD canvas; shown while the local desktop player holds a gun")]
	[SerializeField] private GameObject ammoPanel;
	[SerializeField] private TMPro.TextMeshProUGUI ammoCountText;
	[SerializeField] private TMPro.TextMeshProUGUI ammoCapacityText;
	[SerializeField] private TMPro.TextMeshProUGUI reloadKeyText;
	[SerializeField] private Color ammoColor = Color.white;
	[SerializeField] private Color promptActiveColor = new Color(0.2f, 0.85f, 0.55f, 1f); // VRChat HUD's active-key green
	[SerializeField] private float ammoPollSeconds = 0.1f;

	private VRCPlayerApi localPlayer;
	private int shownAmmo = int.MinValue;
	private int shownCapacity = int.MinValue;
	private bool shownInfinite;
	private bool isVR;
	private float lastHealthPercent = -1f;
	private float pushedHealth = 100f;
	private float pushedMaxHealth = 100f;
	private int headHudRequests;

	private void Start()
	{
		localPlayer = Networking.LocalPlayer;

		if (Utilities.IsValid(localPlayer))
		{
			isVR = localPlayer.IsUserInVR();
		}

		RefreshHealthBar(true);

		if (ammoPanel != null)
		{
			ammoPanel.SetActive(false);
			// desktop only: VR players read ammo off the gun / mag itself
			if (!isVR) SendCustomEventDelayedSeconds(nameof(_PollAmmo), ammoPollSeconds);
		}
	}

	// ponytail: polls the local player's hands at 10 Hz instead of every gun pushing to the HUD, so guns need no HUD wiring
	public void _PollAmmo()
	{
		UCS_BaseGun gun = GetHeldGun(VRC_Pickup.PickupHand.Right);
		if (gun == null) gun = GetHeldGun(VRC_Pickup.PickupHand.Left);

		if (gun == null)
		{
			if (ammoPanel.activeSelf) ammoPanel.SetActive(false);
		}
		else
		{
			if (!ammoPanel.activeSelf)
			{
				ammoPanel.SetActive(true);
				shownAmmo = int.MinValue; // force a redraw for the newly held gun
			}
			UpdateAmmoText(gun.GetAmmoCount(), gun.GetAmmoCapacity(), gun.HasInfiniteAmmo());
		}

		SendCustomEventDelayedSeconds(nameof(_PollAmmo), ammoPollSeconds);
	}

	private UCS_BaseGun GetHeldGun(VRC_Pickup.PickupHand hand)
	{
		if (!Utilities.IsValid(localPlayer)) return null;
		VRC_Pickup pickup = localPlayer.GetPickupInHand(hand);
		if (pickup == null) return null;
		// the pickup is usually the gun's "Primary Grip" child
		UCS_BaseGun gun = pickup.GetComponent<UCS_BaseGun>();
		if (gun == null && pickup.transform.parent != null) gun = pickup.transform.parent.GetComponent<UCS_BaseGun>();
		return gun;
	}

	private void UpdateAmmoText(int ammo, int capacity, bool infinite)
	{
		if (ammo == shownAmmo && capacity == shownCapacity && infinite == shownInfinite) return;
		shownAmmo = ammo;
		shownCapacity = capacity;
		shownInfinite = infinite;

		bool empty = !infinite && ammo <= 0;
		bool low = !infinite && capacity > 0 && ammo <= capacity / 4;
		if (ammoCountText != null)
		{
			ammoCountText.text = infinite ? "INF" : ammo.ToString();
			ammoCountText.color = low ? lowHealthColor : ammoColor;
		}
		if (ammoCapacityText != null)
		{
			ammoCapacityText.text = infinite || capacity <= 0 ? "" : "/ " + capacity.ToString();
		}
		if (reloadKeyText != null)
		{
			// light up the reload key like VRChat's active HUD keys when the gun is dry
			reloadKeyText.color = empty ? promptActiveColor : Color.white;
		}
	}

	private void RefreshHealthBar(bool force)
	{
		float maxHealth = Mathf.Max(1f, pushedMaxHealth);
		float healthPercent = Mathf.Clamp01(pushedHealth / maxHealth);

		if (!force && Mathf.Approximately(healthPercent, lastHealthPercent))
		{
			return;
		}

		lastHealthPercent = healthPercent;

		if (desktopHealthFill != null)
		{
			desktopHealthFill.fillAmount = healthPercent;
			desktopHealthFill.color = GetHealthColor(healthPercent);
		}

		if (vrHealthFill != null)
		{
			vrHealthFill.fillAmount = healthPercent;
			vrHealthFill.color = GetHealthColor(healthPercent);
		}

		UpdateBarVisibility();
	}

	public void PushHealthUpdate(float currentHealth, float maxHealth)
	{
		pushedHealth = currentHealth;
		pushedMaxHealth = Mathf.Max(1f, maxHealth);
		RefreshHealthBar(true);
	}

	/// <summary>
	/// Borrow the head-tracked HUD for a heads-up message. VR users have it off by default (it
	/// costs a tracking update every frame), so this turns it on until the matching
	/// <see cref="_ReleaseHeadHud"/>. Counted, so overlapping messages don't hide each other.
	/// </summary>
	public void _AcquireHeadHud()
	{
		headHudRequests++;
		UpdateBarVisibility();
	}

	public void _ReleaseHeadHud()
	{
		if (headHudRequests > 0) headHudRequests--;
		UpdateBarVisibility();
	}

	private void UpdateBarVisibility()
	{
		if (desktopHealthSystem != null)
		{
			desktopHealthSystem.SetActive(!isVR || headHudRequests > 0);
		}

		if (desktopHealthBar != null)
		{
			desktopHealthBar.SetActive(!isVR);
		}

		if (vrHealthSystem != null)
		{
			vrHealthSystem.SetActive(isVR);
		}
	}

	private Color GetHealthColor(float healthPercent)
	{
		if (healthPercent > 0.66f)
		{
			return highHealthColor;
		}

		if (healthPercent > 0.33f)
		{
			return mediumHealthColor;
		}

		return lowHealthColor;
	}
}
