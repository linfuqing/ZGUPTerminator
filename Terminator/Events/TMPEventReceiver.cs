using System;
using TMPro;
using UnityEngine;

public class TMPEventReceiver : MonoBehaviour
{
    private TMP_Text __text;

    public TMP_Text text
    {
        get
        {
            if (__text == null)
                __text = GetComponentInChildren<TMP_Text>();

            return __text;
        }
    }

    [UnityEngine.Scripting.Preserve]
    public void InvokeToSetText(Parameters parameters)
    {
        text.SetText(parameters[(int)EffectAttributeID.Damage].ToString());
    }

    /*void Start()
    {
        GetComponent<Renderer>().SetPropertyBlock(new MaterialPropertyBlock());
    }*/
}
