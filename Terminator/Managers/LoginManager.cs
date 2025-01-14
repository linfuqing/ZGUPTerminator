using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting;
using ZG.UI;

public sealed class LoginManager : MonoBehaviour
{
    [Serializable]
    internal struct Level
    {
        public string name;
        public string title;
        public Sprite sprite;
    }

    [SerializeField]
    internal UnityEvent _onStart;

    [SerializeField]
    internal StringEvent _onEnergyMax;
    
    [SerializeField]
    internal StringEvent _onEnergy;

    [SerializeField] 
    internal Progressbar _energy;

    [SerializeField]
    internal LevelStyle _style;

    [SerializeField] 
    internal Level[] _levels;

    private LevelStyle[] __styles;

    private int __selectedIndex;

    public static uint? userID
    {
        get;

        private set;
    } = null;

    public static LoginManager instance
    {
        get;

        private set;
    }

    public int gold
    {
        get;

        set;
    }

    public int energy
    {
        get;

        set;
    }

    public int energyMax
    {
        get;

        private set;
    }
    
    [Preserve]
    public void LoadScene()
    {
        var assetManager = GameAssetManager.instance;
        if (assetManager == null)
            assetManager = gameObject.AddComponent<GameAssetManager>();
        
        assetManager.LoadScene(_levels[__selectedIndex].name, null, new GameSceneActivation());
    }

    private void __LoadScene()
    {
        _onStart.Invoke();
        //GameAssetManager.instance.LoadScene(_levels[__selectedIndex].name, null);
    }

    IEnumerator Start()
    {
        instance = this;

        int level = -1;
        var userData = IUserData.instance;
        if (userData == null)
            userID = 0;
        else
        {
            yield return userData.QueryUser(GameUser.Shared.channelName, GameUser.Shared.channelUser, (user, energy) =>
            {
                userID = user.id;
                gold = user.gold;
                level = user.level;

                this.energy = energy.value;

                energyMax = energy.max;

            });
        }
        
        int numLevels = _levels == null ? 0 : _levels.Length;
        if (level > 0)
            numLevels = Mathf.Min(numLevels, level);

        Transform parent = _style.transform.parent;
        LevelStyle style;
        __styles = new LevelStyle[numLevels];
        for (int i = 0; i < numLevels; ++i)
        {
            style = Instantiate(_style, parent);
            
            if(style.onImage != null)
                style.onImage.Invoke(_levels[i].sprite);

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
