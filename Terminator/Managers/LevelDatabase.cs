using System;
using UnityEngine;
using ZG;

[CreateAssetMenu(menuName = "LevelDatabase", fileName = "Level Database")]
public class LevelDatabase : ScriptableObject
{
    [Serializable]
    public struct Scene
    {
        [Serializable]
        public struct Stage
        {
            public string name;

            public string bossTitle;
            public string bossDescription;

            public int index;
        }

        public string name;
        public string title;
        public string description;

        public AssetObjectLoader prefab;

        public Stage[] stages;

        public int StageIndexOf(int stageIndex)
        {
            int numStages = stages.Length;
            for (int i = 0; i < numStages; ++i)
            {
                if (stages[i].index == stageIndex)
                    return i;
            }

            return -1;
        }
    }

    [Serializable]
    public struct Level
    {
        [Flags]
        public enum Flag
        {
            Chapter = 0x01
        }

        public string name;

        public Flag flag;

        public Scene[] scenes;

        public Scene.Stage GetSceneStage(string sceneName, int stageIndex)
        {
            if (scenes != null)
            {
                int index;
                foreach (var scene in scenes)
                {
                    if(scene.name != sceneName)
                        continue;
                    
                    index = scene.StageIndexOf(stageIndex);
                    if (index != -1)
                        return scene.stages[index];
                }
            }

            return default;
        }

#if UNITY_EDITOR
        [CSVField]
        public string 章节名称
        {
            set => name = value;
        }

        [CSVField]
        public int 章节标签
        {
            set => flag = (Flag)value;
        }

        [CSVField]
        public string 章节场景
        {
            set
            {
                string[] parameters = value.Split('+'), temp, temp2;
                int i, j, stageIndex = 0, numStages, numParameters = parameters.Length;
                scenes = new Scene[numParameters];
                Scene scene;
                for (i = 0; i < numParameters; ++i)
                {
                    temp = parameters[i].Split(':');
                    scene.name = temp[0];
                    scene.title = temp[1];
                    scene.description = temp[2];
                    scene.description = scene.description.Replace(@"\n", "\n");
                    scene.prefab = new AssetObjectLoader(AssetObjectLoader.Space.Local, temp[3], temp[4], null, null);
                    temp = temp[5].Split('|');
                    numStages = temp.Length;
                    scene.stages = new Scene.Stage[numStages];
                    for (j = 0; j < numStages; ++j)
                    {
                        temp2 = temp[j].Split('*');
                        ref var stage = ref scene.stages[j];
                        stage.name = temp2[0].Replace(@"\n", "\n");
                        stage.bossTitle = temp2[1];
                        stage.bossDescription = temp2[2];
                        stage.index = temp2.Length < 4 ? stageIndex++ : int.Parse(temp2[3]);
                    }

                    scenes[i] = scene;
                }
            }
        }
#endif
    }

    public Level[] levels;

#if UNITY_EDITOR
    [SerializeField, CSV("levels", guidIndex = -1, nameIndex = 0)] 
    internal string _levelsPath;
#endif
}
