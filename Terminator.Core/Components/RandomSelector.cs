using Unity.Mathematics;
using ZG;

public struct RandomWrapper : IRandomWrapper<Random>
{
    public float NextFloat(ref Random random) => random.NextFloat();
}

public struct RandomSelector
{
    private RandomSelector<Random, RandomWrapper> __instance;

    public RandomSelector(ref Random random)
    {
        __instance = new RandomSelector<Random, RandomWrapper>(ref random);
    }

    public bool Select(ref Random random, float chance) => __instance.Select(ref random, chance);
}
