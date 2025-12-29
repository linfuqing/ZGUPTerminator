using System;
using System.Collections;

public struct UserItem
{
    public string name;

    public uint id;

    public int count;
}

public struct UserAccessory
{
    public enum StageMaterialType
    {
        /// <summary>
        /// 需要同名装备
        /// </summary>
        Normal, 
        /// <summary>
        /// 需要同类型装备
        /// </summary>
        Style
    }

    [Serializable]
    public struct StageMaterial
    {
        public StageMaterialType type;

        public int stage;

        public StageMaterial(string text)
        {
            int index = text.IndexOf('*');
            if (index == -1)
            {
                type = StageMaterialType.Normal;
                
                stage = int.Parse(text);
            }
            else
            {
                type = (StageMaterialType)int.Parse(text.Substring(index + 1));
                
                stage = int.Parse(text.Remove(index));
            }
        }
    }

    [Serializable]
    public struct Stage
    {
        public string name;

        /// <summary>
        /// 升阶之后获得的属性
        /// </summary>
        public UserPropertyData property;

        /// <summary>
        /// 升阶需要的装备的品阶
        /// </summary>
        public StageMaterial[] materials;
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

    public float roleSkillGroupDamage;

    public float skillDamage;

    public float attributeValue;

    public UserPropertyData property;
    
    public Stage stageDesc;

    /// <summary>
    /// 技能
    /// </summary>
    public string[] skillNames;

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
        public float skillDamage;
        public float roleSkillGroupDamage;
    }

    public string name;

    public uint id;

    public UserAttributeType attributeType;

    public Level[] levels;
}

public partial interface IUserData
{
    public struct AccessoryUprankInput
    {
        public uint destinationAccessoryID;
        public uint[] sourceAccessoryIDs;
    }

    public struct AccessoryStages
    {
        public UserAccessory.Stage[] stages;
    }
    
    IEnumerator QueryAccessory(
        uint userID,
        uint accessoryID, 
        Action<UserAccessory> onComplete);

    /// <summary>
    /// 查询装备所有品阶
    /// </summary>
    IEnumerator QueryAccessoryStages(
        uint userID,
        uint[] accessoryIDs, 
        Action<Memory<AccessoryStages>> onComplete);

    /// <summary>
    /// 装备或卸下装备
    /// </summary>
    IEnumerator SetAccessory(uint userID, uint accessoryID, uint groupID, uint slotID, Action<bool> onComplete);

    /// <summary>
    /// 升级装备，返回下一级描述
    /// </summary>
    IEnumerator UpgradeAccessory(
        uint userID, 
        uint accessorySlotID, 
        int maxTimes, 
        Action<int?> onComplete);

    /// <summary>
    /// 升阶装备
    /// </summary>
    IEnumerator UprankAccessory(
        uint userID, 
        AccessoryUprankInput[] inputs, 
        Action<Memory<UserAccessory.Stage>> onComplete);
}
