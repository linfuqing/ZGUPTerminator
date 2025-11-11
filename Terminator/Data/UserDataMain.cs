using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public sealed partial class UserDataMain : MonoBehaviour
{
    [Flags]
    private enum Flag
    {
        PurchasesUnlockFirst = 0x0001, 
        PurchasesUnlock = 0x0002 | PurchasesUnlockFirst, 
        
        TalentsUnlockFirst = 0x0004,
        TalentsUnlock = 0x0008 | TalentsUnlockFirst, 

        CardsCreated = 0x0010, 
        CardsUnlockFirst = 0x0020, 
        CardsUnlock = 0x0040 | CardsUnlockFirst, 
        
        CardUnlockFirst = 0x0080, 
        CardUnlock = 0x0100 | CardUnlockFirst, 

        CardUpgradeFirst = 0x0200, 
        CardUpgrade = 0x0400 | CardUpgradeFirst, 
        
        CardReplaceFirst = 0x0800,
        CardReplace = 0x1000 | CardReplaceFirst, 

        RolesCreated = 0x2000, 
        RolesUnlockFirst = 0x4000, 
        RolesUnlock = 0x8000 | RolesUnlockFirst, 
        
        //RoleUnlockFirst = 0x1000, 
        //RoleUnlock = 0x2000 | RoleUnlockFirst, 
        
        UnlockFirst = PurchasesUnlockFirst | TalentsUnlockFirst | CardsUnlockFirst | CardUnlockFirst | CardUpgradeFirst | CardReplaceFirst | RolesUnlockFirst// | RoleUnlockFirst
    }
    
    private const string NAME_SPACE_USER_FLAG = "UserFlag";

    private static Flag flag
    {
        get => (Flag)PlayerPrefs.GetInt(NAME_SPACE_USER_FLAG);
        
        set => PlayerPrefs.SetInt(NAME_SPACE_USER_FLAG, (int)value);
    }
    
    public static UserDataMain instance
    {
        get;

        private set;
    }
   
    [Serializable]
    internal struct Energy
    {
        public int max;
        public float uintTime;
    }

    private const string NAME_SPACE_USER_GOLD = "UserGold";

    public static int gold
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_GOLD);

        set
        {
            int result = value - gold;
            if (result > 0)
                __AppendQuest(UserQuest.Type.GoldsToGet, result);
            else if(result < 0)
                __AppendQuest(UserQuest.Type.GoldsToUse, -result);

            PlayerPrefs.SetInt(NAME_SPACE_USER_GOLD, value);
        }
    }

    private const string NAME_SPACE_USER_EXP = "UserExp";

    public static int exp
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_EXP);

        set
        {
            PlayerPrefs.SetInt(NAME_SPACE_USER_EXP, value);
        }
    }
    
    public static int goldBank => __GetQuest(UserQuest.Type.GoldsToGet, ActiveType.Day, out _);

    private const string NAME_SPACE_USER_ENERGY = "UserEnergy";

    [Header("Main")]
    [SerializeField]
    internal Energy _energy;

    public int energy
    {
        get => PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY, _energy.max);
        
        set => PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY, value);
    }

    private const string NAME_SPACE_USER_ENERGY_TIME = "UserEnergyTime";
    private const string NAME_SPACE_USER_ENERGY_MAX = "UserEnergyMax";

    public UserEnergy userEnergy
    {
        get
        {
            int time = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY_TIME);
            if (time == 0)
            {
                time = (int)DateTimeUtility.GetSeconds();
                PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY_TIME, time);
            }

            UserEnergy userEnergy;
            userEnergy.value = energy;
            userEnergy.max = _energy.max + PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY_MAX);
            userEnergy.unitTime = (uint)Mathf.RoundToInt(_energy.uintTime * 1000);
            userEnergy.tick = DateTimeUtility.GetTicks((uint)time);

            return userEnergy;
        }
    }

    public IEnumerator QueryUser(
        string channelName, 
        string channelUser,
        Action<User, UserEnergy> onComplete)
    {
        yield return __CreateEnumerator();
        
        User user;
        user.id = UserData.id;
        user.gold = gold;
        //user.level = UserData.level;

        onComplete(user, userEnergy);
    }

    private bool __ApplyEnergy(int value, out int energy)
    {
        uint now = DateTimeUtility.GetSeconds(), time = now;
        energy = this.energy;
        if (_energy.uintTime > Mathf.Epsilon)
        {
            float energyFloat = (time - (uint)PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY_TIME, (int)time)) /
                                _energy.uintTime;
            int energyInt =  Mathf.FloorToInt(energyFloat);
            if (energy < _energy.max)
            {
                energy += energyInt;
                if (energy < _energy.max)
                    time -= (uint)Mathf.RoundToInt((energyFloat - energyInt) * _energy.uintTime);
                else
                    energy = _energy.max;
            }
        }

        if (energy < value)
            return false;
        
        energy -= value;
        
        if(value < 0)
            __AppendQuest(UserQuest.Type.EnergiesToBuy, 1);
        else if(value > 0)
            __AppendQuest(UserQuest.Type.EnergiesToUse, value);

        this.energy = energy;
        PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY_TIME, (int)(energy < _energy.max ? time : now));

        return true;
    }

    private bool __ApplyEnergy(int value)
    {
        return __ApplyEnergy(value, out _);
    }

    private static uint __ToID(int index) => (uint)(index + 1);
    
    private static int __ToIndex(uint id) => (int)(id - 1);
    
#if UNITY_EDITOR
    [SerializeField]
    internal bool _isDebugLevel = true;
#endif
    
    void Awake()
    {
        if (IUserData.instance == null)
        {
            gameObject.AddComponent<UserData>();

#if UNITY_EDITOR
            if(_isDebugLevel)
#endif
            UserData.chapter = int.MaxValue - 1;
        }

        if (IPurchaseData.instance == null)
            gameObject.AddComponent<PurchaseData>();

        if (IAdvertisementData.instance == null)
            gameObject.AddComponent<AdvertisementData>();

        instance = this;
    }
}

public partial class UserData
{
    [SerializeField]
    internal string _defaultSceneName = "S1";
    
    public IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete)
    {
        return UserDataMain.instance.QueryUser(channelName, channelUser, onComplete);
    }
}