using UnityEngine;
using TMPro;

public class ReplyMessageChatManager : MonoBehaviour
{
    [SerializeField] 
    internal StringEvent _onOutput;
    
    [SerializeField] 
    internal StringEvent _onInput;

    [SerializeField]
    internal TMP_Dropdown _dropdown;
    
    [SerializeField]
    internal string _format = "{0}:{1}";

    private void __OnValueChanged(int value)
    {
        _onInput?.Invoke(string.Format(_format, LevelPlayerShared<LocalPlayer>.header.name, _dropdown.options[value].text));
    }

    void Start()
    {
        _dropdown.onValueChanged.AddListener(__OnValueChanged);
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
