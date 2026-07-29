using System.Collections.Generic;

internal static class LevelRecordingReplayState
{
    private static readonly List<int> s_simulatedActiveSkillValues = new List<int>(8);

    public static IReadOnlyList<int> SimulatedActiveSkillValues => s_simulatedActiveSkillValues;

    public static List<int> MutableSimulatedActiveSkillValues => s_simulatedActiveSkillValues;

    public static void Reset()
    {
        s_simulatedActiveSkillValues.Clear();
    }
}
