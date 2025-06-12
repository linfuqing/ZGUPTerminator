using System;
using System.Collections;
using UnityEngine;

public partial class UserDataMain
{
    private struct Active<T>
    {
        public T value;
        
        public int day;

        public Active(T value)
        {
            this.value = value;
            day = (int)new TimeSpan(DateTime.Now.Ticks).TotalDays;
        }

        public Active(string value, Func<Memory<string>, T> parse)
        {
            if (string.IsNullOrEmpty(value))
            {
                this = default;
                
                return;
            }

            var parameters = value.Split(UserData.SEPARATOR);
            
            day = int.Parse(parameters[0]);
            this.value = parse(parameters.AsMemory(1));
        }

        public T ToDay()
        {
            return (int)(new TimeSpan(DateTime.Now.Ticks).TotalDays) == day ? value : default;
        }
        
        public T ToWeek()
        {
            DateTime dateTime = new DateTime(day * TimeSpan.TicksPerDay), now = DateTime.Now;
            var totalDays = (now - dateTime).TotalDays;
            if (totalDays < 7d && totalDays > -7d)
            {
                DayOfWeek dayOfWeek = dateTime.DayOfWeek, nowDayOfWeek = now.DayOfWeek;
                if ((totalDays >= 0.0f) ^ (dayOfWeek >= nowDayOfWeek))
                    return value;
            }

            return default;
        }

        public T ToMonth()
        {
            DateTime dateTime = new DateTime(day * TimeSpan.TicksPerDay), now = DateTime.Now;
            var totalDays = (now - dateTime).TotalDays;
            if (totalDays < 30d && totalDays > -30d)
            {
                if (dateTime.Month == now.Month)
                    return value;
            }

            return default;
        }

        public override string ToString()
        {
            return $"{day}{UserData.SEPARATOR}{value}";
        }
    }
    
    public const string NAME_SPACE_USER_ACTIVE_DAY = "UserActiveDay";
    public const string NAME_SPACE_USER_ACTIVE_WEEK = "UserActiveWeek";
    public const string NAME_SPACE_USER_ACTIVE_MONTH = "UserActiveMonth";

    public static int ad
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_ACTIVE_DAY), __Parse).ToDay();

        set => PlayerPrefs.SetString(NAME_SPACE_USER_ACTIVE_DAY, new Active<int>(value).ToString());
    }

    public static int aw
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_ACTIVE_WEEK), __Parse).ToWeek();

        set => PlayerPrefs.SetString(NAME_SPACE_USER_ACTIVE_WEEK, new Active<int>(value).ToString());
    }

    public static int am
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_ACTIVE_MONTH), __Parse).ToMonth();

        set => PlayerPrefs.SetString(NAME_SPACE_USER_ACTIVE_MONTH, new Active<int>(value).ToString());
    }

    private static int __Parse(Memory<string> parameters)
    {
        return int.Parse(parameters.Span[0]);
    }

    /*public IEnumerator CollectReward(uint userID, uint rewardID, Action<bool> onComplete)
    {
        
    }
    
    public IEnumerator CollectQuest(uint userID, uint questID, Action<bool> onComplete)
    {
        
    }*/
}

public partial class UserData
{
    public IEnumerator QuerySignIn(uint userID, Action<IUserData.SignIn> onComplete)
    {
        return null;
    }

    public IEnumerator CollectSignIn(uint userID, uint activeID, Action<Memory<UserReward>> onComplete)
    {
        return null;
    }

    public IEnumerator QueryActive(uint userID, UserActiveType type, Action<IUserData.Active> onComplete)
    {
        return null;
    }

    public IEnumerator CollectActive(
        uint userID,
        UserActiveType type,
        int activeID,
        Action<Memory<UserReward>> onComplete)
    {
        return null;
    }
    
    public IEnumerator CollectActiveQuest(
        uint userID, 
        UserActiveType type, 
        int questID, 
        Action<Memory<UserReward>> onComplete)
    {
        return null;
    }
    
    public IEnumerator CollectAchievementQuest(
        uint userID, 
        int questID, 
        Action<Memory<UserReward>> onComplete)
    {
        return null;
    }
    
    public IEnumerator QueryAchievements(
        uint userID, 
        Action<Memory<UserQuest>> onComplete)
    {
        return null;
    }
}
