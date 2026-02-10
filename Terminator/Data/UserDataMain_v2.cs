using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZG;

public partial class UserDataMain
{
    [Serializable]
    internal struct Talent
    {
        public string name;
        public string roleName;
        public int roleRank;
        public int roleCount;
        public int gold;
        public int exp;
        public float skillGroupDamage;
        public UserAttributeData attribute;
        
#if UNITY_EDITOR
        [CSVField]
        public string 能力名字
        {
            set
            {
                name = value;
            }
        }
        
        [CSVField]
        public string 能力角色名
        {
            set
            {
                roleName = value;
            }
        }
        
        [CSVField]
        public int 能力角色星级
        {
            set
            {
                roleRank = value;
            }
        }

        [CSVField]
        public int 能力角色碎片
        {
            set
            {
                roleCount = value;
            }
        }
        
        [CSVField]
        public int 能力解锁消耗
        {
            set
            {
                gold = value;
            }
        }
        
        [CSVField]
        public int 能力解锁消耗经验
        {
            set
            {
                exp = value;
            }
        }

        [CSVField]
        public float 能力技能组伤害加成
        {
            set
            {
                skillGroupDamage = value;
            }
        }
        
        [CSVField]
        public int 能力属性类型
        {
            set
            {
                attribute.type = (UserAttributeType)value;
            }
        }
        
        [CSVField]
        public float 能力属性值
        {
            set
            {
                attribute.value = value;
            }
        }
#endif
    }

    private const string NAME_SPACE_USER_TALENT_FLAG = "UserTalentFlag";

    [Header("Talents")]
    [SerializeField]
    internal Talent[] _talents;

#if UNITY_EDITOR
    [SerializeField, CSV("_talents", guidIndex = -1, nameIndex = 0)] 
    internal string _talentsPath;
#endif

    public IEnumerator QueryTalents(
        uint userID,
        Action<IUserData.Talents> onComplete)
    {
        yield return __CreateEnumerator();

        IUserData.Talents result;
        result.exp = exp;
        result.flag = 0;
        if ((flag & Flag.TalentsUnlock) != 0)
        {
            if ((flag & Flag.UnlockFirst) == Flag.TalentsUnlockFirst)
                result.flag |= IUserData.Talents.Flag.UnlockFirst;
            else if((flag & Flag.TalentsUnlockFirst) == 0)
                result.flag |= IUserData.Talents.Flag.Unlock;
        }

        if (result.flag == 0)
            result.talents = null;
        else
        {
            int numTalents = _talents.Length;
            Talent talent;
            UserTalent userTalent;
            var userTalents = new UserTalent[numTalents];
            for (int i = 0; i < numTalents; ++i)
            {
                talent = _talents[i];
                if (!string.IsNullOrEmpty(talent.roleName))
                    continue;

                userTalent.name = talent.name;
                userTalent.id = __ToID(i);
                userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}");
                userTalent.gold = talent.gold;
                userTalent.exp = talent.exp;
                userTalent.skillGroupDamage = talent.skillGroupDamage;
                userTalent.attribute = talent.attribute;
                userTalents[i] = userTalent;
            }

            result.talents = userTalents;
        }

        onComplete(result);
    }

    public IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        var talent = _talents[__ToIndex(talentID)];
        string key = $"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}";
        var flag = (UserTalent.Flag)PlayerPrefs.GetInt(key);
        if ((flag & UserTalent.Flag.Collected) == UserTalent.Flag.Collected)
        {
            onComplete(false);
            
            yield break;
        }

        int gold = UserDataMain.gold, exp = UserDataMain.exp;
        
        if (talent.gold > gold || talent.exp > exp)
        {
            onComplete(false);
            
            yield break;
        }

        UserDataMain.gold = gold - talent.gold;
        UserDataMain.exp = exp - talent.exp;

        flag |= UserTalent.Flag.Collected;
        PlayerPrefs.SetInt(key, (int)flag);
        
        UserDataMain.flag &= ~Flag.TalentsUnlockFirst;
        
        __AppendQuest(UserQuest.Type.Talents, 1);

        onComplete(true);
    }
}

public partial class UserData
{
    public IEnumerator QueryTalents(
        uint userID,
        Action<IUserData.Talents> onComplete)
    {
        return UserDataMain.instance.QueryTalents(userID, onComplete);
    }

    public IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete)
    {
        return UserDataMain.instance.CollectTalent(userID, talentID, onComplete);
    }
}