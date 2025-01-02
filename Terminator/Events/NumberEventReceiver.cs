using System.Collections.Generic;
using UnityEngine;

public class NumberEventReceiver : MonoBehaviour
{
    //[SerializeField]
    //internal float _destroyTime = 5.0f;

    [SerializeField]
    internal Vector3 _viewOffset = new Vector3(2, 0, 0);

    [SerializeField]
    internal GameObject[] _prefabs;

    //private List<GameObject> __gameObjects;
    private static List<GameObject>[] __gameObjectPool;
    private static Camera _mainCamera;

    [UnityEngine.Scripting.Preserve]
    public void SetNumber(Parameters parameters)
    {
        int value = parameters[0], count = 0, temp = value;
        while (temp != 0)
        {
            ++count;

            temp /= 10;
        }

        if (count == 0)
            return;

        if(_mainCamera == null)
            _mainCamera = Camera.main;

        GameObject instance;
        Quaternion rotation = _mainCamera.transform.rotation;
        Vector3 positionOffset = _mainCamera.ScreenToWorldPoint(_viewOffset), position = transform.position - positionOffset * (count * 0.5f);
        for(int i = count - 1; i >= 0; ++i)
        {
            temp = value / (int)Mathf.Pow(10, i);
            instance = Instantiate(_prefabs[temp], position, rotation);

            position += positionOffset;
        }
    }

    private GameObject __Instantiate(int number, in Vector3 position, in Quaternion rotation)
    {
        var gameObjects = __gameObjectPool == null ? null : __gameObjectPool[number];
        GameObject gameObject = null;
        int numGameObjects = gameObjects == null ? 0 : gameObjects.Count;
        if(numGameObjects > 0)
        {
            gameObject = gameObjects[--numGameObjects];

            gameObjects.RemoveAt(numGameObjects);
        }

        if(gameObject == null)
            gameObject = Instantiate(_prefabs[number], position, rotation);
        else
        {
            var transform = gameObject.transform;
            transform.position = position;
            transform.rotation = rotation;
        }

        gameObject.SetActive(true);

        return gameObject;
    }
}
