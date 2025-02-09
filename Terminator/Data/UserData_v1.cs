using System;
using System.Collections;

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
}

public struct UserPurchases
{
    [Flags]
    public enum Flag
    {
        FirstUnlock = 0x01
    }

    public struct PoolKey
    {
        public uint poolID;
        public int count;
    }

    /// <summary>
    /// �����ж��״ν��������Ŷ���
    /// </summary>
    public Flag flag;
    /// <summary>
    /// ��ʯ����
    /// </summary>
    public int diamond;
    /// <summary>
    /// ����
    /// </summary>
    public UserPurchasePool[] pools;
    /// <summary>
    /// Կ��
    /// </summary>
    public PoolKey[] poolKeys;
}

[Serializable]
public struct UserCardStyle
{
    [Serializable]
    public struct Level
    {
        public string name;

        /// <summary>
        /// ������Ҫ�Ŀ�Ƭ����
        /// </summary>
        public int count;
        /// <summary>
        /// ������Ҫ�Ľ������
        /// </summary>
        public int gold;
    }

    public string name;

    public uint id;

    public Level[] levels;
}

public struct UserCard
{
    public struct Group
    {
        public uint groupID;

        /// <summary>
        /// װ��λ�ã�-1����ûװ��
        /// </summary>
        public int position;
    }

    public string name;

    public uint id;

    /// <summary>
    /// ����ʲôƷ��<see cref="UserCardStyle"/>����ͨ��ϡ�С�ʷʫ����˵
    /// </summary>
    public uint styleID;

    /// <summary>
    /// �ȼ�
    /// </summary>
    public int level;

    /// <summary>
    /// ��Ƭ����
    /// </summary>
    public int count;

    /// <summary>
    /// װ������
    /// </summary>
    public Group[] groups;
}

public struct UserCards
{
    [Flags]
    public enum Flag
    {
        FirstUnlock = 0x01
    }

    /// <summary>
    /// �����ж��״ν����������۲����Ŷ���
    /// </summary>
    public Flag flag;

    /// <summary>
    /// ��������
    /// </summary>
    public int capacity;

    /// <summary>
    /// ����Ʒ��
    /// </summary>
    public UserCardStyle[] cardStyles;

    /// <summary>
    /// ����
    /// </summary>
    public UserCard[] cards;

    /// <summary>
    /// ����
    /// </summary>
    public UserGroup[] groups;
}

[Serializable]
public struct UserRole
{
    public string name;

    public uint id;

    public int hp;
    public int attack;
    public int defence;

    /// <summary>
    /// ��װ��������װID
    /// </summary>
    public uint[] groupIDs;
}

[Serializable]
public struct UserAccessoryStyle
{
    [Serializable]
    public struct Level
    {
        public string name;

        /// <summary>
        /// ������Ҫ�ľ���ID
        /// </summary>
        public uint itemID;

        /// <summary>
        /// ������Ҫ�ľ�������
        /// </summary>
        public int count;
    }

    [Serializable]
    public struct Stage
    {
        public string name;

        /// <summary>
        /// ������Ҫ����ͬװ������
        /// </summary>
        public int count;
    }

    public string name;

    public uint id;

    /// <summary>
    /// ��ǰ�ȼ�
    /// </summary>
    public int level;

    /// <summary>
    /// ��ǰ�ȼ�����
    /// </summary>
    public Level levelDesc;

    /// <summary>
    /// ��
    /// </summary>
    public Stage[] stages;
}

public struct UserAccessory
{
    public string name;

    public uint id;

    /// <summary>
    /// ����ʲô����<see cref="UserAccessoryStyle"/>��ͷ���֡��š���������������
    /// </summary>
    public uint styleID;

    /// <summary>
    /// ��
    /// </summary>
    public int stage;

    /// <summary>
    /// ��װ��������װID
    /// </summary>
    public int[] groupIDs;
}

public struct UserRoles
{
    [Flags]
    public enum Flag
    {
        FirstUnlock = 0x01
    }

    public Flag flag;

    /// <summary>
    /// ��ɫ
    /// </summary>
    public UserRole[] roles;

    /// <summary>
    /// װ������
    /// </summary>
    public UserAccessoryStyle[] accessoryStyles;

    /// <summary>
    /// װ��
    /// </summary>
    public UserAccessory[] accessories;

    /// <summary>
    /// ����
    /// </summary>
    public UserItem[] items;

    /// <summary>
    /// ��װ
    /// </summary>
    public UserGroup[] groups;
}

public partial interface IUserData
{
    /// <summary>
    /// �̵�
    /// </summary>
    IEnumerator QueryPurchases(
        uint userID,
        Action<UserPurchases> onComplete);

    /// <summary>
    /// �鿨
    /// </summary>
    IEnumerator Purchase(
        uint userID,
        uint purchasePoolID, 
        int times, 
        Action<Memory<UserItem>> onComplete);

    /// <summary>
    /// ����
    /// </summary>
    IEnumerator QueryCards(
        uint userID,
        Action<UserCards> onComplete);

    /// <summary>
    /// װ�������ж�¿���(positionΪ-1��
    /// </summary>
    IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete);

    /// <summary>
    /// ��������
    /// </summary>
    IEnumerator UpgradeCard(uint userID, Action<bool> onComplete);

    /// <summary>
    /// ��ɫ
    /// </summary>
    IEnumerator QueryRoles(
        uint userID,
        Action<UserRoles> onComplete);

    /// <summary>
    /// װ����ɫ
    /// </summary>
    IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete);

    /// <summary>
    /// ��ɫ����
    /// </summary>
    IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID, 
        Action<Memory<UserTalent>> onComplete);

    /// <summary>
    /// ��ɫ��������
    /// </summary>
    IEnumerator UpgradeRoleTalent(
        uint userID,
        uint roleID,
        uint talentID,
        Action<bool> onComplete);

    /// <summary>
    /// װ����ж��װ��
    /// </summary>
    IEnumerator SetAccessory(uint userID, uint accessoryID, int groupID, Action<bool> onComplete);

    /// <summary>
    /// ����װ����������һ������
    /// </summary>
    IEnumerator UpgradeAccessory(uint userID, uint accessoryStyleID, Action<UserAccessoryStyle.Level?> onComplete);

    /// <summary>
    /// ����װ��
    /// </summary>
    IEnumerator UprankAccessory(uint userID, uint accessoryID, Action<bool> onComplete);
}

public partial class UserData
{
    public IEnumerator QueryPurchases(
        uint userID,
        Action<UserPurchases> onComplete)
    {
        return null;
    }

    public IEnumerator Purchase(
        uint userID,
        uint purchasePoolID,
        int times,
        Action<Memory<UserItem>> onComplete)
    {
        return null;
    }

    public IEnumerator QueryCards(
        uint userID,
        Action<UserCards> onComplete)
    {
        return null;
    }

    public IEnumerator SetCard(uint userID, uint cardID, uint groupID, int position, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator UpgradeCard(uint userID, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator QueryRoles(
        uint userID,
        Action<UserRoles> onComplete)
    {
        return null;
    }

    public IEnumerator SetRole(uint userID, uint roleID, uint groupID, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator QueryRoleTalents(
        uint userID,
        uint roleID,
        Action<Memory<UserTalent>> onComplete)
    {
        return null;
    }

    public IEnumerator UpgradeRoleTalent(
        uint userID,
        uint roleID,
        uint talentID,
        Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator SetAccessory(uint userID, uint accessoryID, int groupID, Action<bool> onComplete)
    {
        return null;
    }

    public IEnumerator UpgradeAccessory(uint userID, uint accessoryStyleID, Action<UserAccessoryStyle.Level?> onComplete)
    {
        return null;
    }

    public IEnumerator UprankAccessory(uint userID, uint accessoryID, Action<bool> onComplete)
    {
        return null;
    }
}