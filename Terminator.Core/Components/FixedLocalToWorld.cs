using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;

public struct FixedLocalToWorld
{
    [ReadOnly]
    public ComponentLookup<Parent> parents;

    [ReadOnly] 
    public ComponentLookup<LocalTransform> localTransforms;

    public FixedLocalToWorld(ref SystemState state)
    {
        parents = state.GetComponentLookup<Parent>(true);
        localTransforms = state.GetComponentLookup<LocalTransform>(true);
    }
    
    public FixedLocalToWorld(in ComponentLookup<Parent> parents, in ComponentLookup<LocalTransform> localTransforms)
    {
        this.parents = parents;
        this.localTransforms = localTransforms;
    }

    public void Update(ref SystemState state)
    {
        parents.Update(ref state);
        localTransforms.Update(ref state);
    }
    
    public bool TryGetMatrix(
        in Entity entity, 
        out float4x4 matrix)
    {
        if (!localTransforms.TryGetComponent(entity, out var localTransform))
        {
            matrix = float4x4.identity;

            return false;
        }

        matrix = localTransform.ToMatrix();
        if (parents.TryGetComponent(entity, out var parent) && 
            TryGetMatrix(
                parent.Value, 
                out var parentMatrix))
            matrix = math.mul(parentMatrix, matrix);

        return true;
    }
    
    
    public float4x4 GetMatrix(in Entity entity)
    {
        float4x4 matrix = localTransforms.TryGetComponent(entity, out var localTransform)
            ? localTransform.ToMatrix()
            : float4x4.identity;

        if (parents.TryGetComponent(entity, out var parent))
            matrix = math.mul(GetMatrix(parent.Value), matrix);

        return matrix;
    }
}