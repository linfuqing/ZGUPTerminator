using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class AvatarManager : MonoBehaviour
{
    public static AvatarManager instance;

    internal AvatarDatabase _database;

    public static Sprite Get(string name) => instance._database.Get(name);

    void Awake()
    {
        instance = this;
    }
}
