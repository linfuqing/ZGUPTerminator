using System.Collections.Generic;

#if DEBUG

public partial class UserDataMain
{
    public void ApplyRewards(UserRewardData[] rewards, List<UserReward> outRewards)
    {
        __ApplyRewards(rewards, outRewards);
    }
}

#endif