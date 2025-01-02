using Unity.Entities;

public struct CopyMatrixToTransformInstanceID : ICleanupComponentData
{
    public bool isSendMessageOnDestroy;
    public int value;
}
