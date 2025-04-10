using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Scripting;
using ZG;

[RequireComponent(typeof(TimelineManager))]
public class TimelineBinder : MonoBehaviour
{
    public enum CallbackType
    {
        Played, 
        Stopped, 
        Paused
    }
    
    [Serializable]
    public struct CallbackKey : IEquatable<CallbackKey>
    {
        [Name]
        public string timelineName;
        public CallbackType type;

        public bool Equals(CallbackKey other)
        {
            return type == other.type && timelineName == other.timelineName;
        }

        public override int GetHashCode()
        {
            return (int)type ^ (timelineName == null ? 0 : timelineName.GetHashCode());
        }
    }
    
    [Serializable]
    public struct CallbackValue
    {
        public UnityEvent value;
    }
    
    
    [Serializable]
    public class Callbacks : Map<CallbackKey, CallbackValue>
    {
        public static CallbackKey GetUniqueValue(CallbackKey key, IEnumerable<CallbackKey> keys)
        {
            key.timelineName = NameHelper.MakeUnique(key.timelineName, keys);

            return key;
        }

        public Callbacks() : base(GetUniqueValue)
        {
            
        }
    }

    
    [Serializable]
    public struct BindingStream
    {
        [Flags]
        public enum Flag
        {
            NotNeed = 0x01,
            UseGameObject = 0x02,
            IsComponentFromParent = 0x04, 
            IsInvert = 0x08, 
        }
        
        public TimelineManager.BindingStreamType type;

        public Flag flag;

        public string path;
        [Tooltip("对应timelineManager的名称，新增字段")]
        public string timelineName;
        public string streamName;
        
        /// <summary>
        /// <see cref="ZG.ComponentManager"/>
        /// </summary>
        public string componentName;

        public Transform root;
        
        public UnityEvent onBind;

        public bool TryParse(string timelineName, out TimelineManager.BindingStream result, out bool notNeed, out UnityEvent onBind)
        {
            onBind = this.onBind;
            notNeed = (flag & Flag.NotNeed) == Flag.NotNeed;

            result.type = type;
            result.isInvert = (flag & Flag.IsInvert) == Flag.IsInvert;
            result.path = path;
            result.name = streamName;
            result.root = root;

            bool isSameTimeline = this.timelineName == timelineName;
            
            if ((flag & Flag.IsComponentFromParent) == Flag.IsComponentFromParent)
            {
                result.targetParentPath = componentName;
                result.target = null;

                return isSameTimeline;
            }

            result.targetParentPath = null;
            
            var target = ZG.ComponentManager.Find(componentName);
            result.target = ((flag & Flag.UseGameObject) == Flag.UseGameObject) ? target == null ? null : target.gameObject : target;

            return result.target != null && isSameTimeline;
        }
    }

    public string timelineOnStart;

    public BindingStream[] bindingStreams;

    [Map]
    public Callbacks callbacks;

    private TimelineManager __manager;

    public TimelineManager manager
    {
        get
        {
            if (__manager == null)
                __manager = GetComponent<TimelineManager>();

            return __manager;
        }
    }

    public bool Play(string timelineName, out PlayableDirector playableDirector)
    {
        var bindingStreams = __Bind(timelineName);
        if (bindingStreams == null)
        {
            playableDirector = null;
            
            return false;
        }

        __Prepare(timelineName, out var played, out var paused, out var stopped);

        playableDirector = manager.Play(
            timelineName,
            paused,
            stopped,
            bindingStreams);

        if (playableDirector != null)
        {
            if (played != null)
                played();

            return true;
        }

        return false;
    }

    public void StopAll()
    {
        manager.StopAll();
    }

    [Preserve]
    public void ResumeRightNow(string timelineName)
    {
        __Prepare(timelineName, out var played, out var paused, out var stopped);
        
        if (manager.Resume(
                timelineName,
                paused,
                stopped))
        {
            if (played != null)
                played();
        }
    }

    [Preserve]
    public void PlayRightNow(string timelineName)
    {
        Play(timelineName, out _);
    }

    [Preserve]
    public void BindRightNow(string timelineName)
    {
        var playableDirector = manager.Find(timelineName);
        if (playableDirector == null)
            return;

        TimelineManager.Bind(playableDirector, __Bind(timelineName));
    }

    private IReadOnlyCollection<TimelineManager.BindingStream> __Bind(string timelineName)
    {
        bool notNeed;
        int numBindingStreams = this.bindingStreams.Length;
        TimelineManager.BindingStream bindingStream;
        UnityEvent onBind;
        var bindingStreams = new Dictionary<TimelineManager.BindingStream, UnityEvent>();
        for (int i = 0; i < numBindingStreams; ++i)
        {
            if (!this.bindingStreams[i].TryParse(timelineName, out bindingStream, out notNeed, out onBind))
            {
                if (!notNeed)
                    return null;

                continue;
            }

            bindingStreams.Add(bindingStream, onBind);
        }

        foreach (var value in bindingStreams.Values)
        {
            if(value != null)
                value.Invoke();
        }
        
        return bindingStreams.Keys;
    }

    private void __Prepare(string timelineName, out Action played, out Action paused, out Action stopped)
    {
        played = null;
        paused = null;
        stopped = null;
        
        CallbackValue onPlayed, onPaused, onStopped;
        if (callbacks != null)
        {
            CallbackKey key;
            key.timelineName = timelineName;
            
            key.type = CallbackType.Played;
            if(callbacks.TryGetValue(key, out onPlayed) && onPlayed.value != null)
                onPlayed.value.Invoke();

            key.type = CallbackType.Paused;
            if (callbacks.TryGetValue(key, out onPaused) && onPaused.value != null)
                paused = onPaused.value.Invoke;
            
            key.type = CallbackType.Stopped;
            if (callbacks.TryGetValue(key, out onStopped) && onStopped.value != null)
                stopped = onStopped.value.Invoke;
        }
    }

    IEnumerator Start()
    {
        if (!string.IsNullOrEmpty(timelineOnStart))
        {
            while (!Play(timelineOnStart, out _))
                yield return null;
        }
    }
}
