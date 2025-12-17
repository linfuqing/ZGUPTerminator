using System;
using System.Collections;
using UnityEngine;

public enum UserRewardType
{
    PurchasePoolKey, 
    CardsCapacity, 
    Card, 
    Role, 
    Accessory, 
    Item, 
    Diamond, 
    Gold, 
    Energy, 
    EnergyMax, 
    ActiveDay, 
    ActiveWeek, 
    Ticket,
    Exp,
    RoleExp
}

public enum UserSkillType
{
    Individual, 
    Group
}

public enum UserAttributeType
{
    None, 
    Hp, 
    Attack, 
    Defence, 
    Recovery, 
}

[Serializable]
public struct UserAttributeData
{
    public UserAttributeType type;
    public float value;
}

[Serializable]
public struct UserRewardData
{
    public string name;
    
    public UserRewardType type;

    public int count;

    public static UserRewardData Parse(string text)
    {
        UserRewardData result;
        var parameters = text.Split(':');

        result.name = parameters[0];
        result.type = (UserRewardType)int.Parse(parameters[1]);
        result.count = int.Parse(parameters[2]);
        return result;
    }
    
    public UserRewardData(string text)
    {
        var parameters = text.Split('*');

        name = parameters[0];
        type = (UserRewardType)int.Parse(parameters[1]);
        count = int.Parse(parameters[2]);
    }
}

public struct UserReward
{
    public string name;

    public uint id;
    
    public UserRewardType type;

    public int count;
}

public struct User
{
    public uint id;
    public int gold;
    //public int level;
}

public struct UserEnergy
{
    public int value;
    public int max;
    public uint unitTime;
    public long tick;

    public int current =>
        Mathf.Min(value + (int)((DateTime.UtcNow.Ticks - tick) / (TimeSpan.TicksPerMillisecond * unitTime)));
}

public struct UserTalent
{
    [Flags]
    public enum Flag
    {
        Collected = 0x01
    }

    public string name;
    public uint id;
    public Flag flag;
    public int gold;
    public int exp;
    public float skillGroupDamage;
    public UserAttributeData attribute;
}

public partial interface IUserData : IGameUserData
{
    public enum Status
    {
        Normal, 
        Guide
    }
    
    public static IUserData instance;

    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<Status, uint> onComplete);
    
    IEnumerator QueryUser(
        string channelName, 
        string channelUser, 
        Action<User, UserEnergy> onComplete);
}

public partial class UserData : MonoBehaviour, IUserData
{
    private const string NAME_SPACE_USER_ID = "UserID";
    
    public const char SEPARATOR = ',';
    
    public static uint id
    {
        get
        {
            int id = PlayerPrefs.GetInt(NAME_SPACE_USER_ID);
            if (id < 1)
            {
                id = UnityEngine.Random.Range(1, int.MaxValue);
                
                PlayerPrefs.SetInt(NAME_SPACE_USER_ID, id);
            }

            return (uint)id;
        }
    }

    public IEnumerator QueryUser(
        string channelName,
        string channelUser,
        Action<IUserData.Status, uint> onComplete)
    {
        yield return null;

        onComplete(chapter > 0 ? IUserData.Status.Normal : IUserData.Status.Guide, id);
    }
    
    public IEnumerator Activate(
        string code,
        string channel,
        string channelUser,
        Action<IGameUserData.UserStatus> onComplete)
    {
        yield return null;

        onComplete(PlayerPrefs.GetInt(NAME_SPACE_USER_ID) == 0
            ? IGameUserData.UserStatus.New
            : IGameUserData.UserStatus.Ok);
    }

    public IEnumerator Check(
        string channel,
        string channelUser,
        Action<IGameUserData.UserStatus> onComplete)
    {
        yield return null;

        onComplete(PlayerPrefs.GetInt(NAME_SPACE_USER_ID) == 0
            ? IGameUserData.UserStatus.New
            : IGameUserData.UserStatus.Ok);
    }

    public IEnumerator Bind(
        int userID,
        string channelUser,
        string channel,
        Action<bool?> onComplete)
    {
        yield return null;
    }

    public IEnumerator Unbind(
        string channel,
        string channelUser,
        Action<bool?> onComplete)
    {
        yield return null;
    }

    void Awake()
    {
        IUserData.instance = this;
    }
}
