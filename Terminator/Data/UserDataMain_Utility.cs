using System.Collections.Generic;

public partial class UserDataMain
{
    private Dictionary<string, int> __itemNameToIndices;

    private int __GetItemIndex(string name)
    {
        if (__itemNameToIndices == null)
        {
            int numItems = _items.Length;
            __itemNameToIndices = new Dictionary<string, int>(numItems);
            for (int i = 0; i < numItems; ++i)
                __itemNameToIndices.Add(_items[i].name, i);
        }

        return __itemNameToIndices[name];
    }

    private Dictionary<string, int> __accessoryNameToIndices;

    private int __GetAccessoryIndex(string name)
    {
        if (__accessoryNameToIndices == null)
        {
            int numAccessories = _accessories.Length;
            __accessoryNameToIndices = new Dictionary<string, int>(numAccessories);
            for (int i = 0; i < numAccessories; ++i)
                __accessoryNameToIndices.Add(_accessories[i].name, i);
        }

        return __accessoryNameToIndices[name];
    }
    
    private Dictionary<string, int> __accessoryStyleNameToIndices;
    
    private int __GetAccessoryStyleIndex(string name)
    {
        if (__accessoryStyleNameToIndices == null)
        {
            int numAccessoryStyles = _accessoryStyles.Length;
            __accessoryStyleNameToIndices = new Dictionary<string, int>(numAccessoryStyles);
            for (int i = 0; i < numAccessoryStyles; ++i)
                __accessoryStyleNameToIndices.Add(_accessoryStyles[i].name, i);
        }

        return __accessoryStyleNameToIndices[name];
    }
    
    private List<int>[] __accessoryStageIndices;

    private List<int> __GetAccessoryStageIndices(int index)
    {
        if (__accessoryStageIndices == null)
        {
            int numAccessories = _accessories.Length;
            
            __accessoryStageIndices = new List<int>[numAccessories];

            List<int> accessoryStageIndices;
            int accessoryIndex, numAccessoryStages = _accessoryStages.Length;
            for (int i = 0; i < numAccessoryStages; ++i)
            {
                accessoryIndex = __GetAccessoryIndex(_accessoryStages[i].accessoryName);
                accessoryStageIndices = __accessoryStageIndices[accessoryIndex];
                if (accessoryStageIndices == null)
                {
                    accessoryStageIndices = new List<int>();

                    __accessoryStageIndices[accessoryIndex] = accessoryStageIndices;
                }
                
                accessoryStageIndices.Add(i);
            }
        }
        
        return __accessoryStageIndices[index];
    }

    
    private List<int>[] __accessoryStyleLevelIndices;
    
    private List<int> __GetAccessoryStyleLevelIndices(int styleIndex)
    {
        if (__accessoryStyleLevelIndices == null)
        {
            int numAccessoryStyles = _accessoryStyles.Length;
            __accessoryStyleLevelIndices = new List<int>[numAccessoryStyles];
            
            int numAccessoryLevels = _accessoryLevels.Length, accessoryStyleIndex;
            List<int> accessoryLevelIndices;
            for (int i = 0; i < numAccessoryLevels; ++i)
            {
                accessoryStyleIndex = __GetAccessoryStyleIndex(_accessoryLevels[i].styleName);
                
                accessoryLevelIndices = __accessoryStyleLevelIndices[accessoryStyleIndex];
                if (accessoryLevelIndices == null)
                {
                    accessoryLevelIndices = new List<int>();
                    
                    __accessoryStyleLevelIndices[accessoryStyleIndex] = accessoryLevelIndices;
                }
                
                accessoryLevelIndices.Add(i);
            }
        }

        return __accessoryStyleLevelIndices[styleIndex];
    }
    
    private struct AccessoryInfo
    {
        public int index;
        public int stage;
    }
    
    private Dictionary<uint, AccessoryInfo> __accessoryIDToInfos;

    private bool __TryGetAccessory(uint id, out AccessoryInfo info)
    {
        if (__accessoryIDToInfos == null)
        {
            __accessoryIDToInfos = new Dictionary<uint, AccessoryInfo>();

            int i,
                j,
                numAccessoryStages,
                numAccessories = _accessories.Length;
            AccessoryInfo accessoryInfo;
            string name, key;
            string[] ids;
            List<int> accessoryStageIndices;
            for (i = 0; i < numAccessories; ++i)
            {
                name = _accessories[i].name;
                
                accessoryInfo.index = i;

                accessoryStageIndices = __GetAccessoryStageIndices(i);
                numAccessoryStages = accessoryStageIndices.Count;
                for (j = 0; j < numAccessoryStages; ++j)
                {
                    accessoryInfo.stage = j;
                    
                    key =
                        $"{NAME_SPACE_USER_ACCESSORY_IDS}{name}{UserData.SEPARATOR}{_accessoryStages[accessoryStageIndices[j]].name}";
                    key = PlayerPrefs.GetString(key);
                    ids = string.IsNullOrEmpty(key) ? null : key.Split(UserData.SEPARATOR);
                    if (ids == null || ids.Length < 1)
                        continue;

                    foreach (var idString in ids)
                        __accessoryIDToInfos.Add(uint.Parse(idString), accessoryInfo);
                }
            }
        }

        return __accessoryIDToInfos.TryGetValue(id, out info);
    }

    private bool __DeleteAccessory(uint id)
    {
        if(!__TryGetAccessory(id, out AccessoryInfo info))
            return false;

        int accessoryStageIndex = __GetAccessoryStageIndices(info.index)[info.stage];
        string accessoryStageName = _accessoryStages[accessoryStageIndex].name, 
            accessoryName = _accessories[info.index].name, 
            key = $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessoryName}{UserData.SEPARATOR}{accessoryStageName}", 
            idsString = PlayerPrefs.GetString(key);

        if (string.IsNullOrEmpty(idsString))
            return false;
        
        var ids = new HashSet<string>(idsString.Split(UserData.SEPARATOR));
        if (!ids.Remove(id.ToString()))
            return false;

        int numIDs = ids.Count;
        if (numIDs > 0)
        {
            idsString = string.Join(UserData.SEPARATOR, ids);
            PlayerPrefs.SetString(key, idsString);
        }
        else
            PlayerPrefs.DeleteKey(key);

        return true;
    }

    private void __CreateAccessory(uint id, int index, int stage)
    {
        int accessoryStageIndex = __GetAccessoryStageIndices(index)[stage];
        string accessoryStageName = _accessoryStages[accessoryStageIndex].name, 
            accessoryName = _accessories[index].name, 
            key = $"{NAME_SPACE_USER_ACCESSORY_IDS}{accessoryName}{UserData.SEPARATOR}{accessoryStageName}", 
            idsString = PlayerPrefs.GetString(key);
        
        idsString = string.IsNullOrEmpty(idsString) ? id.ToString() : $"{idsString}{UserData.SEPARATOR}{id}";
        PlayerPrefs.SetString(key, idsString);
    }

}
