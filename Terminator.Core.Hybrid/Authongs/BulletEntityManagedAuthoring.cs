using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class BulletEntityManagedAuthoring : MonoBehaviour
{
    class Baker : Baker<BulletEntityManagedAuthoring>
    {
        public override void Bake(BulletEntityManagedAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject.
            // By using a reference, we only need one baked prefab entity instead of
            // duplicating the prefab entity everywhere it is used.

            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent<BulletEntityManaged>(entity);
        }
    }
}
