using UnityEngine;
using ZG;

public class HierarchyActivator : TimeActivator
{
    private int __sortCode = -1;

    public override int sortCode
    {
        get
        {
            if(__sortCode == -1 && this != null)
            {
                var transform = base.transform;
                if (transform.root.FindNode(transform, out int sortCode))
                    __sortCode = sortCode;
            }

            return __sortCode;
        }
    }
}
