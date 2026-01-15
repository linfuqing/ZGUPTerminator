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
        
        TicketsUnlockFirst = 0x10000,
        TicketsUnlock = 0x20000 | TicketsUnlockFirst,
        //RoleUnlockFirst = 0x1000, 
        //RoleUnlock = 0x2000 | RoleUnlockFirst, 
        
        UnlockFirst = PurchasesUnlockFirst | TalentsUnlockFirst | CardsUnlockFirst | CardUnlockFirst | CardUpgradeFirst | CardReplaceFirst | RolesUnlockFirst | TicketsUnlockFirst// | RoleUnlockFirst
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

    public int energy => __GetEnergy(out _, out _);

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
            userEnergy.value = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY, _energy.max);
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

    private int __GetEnergy(out uint now, out uint time)
    {
        now = DateTimeUtility.GetSeconds();
        time = now;
        int energy = PlayerPrefs.GetInt(NAME_SPACE_USER_ENERGY, _energy.max);
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

        return energy;
    }

    private bool __ApplyEnergy(int value, out int energy)
    {
        energy = __GetEnergy(out uint now, out uint time);
        
        if (energy < value)
            return false;
        
        energy -= value;
        
        if(value < 0)
            __AppendQuest(UserQuest.Type.EnergiesToBuy, 1);
        else if(value > 0)
            __AppendQuest(UserQuest.Type.EnergiesToUse, value);

        PlayerPrefs.SetInt(NAME_SPACE_USER_ENERGY, energy);
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
    //[SerializeField]
    //internal string _defaultSceneName = "S1";

    [Serializable]
    internal struct Chapter
    {
        public string name;

        public uint id;

        public int stages;
    }

    [SerializeField] 
    internal Chapter[] _chapters =
    {
        new Chapter()
        {
            name = "S01",
            id = 1, 
            stages = 2,
        }, 
        
        new Chapter()
        {
            name = "S02",
            id = 2, 
            stages = 2,
        }, 
        
        new Chapter()
        {
            name = "S03",
            id = 3, 
            stages = 1,
        }
    };
    
    public IEnumerator QueryUser(
        string channelName,
        string channelUser,
        Action<IUserData.Status, uint> onComplete)
    {
        yield return null;

        IUserData.Status status;
        status.levelID = 0;
        status.stage = -1;
        status.chapter = chapter;
        if (status.chapter < _chapters.Length)
        {
            var chapter = _chapters[status.chapter];
            var levelCache = UserData.levelCache;
            if (levelCache != null)
            {
                var temp = levelCache.Value;
                if (temp.id == chapter.id && temp.stage == chapter.stages)
                {
                    onComplete(status, id);

                    yield break;
                }
            }

            int i;
            for (i = 0; i < chapter.stages; ++i)
            {
                if (GetStageFlag(chapter.name, i) == 0)
                    break;
            }

            if (i < chapter.stages)
            {
                status.stage = i;
                status.levelID = chapter.id;
            }
        }
        
        onComplete(status, id);
    }
    
    public IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete)
    {
        return UserDataMain.instance.QueryUser(channelName, channelUser, onComplete);
    }
}