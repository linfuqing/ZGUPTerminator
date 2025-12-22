using System;
using System.Collections;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static Action<bool> onLoading;
    
    public static Action<Memory<UserRewardData>, Transform> onRewardInit;
    
    public static Action<Memory<UserReward>> onRewardSubmit;
    
    public const string CONSTANT_KEY_VERSION_NOTICE = "noticeVersion";
    public const string CONSTANT_KEY_VERSION_CODE = "codeVersion";
    
    public void Apply(string code)
    {
        if (onLoading != null)
            onLoading(true);
        
        string version = GameConstantManager.Get(CONSTANT_KEY_VERSION_CODE);
        StartCoroutine(IGameData.instance.ApplyCode(
            GameMain.userID,
            string.IsNullOrEmpty(version) || !uint.TryParse(version, out uint versionValue) ? 0 : versionValue,
            code,
            __OnApplyCode));
    }

    private void __OnApplyCode(Memory<UserReward> rewards)
    {
        if (onLoading != null)
            onLoading(false);

        if(onRewardSubmit != null)
            onRewardSubmit(rewards);
    }

    private void __OnQueryNotice(Memory<UserRewardData> rewards)
    {
        
    }

    private IEnumerator __QueryNotice()
    {
        if(IGameData.instance == null)
            yield break;
        
        /*string version = GameConstantManager.Get(CONSTANT_KEY_VERSION_CODE);
        IGameData.instance.QueryNotice(
            GameMain.userID, 
            string.IsNullOrEmpty(version) || !uint.TryParse(version, out uint versionValue) ? 0 : versionValue, 
            GameLanguage.overrideLanguage, )*/
    }
}
