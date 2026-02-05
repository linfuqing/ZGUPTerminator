using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    private enum ActiveType
    {
        Day, 
        Week, 
        Month, 
        
        Achievement
    }
    
    private struct Active<T>
    {
        public T value;
        
        public uint seconds;

        public Active(T value)
        {
            this.value = value;
            seconds = ZG.DateTimeUtility.GetSeconds();
        }

        public Active(string value, Func<Memory<string>, T> parse)
        {
            if (string.IsNullOrEmpty(value))
            {
                this = default;
                
                return;
            }

            var parameters = value.Split(UserData.SEPARATOR);
            
            seconds = uint.Parse(parameters[0]);
            this.value = parse(parameters.AsMemory(1));
        }

        public T ToDay()
        {
            return ZG.DateTimeUtility.IsToday(seconds, DateTimeUtility.DataTimeType.UTC) ? value : default;
        }
        
        public T ToWeek()
        {
            return ZG.DateTimeUtility.IsThisWeek(seconds, DateTimeUtility.DataTimeType.UTC) ? value : default;
        }

        public T ToMonth()
        {
            return ZG.DateTimeUtility.IsThisMonth(seconds, DateTimeUtility.DataTimeType.UTC) ? value : default;
        }

        public override string ToString()
        {
            return $"{seconds}{UserData.SEPARATOR}{value}";
        }
    }

    public static int ad => __GetQuest(UserQuest.Type.Unknown, ActiveType.Day, out _);

    public static int aw => __GetQuest(UserQuest.Type.Unknown, ActiveType.Week, out _);

    public static int am => __GetQuest(UserQuest.Type.Unknown, ActiveType.Month, out _);

    [Serializable]
    internal struct Active
    {
        public string name;

        [Tooltip("天数或活跃值")]
        public int exp;
        
        public UserRewardData[] rewards;
#if UNITY_EDITOR
        [CSVField]
        public string 活跃名称
        {
            set => name = value;
        }
        
        [CSVField]
        public int 活跃值
        {
            set => exp = value;
        }
        
        [CSVField]
        public string 活跃奖励
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    rewards = null;

                    return;
                }

                var parameters = value.Split('/');
                int numParameters = parameters.Length;
                rewards = new UserRewardData[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    rewards[i] = new UserRewardData(parameters[i]);
            }
        }
#endif
    }

    [Header("Actives")] 
    [SerializeField] 
    internal Active[] _actives;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_actives", guidIndex = -1, nameIndex = 0)] 
    internal string _activesPath;
#endif
    
    [SerializeField] 
    internal string[] _signInActiveNames;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_signInActiveNames", guidIndex = -1, nameIndex = 0)] 
    internal string _signInActiveNamesPath;
#endif

    public const string NAME_SPACE_USER_SIGN_IN_ACTIVE = "UserSignInActive";
    
    public IEnumerator QuerySignIn(uint userID, Action<IUserData.SignIn> onComplete)
    {
        yield return __CreateEnumerator();

        if(__GetQuest(UserQuest.Type.Login, ActiveType.Day) < 1)
            __AppendQuest(UserQuest.Type.Login, 1);

        int day = __GetQuest(UserQuest.Type.Login, ActiveType.Achievement), week = __ToWeek(day);

        IUserData.SignIn result;
        result.day = __ToDay(day);

        int numSignInActives = _signInActiveNames.Length;
        Active signInActive;
        UserActive userActive;
        result.actives = new UserActive[numSignInActives];
        for (int i = 0; i < numSignInActives; ++i)
        {
            signInActive = _actives[__GetActiveIndex(_signInActiveNames[i])];
            userActive.name = signInActive.name;
            userActive.id = __ToID(i);
            userActive.flag =
                PlayerPrefs.GetInt($"{NAME_SPACE_USER_SIGN_IN_ACTIVE}{signInActive.name}") > week
                    ? UserActive.Flag.Collected
                    : 0;
            userActive.exp = signInActive.exp;
            userActive.rewards = signInActive.rewards;
            result.actives[i] = userActive;
        }
        
        onComplete(result);
    }

    public IEnumerator CollectSignIn(uint userID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        int day = __GetQuest(UserQuest.Type.Login, ActiveType.Achievement), 
            week =__ToWeek(day);
        day = __ToDay(day);
        
        string key;
        Active signInActive;
        List<UserRewardData> rewards = null;
        foreach (var signInActiveName in _signInActiveNames)
        {
            signInActive = _actives[__GetActiveIndex(signInActiveName)];
            if(signInActive.exp > day)
                continue;

            key = $"{NAME_SPACE_USER_SIGN_IN_ACTIVE}{signInActive.name}";
            if(PlayerPrefs.GetInt(key) > week)
                continue;

            PlayerPrefs.SetInt(key, week + 1);

            if (rewards == null)
                rewards = new List<UserRewardData>();
            
            rewards.AddRange(signInActive.rewards);
        }

        onComplete(rewards == null ? null : __ApplyRewards(rewards.ToArray()).ToArray());
    }

    [Serializable]
    internal struct Quest
    {
        public string name;

        public UserQuest.Type type;
        
        public int capacity;

        public UserRewardData[] rewards;
        
#if UNITY_EDITOR
        [CSVField]
        public string 任务名称
        {
            set => name = value;
        }
        
        [CSVField]
        public int 任务类型
        {
            set => type = (UserQuest.Type)value;
        }
        
        [CSVField]
        public int 任务计数
        {
            set => capacity = value;
        }
        
        [CSVField]
        public string 任务奖励
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    rewards = null;

                    return;
                }

                var parameters = value.Split('/');
                int numParameters = parameters.Length;
                rewards = new UserRewardData[numParameters];
                for (int i = 0; i < numParameters; ++i)
                    rewards[i] = new UserRewardData(parameters[i]);
            }
        }
#endif
    }
    
    [SerializeField]
    internal Quest[] _quests;

#if UNITY_EDITOR
    [SerializeField, CSV("_quests", guidIndex = -1, nameIndex = 0)] 
    internal string _questsPath;
#endif

    [Serializable]
    internal struct Actives
    {
        public string[] activeNames;

#if UNITY_EDITOR
        [SerializeField, CSV("activeNames", guidIndex = -1, nameIndex = 0)] 
        internal string _activeNamesPath;
#endif
        
        public string[] questNames;
        
#if UNITY_EDITOR
        [SerializeField, CSV("questNames", guidIndex = -1, nameIndex = 0)] 
        internal string _questNamesPath;
#endif

    }

    [SerializeField]
    internal Actives _activesForDay;

    [SerializeField]
    internal Actives _activesForWeek;
    
    private const string NAME_SPACE_USER_ACTIVES_ACTIVE = "UserActivesActive";
    private const string NAME_SPACE_USER_ACTIVES_QUEST = "UserActivesQuest";

    public IEnumerator QueryActives(uint userID, UserActiveType type, Action<IUserData.Actives> onComplete)
    {
        yield return __CreateEnumerator();

        if(__GetQuest(UserQuest.Type.Login, ActiveType.Day) < 1)
            __AppendQuest(UserQuest.Type.Login, 1);

        int count;
        UserQuest userQuest;
        UserActive userActive;
        Active active;
        Quest quest;
        IUserData.Actives result;
        switch (type)
        {
            case UserActiveType.Day:
                result.exp = ad;

                count = _activesForDay.activeNames.Length;
                result.actives = new UserActive[count];
                for (int i = 0; i < count; ++i)
                {
                    active = _actives[__GetActiveIndex(_activesForDay.activeNames[i])];
                    userActive.name = active.name;
                    userActive.id = __ToID(i);
                    userActive.flag =
                        new Active<int>(PlayerPrefs.GetString($"{NAME_SPACE_USER_ACTIVES_ACTIVE}{type}{active.name}"), __Parse).ToDay() == 0
                            ? 0
                            : UserActive.Flag.Collected;
                    userActive.exp = active.exp;
                    userActive.rewards = active.rewards;
                    result.actives[i] = userActive;
                }
                
                count = _activesForDay.questNames.Length;
                result.quests = new UserQuest[count];
                for (int i = 0; i < count; ++i)
                {
                    quest = _quests[__GetQuestIndex(_activesForDay.questNames[i])];
                    userQuest.name = quest.name;
                    userQuest.id = __ToID(i);
                    userQuest.type = quest.type;
                    userQuest.flag =
                        new Active<int>(PlayerPrefs.GetString($"{NAME_SPACE_USER_ACTIVES_QUEST}{type}{quest.name}"),
                            __Parse).ToDay() == 0
                            ? 0
                            : UserQuest.Flag.Collected;
                    userQuest.count = __GetQuest(userQuest.type, ActiveType.Day);
                    userQuest.capacity = quest.capacity;
                    userQuest.rewards = quest.rewards;
                    result.quests[i] = userQuest;
                }

                break;
            case UserActiveType.Week:
                result.exp = aw;

                count = _activesForWeek.activeNames.Length;
                result.actives = new UserActive[count];
                for (int i = 0; i < count; ++i)
                {
                    active = _actives[__GetActiveIndex(_activesForWeek.activeNames[i])];
                    userActive.name = active.name;
                    userActive.id = __ToID(i);
                    userActive.flag =
                        new Active<int>(PlayerPrefs.GetString($"{NAME_SPACE_USER_ACTIVES_ACTIVE}{type}{active.name}"), __Parse).ToWeek() == 0
                            ? 0
                            : UserActive.Flag.Collected;
                    userActive.exp = active.exp;
                    userActive.rewards = active.rewards;
                    result.actives[i] = userActive;
                }
                
                count = _activesForWeek.questNames.Length;
                result.quests = new UserQuest[count];
                for (int i = 0; i < count; ++i)
                {
                    quest = _quests[__GetQuestIndex(_activesForWeek.questNames[i])];
                    userQuest.name = quest.name;
                    userQuest.id = __ToID(i);
                    userQuest.type = quest.type;
                    userQuest.flag =
                        new Active<int>(PlayerPrefs.GetString($"{NAME_SPACE_USER_ACTIVES_QUEST}{type}{quest.name}"),
                            __Parse).ToWeek() == 0
                            ? 0
                            : UserQuest.Flag.Collected;
                    userQuest.count = __GetQuest(userQuest.type, ActiveType.Week);
                    userQuest.capacity = quest.capacity;
                    userQuest.rewards = quest.rewards;
                    result.quests[i] = userQuest;
                }

                break;
            default:
                yield break;
        }

        onComplete(result);
    }

    public IEnumerator CollectActive(
        uint userID,
        uint activeID,
        UserActiveType type,
        Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        string activeName, key;
        int exp, times;
        switch (type)
        {
            case UserActiveType.Day:
                activeName = _activesForDay.activeNames[__ToIndex(activeID)];
                key = $"{NAME_SPACE_USER_ACTIVES_ACTIVE}{type}{activeName}";
                
                times = new Active<int>(PlayerPrefs.GetString(key), __Parse).ToDay();
                exp = ad;
                break;
            case UserActiveType.Week:
                activeName = _activesForWeek.activeNames[__ToIndex(activeID)];
                key = $"{NAME_SPACE_USER_ACTIVES_ACTIVE}{type}{activeName}";
                
                times = new Active<int>(PlayerPrefs.GetString(key), __Parse).ToWeek();
                exp = aw;
                break;
            default:
                yield break;
        }

        if (times == 0)
        {
            var active = _actives[__GetActiveIndex(activeName)];
            if (active.exp <= exp)
            {
                PlayerPrefs.SetString(key, new Active<int>(1).ToString());

                onComplete(__ApplyRewards(active.rewards).ToArray());

                yield break;
            }
        }

        onComplete(null);
    }

    public IEnumerator CollectActiveQuest(
        uint userID,
        uint questID,
        UserActiveType type,
        Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();
        
        int count, times;
        string key;
        Quest quest;
        switch (type)
        {
            case UserActiveType.Day:
                quest = _quests[__GetQuestIndex(_activesForDay.questNames[__ToIndex(questID)])];
                key = $"{NAME_SPACE_USER_ACTIVES_QUEST}{type}{quest.name}";
                times = new Active<int>(PlayerPrefs.GetString(key), __Parse).ToDay();
                count = __GetQuest(quest.type, ActiveType.Day);
                break;
            case UserActiveType.Week:
                quest = _quests[__GetQuestIndex(_activesForWeek.questNames[__ToIndex(questID)])];
                key = $"{NAME_SPACE_USER_ACTIVES_QUEST}{type}{quest.name}";
                times = new Active<int>(PlayerPrefs.GetString(key), __Parse).ToWeek();
                count = __GetQuest(quest.type, ActiveType.Week);
                break;
            default:
                yield break;
        }

        if (times == 0 && quest.capacity <= count)
        {
            PlayerPrefs.SetString(key, new Active<int>(1).ToString());

            onComplete(__ApplyRewards(quest.rewards).ToArray());

            yield break;
        }

        onComplete(null);
    }

    [SerializeField] 
    internal string[] _achievementQuestNames;

#if UNITY_EDITOR
    [SerializeField, CSV("_achievementQuestNames", guidIndex = -1, nameIndex = 0)] 
    internal string _achievementQuestNamesPath;
#endif

    public const string NAME_SPACE_USER_ACHIEVEMENT_QUEST = "UserActivesQuest";
    
    public IEnumerator CollectAchievementQuest(
        uint userID,
        uint questID,
        Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();
        
        if(__GetQuest(UserQuest.Type.Login, ActiveType.Day) < 1)
            __AppendQuest(UserQuest.Type.Login, 1);

        var quest = _quests[__GetQuestIndex(_achievementQuestNames[__ToIndex(questID)])];
        string key = $"{NAME_SPACE_USER_ACHIEVEMENT_QUEST}{quest.name}";
        if (PlayerPrefs.GetInt(key) == 0 && quest.capacity <= __GetQuest(quest.type, ActiveType.Achievement))
        {
            PlayerPrefs.SetInt(key, 1);

            onComplete(__ApplyRewards(quest.rewards).ToArray());

            yield break;
        }

        onComplete(null);
    }

    public IEnumerator QueryAchievements(
        uint userID,
        Action<Memory<UserQuest>> onComplete)
    {
        yield return __CreateEnumerator();
        
        if(__GetQuest(UserQuest.Type.Login, ActiveType.Day) < 1)
            __AppendQuest(UserQuest.Type.Login, 1);

        int numAchievementQuestNames = _achievementQuestNames.Length;
        Quest quest;
        UserQuest userQuest;
        var results = new UserQuest[numAchievementQuestNames];
        for (int i = 0; i < numAchievementQuestNames; ++i)
        {
            quest = _quests[__GetQuestIndex(_achievementQuestNames[i])];
            userQuest.name = quest.name;
            userQuest.id = __ToID(i);
            userQuest.type = quest.type;
            userQuest.flag =
                PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACHIEVEMENT_QUEST}{quest.name}") == 0
                    ? 0
                    : UserQuest.Flag.Collected;
            userQuest.count = __GetQuest(userQuest.type, ActiveType.Achievement);
            userQuest.capacity = quest.capacity;
            userQuest.rewards = quest.rewards;
            results[i] = userQuest;
        }

        onComplete(results);
    }
    
    [Serializable]
    internal struct ActiveEvent
    {
        public string name;
        
        public int startDay;
        public int days;
        
        public string[] activeNames;

        public string[] questNames;
        
#if UNITY_EDITOR
        [CSVField]
        public string 活跃活动名
        {
            set => name = value;
        }
        
        [CSVField]
        public int 活跃活动开始天数
        {
            set => startDay =value;
        }
        
        [CSVField]
        public int 活跃活动总天数
        {
            set => days = value;
        }
        
        [CSVField]
        public string 活跃活动活跃名
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    activeNames = null;

                    return;
                }

                activeNames = value.Split('/');
            }
        }
        
        [CSVField]
        public string 活跃活动任务名
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    questNames = null;

                    return;
                }

                questNames = value.Split('/');
            }
        }
#endif
    }
    
    [SerializeField]
    internal ActiveEvent[] _activeEvents;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_activeEvents", guidIndex = -1, nameIndex = 0)] 
    internal string _activeEventsPath;
#endif

    private const string NAME_SPACE_USER_ACTIVE_EVENT_TIME = "UserActiveEventTime";

    public IEnumerator QueryActiveEvents(
        uint userID,
        Action<IUserData.ActiveEvents> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.ActiveEvents results;
        int seconds = PlayerPrefs.GetInt(NAME_SPACE_USER_ACTIVE_EVENT_TIME);
        if (seconds == 0)
        {
            PlayerPrefs.SetInt(NAME_SPACE_USER_ACTIVE_EVENT_TIME, (int)DateTimeUtility.GetSeconds());

            results.days = 0;
        }
        else
            results.days = DateTimeUtility.GetTotalDays((uint)seconds, out _, out _, DateTimeUtility.DataTimeType.UTC);
        
        if(__GetQuest(UserQuest.Type.Login, ActiveType.Day) < 1)
            __AppendQuest(UserQuest.Type.Login, 1);
        
        int numActiveEvents = _activeEvents.Length;
        results.values = new UserActiveEvent[numActiveEvents];

        int i, j, numActives, numQuests;
        UserActiveEvent result;
        ActiveEvent activeEvent;
        Active active;
        Quest quest;
        UserQuest userQuest;
        UserActive userActive;
        for (i = 0; i < numActiveEvents; ++i)
        {
            activeEvent = _activeEvents[i];
            if(activeEvent.startDay > results.days || activeEvent.days > 0 && activeEvent.startDay + activeEvent.days <= results.days)
                continue;

            result.id = __ToID(i);
            result.name = activeEvent.name;
            result.startDay = activeEvent.startDay;
            result.days = activeEvent.days;
            result.exp = __GetEvent(activeEvent.name);
            numActives = activeEvent.activeNames == null ? 0 : activeEvent.activeNames.Length;
            result.actives = new UserActive[numActives];
            for (j = 0; j < numActives; ++j)
            {
                active = _actives[__GetActiveIndex(activeEvent.activeNames[j])];
                userActive.name = active.name;
                userActive.id = __ToID(j);
                userActive.flag = result.exp < active.exp
                    ? UserActive.Flag.Locked
                    : (UserActive.Flag)PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_ACTIVES_ACTIVE}{activeEvent.name}{UserData.SEPARATOR}{active.name}");
                userActive.exp = active.exp;
                userActive.rewards = active.rewards;
                result.actives[j] = userActive;
            }
            
            numQuests = activeEvent.questNames == null ? 0 : activeEvent.questNames.Length;
            result.quests = new UserQuest[numQuests];
            for (j = 0; j < numQuests; ++j)
            {
                quest = _quests[__GetQuestIndex(activeEvent.questNames[j])];
                userQuest.name = quest.name;
                userQuest.id = __ToID(j);
                userQuest.type = quest.type;
                userQuest.count = __GetQuest(userQuest.type, ActiveType.Achievement);
                userQuest.capacity = quest.capacity;
                userQuest.flag =
                    (UserQuest.Flag)PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_ACTIVES_QUEST}{activeEvent.name}{UserData.SEPARATOR}{quest.name}");
                userQuest.rewards = quest.rewards;
                result.quests[j] = userQuest;
            }

            results.values[i] = result;
        }

        onComplete(results);
    }

    public IEnumerator CollectActiveEventActive(uint userID, uint activeEventID, uint activeID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();
        
        var activeEvent = _activeEvents[__ToIndex(activeEventID)];
        var active = _actives[__GetActiveIndex(activeEvent.activeNames[__ToIndex(activeID)])];
        string key = $"{NAME_SPACE_USER_ACTIVES_ACTIVE}{activeEvent.name}{UserData.SEPARATOR}{active.name}";
        if (PlayerPrefs.GetInt(key) == 0 && active.exp <= __GetEvent(activeEvent.name))
        {
            PlayerPrefs.SetInt(key, (int)UserActive.Flag.Collected);

            onComplete(__ApplyRewards(active.rewards).ToArray());
        }
        else
            onComplete(default);
    }

    public IEnumerator CollectActiveEventQuest(uint userID, uint activeEventID, uint questID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();
        
        var activeEvent = _activeEvents[__ToIndex(activeEventID)];
        var quest = _quests[__GetQuestIndex(activeEvent.questNames[__ToIndex(questID)])];
        string key = $"{NAME_SPACE_USER_ACTIVES_QUEST}{activeEvent.name}{UserData.SEPARATOR}{quest.name}";
        if (PlayerPrefs.GetInt(key) == 0 && __GetQuest(quest.type, ActiveType.Achievement) >= quest.capacity)
        {
            PlayerPrefs.SetInt(key, (int)UserQuest.Flag.Collected);

            onComplete(__ApplyRewards(quest.rewards).ToArray());
        }
        else
            onComplete(default);
    }
    
    private int __GetQuest(UserQuest.Type questType, ActiveType activeType)
    {
        switch (questType)
        {
            case UserQuest.Type.AchievementChapters:
                return UserData.chapter;
            case UserQuest.Type.AchievementCard:
                int maxCardLevel = 0;
                foreach (var card in _cards)
                    maxCardLevel = Mathf.Max(maxCardLevel,
                        __GetCardLevel(card.name, out _));
                return maxCardLevel - 1;
            case UserQuest.Type.AchievementCardStyles:
                int cardCount = 0;
                foreach (var card in _cards)
                {
                    if (__GetCardLevel(card.name, out _) != -1)
                        ++cardCount;
                }
                return cardCount;
            case UserQuest.Type.AchievementAccessoryStyles:
                HashSet<string> accessoryStyles = null;
                List<int> accessoryStageIndices;
                Accessory accessory;
                string key;
                int i, j, numAccessoryStages, numAccessories = _accessories.Length;
                for (i = 0; i < numAccessories; ++i)
                {
                    accessory = _accessories[i];
                    accessoryStageIndices = __GetAccessoryStageIndices(i);

                    numAccessoryStages = accessoryStageIndices.Count;
                    for (j = 0; j <= numAccessoryStages; ++j)
                    {
                        key =
                            $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessory.name}{UserData.SEPARATOR}{j}";
                        key = PlayerPrefs.GetString(key);
                        if (string.IsNullOrEmpty(key))
                            continue;

                        break;
                    }
                    
                    if(j > numAccessoryStages)
                        continue;

                    if (accessoryStyles == null)
                        accessoryStyles = new HashSet<string>();

                    accessoryStyles.Add(accessory.styleName);
                }

                return accessoryStyles == null ? 0 : accessoryStyles.Count;
            case UserQuest.Type.AchievementAccessorySlot:
                int maxAccessorySlotLevel = 0;
                foreach (var accessorySlot in _accessorySlots)
                    maxAccessorySlotLevel = Mathf.Max(maxAccessorySlotLevel,
                        PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}"));
                return maxAccessorySlotLevel + 1;
            case UserQuest.Type.AchievementAccessorySlots:
                int minAccessorySlotLevel = int.MaxValue;
                foreach (var accessorySlot in _accessorySlots)
                    minAccessorySlotLevel = Mathf.Min(minAccessorySlotLevel,
                        PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}"));
                
                return minAccessorySlotLevel < int.MaxValue ? minAccessorySlotLevel + 1 : 0;
            case UserQuest.Type.AchievementRoles:
                int numRoles = 0;
                foreach (var role in _roles)
                {
                    if(PlayerPrefs.GetInt($"{NAME_SPACE_USER_ROLE_FLAG}{role.name}") != 0)
                        ++numRoles;
                }
                
                return numRoles;
            default:
                return __GetQuest(questType, activeType, out _);
        }
    }

    public const string NAME_SPACE_USER_ACTIVE = "UserActive";

    private static int __GetQuest(UserQuest.Type questType, ActiveType activeType, out string key)
    {
        key = $"{NAME_SPACE_USER_ACTIVE}{activeType}{questType}";
        switch (activeType)
        {
            case ActiveType.Achievement:
                return PlayerPrefs.GetInt(key);
            case ActiveType.Day:
                return new Active<int>(PlayerPrefs.GetString(key), __Parse).ToDay();
            case ActiveType.Week:
                return new Active<int>(PlayerPrefs.GetString(key), __Parse).ToWeek();
            case ActiveType.Month:
                return new Active<int>(PlayerPrefs.GetString(key), __Parse).ToMonth();
            default:
                return 0;
        }
    }

    private static void __AppendQuest(UserQuest.Type type, int value)
    {
        int result = __GetQuest(type, ActiveType.Achievement, out string key);
        result += value;
        PlayerPrefs.SetInt(key, result);
        
        result = __GetQuest(type, ActiveType.Day, out key);
        result += value;
        PlayerPrefs.SetString(key, new Active<int>(result).ToString());
        
        result = __GetQuest(type, ActiveType.Week, out key);
        result += value;
        PlayerPrefs.SetString(key, new Active<int>(result).ToString());

        /*if (type == UserQuest.Type.Unknown)
        {
            result = __GetQuest(type, ActiveType.Month, out key);
            result += value;
            PlayerPrefs.SetString(key, new Active<int>(result).ToString());
        }*/
    }

    private static void __AppendActive(int value, ActiveType type)
    {
        var questType = UserQuest.Type.Unknown;
        int result = __GetQuest(questType, type, out string key);
        result += value;
        PlayerPrefs.SetString(key, new Active<int>(result).ToString());
        
        if (ActiveType.Month != type)
        {
            result = __GetQuest(questType, ActiveType.Month, out key);
            result += value;
            PlayerPrefs.SetString(key, new Active<int>(result).ToString());
        }
    }

    private static int __Parse(Memory<string> parameters)
    {
        return int.Parse(parameters.Span[0]);
    }

    private static int __ToWeek(int day)
    {
        return (day - 1) / 7;
    }
    
    private static int __ToDay(int day)
    {
        return ((day - 1) % 7) + 1;
    }
    
    private Dictionary<string, int> __activeNameToIndices;

    private int __GetActiveIndex(string name)
    {
        if (__activeNameToIndices == null)
        {
            __activeNameToIndices = new Dictionary<string, int>();

            int numActives = _actives.Length;
            for(int i = 0; i < numActives; ++i)
                __activeNameToIndices.Add(_actives[i].name, i);
        }

        return __activeNameToIndices.TryGetValue(name, out int index) ? index : -1;
    }
    
    private Dictionary<string, int> __questNameToIndices;

    private int __GetQuestIndex(string name)
    {
        if (__questNameToIndices == null)
        {
            __questNameToIndices = new Dictionary<string, int>();

            int numQuests = _quests.Length;
            for(int i = 0; i < numQuests; ++i)
                __questNameToIndices.Add(_quests[i].name, i);
        }

        return __questNameToIndices.TryGetValue(name, out int index) ? index : -1;
    }
}

public partial class UserData
{
    public IEnumerator QuerySignIn(uint userID, Action<IUserData.SignIn> onComplete)
    {
        return UserDataMain.instance.QuerySignIn(userID, onComplete);
    }

    public IEnumerator CollectSignIn(uint userID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectSignIn(userID, onComplete);
    }

    public IEnumerator QueryActives(uint userID, UserActiveType type, Action<IUserData.Actives> onComplete)
    {
        return UserDataMain.instance.QueryActives(userID, type, onComplete);
    }

    public IEnumerator CollectActive(
        uint userID,
        uint activeID,
        UserActiveType type,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectActive(userID, activeID, type, onComplete);
    }
    
    public IEnumerator CollectActiveQuest(
        uint userID, 
        uint questID, 
        UserActiveType type, 
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectActiveQuest(userID, questID, type, onComplete);
    }
    
    public IEnumerator CollectAchievementQuest(
        uint userID, 
        uint questID, 
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectAchievementQuest(userID, questID, onComplete);
    }
    
    public IEnumerator QueryAchievements(
        uint userID, 
        Action<Memory<UserQuest>> onComplete)
    {
        return UserDataMain.instance.QueryAchievements(userID, onComplete);;
    }

    public IEnumerator QueryActiveEvents(
        uint userID,
        Action<IUserData.ActiveEvents> onComplete)
    {
        return UserDataMain.instance.QueryActiveEvents(userID, onComplete);
    }

    public IEnumerator CollectActiveEventActive(uint userID, uint activeEventID, uint activeID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectActiveEventActive(userID, activeEventID, activeID, onComplete);
    }
    
    public IEnumerator CollectActiveEventQuest(uint userID, uint activeEventID, uint questID,
        Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectActiveEventQuest(userID, activeEventID, questID, onComplete);
    }
}
