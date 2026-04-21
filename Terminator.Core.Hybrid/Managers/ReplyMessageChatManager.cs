using UnityEngine;
using TMPro;

public class ReplyMessageChatManager : MonoBehaviour
{
    [SerializeField] 
    internal StringEvent _onOutput;
    
    [SerializeField] 
    internal StringEvent _onInput;

    [SerializeField]
    internal TMP_Dropdown[] _dropdowns;
    
    [SerializeField]
    internal string _format = "{0}:{1}";

    void Start()
    {
        foreach (var dropdown in _dropdowns)
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
        }
    }
    
    // Update is called once per frame
    void Update()
    {
        var output = ReplyMessageChatShared.output;
        if (output.IsEmpty)
            return;
        
        _onOutput?.Invoke(string.Format(_format, LevelPlayerShared<RemotePlayer>.header.name, output.ToString()));
    }
}
