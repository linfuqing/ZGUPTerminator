using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    /*private enum AnimatorStatus
    {
        Normal, 
        Jump
    }*/
    
    //public static readonly int AxisX = Animator.StringToHash("AxisX");
    //public static readonly int AxisY = Animator.StringToHash("AxisY");
    
    //public static readonly int Jump = Animator.StringToHash("Jump");
    
    //public static readonly int Status = Animator.StringToHash("Status");

    //[SerializeField]
    //internal float _smoothTime = 0.01f;

    //[SerializeField]
    //internal float _minJumpHeight = 0.1f;

    //[SerializeField]
    //internal float _minJumpVelocity = 0.01f;

    private bool __isDead;
    
    //private float __jumpHeight;
    
    private Animator __animator;
    private AttributeEventReceiver __attributeEventReceiver;

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
    public void Play(AnimatorParameters parameters)
    {
        switch (parameters.name)
        {
            case "Hit":
                LevelManager.instance.dataFlag |= (int)ILevelData.Flag.HasBeenDamaged;
                break;
            case "Die":
                __isDead = true;
                
                if(__attributeEventReceiver != null)
                    __attributeEventReceiver.Die();
                break;
            case "Respawn":
                PlayerEvents.Respawn();
                
                if(__attributeEventReceiver != null)
                    __attributeEventReceiver.Die();
                break;
        }
        
        parameters.Apply(animator);
    }

    protected void Awake()
    {
        if (__attributeEventReceiver == null)
            __attributeEventReceiver = GetComponentInChildren<AttributeEventReceiver>();

        if (__attributeEventReceiver != null)
        {
            var analytics = IAnalyticsEx.instance as IAnalyticsEx;
            if (analytics != null)
            {
                __attributeEventReceiver.onHPMaxChanged += analytics.SetPlayerHPMax;
                __attributeEventReceiver.onHPChanged += analytics.SetPlayerHP;
            }
        }
    }

    protected void OnEnable()
    {
        if (__isDead)
        {
            __isDead = false;

            Time.timeScale = 1.0f;
            
            PlayerEvents.isActive = true;
        }
    }

    protected void OnDisable()
    {
        if (__isDead)
        {
            PlayerEvents.isActive = false;
            
            Time.timeScale = 0.0f;
        }
    }

    protected void Update()
    {
        var transform = this.transform;
        var position = transform.position;

        var positionInstance = PlayerPosition.instance;
        if(positionInstance != null)
            positionInstance.transform.position = position;
        
        var rotationInstance = PlayerRotation.instance;
        if(rotationInstance != null)
            rotationInstance.transform.rotation = transform.rotation;

        JoystickAnimatorController.Update(animator);

        /*if (position.y > _minJumpHeight)
        {
            float velocity = position.y - __jumpHeight;
            int sign = Mathf.Abs(velocity) > _minJumpVelocity ? (int)Mathf.Sign(velocity) : 0;
            //Debug.LogError(sign);
            animator.SetInteger(Jump, sign);

            animator.SetInteger(Status, (int)AnimatorStatus.Jump);
        }
        else
        {
            animator.SetInteger(Jump, 0);
            
            animator.SetInteger(Status, (int)AnimatorStatus.Normal);
        }

        __jumpHeight = position.y;*/
    }
}
