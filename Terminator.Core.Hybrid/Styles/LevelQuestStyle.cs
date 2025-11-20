using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class LevelQuestStyle : MonoBehaviour
{
    public UnityEvent onSuccess;
    public UnityEvent onFail;
    public StringEvent onCount;
    public StringEvent onCapacity;
    public ZG.UI.Progressbar progressbar;
}
