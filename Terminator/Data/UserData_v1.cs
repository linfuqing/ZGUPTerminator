using System;
using System.Collections;
using System.Collections.Generic;
using ZG;

public struct UserStageReward
{
    [Flags]
    public enum Flag
    {
        Unlock = 0x01, 
        Collected = 0x02
    }

    public enum Condition
    {
        Normal, 
        Once, 
        NoDamage, 
        KillCount
    }
    
    public string name;
    public uint id;
    public Flag flag;
    public Condition condition;
    public int conditionValue;
    public UserRewardData[] values;
}

public struct UserGroup
{
    public string name;

    public uint id;
}

public struct UserItem
{
    public string name;

    public uint id;

    public int count;
}

public struct UserPurchasePool
{
    public string name;

    public uint id;

    //花费的钻石数量
    public int diamond;
    
    //抽一次获得多少金币
    public int gold;
}

public struct UserCardStyle
{
    [Serializable]
    public struct Level
    {
        public string name;

        /// <summary>
        /// 升级需要的卡片数量
        /// </summary>
        public int count;
        /// <summary>
        /// 升级需要的金币数量
        /// </summary>
        public int gold;

        /// <summary>
        /// 技能组伤害
        /// </summary>
        public float skillGroupDamage;
    }

    public string name;

    public uint id;

    public Level[] levels;
}

public struct UserCard
{
    public struct Group
    {
        /// <summary>
        /// 被装备到的套装ID
        /// </summary>
        public uint groupID;

        /// <summary>
        /// 装备位置，-1代表没装备
        /// </summary>
        public int position;
    }

    public string name;

    public uint id;

    /// <summary>
    /// 属于什么品质<see cref="UserCardStyle"/>：普通、稀有、史诗、传说
    /// </summary>
    public uint styleID;

    /// <summary>
    /// 等级
    /// </summary>
    public int level;

    /// <summary>
    /// 卡片数量
    /// </summary>
    public int count;

    public float skillGroupDamage;

    /// <summary>
    /// 技能
    /// </summary>
    public string[] skillNames;
    
    /// <summary>
    /// 装备卡组
    /// </summary>
    public Group[] groups;
}

public struct UserRole
{
    public string name;
    
    public uint id;

    public float skillGroupDamage;
    
    /// <summary>
    /// 角色总属性
    /// </summary>
    public UserAttributeData[] attributes;

    /// <summary>
    /// 技能
    /// </summary>
    public string[] skillNames;

    /// <summary>
    /// 被装备到的套装ID
    /// </summary>
    public uint[] groupIDs;
}

public struct UserAccessory
{
    public enum Opcode
    {
        Add, 
        Mul
    }

    [Serializable]
    public struct Skill : IComparable<Skill>
    {
        public string name;

        public UserSkillType type;

        public Opcode opcode;
        
        public float damage;

        public int CompareTo(Skill other)
        {
            return ((int)opcode).CompareTo((int)other.opcode);
        }
    }

    [Serializable]
    public struct Attribute : IComparable<Attribute>
    {
        public UserAttributeType type;
        public Opcode opcode;
        public float value;
        
        public int CompareTo(Attribute other)
        {
            return ((int)opcode).CompareTo((int)other.opcode);
        }
    }

    [Serializable]
    public struct Property
    {
        public Skill[] skills;
        public Attribute[] attributes;
    }

    [Serializable]
    public struct Stage
    {
        public string name;

        /// <summary>
        /// 升阶需要的相同装备数量
        /// </summary>
        public int count;

        public Property property;
    }

    public struct Group
    {
        public uint groupID;
        
        public uint slotID;
    }

    public string name;
    
    public uint id;

    /// <summary>
    /// 属于什么类型<see cref="UserAccessoryStyle"/>：头、手、脚、背包、超能武器
    /// </summary>
    public uint styleID;

    /// <summary>
    /// 阶
    /// </summary>
    public int stage;

    public float attributeValue;

    /// <summary>
    /// 技能
    /// </summary>
    public string[] skillNames;

    public Property property;
    
    public Stage stageDesc;

    /// <summary>
    /// 被装备到的套装
    /// </summary>
    public Group[] groups;
}

public struct UserAccessorySlot
{
    public string name;

    public uint id;

    public uint styleID;

    /// <summary>
    /// 当前等级
    /// </summary>
    public int level;
}

public struct UserAccessoryStyle
{
    public struct Level
    {
        public string name;

        /// <summary>
        /// 升级需要的卷轴
        /// </summary>
        public string itemName;

        /// <summary>
        /// 升级需要的卷轴数量
        /// </summary>
        public int itemCount;
        
        public float attributeValue;
    }

    public string name;

    public uint id;

    public UserAttributeType attributeType;

    public Level[] levels;
}

public struct UserStage
{
    [Serializable]
    public struct RewardPoolOption
    {
        public float chance;
        
        public UserRewardData value;

        public RewardPoolOption(string value)
        {
            var parameters = value.Split(':');
            this.value.name = parameters[0];
            this.value.type = (UserRewardType)int.Parse(parameters[1]);
            this.value.count = 2 < parameters.Length ? int.Parse(parameters[2]) : 1;
            chance = 3 < parameters.Length ? float.Parse(parameters[3]) : 1.0f;
        }
    }
    
    [Serializable]
    public struct RewardPool
    {
        public string name;
        
        public RewardPoolOption[] options;
        
#if UNITY_EDITOR
        [CSVField]
        public string 奖池名字
        {
            set => name = value;
        }

        [CSVField]
        public string 奖池选项
        {
            set
            {
                var parameters = value.Split('/');
                
                int numParameters = parameters.Length;
                options = new RewardPoolOption[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    options[i] = new RewardPoolOption(parameters[i]);
            }
        }
#endif
    }
    
    public string name;
    public uint id;
    public int energy;
    public UserRewardData[] rewards;
    public UserStageReward.Flag[] rewardFlags;
    //public RewardPool[] rewardPools;
}

public partial struct UserLevel
{
    public UserStage[] stages;
}

public partial interface IUserData
{
    public struct Purchases
    {
        [Flags]
        public enum Flag
        {
            Unlock = 0x01, 
            UnlockFirst = 0x02 | Unlock, 
        }

        public struct PoolKey
        {
            public uint poolID;
            public int count;
        }

        /// <summary>
        /// 用来判定首次解锁并播放动画
        /// </summary>
        public Flag flag;
        /// <summary>
        /// 钻石数量
        /// </summary>
        public int diamond;
        /// <summary>
        /// 卡池
        /// </summary>
        public UserPurchasePool[] pools;
        /// <summary>
        /// 钥匙
        /// </summary>
        public PoolKey[] poolKeys;
    }

    public struct Cards
    {
        [Flags]
        public enum Flag
        {
            Unlock = 0x01, 
            UnlockFirst = 0x02 | Unlock, 
            
            CardFirst = 0x04, 
            CardUpgrade = 0x08
        }

        /// <summary>
        /// 用来判定首次解锁完整卡槽并播放动画
        /// </summary>
        public Flag flag;

        /// <summary>
        /// 卡牌容量
        /// </summary>
        public int capacity;

        public uint selectedGroupID;

        /// <summary>
        /// 卡组
        /// </summary>
        public UserGroup[] groups;
        
        /// <summary>
        /// 卡牌
        /// </summary>
        public UserCard[] cards;

        /// <summary>
        /// 卡牌品质
        /// </summary>
        public UserCardStyle[] cardStyles;
    }

    public struct Roles
    {
        [Flags]
        public enum Flag
        {
            Unlock = 0x01, 

            UnlockFirst = 0x02 | Unlock, 
            
            //RoleUnlock = 0x04, 
            
            //RoleUnlockFirst = 0x08
        }

        public Flag flag;

        public uint selectedGroupID;

        /// <summary>
        /// 套装
        /// </summary>
        public UserGroup[] groups;
        
        /// <summary>
        /// 卷轴
        /// </summary>
        public UserItem[] items;

        /// <summary>
        /// 角色
        /// </summary>
        public UserRole[] roles;

        /// <summary>
        /// 装备
        /// </summary>
        public UserAccessory[] accessories;

        /// <summary>
        /// 装备槽
        /// </summary>
        public UserAccessorySlot[] accessorySlots;
        
        /// <summary>
        /// 装备类型
        /// </summary>
        public UserAccessoryStyle[] accessoryStyles;
    }

    public struct StageCache
    {
        public int rage;
        public int exp;
        public int expMax;
        public string[] skills;

        public static readonly StageCache Empty = new StageCache(string.Empty);

        public StageCache(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                rage = 0;
                exp = 0;
                expMax = 0;

                skills = Array.Empty<string>();

                return;
            }
            
            skills = value.Split(UserData.SEPARATOR);
            
            int length = skills.Length;
            rage = int.Parse(skills[--length]);
            exp = int.Parse(skills[--length]);
            expMax = int.Parse(skills[--length]);

            Array.Resize(ref skills, length);
        }

        public override string ToString()
        {
            string result = $"{expMax}{UserData.SEPARATOR}{exp}{UserData.SEPARATOR}{rage}";
            if(skills != null && skills.Length > 0)
                result = $"{string.Join(UserData.SEPARATOR, skills)}{UserData.SEPARATOR}{result}";
            
            return result;
        }
    }

    public struct Stage
    {
        public int energy;
        public int levelEnergy;
        public StageCache cache;
        public UserStageReward[] rewards;
    }

    public struct StageProperty
    {
        public Property value;

        public StageCache cache;
    }

    /// <summary>
    /// 商店
    /// </summary>
    IEnumerator QueryPurchases(
        uint userID,
        Action<Purchases> onComplete);

    /// <summary>
    /// 抽卡
    /// </summary>
    IEnumerator Purchase(
        uint userID,
        uint purchasePoolID, 
        int times, 
        Action<Memory<UserItem>> onComplete);

    /// <summary>
    /// 卡牌
    /// </summary>
    IEnumerator QueryCards(
        uint userID,
        Action<Cards> onComplete);

    /// <summary>
    /// 卡牌
    /// </summary>
    IEnumerator QueryCard(
        uint userID,
        uint cardID, 
        Action<UserCard> onComplete);

    /// <summary>
    /// 设置卡组
    /// </summary>
    IEnumerator SetCardGroup(uint userID, uint groupID, Action<bool> onComplete);

    /// <summary>
    /// 装备卡组或卸下卡组(position为-1）
    /// </summary>
    IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete);

    /// <summary>
    /// 升级卡牌
    /// </summary>
    IEnumerator UpgradeCard(uint userID, uint cardID, Action<bool> onComplete);

    /// <summary>
    /// 角色
    /// </summary>
    IEnumerator QueryRoles(
        uint userID,
        Action<Roles> onComplete);
    
    IEnumerator QueryRole(
        uint userID,
        uint roleID, 
        Action<UserRole> onComplete);

    /// <summary>
    /// 设置套装
    /// </summary>
    IEnumerator SetRoleGroup(uint userID, uint groupID, Action<bool> onComplete);
    
    /// <summary>
    /// 装备角色
    /// </summary>
    IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete);

    /// <summary>
    /// 角色养成
    /// </summary>
    IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID, 
        Action<Memory<UserTalent>> onComplete);

    /// <summary>
    /// 角色养成升级
    /// </summary>
    IEnumerator UpgradeRoleTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete);

    IEnumerator QueryAccessory(
        uint userID,
        uint accessoryID, 
        Action<UserAccessory> onComplete);

    /// <summary>
    /// 查询装备所有品阶
    /// </summary>
    IEnumerator QueryAccessoryStages(
        uint userID,
        uint accessoryID, 
        Action<Memory<UserAccessory.Stage>> onComplete);

    /// <summary>
    /// 装备或卸下装备
    /// </summary>
    IEnumerator SetAccessory(uint userID, uint accessoryID, uint groupID, uint slotID, Action<bool> onComplete);

    /// <summary>
    /// 升级装备，返回下一级描述
    /// </summary>
    IEnumerator UpgradeAccessory(uint userID, uint accessorySlotID, Action<bool> onComplete);

    /// <summary>
    /// 升阶装备
    /// </summary>
    IEnumerator UprankAccessory(
        uint userID, 
        uint destinationAccessoryID, 
        uint[] sourceAccessoryIDs, 
        Action<UserAccessory.Stage?> onComplete);
    
    /// <summary>
    /// 查询关卡
    /// </summary>
    IEnumerator QueryStage(
        uint userID,
        uint stageID,
        Action<Stage> onComplete);

    /// <summary>
    /// 继续游戏
    /// </summary>
    IEnumerator ApplyStage(
        uint userID,
        uint stageID,
        Action<StageProperty> onComplete);

    IEnumerator SubmitStage(
        uint userID,
        StageFlag flag,
        int stage,
        int killCount, 
        int gold,
        int rage, 
        int exp,
        int expMax,
        string[] skills,
        Action<int> onComplete);
    
    /// <summary>
    /// 收集关卡奖励
    /// </summary>
    IEnumerator CollectStageReward(uint userID, uint stageRewardID, Action<Memory<UserReward>> onComplete);

    /// <summary>
    /// 一键收集全部关卡奖励
    /// </summary>
    IEnumerator CollectStageRewards(uint userID, Action<Memory<UserReward>> onComplete);

    IEnumerator ApplyReward(uint userID, string poolName, Action<Memory<UserReward>> onComplete);
}

public partial class UserData
{
    public static readonly List<UserRewardData> Rewards = new List<UserRewardData>();

    public static void ApplyReward(
        string poolName, 
        UserStage.RewardPool[] rewardPools)
    {
        bool isSelected;
        float chance, total;
        foreach (var rewardPool in rewardPools)
        {
            if (rewardPool.name == poolName)
            {
                isSelected = false;
                chance = UnityEngine.Random.value;
                total = 0.0f;
                foreach (var option in rewardPool.options)
                {
                    total += option.chance;
                    if (total > 1.0f)
                    {
                        total -= 1.0f;
                        
                        chance = UnityEngine.Random.value;

                        isSelected = false;
                    }
                    
                    if(isSelected || total < chance)
                        continue;

                    isSelected = true;

                    Rewards.Add(option.value);
                }
                
                break;
            }
        }
    }
}