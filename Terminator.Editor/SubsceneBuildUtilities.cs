using System;
using System.Collections.Generic;
using System.IO;
using Unity.Scenes.Editor;
using UnityEngine;
using UnityEditor;
using Unity.Entities.Build;
using Unity.Entities.Content;

static class SubSceneBuildUtilities
{
    [MenuItem("SubSceneBuildUtilities/BuildContent")]
    //prepares the content files for publish.  The original files can be deleted or retained during this process by changing the last parameter of the PublishContent call.
    static void BuildContent()
    {
        var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish",
            Path.GetDirectoryName(Application.dataPath), "Builds");
        if (!string.IsNullOrEmpty(buildFolder))
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;

            var instance = DotsGlobalSettings.Instance;
            var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client ? instance.GetClientGUID() : instance.GetServerGUID();
            if (!playerGuid.IsValid)
                throw new Exception("Invalid Player GUID");

            var subSceneGuids = new HashSet<Unity.Entities.Hash128>();
            foreach(var sceneGUID in Selection.assetGUIDs)
            {
                if(!GUID.TryParse(sceneGUID, out var guid))
                    continue;
                
                var ssGuids = EditorEntityScenes.GetSubScenes(guid);
                foreach (var ss in ssGuids)
                    subSceneGuids.Add(ss);
            }
            RemoteContentCatalogBuildUtility.BuildContent(subSceneGuids, playerGuid, buildTarget, buildFolder);
        }
    }
    
    [MenuItem("SubSceneBuildUtilities/PublishContent")]
    //This method is somewhat complicated because it will build the scenes from a player build but without fully building the player.
    static void PublishContent()
    {
        var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish",
        Path.GetDirectoryName(Application.dataPath), "Builds");
        if (!string.IsNullOrEmpty(buildFolder))
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var tmpBuildFolder = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                        $"/Library/ContentUpdateBuildDir/{PlayerSettings.productName}");

            var instance = DotsGlobalSettings.Instance;
            var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client ? instance.GetClientGUID() : instance.GetServerGUID();
            if (!playerGuid.IsValid)
                throw new Exception("Invalid Player GUID");

            var subSceneGuids = new HashSet<Unity.Entities.Hash128>();
            foreach(var sceneGUID in Selection.assetGUIDs)
            {
                if(!GUID.TryParse(sceneGUID, out var guid))
                    continue;
                
                var ssGuids = EditorEntityScenes.GetSubScenes(guid);
                foreach (var ss in ssGuids)
                    subSceneGuids.Add(ss);
            }
            RemoteContentCatalogBuildUtility.BuildContent(subSceneGuids, playerGuid, buildTarget, tmpBuildFolder);

            var publishFolder = Path.Combine(Application.dataPath, buildFolder);//Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", $"{buildFolder}-RemoteContent");
            RemoteContentCatalogBuildUtility.PublishContent(tmpBuildFolder, publishFolder, f => new string[] { "all" });
        }
    }
}