using System;
using System.Collections;

[Serializable]
public struct UserPropertyData
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

    public Skill[] skills;
    public Attribute[] attributes;
}

public struct UserRole
{
    [Flags]
    public enum Flag
    {
        Unlocked = 0x01
    }
    
    public struct Rank
    {
        public string name;

        /// <summary>
        /// 解锁或进阶需要的碎片数量
        /// </summary>
        public int count;

        /// <summary>
        /// 升阶之后获得的属性
        /// </summary>
        public UserPropertyData property;
    }
    
    public string name;
    
    public uint id;
    
    public Flag flag;

    /// <summary>
    /// 碎片数量
    /// </summary>
    public int count;

    /// <summary>
    /// 星级
    /// </summary>
    public int rank;

    public float skillGroupDamage;
    
    public Rank rankDesc;

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

public partial interface IUserData
{
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

        public int exp;

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
    /// 装备角色
    /// </summary>
    IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete);

    /// <summary>
    /// 设置套装
    /// </summary>
    IEnumerator SetRoleGroup(uint userID, uint groupID, Action<bool> onComplete);
    
    /// <summary>
    /// 角色升星
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="roleID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator UprankRole(uint userID, uint roleID, Action<UserRole.Rank?> onComplete);
    
    /// <summary>
    /// 角色养成升级
    /// </summary>
    IEnumerator UpgradeRoleTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete);
    
    /// <summary>
    /// 角色养成
    /// </summary>
    IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID, 
        Action<Memory<UserTalent>> onComplete);
}
