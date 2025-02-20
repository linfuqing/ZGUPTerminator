using System;
using Unity.Mathematics;
using UnityEngine;

public class AxisEventReceiver : MonoBehaviour
{
    public static readonly int AxisX = Animator.StringToHash("AxisX");
    public static readonly int AxisY = Animator.StringToHash("AxisY");

    private Vector2 __axis;
    
    private Animator __animator;

    public Animator animator
    {
        get
        {
            if (__animator == null)
                __animator = GetComponentInChildren<Animator>();

            return __animator;
        }
    }
    
    [UnityEngine.Scripting.Preserve]
    public void SetAxis(Parameters parameters)
    {
        int value, count;
        #if UNITY_EDITOR
        count = 2;
        #else
        count = Mathf.Min(parameters == null ? 0 : parameters.count, 2);
        #endif
        for (int i = 0; i < count; ++i)
            __axis[i] = parameters.TryGet(i, out value) ? math.asfloat(value) : 0.0f;
    }

    protected void Update()
    {
        var animator = this.animator;
        var axis3D = transform.InverseTransformVector(new Vector3(__axis.x, 0.0f, __axis.y));
        animator.SetFloat(AxisX, axis3D.x);
        animator.SetFloat(AxisY, axis3D.z);

    }
}
