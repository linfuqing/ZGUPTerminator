using UnityEngine;
using UnityEngine.Playables;

public class PlayableDirectorController : MonoBehaviour
{
    private PlayableDirector __playableDirector;

    public string time
    {
        set
        {
            if(__playableDirector == null)
                __playableDirector = GetComponent<PlayableDirector>();
            
            __playableDirector.time = double.Parse(value);
        }
    }
}
