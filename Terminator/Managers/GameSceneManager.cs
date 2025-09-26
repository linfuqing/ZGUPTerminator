using UnityEngine;

public class GameSceneManager : MonoBehaviour
{
    public const string NAME_SPACE_NEW = "GameSceneManagerNew";

    [Tooltip("第一次进这个场景"), SerializeField] 
    internal UnityEngine.Events.UnityEvent _onNew;
    
    [SerializeField]
    internal string _defaultSceneName = "Scenes/Login.scene";
    
    [UnityEngine.Scripting.Preserve]
    public void LoadToDefault()
    {
        GameAssetManager.instance.LoadScene(_defaultSceneName, null, null, false);
    }

    void Start()
    {
        string key = $"{NAME_SPACE_NEW}{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}";
        int times = PlayerPrefs.GetInt(key);
        if (times == 0)
        {
            if(_onNew != null)
                _onNew.Invoke();
        }
        
        PlayerPrefs.SetInt(key, ++times);
    }
}
