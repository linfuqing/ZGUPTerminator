using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Scripting;

public sealed class LoginManager : MonoBehaviour
{
    [Serializable]
    internal struct Level
    {
        public string name;
    }

    [SerializeField]
    internal UnityEvent _onStart;

    [SerializeField]
    internal LevelStyle _style;

    [SerializeField] 
    internal Level[] _levels;

    private LevelStyle[] __styles;

    private int __selectedIndex;

    [Preserve]
    public void LoadScene()
    {
        GameAssetManager.instance.LoadScene(_levels[__selectedIndex].name, null);
    }

    private void __LoadScene()
    {
        _onStart.Invoke();
        //GameAssetManager.instance.LoadScene(_levels[__selectedIndex].name, null);
    }

    void OnEnable()
    {
        int numLevels = _levels == null ? 0 : _levels.Length;

        Transform parent = _style.transform.parent;
        LevelStyle style;
        __styles = new LevelStyle[numLevels];
        for (int i = 0; i < numLevels; ++i)
        {
            style = Instantiate(_style, parent);

            int index = i;
            style.toggle.onValueChanged.AddListener(x =>
            {
                if(x)
                    __selectedIndex = index;
            });
            
            style.button.onClick.RemoveAllListeners();
            style.button.onClick.AddListener(__LoadScene);
            
            style.gameObject.SetActive(true);

            __styles[i] = style;
        }
    }
}
