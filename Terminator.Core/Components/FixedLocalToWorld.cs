using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;

public readonly struct FixedLocalToWorld
{
    [ReadOnly]
    public readonly ComponentLookup<Parent> Parents;

    [ReadOnly] 
    public readonly ComponentLookup<LocalTransform> LocalTransforms;

    public FixedLocalToWorld(ref SystemState state)
    {
        Parents = state.GetComponentLookup<Parent>(true);
        LocalTransforms = state.GetComponentLookup<LocalTransform>(true);
    }
    
    public FixedLocalToWorld(in ComponentLookup<Parent> parents, in ComponentLookup<LocalTransform> localTransforms)
    {
        Parents = parents;
        LocalTransforms = localTransforms;
    }

    public void Update(ref SystemState state)
    {
        Parents.Update(ref state);
        LocalTransforms.Update(ref state);
    }
    
    public bool TryGetMatrix(
        in Entity entity, 
        out float4x4 matrix)
    {
        if (!LocalTransforms.TryGetComponent(entity, out var localTransform))
        {
            matrix = float4x4.identity;

            return false;
        }

        matrix = localTransform.ToMatrix();
        if (Parents.TryGetComponent(entity, out var parent) && 
            TryGetMatrix(
                parent.Value, 
                out var parentMatrix))
            matrix = math.mul(parentMatrix, matrix);

        return true;
    }
    
    
    public float4x4 GetMatrix(in Entity entity)
    {
        float4x4 matrix = LocalTransforms.TryGetComponent(entity, out var localTransform)
            ? localTransform.ToMatrix()
            : float4x4.identity;

        if (Parents.TryGetComponent(entity, out var parent))
            matrix = math.mul(GetMatrix(parent.Value), matrix);

        return matrix;
    }
}