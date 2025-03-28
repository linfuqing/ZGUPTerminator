using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Profiling;

[RequireComponent(typeof(Renderer))]
public class AnimatedSkinController : MonoBehaviour
{
    private struct Transition
    {
        public bool isLoop;
        public float offsetSeconds;
        public string animationName;
    }
    
    private class Playable
    {
        private int __transitionIndex;
        private int __animationIndex;
        private float __time;
        private List<Transition> __transitions;

        public Playable(AnimatedSkinTransition input)
        {
            __transitionIndex = 0;
            __animationIndex = -1;
            __time = 0.0f;
            __transitions = new List<Transition>();

            Transition output;
            while (input != null)
            {
                output.isLoop = input.isLoop;
                output.offsetSeconds = input.offsetSeconds;
                output.animationName = input.animationName;
                __transitions.Add(output);
                
                input = input.next;
            }
        }

        public void Reset(AnimatedSkinTransition input)
        {
            __transitionIndex = 0;
            __animationIndex = -1;
            __time = 0.0f;
            
            __transitions.Clear();
            Transition output;
            while (input != null)
            {
                output.isLoop = input.isLoop;
                output.offsetSeconds = input.offsetSeconds;
                output.animationName = input.animationName;
                __transitions.Add(output);
                
                input = input.next;
            }
        }

        public bool Update(AnimatedSkinController controller)
        {
            if (__transitionIndex < 0 || __transitionIndex >= __transitions.Count)
                return true;
            
            Profiler.BeginSample(controller.name);
            var transition = __transitions[__transitionIndex];

            bool result = false;
            if (__time > 0.0f)
            {
                __time -= Time.deltaTime;
                if (__time < 0.0f)
                {
                    controller.__Invoke(transition.animationName, EventType.End);

                    if (++__transitionIndex == __transitions.Count)
                    {
                        if (!transition.isLoop)
                        {
                            var animation = controller._database.animations[__animationIndex];
                            animation.startFrame += animation.frameCount - 1;
                            animation.frameCount = 1;
                            controller.__materialPropertyBlock.Play(animation, -Time.time);
                            controller.__renderer.SetPropertyBlock(controller.__materialPropertyBlock);
                        }

                        //controller.__coroutine = null;

                        result = true;
                    }
                    else
                    {
                        __time = 0.0f;

                        __animationIndex = -1;
                    }
                }
            }
            else if(__animationIndex == -1)
            {
                __animationIndex = controller.Play(transition.animationName, transition.offsetSeconds);
                if (__animationIndex != -1)
                {
                    var animation = controller._database.animations[__animationIndex];
                    __time = animation.frameCount * 1.0f /
                        controller._frameCountPerSecond - transition.offsetSeconds;
                }
            }
            Profiler.EndSample();
            
            return result;
        }
    }

    private sealed class Manager : MonoBehaviour
    {
        private Dictionary<AnimatedSkinController, Playable> __playables;
        private List<Playable> __pool;
        private List<AnimatedSkinController> __controllersToRemove;

        private static Manager __instance;

        public static Manager instance
        {
            get
            {
                if (__instance == null)
                {
                    var gameObject = new GameObject();
                    gameObject.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
                    __instance = gameObject.AddComponent<Manager>();
                }

                return __instance;
            }
        }

        public void Play(AnimatedSkinController controller, AnimatedSkinTransition transition)
        {
            if(__playables != null && __playables.TryGetValue(controller, out var playable))
                playable.Reset(transition);
            else
            {
                int count = __pool == null ? 0 : __pool.Count;
                if (count > 0)
                {
                    playable = __pool[--count];

                    __pool.RemoveAt(count);

                    playable.Reset(transition);
                }
                else
                    playable = new Playable(transition);

                if (__playables == null)
                    __playables = new Dictionary<AnimatedSkinController, Playable>();

                __playables[controller] = playable;
            }
        }

        public void Stop(AnimatedSkinController controller)
        {
            if (__playables != null && __playables.Remove(controller, out var playable))
            {
                if (__pool == null)
                    __pool = new List<Playable>();

                __pool.Add(playable);
            }
        }
        
        void Update()
        {
            if (__playables == null)
                return;

            Profiler.BeginSample("Play");
            AnimatedSkinController controller;
            foreach (var playable in __playables)
            {
                controller = playable.Key;
                if(!playable.Value.Update(controller))
                    continue;

                if (__controllersToRemove == null)
                    __controllersToRemove = new List<AnimatedSkinController>();
                
                __controllersToRemove.Add(controller);
            }
            Profiler.EndSample();
            
            Profiler.BeginSample("Remove");
            if (__controllersToRemove != null)
            {
                Playable playable;
                foreach (var controllerToRemove in __controllersToRemove)
                {
                    if(!__playables.Remove(controllerToRemove, out playable))
                        continue;

                    if (__pool == null)
                        __pool = new List<Playable>();
                    
                    __pool.Add(playable);
                }
                
                __controllersToRemove.Clear();
            }
            Profiler.EndSample();
        }
    }
    
    public enum EventType
    {
        Start, 
        End
    }
    
    [Serializable]
    public struct Event
    {
        public EventType type;
        public string animationName;
        public UnityEvent value;
    }
    
    public static readonly int FrameCountPerSecondID = Shader.PropertyToID("_AnimatedSkinFrameCountPerSecond");
    
    [SerializeField] 
    internal int _frameCountPerSecond = 30;

    [SerializeField]
    internal string _defaultPlayingAnimation = "Idle";

    [SerializeField]
    internal AnimatedSkinDatabase _database;

    [SerializeField]
    internal UnityEvent _onReset;

    [SerializeField]
    internal Event[] _events;

    private Dictionary<(string, EventType), int> __eventIndices;
    
    private MaterialPropertyBlock __materialPropertyBlock;
    private Renderer __renderer;
    //private Coroutine __coroutine;

    public void Setup(AnimatedSkinDatabase database)
    {
        _database = database;
    }

    [UnityEngine.Scripting.Preserve]
    public void Play(UnityEngine.Object transition)
    {
        /*if (__coroutine != null)
            StopCoroutine(__coroutine);
        
        __coroutine = StartCoroutine(__Play(transition as AnimatedSkinTransition));*/
        
        Manager.instance.Play(this, transition as AnimatedSkinTransition);
    }

    public int Play(string animationName, float offsetSeconds)
    {
        __Init();

        int animationIndex = _database.FindAnimationIndex(animationName);
        if (animationIndex != -1)
        {
            __Invoke(animationName, EventType.Start);

            __materialPropertyBlock.Play(_database.animations[animationIndex], offsetSeconds - Time.time);

            __renderer.SetPropertyBlock(__materialPropertyBlock);
        }

        return animationIndex;
    }

    public void Stop()
    {
        Manager.instance.Stop(this);
        /*if (__coroutine != null)
        {
            StopCoroutine(__coroutine);

            __coroutine = null;
        }*/

        __materialPropertyBlock.Stop();

        __renderer.SetPropertyBlock(__materialPropertyBlock);
    }

    protected void OnEnable()
    {
        __Init();
        
        AnimatedSkinUtility.SetInt(__materialPropertyBlock, FrameCountPerSecondID, _frameCountPerSecond);

        int animationIndex = _database.FindAnimationIndex(_defaultPlayingAnimation);
        if (animationIndex != -1)
        {
            __Invoke(_defaultPlayingAnimation, EventType.Start);

            __materialPropertyBlock.Play(_database.animations[animationIndex], 0.0f);
        }

        __renderer.SetPropertyBlock(__materialPropertyBlock);

        if (_onReset != null)
            _onReset.Invoke();
    }

    protected void OnDisable()
    {
        Stop();
    }

    /*private IEnumerator __Play(AnimatedSkinTransition transition)
    {
        if (transition == null)
        {
            __coroutine = null;
            
            yield break;
        }

        int animationIndex = Play(transition.animationName, transition.offsetSeconds);
        if (animationIndex != -1)
        {
            var animation = _database.animations[animationIndex];
            float waitForSeconds = animation.frameCount * 1.0f /
                _frameCountPerSecond - transition.offsetSeconds;
            
            if(waitForSeconds > math.FLT_MIN_NORMAL)
                yield return new WaitForSeconds(waitForSeconds);

            __Invoke(transition.animationName, EventType.End);

            if (transition.next == null)
            {
                if (!transition.isLoop)
                {
                    animation.startFrame += animation.frameCount - 1;
                    animation.frameCount = 1;
                    __materialPropertyBlock.Play(animation, -Time.time);
                }

                __renderer.SetPropertyBlock(__materialPropertyBlock);

                __coroutine = null;
            }
            else
                yield return __Play(transition.next);
        }
    }*/

    private void __Invoke(string animationName, EventType type)
    {
        if (__eventIndices == null)
        {
            int numEvents = _events == null ? 0 : _events.Length;
            if (numEvents > 0)
            {
                __eventIndices = new Dictionary<(string, EventType), int>(numEvents);

                for (int i = 0; i < numEvents; ++i)
                {
                    ref var @event = ref _events[i];
                    
                    __eventIndices.Add((@event.animationName, @event.type), i);
                }
            }
        }

        if (__eventIndices != null && __eventIndices.TryGetValue((animationName, type), out var eventIndex))
        {
            var value = _events[eventIndex].value;
            if(value != null)
                value.Invoke();
        }
    }

    private void __Init()
    {
        if (__materialPropertyBlock == null)
        {
            __materialPropertyBlock = new MaterialPropertyBlock();
            
            __renderer = GetComponent<Renderer>();
            
            //__renderer.SetPropertyBlock(__materialPropertyBlock);
        }
    }
}

public static class AnimatedSkinUtility
{
    public static readonly int OffsetSecondsID = Shader.PropertyToID("_AnimatedSkinOffsetSeconds");
    public static readonly int StartFrameID = Shader.PropertyToID("_AnimatedSkinStartFrame");
    public static readonly int FrameCountID = Shader.PropertyToID("_AnimatedSkinFrameCount");

    public static void Play(this MaterialPropertyBlock materialPropertyBlock, in AnimatedSkinDatabase.Animation animation, float offsetSeconds)
    {
        materialPropertyBlock.SetFloat(OffsetSecondsID, offsetSeconds);
            
        SetInt(materialPropertyBlock, StartFrameID, animation.startFrame);
        SetInt(materialPropertyBlock, FrameCountID, animation.frameCount);
    }
        
    public static void Stop(this MaterialPropertyBlock materialPropertyBlock)
    {
        SetInt(materialPropertyBlock, StartFrameID, 0);
        SetInt(materialPropertyBlock, FrameCountID, 1);
    }
    
    public static void SetInt(MaterialPropertyBlock materialPropertyBlock, int id, int value)
    {
        materialPropertyBlock.SetFloat(id, math.asfloat(value));
    }
}