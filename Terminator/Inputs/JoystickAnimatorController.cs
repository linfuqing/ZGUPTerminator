using UnityEngine;

public class JoystickAnimatorController : MonoBehaviour
{
    public static readonly int AxisX = Animator.StringToHash("AxisX");
    public static readonly int AxisY = Animator.StringToHash("AxisY");

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

    public static void Update(Animator animator)
    {
        var axis = Joystick.axis;
        var axis3D = animator.transform.InverseTransformVector(new Vector3(axis.x, 0.0f, axis.y));
        animator.SetFloat(AxisX, axis3D.x);
        animator.SetFloat(AxisY, axis3D.z);
    }

    protected void Update()
    {
        Update(animator);
    }
}
