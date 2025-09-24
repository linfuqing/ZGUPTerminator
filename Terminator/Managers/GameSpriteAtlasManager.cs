using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.U2D;
using ZG;

public class GameSpriteAtlasManager : MonoBehaviour
{
    [Serializable]
    public struct Asset
    {
        public string name;
        
        public string filename;
    }
    
    [SerializeField]
    internal UnityEvent _onAllAssetsLoaded;
    
    [SerializeField]
    internal Asset[] _assets;

    private AssetBundleLoader<SpriteAtlas>[] __loaders;

    IEnumerator Start()
    {
        int numAssets = _assets == null ? 0 : _assets.Length;
        __loaders = new AssetBundleLoader<SpriteAtlas>[numAssets];
        
        var assetManager = GameAssetManager.instance?.dataManager;
        if (assetManager != null)
        {
            AssetBundleLoader<SpriteAtlas> loader;
            Asset asset;
            for (int i = 0; i < numAssets; ++i)
            {
                asset = _assets[i];

                loader = new AssetBundleLoader<SpriteAtlas>(asset.filename, asset.name, assetManager);

                __loaders[i] = loader;

                print($"{name} start to load sprite atlas {asset.name} from {asset.filename}");
                
                yield return loader;
                
                print($"{name} end to load sprite atlas {asset.name} from {asset.filename}");
            }
        }

        if(_onAllAssetsLoaded != null)
            _onAllAssetsLoaded.Invoke();
    }

    void OnDestroy()
    {
        if (__loaders != null)
        {
            foreach (var loader in __loaders)
                loader.Dispose();
        }
    }
}
