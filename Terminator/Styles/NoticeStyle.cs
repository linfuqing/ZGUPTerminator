using UnityEngine;
using UnityEngine.Events;

public class NoticeStyle : MonoBehaviour
{
    public ActiveEvent onNew;

    public StringEvent onTitle;
    public StringEvent onDetail;
    public StringEvent onDealLine;
    
    public Transform rewardParent;

    public UnityEngine.UI.Button button;
}
