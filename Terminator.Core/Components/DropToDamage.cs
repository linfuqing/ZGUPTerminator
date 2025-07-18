using System.Threading;
using Unity.Entities;

public struct DropToDamage : IComponentData, IEnableableComponent
{
    public bool isGrounded;
    public int value;
    public int valueImmunized;

    public int layerMask;

    public void Add(int value, int valueImmunized, int layerMask)
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
    }
}