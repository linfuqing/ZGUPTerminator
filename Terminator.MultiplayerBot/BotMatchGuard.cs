using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

/// <summary>
/// Bot-side guards against Bot-vs-Bot matchmaking. Server logic is unchanged (M3);
/// co-deploy farm enforces at most one bot in Matching, deduplicates squad claims by squad ID,
/// and rejects bot partners after pairing.
/// </summary>
[BurstCompile]
public static class BotMatchGuard
{
    public const int NoMatchingSlotOwner = -1;
    public static bool IsConfiguredBotUser(BotConfig config, uint userID) =>
        config != null && config.IsBotUserID(userID);

    public static bool IsFarmBotUser(in BotRelayFarmNative farm, uint userID)
    {
        if (userID == 0 || !farm.agentStates.IsCreated)
            return false;

        for (int i = 0; i < farm.agentCount; ++i)
        {
            if (farm.agentStates[i].userID == userID)
                return true;
        }

        return false;
    }

    /// <summary>Only one bot agent may hold the farm matching slot (enter Match pool) at a time.</summary>
    public static bool TryClaimMatchingSlot(ref BotRelayFarmNative farm, int index)
    {
        if (!farm.matchingSlotOwner.IsCreated)
            return false;

        ref int slot = ref farm.matchingSlotOwner.ElementAt(0);
        while (true)
        {
            int current = Volatile.Read(ref slot);
            if (current >= 0 && current != index)
                return false;

            if (current == index)
                return true;

            if (Interlocked.CompareExchange(ref slot, index, NoMatchingSlotOwner) == NoMatchingSlotOwner)
                return true;
        }
    }

    public static void ReleaseMatchingSlot(ref BotRelayFarmNative farm, int index)
    {
        if (!farm.matchingSlotOwner.IsCreated)
            return;

        ref int slot = ref farm.matchingSlotOwner.ElementAt(0);
        Interlocked.CompareExchange(ref slot, NoMatchingSlotOwner, index);
    }

    /// <summary>
    /// Claims one external squad for this agent. Claims are unique by squad ID, not farm-wide:
    /// one broadcast still selects exactly one Bot, while different human squads may consume
    /// different available Bots concurrently.
    /// </summary>
    public static bool TryClaimSquadSlot(ref BotRelayFarmNative farm, int index, uint squadInviteID)
    {
        if (!farm.squadSlotLock.IsCreated ||
            !farm.squadSlotSquadKeys.IsCreated ||
            index < 0 ||
            index >= farm.squadSlotSquadKeys.Length)
            return false;

        ref int gate = ref farm.squadSlotLock.ElementAt(0);
        while (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
        {
        }

        long desiredKey = (long)squadInviteID + 1L;
        long currentKey = farm.squadSlotSquadKeys[index];
        bool isClaimed = currentKey == desiredKey;
        if (currentKey == 0)
        {
            isClaimed = true;
            for (int i = 0; i < farm.squadSlotSquadKeys.Length; ++i)
            {
                if (i != index && farm.squadSlotSquadKeys[i] == desiredKey)
                {
                    isClaimed = false;
                    break;
                }
            }

            if (isClaimed)
                farm.squadSlotSquadKeys[index] = desiredKey;
        }

        Volatile.Write(ref gate, 0);
        return isClaimed;
    }

    public static void ReleaseSquadSlot(ref BotRelayFarmNative farm, int index)
    {
        if (!farm.squadSlotLock.IsCreated ||
            !farm.squadSlotSquadKeys.IsCreated ||
            index < 0 ||
            index >= farm.squadSlotSquadKeys.Length)
            return;

        ref int gate = ref farm.squadSlotLock.ElementAt(0);
        while (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
        {
        }

        farm.squadSlotSquadKeys[index] = 0;
        Volatile.Write(ref gate, 0);
    }

    public static bool HasSquadSlotClaim(in BotRelayFarmNative farm, int index, uint squadInviteID)
    {
        return farm.squadSlotSquadKeys.IsCreated &&
               index >= 0 &&
               index < farm.squadSlotSquadKeys.Length &&
               farm.squadSlotSquadKeys[index] == (long)squadInviteID + 1L;
    }

    public static bool HasAnySquadSlotClaim(in BotRelayFarmNative farm, int index)
    {
        return farm.squadSlotSquadKeys.IsCreated &&
               index >= 0 &&
               index < farm.squadSlotSquadKeys.Length &&
               farm.squadSlotSquadKeys[index] != 0;
    }
}
