using System.Threading;
using Unity.Entities;

public struct DropToDamage : IComponentData, IEnableableComponent
{
    public bool isGrounded;
    public int value;
    public int valueImmunized;

    public int layerMask;
    public int messageLayerMask;

    public void Add(int value, int valueImmunized, int layerMask, int messageLayerMask)
    {
        Interlocked.Add(ref this.value, value);
        Interlocked.Add(ref this.valueImmunized, valueImmunized);

        if (layerMask == -1)
            this.layerMask = -1;
        else
        {
            if (layerMask == 0)
                layerMask = 1;
            
            int origin;
            do
            {
                origin = this.layerMask;
            } while (Interlocked.CompareExchange(ref this.layerMask, origin | layerMask, origin) != origin);
        }
        
        if (messageLayerMask == -1)
            this.messageLayerMask = -1;
        else
        {
            if (messageLayerMask == 0)
                messageLayerMask = 1;
            
            int origin;
            do
            {
                origin = this.messageLayerMask;
            } while (Interlocked.CompareExchange(ref this.messageLayerMask, origin | messageLayerMask, origin) != origin);
        }
    }
}