using System;
using UnityEngine;

public partial class UserDataMain
{
    private struct Active
    {
        public int value;
        
        public int day;

        public Active(int value)
        {
            this.value = value;
            day = (int)new TimeSpan(DateTime.Now.Ticks).TotalDays;
        }

        public Active(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                this = default;
                
                return;
            }

            var parameters = value.Split(UserData.SEPARATOR);
            
            this.value = int.Parse(parameters[0]);
            day = int.Parse(parameters[1]);
        }

        public int ToDay()
        {
            return (int)(new TimeSpan(DateTime.Now.Ticks).TotalDays) == day ? value : 0;
        }
        
        public int ToWeek()
        {
            DateTime dateTime = new DateTime(day * TimeSpan.TicksPerDay), now = DateTime.Now;
            var totalDays = (now - dateTime).TotalDays;
            if (totalDays < 7d && totalDays > -7d)
            {
                DayOfWeek dayOfWeek = dateTime.DayOfWeek, nowDayOfWeek = now.DayOfWeek;
                if ((totalDays >= 0.0f) ^ (dayOfWeek >= nowDayOfWeek))
                    return value;
            }

            return 0;
        }

        public int ToMonth()
        {
            DateTime dateTime = new DateTime(day * TimeSpan.TicksPerDay), now = DateTime.Now;
            var totalDays = (now - dateTime).TotalDays;
            if (totalDays < 30d && totalDays > -30d)
            {
                if (dateTime.Month == now.Month)
                    return value;
            }

            return 0;
        }

        public override string ToString()
        {
            return $"{value}{UserData.SEPARATOR}{day}";
        }
    }
    
    public const string NAME_SPACE_USER_ACTIVE_DAY = "UserActiveDay";
    public const string NAME_SPACE_USER_ACTIVE_WEEK = "UserActiveWeek";
    public const string NAME_SPACE_USER_ACTIVE_MONTH = "UserActiveMonth";

    public static int ad
    {
        get => new Active(PlayerPrefs.GetString(NAME_SPACE_USER_ACTIVE_DAY)).ToDay();

        set => PlayerPrefs.SetString(NAME_SPACE_USER_ACTIVE_DAY, new Active().ToString());
    }

    public static int aw
    {
        get => new Active(PlayerPrefs.GetString(NAME_SPACE_USER_ACTIVE_WEEK)).ToWeek();

        set => PlayerPrefs.SetString(NAME_SPACE_USER_ACTIVE_WEEK, new Active().ToString());
    }

    public static int am
    {
        get => new Active(PlayerPrefs.GetString(NAME_SPACE_USER_ACTIVE_MONTH)).ToMonth();

        set => PlayerPrefs.SetString(NAME_SPACE_USER_ACTIVE_MONTH, new Active().ToString());
    }
}
