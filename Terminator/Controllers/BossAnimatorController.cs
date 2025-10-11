using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class BossAnimatorController : MonoBehaviour
{
    [Serializable]
    internal struct BoneRotation
    {
        public Transform transform;
        public Vector3 offset;
        public float weight;
    }

    [Serializable]
    internal struct LookAtPlayer
    {
        public float weightSpeed;
        public Rect range;
        public BoneRotation[] bones;

        private float __weight;

        public void Execute(in Vector3 position, Transform transform)
        {
            int numBones = bones == null ? 0 : bones.Length;
            var head = numBones > 0 ? bones[numBones - 1].transform : transform;

            bool result = range.xMax > range.xMin || range.yMax > range.yMin;
            if (!result)
            {
                Vector3 direction = position - head.position,
                    temp = Quaternion.FromToRotation(new Vector3(direction.x, 0.0f, direction.z).normalized,
                        Vector3.forward) * direction;
                float angle = Mathf.Atan2(temp.y, temp.z) * Mathf.Rad2Deg;
                result = angle < range.yMin || angle > range.yMax;
                if (!result)
                {
                    angle = Vector3.SignedAngle(transform.forward, direction, transform.up);
                    if (angle > 180.0f)
                        angle -= 360.0f;

                    result = angle < range.xMin || angle > range.xMax;
                }
            }

            __weight = Mathf.Lerp(__weight, result ? 0.0f : 1.0f, weightSpeed * Time.deltaTime);
            
            if (numBones > 0)
            {
                Quaternion rotation;
                foreach (var bone in bones)
                {
                    rotation = Quaternion.LookRotation(position - bone.transform.position, Vector3.up);//Quaternion.Lerp(Quaternion.LookRotation(forward, Vector3.up), Quaternion.LookRotation(__target - bone.transform.position, Vector3.up), __weight);
                    rotation *= Quaternion.Euler(bone.offset);
                    rotation = Quaternion.Lerp(bone.transform.rotation, rotation, bone.weight * __weight);
                    bone.transform.rotation = rotation;
                }
            }
        }
    }

    public static readonly int Parameter = Animator.StringToHash("Turn");

    private float __previousRotation;
    private Animator __animator;

    [SerializeField] 
    internal Vector3 _lookAtOffset;
    
    [SerializeField]
    internal LookAtPlayer[] _lookAtPlayers;

    void Start()
    {
        __animator = GetComponent<Animator>();

        __previousRotation = transform.localEulerAngles.y;
    }
    
    void LateUpdate()
    {
        float rotation = transform.localEulerAngles.y;
        float deltaTime = Time.deltaTime;
        if (deltaTime > Mathf.Epsilon)
            __animator.SetFloat(Parameter, (rotation - __previousRotation) / deltaTime);

        __previousRotation = rotation;
        
        if (_lookAtPlayers != null && PlayerPosition.instance != null)
        {
            Vector3 position = PlayerPosition.instance.transform.position + _lookAtOffset;
            var transform = base.transform;
            foreach (var lookAtPlayer in _lookAtPlayers)
                lookAtPlayer.Execute(position, transform);
        }
    }
}
