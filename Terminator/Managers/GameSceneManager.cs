using UnityEngine;

public class GameSceneManager : MonoBehaviour
{
    public void Load(string sceneName)
    {
        GameAssetManager.instance.LoadScene(sceneName, null);
    }
}
