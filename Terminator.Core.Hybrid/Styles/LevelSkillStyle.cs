using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class LevelSkillStyle : SkillStyle
{
    public UnityEvent onGuide;
    public UnityEvent onRecommend;
    public UnityEvent onDestroy;

    public Button button;

    public LevelSkillStyle child;
}
