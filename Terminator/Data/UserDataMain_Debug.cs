using System.Collections;
using System.Collections.Generic;

#if DEBUG

public partial class UserDataMain
{
    public IEnumerator ApplyRewards(UserRewardData[] rewards, List<UserReward> outRewards)
    {
        yield return null;
        
        __ApplyRewards(rewards, outRewards);
    }
}

#endif