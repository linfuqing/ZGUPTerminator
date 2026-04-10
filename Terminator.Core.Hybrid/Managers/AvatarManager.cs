using UnityEngine;

public class AvatarManager : MonoBehaviour
{
    [SerializeField]
    internal StringEvent _onLocalName;
    [SerializeField]
    internal SpriteEvent _onLocalAvatar;
    [SerializeField]
    internal SpriteEvent _onRemoteAvatar;
    [SerializeField]
    internal StringEvent _onRemoteName;

    [SerializeField]
    internal AvatarDatabase _database;

    public static AvatarManager instance
    {
        get;

        private set;
    }

    public static Sprite Get(string name) => instance._database.Get(name);

    public void Apply()
    {
        {
            ref var header = ref LevelPlayerShared<LocalPlayer>.header;
            _onLocalName?.Invoke(header.name.ToString());
            _onLocalAvatar?.Invoke(Get(header.avatar.ToString()));
        }
        
        if (RemotePlayer.status >= RemotePlayer.Status.Joined)
        {
            ref var header = ref LevelPlayerShared<RemotePlayer>.header;
            _onRemoteName?.Invoke(header.name.ToString());
            _onRemoteAvatar?.Invoke(Get(header.avatar.ToString()));
        }
    }

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        Apply();
    }
}
