using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

public static class TransformUtility
{
    public static bool TryGetLocalToWorld(
        in Entity entity, 
        in ComponentLookup<Parent> parents, 
        in ComponentLookup<LocalTransform> localTransforms, 
        out float4x4 matrix)
    {
        if (!localTransforms.TryGetComponent(entity, out var localTransform))
        {
            matrix = float4x4.identity;

            return false;
        }

        matrix = localTransform.ToMatrix();
        if (parents.TryGetComponent(entity, out var parent) && 
            TryGetLocalToWorld(
                parent.Value, 
                parents, 
                localTransforms, 
                out var parentMatrix))
            matrix = math.mul(parentMatrix, matrix);

        return true;
    }

}
