using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class SettingManager : MonoBehaviour
{
    [Serializable]
    public struct ToggleSetting
    {
        public string name;
        public Toggle control;

        public UnityEvent onEnable;
        public UnityEvent onDisable;
    }

    [SerializeField]
    internal ToggleSetting[] _toggleSettings;

    public const string NAME_SPACE = "SettingManager";

    public float audioVolume
    {
        set
        {
            AudioListener.volume = value;
        }
    }

    void Awake()
    {
        bool isOn;
        int numToggleSettings = _toggleSettings == null ? 0 : _toggleSettings.Length;
        for(int i = 0; i < numToggleSettings; ++i)
        {
            var toggleSetting = _toggleSettings[i];
            
            string key = $"{NAME_SPACE}{toggleSetting.name}";
            isOn = PlayerPrefs.GetInt(key, 1) != 0;
            if (isOn)
            {
                if(toggleSetting.onEnable != null)
                    toggleSetting.onEnable.Invoke();
            }
            else if(toggleSetting.onDisable != null)
                toggleSetting.onDisable.Invoke();
            
            toggleSetting.control.isOn = isOn;
            toggleSetting.control.onValueChanged.AddListener(x =>
            {
                if (x)
                {
                    PlayerPrefs.SetInt(key, 1);
                    
                    if(toggleSetting.onEnable != null)
                        toggleSetting.onEnable.Invoke();
                }
                else
                {
                    
                    PlayerPrefs.SetInt(key, 0);
                    
                    if(toggleSetting.onDisable != null)
                        toggleSetting.onDisable.Invoke();
                }
            });
        }
    }
}
