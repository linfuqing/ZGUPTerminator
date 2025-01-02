using Unity.Entities;
using Unity.Mathematics;

public struct Suction : IComponentData
{
    //public float maxTime;
    public float minDistance;
    public float maxDistance;
    public float linearSpeed;
    public float angularSpeed;
    public float3 tangentSpeed;
    public float3 center;

    /*public float acceleration => maxTime > math.FLT_MIN_NORMAL ? 2.0f * maxDistance / maxTime : 0.0f;

    public float3 GetVelocity(
        in float3 center,
        in float3 position,
        float deltaTime)
    {
        float3 distance = center - position;
        float lengthSQ = math.lengthsq(distance);
        if (lengthSQ > maxDistance * maxDistance)
            return float3.zero;

        if (lengthSQ > math.FLT_MIN_NORMAL)
        {
            float acceleration = this.acceleration;
            if (acceleration > math.FLT_MIN_NORMAL)
            {
                float lengthR = math.rsqrt(lengthSQ), velocity = math.sqrt( 2.0f * lengthR)  / acceleration;

                velocity += deltaTime * acceleration * 0.5f;

                return velocity * distance;
            }

            if(deltaTime > math.FLT_MIN_NORMAL)
                return distance / deltaTime;
        }

        return float3.zero;
    }*/
}

public struct SuctionTargetVelocity : IComponentData, IEnableableComponent
{
    public float3 linear;
    public float3 angular;
    public float3 tangent;
}
