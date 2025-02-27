using UnityEngine;
using UnityEngine.Scripting;

public class RewardController : MonoBehaviour
{
    [Preserve]
    public void Apply(string poolName)
    {
        RewardManager.instance.Apply(poolName);
    }
}
