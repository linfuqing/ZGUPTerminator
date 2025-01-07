using UnityEngine;

public class SceneManager : MonoBehaviour
{
    public void Load(string sceneName)
    {
        GameAssetManager.instance.LoadScene(sceneName, null);
    }
}
