using System;
using System.Collections;
using UnityEngine;
using UnityEngine.U2D;
using ZG;

public class SpriteAtlasManager : MonoBehaviour
{
    [Serializable]
    public struct Asset
    {
        public string name;
        
        public string filename;
    }
    
    [SerializeField]
    internal Asset[] _assets;

    private AssetBundleLoader<SpriteAtlas>[] __loaders;

    IEnumerator Start()
    {
        int numAssets = _assets == null ? 0 : _assets.Length;
        __loaders = new AssetBundleLoader<SpriteAtlas>[numAssets];
        
        var assetManager = GameAssetManager.instance.dataManager;
        AssetBundleLoader<SpriteAtlas> loader;
        Asset asset;
        for(int i = 0; i < numAssets; ++i)
        {
            asset = _assets[i];
            
            loader = new AssetBundleLoader<SpriteAtlas>(asset.filename, asset.name, assetManager);

            __loaders[i] = loader;
            
            yield return loader;
        }
    }

    void OnDestroy()
    {
        foreach (var loader in __loaders)
            loader.Dispose();
    }
}
