using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

public class RewardManager : MonoBehaviour
{
    [Serializable]
    internal struct Reward
    {
        public string name;

        public Sprite sprite;
    }

    [Serializable]
    internal struct Style
    {
        public string name;
        
        public RewardStyle value;
    }

    [Serializable]
    internal struct Pool
    {
        public string name;

        public Style[] styles;
    }

    private struct Instance
    {
        public int count;
        public List<RewardStyle> styles;
    }
    
    [SerializeField]
    internal Reward[] _rewards;
    
    [SerializeField]
    internal Pool[] _pools;

    private Dictionary<string, int> __poolIndices;
    private Dictionary<string, int> __rewardIndices;
    
    private Dictionary<int, Instance> __instances;

    public static RewardManager instance
    {
        get;

        private set;
    }

    [Preserve]
    public void Apply(string poolName)
    {
        var rewardData = IRewardData.instance;
        if (rewardData == null)
            return;
        
        StartCoroutine(rewardData.ApplyReward(poolName, __OnReward));
    }

    private void __OnReward(IRewardData.Rewards rewards)
    {
        if (__poolIndices == null)
        {
            __poolIndices = new Dictionary<string, int>();

            int numPools = _pools.Length;
            for (int i = 0; i < numPools; ++i)
                __poolIndices.Add(_pools[i].name, i);
        }

        ref var pool = ref _pools[__poolIndices[rewards.poolName]];
        
        if (__rewardIndices == null)
        {
            __rewardIndices = new Dictionary<string, int>();

            int numRewards = _rewards.Length;
            for(int i = 0; i < numRewards; ++i)
                __rewardIndices[_rewards[i].name] = i;
        }

        int rewardIndex;
        RewardStyle rewardStyle;
        Instance instance;
        foreach (var rewardValue in rewards.values)
        {
            if (!__rewardIndices.TryGetValue(rewardValue.name, out rewardIndex))
                continue;

            if (__instances == null)
                __instances = new Dictionary<int, Instance>();

            if (__instances.TryGetValue(rewardIndex, out instance))
            {
                instance.count += rewardValue.count;

                foreach (var style in instance.styles)
                    style.onCount?.Invoke(instance.count.ToString());
                
                __instances[rewardIndex] = instance;
            }
            else
            {
                instance.styles = null;
                
                ref var reward = ref _rewards[rewardIndex];

                foreach (var style in pool.styles)
                {
                    rewardStyle = Instantiate(style.value, style.value.transform.parent);

                    rewardStyle.onSprite?.Invoke(reward.sprite);
                    rewardStyle.onTitle?.Invoke(reward.name);
                    rewardStyle.onCount?.Invoke(rewardValue.count.ToString());

                    rewardStyle.gameObject.SetActive(true);

                    if (rewardStyle.isDestroyOnDisable)
                        continue;

                    if(instance.styles == null)
                        instance.styles = new List<RewardStyle>();

                    instance.styles.Add(rewardStyle);
                }
                
                instance.count = rewardValue.count;

                __instances[rewardIndex] = instance;
            }
        }
    }

    void Awake()
    {
        instance = this;
    }
}
