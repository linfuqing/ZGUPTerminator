using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public partial class LevelManager
{
    [Serializable]
    internal struct Stage
    {
        public string name;
        public string[] sharedNames;

        public int max;

        public StringEvent onCount;
        
        public UnityEvent onEnable;
        public UnityEvent onDisable;
    }

    [SerializeField] 
    internal Stage[] _stages;
    
    private HashSet<int> __stages;

    private Dictionary<string, int> __stageIndices;

    public bool EnableStage(string name)
    {
        IAnalytics.instance?.EnableStage(name);
        
        if (__stageIndices == null)
        {
            __stageIndices = new Dictionary<string, int>();
            int numEvents = _stages == null ? 0 : _stages.Length;
            for (int i = 0; i < numEvents; ++i)
            {
                ref var stage = ref _stages[i];
                if(stage.sharedNames == null || stage.sharedNames.Length < 1)
                    __stageIndices.Add(stage.name, i);
                else
                {
                    foreach (string sharedName in stage.sharedNames)
                        __stageIndices.Add(sharedName, i);
                }
            }
        }

        if (__stageIndices.TryGetValue(name, out int stageIndex))
        {
            if (__stages == null)
                __stages = new HashSet<int>();

            if (__stages.Add(stageIndex))
            {
                ref var stage = ref _stages[stageIndex];
                if (stage.onEnable != null)
                    stage.onEnable.Invoke();
                
                if (stage.max > 0 && stage.onCount != null)
                    stage.onCount.Invoke(Mathf.Max(0, stage.max - __value).ToString());

                return true;
            }
        }

        return false;
    }

    public bool DisableStage(string name)
    {
        IAnalytics.instance?.DisableStage(name);

        if (__stageIndices != null && 
            __stageIndices.TryGetValue(name, out int stageIndex) && 
            __stages != null && 
            __stages.Remove(stageIndex))
        {
            var onDisable = _stages[stageIndex].onDisable;
            if(onDisable != null)
                onDisable.Invoke();

            return true;
        }

        return false;
    }
}
