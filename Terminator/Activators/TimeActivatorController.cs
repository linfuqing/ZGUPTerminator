using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ITimeActivator
{
    bool isVail { get; }

    bool isActive { get; set; }

    int sortCode { get; }
}

public abstract class TimeActivator : MonoBehaviour, ITimeActivator
{
    public bool isVailOnDisable = true;

    public ZG.ActiveEvent onActive;

    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("controller")]
    internal TimeActivatorController _controller = null;

    private bool __isActiveCache;

    private bool __isActive;

    public bool isActive
    {
        get => __isActive;// gameObject.activeSelf;

        set
        {
            /*if (gameObject.activeSelf == value)
                return;

            gameObject.SetActive(value);*/

            if (__isActiveCache == value)
                return;

            __isActiveCache = value;

            if (onActive != null)
                onActive.Invoke(value);

            __isActive = value;
        }
    }

    public bool isVail => this != null && (isVailOnDisable || isActiveAndEnabled);

    public abstract int sortCode { get; }

    protected void Awake()
    {
        if (isVailOnDisable)
            gameObject.SetActive(false);

        if (_controller != null)
            _controller.Add(this);
    }

    protected void OnEnable()
    {
        if(__isActiveCache == __isActive && isVailOnDisable && !__isActiveCache && _controller != null)
            _controller.Refresh(this);
    }

    protected void OnDestroy()
    {
        if (_controller != null)
            _controller.Remove(this);
    }
}

public class TimeActivatorController : MonoBehaviour
{
    private struct Comparer : IComparer<ITimeActivator>
    {
        public int Compare(ITimeActivator x, ITimeActivator y)
        {
            return x.sortCode.CompareTo(y.sortCode);
        }
    }

    public float time = 0.0f;
    public bool isActivateOnEnable;

    private bool __isActivateOnEnable;
    private bool __isActive;
    private int __index;
    private bool __isActivateRightNow;
    private Coroutine __coroutine;
    private List<ITimeActivator> __buffer;
    private HashSet<ITimeActivator> __activators = new HashSet<ITimeActivator>();

    public bool isActive
    {
        get => __isActive;

        set
        {
            if (__isActive == value)
                return;

            if (value)
            {
                if (isActiveAndEnabled)
                    __coroutine = StartCoroutine(__Activate());
                else
                    __isActivateOnEnable = true;
            }
            else
            {
                __isActivateOnEnable = false;

                if (__coroutine != null)
                {
                    StopCoroutine(__coroutine);

                    __coroutine = null;
                }
                
                foreach (var activactor in __activators)
                    activactor.isActive = false;
            }

            __isActive = value;
        }
    }

    [UnityEngine.Scripting.Preserve]
    public void ActivateRightNow()
    {
        __isActivateRightNow = true;
    }

    public bool Refresh(ITimeActivator activator)
    {
        if (!__activators.Contains(activator))
            return false;
        
        __Refresh(activator);

        return true;
    }

    public bool Add(ITimeActivator activator)
    {
        if (__activators.Add(activator))
        {
            __Refresh(activator);

            return true;
        }

        return false;
    }

    public bool Remove(ITimeActivator activator)
    {
        return __activators.Remove(activator);
    }

    private void __Refresh(ITimeActivator activator)
    {
        if (__isActive)
        {
            if (isActiveAndEnabled)
            {
                if (__coroutine == null)
                    __coroutine = StartCoroutine(__Activate());
                else
                {
                    bool isContains = false;
                    int count = __buffer.Count;
                    if (count > __index)
                    {
                        for (int i = __index; i < count; ++i)
                        {
                            if(__buffer[i] == activator)
                            {
                                isContains = true;

                                break;
                            }
                        }
                    }

                    if(!isContains)
                        __buffer.Add(activator);
                }
            }
            else
                __isActivateOnEnable = true;
        }
    }
    
    private bool __Next()
    {
        ITimeActivator activator;
        int length = __buffer == null ? 0 : __buffer.Count;
        while(__index < length)
        {
            activator = __buffer[__index++];
            if (!activator.isVail)
                continue;

            activator.isActive = true;
            
            return true;
        }

        return false;
    }

    private IEnumerator __Activate()
    {
        __isActivateRightNow = false;
        
        if (__buffer == null)
            __buffer = new List<ITimeActivator>(__activators.Count);
        else
            __buffer.Clear();

        __buffer.AddRange(__activators);

        __index = __buffer.Count;

        yield return null;

        __buffer.Sort(new Comparer());

        __index = 0;

        if (time > 0.0f)
        {
            while (__Next())
            {
                if(__isActivateRightNow)
                    yield return new WaitForSecondsRealtime(time);
            }
        }
        else
        {
            while (__Next())
                yield return null;
        }

        __coroutine = null;
    }

    void OnEnable()
    {
        if(__isActive)
        {
            if(__isActivateOnEnable)
            {
                __isActivateOnEnable = false;

                __coroutine = StartCoroutine(__Activate());
            }
        }
        else if(isActivateOnEnable)
            isActive = true;
    }

    void OnDisable()
    {
        if (__coroutine != null)
        {
            __coroutine = null;

            while (__Next()) ;
        }

        if(isActivateOnEnable)
            isActive = false;
    }
}
