using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public partial class UserDataMain
{
    public static UserFriend friend
    {
        get
        {
            UserFriend result;
            result.id = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            result.name = "客户端测试";
            result.avatar = string.Empty;
            result.chapter = UserData.chapter;
            result.power = UnityEngine.Random.Range(10000, 20000);
            result.ticks = DateTime.UtcNow.Ticks;

            return result;
        }
    }
    
    public IEnumerator QueryFriend(uint userID, uint targetUserID, Action<IUserData.Friend> onComplete)
    {
        yield return __CreateEnumerator();

        //客户端没有这种信息，取自己的做展示
        IUserData.Friend result;

        result.power = UnityEngine.Random.Range(10000, 20000);
        result.rankedPoints = rankedPoints;
        
        string groupName = PlayerPrefs.GetString(NAME_SPACE_USER_ROLE_GROUP);
        uint groupID = __ToID(string.IsNullOrEmpty(groupName) ? 0 : __GetRoleGroupIndex(groupName));
        
        result.role = default;
        
        int i, numRoles = _roles.Length;
        UserRole userRole;
        List<uint> userRoleGroupIDs = null;
        List<string> skillNames = null;
        for (i = 0; i < numRoles; ++i)
        {
            if(!__ToUserRole(groupName, i, out userRole, ref userRoleGroupIDs, ref skillNames) || 
               userRole.groupIDs == null || Array.IndexOf(userRole.groupIDs, groupID) == -1)
                continue;

            result.role = userRole;

            break;
        }

        Card card;
        UserCard userCard;
        UserCard.Group userCardGroup;
        var userCards = new List<UserCard>();
        var userCardGroups = new List<UserCard.Group>();
        int j, numCards = _cards.Length, numCardGroups = _cardGroups.Length;
        for (i = 0; i < numCards; ++i)
        {
            card = _cards[i];

            userCard.level = __GetCardLevel(card.name, out _);
            if (userCard.level == -1)
                continue;
            
            userCardGroups.Clear();
            for (j = 0; j < numCardGroups; ++j)
            {
                userCardGroup.position =
                    PlayerPrefs.GetInt(
                        $"{NAME_SPACE_USER_CARD_GROUP}{_cardGroups[j].name}{UserData.SEPARATOR}{card.name}",
                        -1);
                
                if(userCardGroup.position == -1)
                    continue;

                userCardGroup.groupID = __ToID(j);
                
                userCardGroups.Add(userCardGroup);
            }
            
            if(userCardGroups.Count < 1)
                continue;
            
            userCard.groups = userCardGroups.ToArray();

            userCard.name = card.name;
            userCard.skillNames = __GetSkillGroupSkillNames(__GetSkillGroupName(card.skillName)).ToArray();
            
            userCard.id = __ToID(i);
            userCard.styleID = __ToID(__GetCardStyleIndex(card.styleName));
            
            userCard.count = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARD_COUNT}{card.name}");

            userCard.skillGroupDamage = card.skillGroupDamage;
            
            userCards.Add(userCard);
        }
        
        result.cards = userCards.ToArray();

        int numCardBondLevels, numCardBondCards, numCardBonds = _cardBonds.Length;
        CardBond cardBond;
        UserCardBond userCardBond;
        UserCardBond.Card userCardBondCard;
        List<int> cardBondLevelIndices;
        result.cardBonds = new UserCardBond[numCardBonds];
        for (i = 0; i < numCardBonds; ++i)
        {
            cardBond = _cardBonds[i];
            userCardBond.name = cardBond.name;
            userCardBond.id = __ToID(i);
            userCardBond.level = PlayerPrefs.GetInt($"{NAME_SPACE_USER_CARDS_BONDS_LEVEL}{cardBond.name}");
            
            cardBondLevelIndices = __GetCardBondLevelIndices(userCardBond.name);
            numCardBondLevels = cardBondLevelIndices.Count;
            userCardBond.levels = new UserCardBond.Level[numCardBondLevels];
            for (j = 0; j < numCardBondLevels; ++j)
                userCardBond.levels[j] = _cardBondLevels[cardBondLevelIndices[j]].value;

            numCardBondCards = cardBond.cardNames.Length;
            userCardBond.cards = new UserCardBond.Card[numCardBondCards];
            for (j = 0; j < numCardBondCards; ++j)
            {
                userCardBondCard.name = cardBond.cardNames[j];
                
                userCardBondCard.level = __GetCardLevel(userCardBondCard.name, out _);
                
                userCardBond.cards[j] = userCardBondCard;
            }
            
            result.cardBonds[i] = userCardBond;
        }
        
        int k, l,  
            numAccessoryStages, 
            numAccessorySlots = _accessorySlots.Length, 
            numAccessories = _accessories.Length, 
            numRoleGroups = _roleGroups.Length;
        string userAccessoryGroupKey, skillGroupName, key;
        string[] ids;
        AccessoryStage accessoryStage;
        UserAccessory.Group userAccessoryGroup;
        UserAccessory userAccessory;
        Accessory accessory;
        List<int> accessoryStageIndices;
        var userAccessories = new List<UserAccessory>();
        var userAccessoryGroups = new List<UserAccessory.Group>();
        for (i = 0; i < numAccessories; ++i)
        {
            accessory = _accessories[i];

            userAccessory.name = accessory.name;

            if (string.IsNullOrEmpty(accessory.skillName))
                userAccessory.skillNames = null;
            else
            {
                skillGroupName = __GetSkillGroupName(accessory.skillName);
                if (string.IsNullOrEmpty(skillGroupName))
                {
                    userAccessory.skillNames = new string[1];
                    userAccessory.skillNames[0] = accessory.skillName;
                }
                else
                    userAccessory.skillNames = __GetSkillGroupSkillNames(skillGroupName).ToArray();
            }

            userAccessory.styleID = __ToID(__GetAccessoryStyleIndex(accessory.styleName));

            userAccessory.attributeValue = accessory.attributeValue;

            userAccessory.skillDamage = accessory.skillDamage;

            userAccessory.roleSkillGroupDamage = accessory.roleSkillGroupDamage;

            accessoryStageIndices = __GetAccessoryStageIndices(i);
            numAccessoryStages = accessoryStageIndices.Count;
            for (j = 0; j <= numAccessoryStages; ++j)
            {
                key =
                    $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessory.name}{UserData.SEPARATOR}{j}";
                key = PlayerPrefs.GetString(key);
                ids = string.IsNullOrEmpty(key) ? null : key.Split(UserData.SEPARATOR);
                if (ids == null || ids.Length < 1)
                    continue;

                userAccessory.stage = j;

                if (j > 0)
                    userAccessory.property = _accessoryStages[accessoryStageIndices[j - 1]].property;
                else
                    userAccessory.property = accessory.property;

                accessoryStage = j < numAccessoryStages ? _accessoryStages[accessoryStageIndices[j]] : default;

                userAccessory.stageDesc.name = accessoryStage.name;
                //userAccessory.stageDesc.count = accessoryStage.count;
                userAccessory.stageDesc.property = accessoryStage.property;
                userAccessory.stageDesc.materials = accessoryStage.materials;
                
                foreach (var id in ids)
                {
                    userAccessory.id = uint.Parse(id);
                    
                    userAccessoryGroups.Clear();
                    for (k = 0; k < numRoleGroups; ++k)
                    {
                        userAccessoryGroup.groupID = __ToID(k);
                        if(userAccessoryGroup.groupID != groupID)
                            continue;
                        
                        userAccessoryGroupKey =
                            $"{NAME_SPACE_USER_ROLE_GROUP}{_roleGroups[k].name}{UserData.SEPARATOR}";
                        for (l = 0; l < numAccessorySlots; ++l)
                        {
                            if ((uint)PlayerPrefs.GetInt(
                                    $"{userAccessoryGroupKey}{_accessorySlots[l].name}") ==
                                userAccessory.id)
                                break;
                        }

                        if (l == numAccessorySlots)
                            continue;

                        userAccessoryGroup.slotID = __ToID(l);
                        userAccessoryGroups.Add(userAccessoryGroup);
                    }

                    userAccessory.groups = userAccessoryGroups.ToArray();
                    userAccessories.Add(userAccessory);
                }
            }
        }
        
        result.accessories = userAccessories.ToArray();
        
        result.accessorySlots = new UserAccessorySlot[numAccessorySlots];

        AccessorySlot accessorySlot;
        UserAccessorySlot userAccessorySlot;
        for (i = 0; i < numAccessorySlots; ++i)
        {
            accessorySlot = _accessorySlots[i];
            userAccessorySlot.name = accessorySlot.name;
            userAccessorySlot.id = __ToID(i);
            userAccessorySlot.level =
                PlayerPrefs.GetInt($"{NAME_SPACE_USER_ACCESSORY_SLOT_LEVEL}{accessorySlot.name}");

            userAccessorySlot.styleID = __ToID(__GetAccessoryStyleIndex(accessorySlot.styleName));
            
            result.accessorySlots[i] = userAccessorySlot;
        }
        
        int numTalents = _talents.Length;
        Talent talent;
        UserTalent userTalent;
        result.talents = new UserTalent[numTalents];
        for (i = 0; i < numTalents; ++i)
        {
            talent = _talents[i];
            //if (!string.IsNullOrEmpty(talent.roleName))
            //    continue;

            userTalent.name = talent.name;
            userTalent.id = __ToID(i);
            userTalent.flag = (UserTalent.Flag)PlayerPrefs.GetInt($"{NAME_SPACE_USER_TALENT_FLAG}{talent.name}");
            userTalent.gold = talent.gold;
            userTalent.exp = talent.exp;
            userTalent.skillGroupDamage = talent.skillGroupDamage;
            userTalent.attribute = talent.attribute;
            result.talents[i] = userTalent;
        }

        onComplete(result);
    }
    
    private const string NAME_SPACE_USER_FRIENDS = "UserFriends";

    public IEnumerator QueryFriends(uint userID, Action<Memory<UserFriend>> onComplete)
    {
        yield return __CreateEnumerator();
        
        var friends = PlayerPrefs.GetString(NAME_SPACE_USER_FRIENDS);
        if (string.IsNullOrEmpty(friends))
        {
            onComplete(Array.Empty<UserFriend>());
            
            yield break;
        }
        
        var parameters = friends.Split(UserData.SEPARATOR);
        int numFriends = parameters.Length;
        var results = new UserFriend[numFriends];
        for (int i = 0; i < numFriends; ++i)
            results[i] = new UserFriend(parameters[i], out _);
        
        onComplete(results);
    }

    public IEnumerator QueryFriendsToSquadInvite(
        uint userID, 
        uint levelID, 
        int stage, 
        int [] targetUserIDs, 
        Action<Memory<UserFriend>> onComplete)
    {
        yield return __CreateEnumerator();

        int count = UnityEngine.Random.Range(5, 10);
        
        var friends = new UserFriend[count];
        for (int i = 0; i < count; ++i)
            friends[i] = friend;
        
        onComplete(friends);
    }

    public IEnumerator QueryFriendRecommendations(uint userID, string filter, Action<Memory<UserFriend>> onComplete)
    {
        yield return __CreateEnumerator();

        int count = UnityEngine.Random.Range(5, 10);
        
        var friends = new UserFriend[count];
        for (int i = 0; i < count; ++i)
            friends[i] = friend;
        
        onComplete(friends);
    }
    
    private const string NAME_SPACE_USER_FRIEND_MESSAGE = "UserFriendMessage";
    
    public IEnumerator QueryFriendMessages(uint userID, uint targetUserID, Action<Memory<IUserData.FriendMessage>> onComplete)
    {
        yield return __CreateEnumerator();

        var friendMessages = PlayerPrefs.GetString($"{NAME_SPACE_USER_FRIEND_MESSAGE}{targetUserID}");
        if (string.IsNullOrEmpty(friendMessages))
        {
            onComplete(Array.Empty<IUserData.FriendMessage>());
            
            yield break;
        }

        var values = friendMessages.Split(UserData.SEPARATOR);
        int numValues = values.Length;
        var results = new IUserData.FriendMessage[numValues];
        for(int i = 0; i < numValues; ++i)
            results[i] = new IUserData.FriendMessage(values[i]);
        
        onComplete(results);
    }

    public IEnumerator QueryFriendRequests(uint userID, Action<Memory<IUserData.FriendRequest>> onComplete)
    {
        yield return __CreateEnumerator();
        
        IUserData.FriendRequest friendRequest;
        friendRequest.friend = friend;
        friendRequest.description = "客户端无数据，服务器自己实现";
        
        var results = new IUserData.FriendRequest[1];
        results[0] = friendRequest;
        
        onComplete(results);
    }
    
    public IEnumerator FriendRequestApply(uint userID, uint targetUserID, string description, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();
        
        //客户端没有这些数据，服务器自己实现，注意特殊符号的筛选
        onComplete(true);
    }

    public IEnumerator FriendRequestAgree(uint userID, uint[] targetUserIDs, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        string friends = PlayerPrefs.GetString(NAME_SPACE_USER_FRIENDS);
        var friend = UserDataMain.friend;
        foreach (var targetUserID in targetUserIDs)
        {
            friend.id = targetUserID;
            friends = string.IsNullOrEmpty(friends) ? friend.ToString() :  $"{friends}{UserData.SEPARATOR}{friend}";
        }

        PlayerPrefs.SetString(NAME_SPACE_USER_FRIENDS, friends);
        onComplete(true);
    }
    
    public IEnumerator FriendRequestDisagree(uint userID, uint[] targetUserIDs, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        //客户端没有这些数据，服务器自己实现
        onComplete(true);
    }
    
    public IEnumerator FriendMessageSend(uint userID, uint targetUserID, uint senderUserID, string value, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        string key = $"{NAME_SPACE_USER_FRIEND_MESSAGE}{targetUserID}";
        var friendMessages = PlayerPrefs.GetString(key);

        IUserData.FriendMessage message;
        message.userID = senderUserID;
        message.value = value;
        PlayerPrefs.SetString(key, string.IsNullOrEmpty(friendMessages) ? message.ToString() :  $"{friendMessages}{UserData.SEPARATOR}{message}");

        onComplete(true);
    }

    public IEnumerator FriendDelete(uint userID, uint targetUserID, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();
        
        var friends = PlayerPrefs.GetString(NAME_SPACE_USER_FRIENDS);
        if (string.IsNullOrEmpty(friends))
        {
            onComplete(false);
            
            yield break;
        }
        
        var parameters = friends.Split(UserData.SEPARATOR);
        int numFriends = parameters.Length;
        var results = new UserFriend[numFriends];
        for (int i = 0; i < numFriends; ++i)
            results[i] = new UserFriend(parameters[i], out _);

        var stringBuilder = new StringBuilder();
        UserFriend result;
        int numResults = results.Length;
        for(int i = 0; i < numResults; ++i)
        {
            result = results[i];
            if (result.id == targetUserID)
                continue;

            stringBuilder.Append(i > 0 ? $"{UserData.SEPARATOR}{result}" : result.ToString());
        }

        PlayerPrefs.SetString(NAME_SPACE_USER_FRIENDS, stringBuilder.ToString());
    }
    
    public IEnumerator UpdatePowerForFriends(uint userID, int power, Action<bool> onComplete)
    {
        yield return __CreateEnumerator();

        onComplete(true);
    }

}

public partial class UserData
{
    public IEnumerator QueryFriend(uint userID, uint targetUserID, Action<IUserData.Friend> onComplete)
    {
        return UserDataMain.instance.QueryFriend(userID, targetUserID, onComplete);
    }
    
    public IEnumerator QueryFriends(uint userID, Action<Memory<UserFriend>> onComplete)
    {
        return UserDataMain.instance.QueryFriends(userID, onComplete);
    }

    public IEnumerator QueryFriendsToSquadInvite(uint userID, uint levelID, int stage, 
        int[] targetUserIDs, Action<Memory<UserFriend>> onComplete)
    {
        return UserDataMain.instance.QueryFriendsToSquadInvite(userID, levelID, stage, targetUserIDs, onComplete);
    }
    
    public IEnumerator QueryFriendRecommendations(uint userID, string filter, Action<Memory<UserFriend>> onComplete)
    {
        return UserDataMain.instance.QueryFriendRecommendations(userID, filter, onComplete);
    }
    
    public IEnumerator QueryFriendMessages(uint userID, uint targetUserID, Action<Memory<IUserData.FriendMessage>> onComplete)
    {
        return UserDataMain.instance.QueryFriendMessages(userID, targetUserID, onComplete);
    }
    
    public IEnumerator QueryFriendRequests(uint userID, Action<Memory<IUserData.FriendRequest>> onComplete)
    {
        return UserDataMain.instance.QueryFriendRequests(userID, onComplete);
    }
    
    public IEnumerator FriendRequestApply(uint userID, uint targetUserID, string description, Action<bool> onComplete)
    {
        return UserDataMain.instance.FriendRequestApply(userID, targetUserID, description, onComplete);
    }
    
    public IEnumerator FriendRequestAgree(uint userID, uint[] targetUserIDs, Action<bool> onComplete)
    {
        return UserDataMain.instance.FriendRequestAgree(userID, targetUserIDs, onComplete);
    }
    
    public IEnumerator FriendRequestDisagree(uint userID, uint[] targetUserIDs, Action<bool> onComplete)
    {
        return UserDataMain.instance.FriendRequestDisagree(userID, targetUserIDs, onComplete);
    }
    
    public IEnumerator FriendMessageSend(uint userID, uint targetUserID, uint senderUserID, string value, Action<bool> onComplete)
    {
        return UserDataMain.instance.FriendMessageSend(userID, targetUserID, senderUserID, value, onComplete);
    }
    
    public IEnumerator FriendDelete(uint userID, uint targetUserID, Action<bool> onComplete)
    {
        return UserDataMain.instance.FriendDelete(userID, targetUserID, onComplete);
    }
    
    public IEnumerator UpdatePowerForFriends(uint userID, int power, Action<bool> onComplete)
    {
        return UserDataMain.instance.UpdatePowerForFriends(userID, power, onComplete);
    }
}
