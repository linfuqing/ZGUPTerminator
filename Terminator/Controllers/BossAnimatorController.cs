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

        public void Execute(ref float weight, in Vector3 position, Transform transform)
        {
            int numBones = bones == null ? 0 : bones.Length;
            var head = numBones > 0 ? bones[numBones - 1].transform : transform;

            bool result = range.xMax > range.xMin || range.yMax > range.yMin;
            if (result)
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

            weight = Mathf.Lerp(weight, result ? 0.0f : 1.0f, weightSpeed * Time.deltaTime);
            
            if (numBones > 0)
            {
                Quaternion rotation;
                foreach (var bone in bones)
                {
                    rotation = Quaternion.LookRotation(position - bone.transform.position, Vector3.up);//Quaternion.Lerp(Quaternion.LookRotation(forward, Vector3.up), Quaternion.LookRotation(__target - bone.transform.position, Vector3.up), __weight);
                    rotation *= Quaternion.Euler(bone.offset);
                    rotation = Quaternion.Lerp(bone.transform.rotation, rotation, bone.weight * weight);
                    bone.transform.rotation = rotation;
                }
            }
        }
    }

    public static readonly int Parameter = Animator.StringToHash("Turn");

    private float __previousRotation;
    private Animator __animator;

    private float[] __lookAtWeights;

    [SerializeField]
    internal LookAtPlayer[] _lookAtPlayers;

    [SerializeField] 
    internal Vector3 _lookAtOffset;
    
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
        
        if (PlayerPosition.instance != null)
        {
            int numLookAtPlayers = _lookAtPlayers == null ? 0 : _lookAtPlayers.Length;
            if (numLookAtPlayers > 0)
            {
                Array.Resize(ref __lookAtWeights, numLookAtPlayers);
                
                Vector3 position = PlayerPosition.instance.transform.position + _lookAtOffset;
                var transform = base.transform;
                for(int i = 0; i < numLookAtPlayers; ++i)
                    _lookAtPlayers[i].Execute(ref __lookAtWeights[i], position, transform);
            }
        }
    }
}
