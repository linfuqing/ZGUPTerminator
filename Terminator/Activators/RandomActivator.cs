using UnityEngine;
using ZG;

public class RandomActivator : TimeActivator
{
    private int __sortCode;

    public override int sortCode => __sortCode;
    
    void Start()
    {
        __sortCode = Random.Range(int.MinValue, int.MaxValue);
    }
}
