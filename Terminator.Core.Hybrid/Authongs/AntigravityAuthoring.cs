using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
public class AntigravityAuthoring : MonoBehaviour
{
    class Baker : Baker<AntigravityAuthoring>
    {
        public override void Bake(AntigravityAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            Antigravity instance;
            instance.startMessageName = authoring._startMessageName;
            instance.startMessageValue = new WeakObjectReference<Object>(authoring._startMessageValue);
            instance.endMessageName = authoring._endMessageName;
            instance.endMessageValue = new WeakObjectReference<Object>(authoring._endMessageValue);
            instance.cooldown = authoring._cooldown;
            instance.duration = authoring._duration;
            AddComponent(entity, instance);
            AddComponent<AntigravityStatus>(entity);
        }
    }

    [SerializeField]
    internal string _startMessageName = "Play";

    [SerializeField]
    internal Object _startMessageValue;

    [SerializeField]
    internal string _endMessageName = "Play";

    [SerializeField]
    internal Object _endMessageValue;

    [SerializeField]
    [Tooltip("喷气到开始闪烁的时间")]
    internal float _cooldown = 5.0f;
    [SerializeField]
    [Tooltip("闪烁的时间")]
    internal float _duration = 3.0f;
}

#endif