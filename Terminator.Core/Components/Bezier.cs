using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct BezierControlPoint : IBufferElementData
{
    public float3 value;
}

public struct BezierDistance : IComponentData
{
    public RigidTransform motion;
    public float value;
}

public struct BezierSpeed : IComponentData
{
    public float value;
}

public static class BezierUtility
{
    public static float3 CalculateQuadratic(float t, in float3 startPosition, in float3 controlPoint, in float3 endPosition)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;

        return uu * startPosition + 2.0f * u * t * controlPoint + tt * endPosition;
    }
    
    public static float3 CalculateCubic(
        float t, 
        in float3 startPosition, 
        in float3 startControlPoint, 
        in float3 endControlPoint, 
        in float3 endPosition)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        return uuu * startPosition + 3.0f * uu * t * startControlPoint + 3.0f * u * tt * endControlPoint + ttt * endPosition;
    }

    public static float3 Calculate(
        float t, 
        in float3 startPosition, 
        in float3 endPosition, 
        in NativeArray<float3> points)
    {
        int n = points.Length + 1;
        switch (n)
        {
            case 1:
                return math.lerp(startPosition, endPosition, t);
            case 2:
                return CalculateQuadratic(t, startPosition, points[0], endPosition);
            case 3:
                return CalculateCubic(t, startPosition, points[0], points[1], endPosition);
        }

        float bernstein = __Bernstein(n, 0, t);
        float3 point = bernstein * startPosition;
        for (int i = 1; i < n; i++)
        {
            bernstein = __Bernstein(n, i, t);
            point += bernstein * points[i - 1];
        }
        
        bernstein = __Bernstein(n, n, t);
        point += bernstein * endPosition;

        return point;
    }
    
    private static float __Bernstein(int n, int i, float t)
    {
        return __BinomialCoefficient(n, i) * math.pow(t, i) * math.pow(1 - t, n - i);
    }

    private static int __BinomialCoefficient(int n, int k)
    {
        int result = 1;
        if (k > n - k)
            k = n - k;
        
        for (int i = 0; i < k; ++i)
        {
            result *= n - i;
            result /= i + 1;
        }
        
        return result;
    }

}