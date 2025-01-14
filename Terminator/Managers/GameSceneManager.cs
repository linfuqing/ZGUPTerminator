using UnityEngine;

public class GameSceneManager : MonoBehaviour
{
    internal string _defaultSceneName = "Scenes/Login.scene";
    
    public void LoadToDefault()
    {
        GameAssetManager.instance.LoadScene(_defaultSceneName, null);
    }
}
