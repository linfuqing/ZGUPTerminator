using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct User
{
    public uint id;
    
    public int status;

    public int gold;

    public int power;
}

public interface IUserData
{
    
}

public class UserData : MonoBehaviour
{
}
