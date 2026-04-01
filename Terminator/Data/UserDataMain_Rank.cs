using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class UserDataMain
{
    [Header("Rank")] 
    [SerializeField] 
    internal UserRankData[] _ranks;

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

    public IEnumerator QueryRankList(uint userID, Action<IUserData.RankedList> onComplete)
    {
        yield return __CreateEnumerator();

        onComplete(default);
    }

    public IEnumerator QueryRanks(uint userID, Action<IUserData.Ranks> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Ranks ranks;
        ranks.points = rankedPoints;
        ranks.rank = rank;
        ranks.ranks = _ranks;
        onComplete(ranks);
    }

    public IEnumerator Uprank(uint userID, Action<Memory<UserReward>> onComplete)
    {
        yield return __CreateEnumerator();

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
}