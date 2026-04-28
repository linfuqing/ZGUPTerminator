using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZG;

public class ReplyMessageChatManager : MonoBehaviour
{
    public enum StyleMode
    {
        Normal = 0x01,
        Match = 0x02,
        All = Normal | Match
    }
    
    [System.Serializable]
    internal struct Style
    {
        public StyleMode mode;
        [Tooltip("用Switch")]
        public Toggle toggle;
        public ReplyMessageChatStyle style;
        public string[] texts;
    }

    [SerializeField] 
    internal float _emojiDestroyTime = 5.0f;

    [SerializeField] 
    internal float _emojiToggleDestroyTime = 1.0f;

    [SerializeField] 
    internal string _emojiPrefix = "emoji#";
    
    [SerializeField] 
    internal string _emojiAssetBundleFileName = "emoji";

    [SerializeField]
    internal string _emojiFormat = "{0}:{1}";

    [SerializeField]
    internal string _format = "{0}:{1}";

    [SerializeField] 
    internal Transform _inputEmojiParent;

    [SerializeField] 
    internal Transform _outputEmojiParent;

    [SerializeField] 
    internal StringEvent _onOutputEmoji;

    [SerializeField] 
    internal StringEvent _onOutputText;

    [SerializeField] 
    internal StringEvent _onOutput;
    
    [SerializeField] 
    internal StringEvent _onInput;
    
    [SerializeField] 
    internal StringEvent _onInputText;

    [SerializeField] 
    internal StringEvent _onInputEmoji;

    [SerializeField]
    internal Style[] _styles;

    private List<ReplyMessageChatStyle> __styles;
    private List<AssetObjectLoader> __loaders;

    //[SerializeField]
    //internal TMP_Dropdown[] _dropdowns;
    
    void Start()
    {
        /*foreach (var dropdown in _dropdowns)
        {
            var temp = dropdown;
            temp.onValueChanged.AddListener(x =>
            {
                var text = temp.options[x].text;
                if (string.IsNullOrEmpty(text))
                    return;
        
                ReplyMessageChatShared.input = text;
        
                _onInput?.Invoke(string.Format(_format, LevelPlayerShared<LocalPlayer>.header.name, text));
            });
        }*/
        Transform parent;
        ReplyMessageChatStyle instance;
        foreach (var style in _styles)
        {
            switch (style.mode)
            {
                case StyleMode.Normal:
                    if(LevelShared.match != 0)
                        continue;
                    
                    break;
                case StyleMode.Match:
                    if(LevelShared.match == 0)
                        continue;

                    break;
            }

            style.toggle.onValueChanged.AddListener(x =>
            {
                if (x)
                {
                    var assetManager = GameAssetManager.instance?.dataManager;
                    parent = style.style.transform.parent;
                    foreach (var text in style.texts)
                    {
                        instance = Instantiate(style.style, parent);
                        
                        bool isEmoji = text.StartsWith(_emojiPrefix);
                        string value;
                        AssetObjectLoader loader;
                        if (text.StartsWith(_emojiPrefix))
                        {
                            value = text.Substring(_emojiPrefix.Length);
                            loader = new AssetObjectLoader(AssetObjectLoader.Space.Local, _emojiAssetBundleFileName,
                                value, this, instance.emojiParent);
                            
                            loader.Load(assetManager);

                            if (__loaders == null)
                                __loaders = new List<AssetObjectLoader>();
                            
                            __loaders.Add(loader);
                        }
                        else
                        {
                            value = text;

                            loader = null;
                        }
                        
                        instance.onText?.Invoke(value);

                        instance.button.onClick.AddListener(() =>
                        {
                            if (isEmoji)
                            {
                                var newLoader = new AssetObjectLoader(loader);
                                newLoader.Init(this, _inputEmojiParent);

                                newLoader.onLoadComplete += x =>
                                {
                                    var audioSource = x.GetComponent<AudioSource>();
                                    if(audioSource != null)
                                        audioSource.Play();

                                    Destroy(x, _emojiDestroyTime);
                                };

                                newLoader.Load(assetManager);
                                
                                ReplyMessageChatShared.input = $"{_emojiPrefix}{value}";

                                _onInputEmoji?.Invoke(string.Format(_emojiFormat, LevelPlayerShared<LocalPlayer>.header.name, value));
                            }
                            else
                            {

                                ReplyMessageChatShared.input = value;

                                _onInputText?.Invoke(value);
                                _onInput?.Invoke(string.Format(_format, LevelPlayerShared<LocalPlayer>.header.name,
                                    value));
                            }
                        });
                        
                        instance.gameObject.SetActive(true);
                        
                        if (__styles == null)
                            __styles = new List<ReplyMessageChatStyle>();
                        
                        __styles.Add(instance);
                    }
                }
                else
                {
                    if (__loaders != null)
                    {
                        foreach (var loader in __loaders)
                            loader.Dispose(_emojiToggleDestroyTime);

                        __loaders.Clear();
                    }

                    if (__styles != null)
                    {
                        foreach (var style in __styles)
                            Destroy(style.gameObject, _emojiToggleDestroyTime);
                        
                        __styles.Clear();
                    }
                }
            });

            style.toggle.interactable = true;
            style.toggle.gameObject.SetActive(true);
        }
    }
    
    // Update is called once per frame
    void Update()
    {
        var output = ReplyMessageChatShared.output;
        if (output.IsEmpty)
            return;
        
        string text = output.ToString();
        AssetObjectLoader loader;
        if (text.StartsWith(_emojiPrefix))
        {
            text = text.Substring(_emojiPrefix.Length);
            loader = new AssetObjectLoader(AssetObjectLoader.Space.Local, _emojiAssetBundleFileName,
                text, this, _outputEmojiParent);
            
            loader.onLoadComplete += x =>
            {
                var audioSource = x.GetComponent<AudioSource>();
                if(audioSource != null)
                    audioSource.Play();
                
                Destroy(x, _emojiDestroyTime);
            };

            loader.Load(GameAssetManager.instance?.dataManager);

            _onOutputEmoji.Invoke(string.Format(_emojiFormat, LevelPlayerShared<RemotePlayer>.header.name, text));
        }
        else
        {
            _onOutputText?.Invoke(text);
            _onOutput?.Invoke(string.Format(_format, LevelPlayerShared<RemotePlayer>.header.name, text));
        }
    }
}
