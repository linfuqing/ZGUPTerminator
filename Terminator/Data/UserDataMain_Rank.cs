using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    public struct Rank
    {
        public string name;
    
        public int points;
        
        public UserRewardData[] rewards;

        public UserRankData ToUserRankData()
        {
            UserRankData result;
            result.name = name;
            result.points = points;
            result.rewards = rewards;
            return result;
        }
        
#if UNITY_EDITOR
        [CSVField]
        public string 段位名字
        {
            set => name = value;
        }
        
        [CSVField]
        public int 段位积分
        {
            set => points = value;
        }
        
        [CSVField]
        public string 段位奖励
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
                for(int i = 0; i < numParameters; ++i)
                    rewards[i] = UserRewardData.Parse(parameters[i]);
            }
        }
#endif
    }
    
    
    [Serializable]
    public struct RankListElement
    {
        private struct Comparer : IComparer<int>
        {
            public int Compare(int x, int y)
            {
                return y.CompareTo(x);
            }
        }
    
        private struct Wrapper : IReadOnlyListWrapper<int, RankListElement[]>
        {
            public int GetCount(RankListElement[] list) => list.Length;

            public int Get(RankListElement[] list, int index) => list[index].points;
        }


        public string name;
    
        public int points;
        
        public UserRewardData[] rewards;
        
        public UserRankData ToUserRankData()
        {
            UserRankData result;
            result.name = name;
            result.points = points;
            result.rewards = rewards;
            return result;
        }

        public static int BinarySearch(RankListElement[] values, int points)
        {
            return values.BinarySearch(points, new Comparer(), new Wrapper());
        }
        
#if UNITY_EDITOR
        [CSVField]
        public string 排名名字
        {
            set => name = value;
        }
        
        [CSVField]
        public int 排名名次
        {
            set => points = value;
        }
        
        [CSVField]
        public string 排名奖励
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
                for(int i = 0; i < numParameters; ++i)
                    rewards[i] = UserRewardData.Parse(parameters[i]);
            }
        }
#endif
    }
    
    [Header("Rank")] 
    [SerializeField] 
    internal Rank[] _ranks;

#if UNITY_EDITOR
    [SerializeField, CSV("_ranks", guidIndex = -1, nameIndex = 0)] 
    internal string _ranksPath;
#endif
    
    [SerializeField] 
    internal RankListElement[] _rankedList;
    
#if UNITY_EDITOR
    [SerializeField, CSV("_rankedList", guidIndex = -1, nameIndex = 0)] 
    internal string _rankedListPath;
#endif

    private const string NAME_SPACE_USER_RANK = "UserRank";

    public static int rank
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_RANK), __Parse).ToMonth();
        
        private set => PlayerPrefs.SetString(NAME_SPACE_USER_RANK, new Active<int>(value).ToString());
    }

    private const string NAME_SPACE_USER_RANKED_POINTS = "UserRankPoints";

    public static int rankedPoints
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_RANKED_POINTS), __Parse).ToMonth();
        
        private set => PlayerPrefs.SetString(NAME_SPACE_USER_RANKED_POINTS, new Active<int>(value).ToString());
    }

    private const string NAME_SPACE_USER_RANKED_PASS = "UserRankPass";

    public static int rankedPass
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_RANKED_PASS), __Parse).ToMonth();
        
        private set => PlayerPrefs.SetString(NAME_SPACE_USER_RANKED_PASS, new Active<int>(value).ToString());
    }

    private const string NAME_SPACE_USER_RANKED_LIST_TOP = "UserRankListTop";

    private const string NAME_SPACE_USER_RANKED_LIST_POINTS = "UserRankListPoints";

    public static int rankedListPoints
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_RANKED_LIST_POINTS), __Parse).ToMonth();
        
        private set => PlayerPrefs.SetString(NAME_SPACE_USER_RANKED_LIST_POINTS, new Active<int>(value).ToString());
    }
    
    public IEnumerator QueryRankList(uint userID, Action<IUserData.RankedList> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.RankedList result;
        result.points = rankedListPoints;
        if (result.points == 0)
        {
            result.points = UnityEngine.Random.Range(500, 1000);
            
            rankedListPoints = result.points;
        }

        result.users = new IUserData.RankedUser[100];

        int numRanks = _rankedList == null ? 0 : _rankedList.Length;
        
        result.ranks = new UserRankData[numRanks];
        for (int i = 0; i < numRanks; ++i)
            result.ranks[i] = _rankedList[i].ToUserRankData();

        onComplete(result);
    }

    public IEnumerator QueryRanks(uint userID, Action<IUserData.Ranks> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Ranks ranks;
        ranks.points = rankedPoints;
        ranks.rank = rank;
        
        int numRanks = _ranks == null ? 0 : _ranks.Length;

        ranks.ranks = new UserRankData[numRanks];
        for (int i = 0; i < numRanks; ++i)
            ranks.ranks[i] = _rankedList[i].ToUserRankData();

        onComplete(ranks);
    }

    public IEnumerator Uprank(uint userID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();
        
        rankedListPoints = Mathf.Max(rankedListPoints - UnityEngine.Random.Range(5, 10), 1);

        int rank = UserDataMain.rank;
        if (rank < _ranks.Length)
        {
            var temp = _ranks[rank];
            int rankedPoints = UserDataMain.rankedPoints;
            if (rankedPoints >= temp.points)
            {
                rankedPoints -= temp.points;

                UserDataMain.rankedPoints = rankedPoints;
                UserDataMain.rank = rank + 1;

                onComplete(__ApplyRewards(temp.rewards)?.ToArray());
                
                yield break;
            }
        }
        
        onComplete(null);
    }

    public static int rankedListTop
    {
        get => new Active<int>(PlayerPrefs.GetString(NAME_SPACE_USER_RANKED_LIST_TOP), __Parse).ToMonth();
        
        private set => PlayerPrefs.SetString(NAME_SPACE_USER_RANKED_LIST_TOP, new Active<int>(value).ToString());
    }

    public IEnumerator CollectRankList(uint userID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

        UserReward[] results = null;
        int source = rankedListTop == 0 ? -1 : RankListElement.BinarySearch(_rankedList, rankedListTop),
            destination = RankListElement.BinarySearch(_rankedList, rankedListPoints);

        if (source < destination)
        {
            rankedListTop = destination;

            var rewards = new List<UserReward>();
            for (int i = source + 1; i <= destination; ++i)
                __ApplyRewards(_rankedList[i].rewards, rewards);

            results = rewards.ToArray();
        }

        onComplete(results);
    }
}

public partial class UserData
{
    public IEnumerator QueryRankList(uint userID, Action<IUserData.RankedList> onComplete)
    {
        return UserDataMain.instance.QueryRankList(userID, onComplete);
    }
    
    public IEnumerator QueryRanks(uint userID, Action<IUserData.Ranks> onComplete)
    {
        return UserDataMain.instance.QueryRanks(userID, onComplete);
    }

    public IEnumerator Uprank(uint userID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.Uprank(userID, onComplete);
    }

    public IEnumerator CollectRankList(uint userID, Action<Memory<UserReward>> onComplete)
    {
        return UserDataMain.instance.CollectRankList(userID, onComplete);
    }
}