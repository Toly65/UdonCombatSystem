
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

	[Header("Health Colors")]
	[SerializeField] private Color highHealthColor = new Color(0.2f, 0.85f, 0.3f, 1f);
	[SerializeField] private Color mediumHealthColor = new Color(0.95f, 0.8f, 0.2f, 1f);
	[SerializeField] private Color lowHealthColor = new Color(0.9f, 0.2f, 0.2f, 1f);

	private VRCPlayerApi localPlayer;
	private bool isVR;
	private float lastHealthPercent = -1f;
	private float pushedHealth = 100f;
	private float pushedMaxHealth = 100f;

	private void Start()
	{
		localPlayer = Networking.LocalPlayer;

		if (Utilities.IsValid(localPlayer))
		{
			isVR = localPlayer.IsUserInVR();
		}

		RefreshHealthBar(true);
	}

	private void Update()
	{
		if (Utilities.IsValid(localPlayer))
		{
			bool currentIsVR = localPlayer.IsUserInVR();
			if (currentIsVR != isVR)
			{
				isVR = currentIsVR;
				UpdateBarVisibility();
			}
		}

		RefreshHealthBar(false);
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

	private void UpdateBarVisibility()
	{
		if (desktopHealthSystem != null)
		{
			desktopHealthSystem.SetActive(!isVR);
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
