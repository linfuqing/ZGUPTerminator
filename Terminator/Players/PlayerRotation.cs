using UnityEngine;

public class PlayerRotation : MonoBehaviour
{
    public static PlayerRotation[] instances;

    [SerializeField] 
    internal PlayerType _type;

    [SerializeField] 
    internal Transform[] _children;

    public void SetRotation(in Quaternion rotation)
    {
        transform.rotation = rotation;
        
        if (_children != null)
        {
            foreach (var child in _children)
            {
                if (child == null)
                    continue;

                child.rotation = rotation;
            }
        }
    }

    void OnEnable()
    {
        instances ??= new PlayerRotation[(int)PlayerType.Total];

        instances[(int)_type] = this;
    }

    void OnDisable()
    {
        instances[(int)_type] = null;
    }
}
