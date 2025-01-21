using System;
using UnityEngine;
using UnityEngine.UI;

public class SettingManager : MonoBehaviour
{
    [Serializable]
    public struct ToggleSetting
    {
        public string name;
        public Toggle control;
    }

    [SerializeField]
    internal ToggleSetting[] _toggleSettings;

    public const string NAME_SPACE = "SettingManager";

    void Awake()
    {
        if (_toggleSettings != null)
        {
            foreach (var toggleSetting in _toggleSettings)
            {
                string key = $"{NAME_SPACE}{toggleSetting.name}";
                toggleSetting.control.isOn = PlayerPrefs.GetInt(key) != 0;
                toggleSetting.control.onValueChanged.AddListener(x =>
                {
                    PlayerPrefs.SetInt(key, x ? 1 : 0);
                });
            }
        }
    }
}
