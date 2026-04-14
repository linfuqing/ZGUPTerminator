using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class SensitiveWordInputField : TMP_InputField
{
    [SerializeField]
    internal UnityEvent _onSensitiveCheckFailed;
    
    private SubmitEvent __submitEvent;
    private Coroutine __coroutine;

    public SensitiveWordInputField()
    {
        __submitEvent = new SubmitEvent();
        __submitEvent.AddListener(__OnSubmit);
    }
    
    public override void OnSubmit(BaseEventData eventData)
    {
        var submitEvent = onSubmit;
        onSubmit = __submitEvent;
        base.OnSubmit(eventData);
        onSubmit = submitEvent;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        
        __coroutine = null;
    }

    private void __OnSubmit(string text)
    {
        if (__coroutine != null)
        {
            StopCoroutine(__coroutine);

            __coroutine = null;
        }

        if (!string.IsNullOrEmpty(text))
        {
            var sensitiveWordData = ISensitiveWordData.instance;
            if(sensitiveWordData == null)
                __Submit(text);
            else
                __coroutine = StartCoroutine(sensitiveWordData.Check(text, __Submit));
        }
    }

    private void __Submit(string text)
    {
        __coroutine = null;
        
        if(string.IsNullOrEmpty(text))
            _onSensitiveCheckFailed?.Invoke();
        else
            onSubmit?.Invoke(text);
    }
}
