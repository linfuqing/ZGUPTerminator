using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public partial interface IUserData
{
    public struct Talents
    {
        [Flags]
        public enum Flag
        {
            Unlock = 0x01, 
            UnlockFirst = 0x02 | Unlock
        }

        public Flag flag;

        public int exp;

        public UserTalent[] talents;
    }
    
    IEnumerator QueryTalents(
        uint userID, 
        Action<Talents> onComplete);

    IEnumerator CollectTalent(
        uint userID,
        uint talentID,
        Action<bool> onComplete);
}
