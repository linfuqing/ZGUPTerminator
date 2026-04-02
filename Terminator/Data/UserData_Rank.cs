using System;
using System.Collections;
using System.Collections.Generic;
using ZG;

public struct UserRankData
{
    public string name;
    
    /// <summary>
    /// 名次或积分
    /// </summary>
    public int points;
        
    public UserRewardData[] rewards;
}

public partial interface IUserData
{
    public struct RankedUser
    {
        public string name;
        public string avatar;
        public uint id;
        public int points;
    }
    
    public struct RankedList
    {
        /// <summary>
        /// 自己的名次
        /// </summary>
        public int points;
        
        /// <summary>
        /// 前N名
        /// </summary>
        public RankedUser[] users; 

        /// <summary>
        /// 每个名次阶段的奖励
        /// </summary>
        public UserRankData[] ranks;
    }
    
    public struct Ranks
    {
        /// <summary>
        /// 段位积分
        /// </summary>
        public int points;

        /// <summary>
        /// 当前段位
        /// </summary>
        public int rank;

        public UserRankData[] ranks;
    }

    /// <summary>
    /// 排行榜
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryRankList(uint userID, Action<RankedList> onComplete);
    
    /// <summary>
    /// 排位赛积分和段位
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator QueryRanks(uint userID, Action<Ranks> onComplete);

    /// <summary>
    /// 升段并且获取奖励，跟无尽轮回不同，我们播放完升段动画完成后自动触发领取奖励
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator Uprank(uint userID, Action<Memory<UserReward>> onComplete);

    /// <summary>
    /// 领取排行榜奖励
    /// </summary>
    /// <param name="userID"></param>
    /// <param name="onComplete"></param>
    /// <returns></returns>
    IEnumerator CollectRankList(uint userID, Action<Memory<UserReward>> onComplete);
}

