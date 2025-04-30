using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class StageStyle : MonoBehaviour
{
    public UnityEvent onSelected;

    public UnityEvent onDestroy;
    public ActiveEvent onHot;
    public StringEvent onTitle;
    
    public Toggle toggle;
    
    public GameObject[] ranks;
}
