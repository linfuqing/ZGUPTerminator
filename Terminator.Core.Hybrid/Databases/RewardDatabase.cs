using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

[CreateAssetMenu(fileName = "RewardDatabase", menuName = "Game/Reward Database")]
public class RewardDatabase : ScriptableObject
{
    [Serializable]
    internal struct Reward
    {
        public string name;

        public string title;

        public Sprite sprite;

        public int rank;

        #if UNITY_EDITOR
        [CSVField]
        public string 关卡奖励名称
        {
            set => name = value;
        }
        
        [CSVField]
        public string 关卡奖励标题
        {
            set => title = value;
        }
        
        [CSVField]
        public string 关卡奖励图标
        {
            set => sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(value);
        }
        
        [CSVField]
        public int 关卡奖励稀有度
        {
            set => rank = value;
        }
        #endif
    }

    [SerializeField]
    internal Reward[] _rewards;

    [SerializeField, CSV("_rewards", guidIndex = -1, nameIndex = 0)]
    internal string _rewardsPath;
}
