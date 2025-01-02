//#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class AnimatedSkinGenerator : EditorWindow
{
    private const int BoneMatrixRowCount = 3;
    private const int TargetFrameRate = 30;

    [MenuItem("AnimatedSkinGenerator/Config")]
    public static void Config()
    {
        GetWindow<AnimatedSkinGenerator>();
    }

    [MenuItem("AnimatedSkinGenerator/Generate")]
    public static void Generate()
    {
        var targetObjects = Selection.gameObjects;
        if (targetObjects == null)
            return;

        foreach (var targetObject in targetObjects)
            GenerateGameObject(targetObject);
    }

    public static void GenerateGameObject(GameObject targetObject)
    {
        if (targetObject == null)
        {
            EditorUtility.DisplayDialog("Warning", "Selected object type is not gameobject.", "OK");
            return;
        }

        var skinnedMeshRenderers = targetObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (!skinnedMeshRenderers.Any() || skinnedMeshRenderers.Count() != 1)
        {
            EditorUtility.DisplayDialog("Warning", "Selected object does not have one skinnedMeshRenderer.", "OK");
            return;
        }

        var animator = targetObject.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            EditorUtility.DisplayDialog("Warning", "Selected object does not have Animator.", "OK");
            return;
        }

        var selectionPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(targetObject));
        var skinnedMeshRenderer = skinnedMeshRenderers.First();
        var clips = animator.runtimeAnimatorController.animationClips;

        Directory.CreateDirectory(Path.Combine(selectionPath, "AnimatedSkinMesh"));

        var animationTexture = GenerateAnimationTexture(targetObject, clips, skinnedMeshRenderer);
        AssetDatabase.CreateAsset(animationTexture, string.Format("{0}/AnimatedSkinMesh/{1}_AnimationTexture.asset", selectionPath, targetObject.name));

        var mesh = GenerateUvBoneWeightedMesh(skinnedMeshRenderer);
        AssetDatabase.CreateAsset(mesh, string.Format("{0}/AnimatedSkinMesh/{1}_Mesh.asset", selectionPath, targetObject.name));

        var material = GenerateMaterial(targetObject, skinnedMeshRenderer, animationTexture, clips, skinnedMeshRenderer.bones.Length);
        AssetDatabase.CreateAsset(material, string.Format("{0}/AnimatedSkinMesh/{1}_Material.asset", selectionPath, targetObject.name));

        var database = GenerateDatabase(clips);
        AssetDatabase.CreateAsset(database, string.Format("{0}/AnimatedSkinMesh/{1}_Database.asset", selectionPath, targetObject.name));
        
        var go = GenerateMeshRendererObject(targetObject, mesh, material, database);
        PrefabUtility.SaveAsPrefabAsset(go, string.Format("{0}/AnimatedSkinMesh/{1}.prefab", selectionPath, targetObject.name));

        Object.DestroyImmediate(go);
    }

    public static Mesh GenerateUvBoneWeightedMesh(SkinnedMeshRenderer smr)
    {
        var mesh = Object.Instantiate(smr.sharedMesh);

        /*var boneSets = smr.sharedMesh.boneWeights;
        var boneIndexes = boneSets.Select(x => new Vector4(x.boneIndex0, x.boneIndex1, x.boneIndex2, x.boneIndex3)).ToList();
        var boneWeights = boneSets.Select(x => new Vector4(x.weight0, x.weight1, x.weight2, x.weight3)).ToList();

        mesh.SetUVs(2, boneIndexes);
        mesh.SetUVs(3, boneWeights);*/

        return mesh;
    }

    public static Texture GenerateAnimationTexture(GameObject targetObject, IEnumerable<AnimationClip> clips, SkinnedMeshRenderer smr)
    {
        var textureBoundary = GetCalculatedTextureBoundary(clips, smr.bones.Count());

        var texture = new Texture2D((int)textureBoundary.x, (int)textureBoundary.y, TextureFormat.RGBAHalf, false, true);
        var pixels = texture.GetPixels();
        var pixelIndex = 0;

        //Setup 0 to bindPoses
        foreach (var boneMatrix in smr.bones.Select((b, idx) => b.localToWorldMatrix * smr.sharedMesh.bindposes[idx]))
        {
            pixels[pixelIndex++] = new Color(boneMatrix.m00, boneMatrix.m01, boneMatrix.m02, boneMatrix.m03);
            pixels[pixelIndex++] = new Color(boneMatrix.m10, boneMatrix.m11, boneMatrix.m12, boneMatrix.m13);
            pixels[pixelIndex++] = new Color(boneMatrix.m20, boneMatrix.m21, boneMatrix.m22, boneMatrix.m23);
        }

        foreach (var clip in clips)
        {
            var totalFrames = (int)(clip.length * TargetFrameRate);
            foreach (var frame in Enumerable.Range(0, totalFrames))
            {
                clip.SampleAnimation(targetObject, (float)frame / TargetFrameRate);

                foreach (var boneMatrix in smr.bones.Select((b, idx) => b.localToWorldMatrix * smr.sharedMesh.bindposes[idx]))
                {
                    pixels[pixelIndex++] = new Color(boneMatrix.m00, boneMatrix.m01, boneMatrix.m02, boneMatrix.m03);
                    pixels[pixelIndex++] = new Color(boneMatrix.m10, boneMatrix.m11, boneMatrix.m12, boneMatrix.m13);
                    pixels[pixelIndex++] = new Color(boneMatrix.m20, boneMatrix.m21, boneMatrix.m22, boneMatrix.m23);
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Point;

        return texture;
    }

    public static Vector2 GetCalculatedTextureBoundary(IEnumerable<AnimationClip> clips, int boneLength)
    {
        var boneMatrixCount = BoneMatrixRowCount * boneLength;

        var totalPixels = clips.Aggregate(boneMatrixCount, (pixels, currentClip) => pixels + boneMatrixCount * (int)(currentClip.length * TargetFrameRate));

        var textureWidth = 1;
        var textureHeight = 1;

        while (textureWidth * textureHeight < totalPixels)
        {
            if (textureWidth <= textureHeight)
            {
                textureWidth *= 2;
            }
            else
            {
                textureHeight *= 2;
            }
        }

        return new Vector2(textureWidth, textureHeight);
    }

    public static Material GenerateMaterial(GameObject targetObject, SkinnedMeshRenderer smr, Texture texture, IEnumerable<AnimationClip> clips, int boneLength)
    {
        var material = Object.Instantiate(smr.sharedMaterial);
        material.shader = Shader.Find("Shader Graphs/AnimatedSkinTexture");
        material.SetTexture("_AnimatedSkinMap", texture);
        material.SetInt("_PixelCountPerFrame", BoneMatrixRowCount * boneLength);
        material.enableInstancing = true;

        return material;
    }
    
    public static AnimatedSkinDatabase GenerateDatabase(IEnumerable<AnimationClip> clips)
    {
        var animations = new List<AnimatedSkinDatabase.Animation>();
        AnimatedSkinDatabase.Animation animation;
        var currentClipFrames = 0;

        foreach (var clip in clips)
        {
            var frameCount = (int)(clip.length * TargetFrameRate);
            var startFrame = currentClipFrames + 1;
            var endFrame = startFrame + frameCount - 1;

            animation.name = clip.name;
            animation.startFrame = startFrame;
            //animation.endFrame = endFrame;
            animation.frameCount = frameCount;
            animations.Add(animation);

            currentClipFrames = endFrame;
        }

        var database = AnimatedSkinDatabase.CreateInstance<AnimatedSkinDatabase>();
        database.animations = animations.ToArray();
        
        return database;
    }

    public static GameObject GenerateMeshRendererObject(GameObject targetObject, Mesh mesh, Material material, AnimatedSkinDatabase database)
    {
        var go = new GameObject();
        go.name = targetObject.name;

        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.lightProbeUsage = LightProbeUsage.Off;

        var animatedSkinController = go.AddComponent<AnimatedSkinController>();
        animatedSkinController.Setup(database);

        return go;
    }

    public void OnGUI()
    {
        EditorGUILayout.ObjectField(null, typeof(Material), false);
    }
}
//#endif