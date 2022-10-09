
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
[AddComponentMenu("")]
public class BuffManager : UdonSharpBehaviour
{
    public UdonBehaviour HealthManager;
    public bool isBleeding;
    public bool isPoisoned;
    public float BleedDamage = 5f;
    public float PoisonDamage = 5f;
    public int BleedTime = 5;
    public int PoisonTime = 10;
    
    private float currentTime_Bleed;
    private float currentTime_Poison;
    private float wantedTime_Bleed;
    private float wantedTime_Poison;
    private float waitTime = 1f;
    private bool bleedIsFirstActive = true;
    private bool poisonIsFirstActive = true;
    private int currentBleedTime = 0;
    private int currentPoisonTime = 0;
    private int defaultBleedTime;
    private int defaultPoisonTime;
    private float defaultBleedDamage;
    private float defaultPoisonDamage;

    private void Start()
    {
        defaultBleedTime = BleedTime;
        defaultPoisonTime = PoisonTime;
        defaultBleedDamage = BleedDamage;
        defaultPoisonDamage = PoisonDamage;
    }
    
    private void Update()
    {
        
        if (isBleeding)
        {
            if (bleedIsFirstActive)
            {
                currentTime_Bleed = Time.time;
                wantedTime_Bleed = Time.time + waitTime;
                bleedIsFirstActive = false;
            }
            
            currentTime_Bleed += Time.deltaTime;
            
            if (currentTime_Bleed >= wantedTime_Bleed)
            {
                HealthManager.SetProgramVariable("Modifier", BleedDamage*-1);
                HealthManager.SendCustomEvent("ModifyHealth");
                bleedIsFirstActive = true;
                currentBleedTime++;
            }
        }

        if (isPoisoned)
        {
            if (poisonIsFirstActive)
            {
                currentTime_Poison = Time.time;
                poisonIsFirstActive = false;
                wantedTime_Poison = Time.time + waitTime;
            }
            else
            {
                currentTime_Poison += Time.deltaTime;
            }

            if (currentTime_Poison > wantedTime_Poison)
            {
                HealthManager.SetProgramVariable("Modifier", PoisonDamage*-1);
                poisonIsFirstActive = true;
                currentPoisonTime++;
            }
        }

        if (currentBleedTime > BleedTime)
        {
            ResetBleeding();
        }
        if (currentPoisonTime > PoisonTime)
        {
            ResetPoison();
        }
    }

    public void ResetBleeding()
    {
        isBleeding = false;
        currentBleedTime = 0;
        BleedTime = defaultBleedTime;
        BleedDamage = defaultBleedDamage;
    }

    public void ResetPoison()
    {
        isPoisoned = false;
        currentPoisonTime = 0;
        PoisonTime = defaultPoisonTime;
        PoisonDamage = defaultPoisonDamage;
    }
}
