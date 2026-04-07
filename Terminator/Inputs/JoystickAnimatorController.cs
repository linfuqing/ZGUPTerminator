using UnityEngine;

public class JoystickAnimatorController : MonoBehaviour
{
    public static readonly int AxisX = Animator.StringToHash("AxisX");
    public static readonly int AxisY = Animator.StringToHash("AxisY");

    public float smoothTime = 0.1f;
    private Vector3 __position;
    private Vector3 __velocity;

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

    public static void Update(Animator animator, ref Vector3 position, ref Vector3 velocity, float smoothTime)
    {
        var transform = animator.transform;
        position = Vector3.SmoothDamp(position, transform.position, ref velocity, smoothTime);
        var axis = velocity;
        axis.y = 0.0f;
        float m = axis.magnitude;
        if (m > 1.0f)
            axis /= m;
        /*else
            axis = Vector3.zero;*/

        var axis3D = transform.InverseTransformVector(axis);
        animator.SetFloat(AxisX, axis3D.x);
        animator.SetFloat(AxisY, axis3D.z);
    }

    protected void Update()
    {
        if (smoothTime > Mathf.Epsilon)
            Update(animator, ref __position, ref __velocity, smoothTime);
        else
            Update(animator);
    }
}
