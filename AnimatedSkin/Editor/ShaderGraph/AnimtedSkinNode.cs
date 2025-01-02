using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Internal;

namespace UnityEditor.ShaderGraph
{
    [Title("Input", "Mesh Deformation", "Animated Skin Node")]
    class AnimatedSkinNode : AbstractMaterialNode, 
        IGeneratesBodyCode, 
        IGeneratesFunction, 
        IMayRequireVertexSkinning,
        //IMayRequireMeshUV, 
        IMayRequirePosition, 
        IMayRequireNormal, 
        IMayRequireTangent
    {
        public const int kTexSlotId = 0;
        public const int kPixelCountPerFrameSlotId = 1;
        //public const int kBoneIndexSlotId = 8;
        //public const int kBoneWeightSlotId = 9;
        public const int kPositionSlotId = 2;
        public const int kNormalSlotId = 3;
        public const int kTangentSlotId = 4;
        public const int kPositionOutputSlotId = 5;
        public const int kNormalOutputSlotId = 6;
        public const int kTangentOutputSlotId = 7;

        public const string kSlotTexName = "Animated Skin Map";
        public const string kSlotPixelCountPerFrameName = "Pixel Count Per Frame";
        
        //public const string kSlotBoneIndexName = "Bone Index";
        //public const string kSlotBoneWeightName = "Bone Weight";
        
        public const string kSlotPositionName = "Vertex Position";
        public const string kSlotNormalName = "Vertex Normal";
        public const string kSlotTangentName = "Vertex Tangent";
        public const string kOutputSlotPositionName = "Skinned Position";
        public const string kOutputSlotNormalName = "Skinned Normal";
        public const string kOutputSlotTangentName = "Skinned Tangent";

        public AnimatedSkinNode()
        {
            name = "Animated Skin Node";
            UpdateNodeAfterDeserialization();
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Texture2DMaterialSlot(kTexSlotId, kSlotTexName, kSlotTexName, SlotType.Input, ShaderStageCapability.Vertex));
            
            AddSlot(new Vector1MaterialSlot(
                kPixelCountPerFrameSlotId, 
                kSlotPixelCountPerFrameName, 
                kSlotPixelCountPerFrameName,
                SlotType.Input, 
                0.0f, 
                ShaderStageCapability.Vertex));

            /*AddSlot(new Vector4MaterialSlot(kBoneIndexSlotId, kSlotBoneIndexName, kSlotBoneIndexName,
                SlotType.Input, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector4MaterialSlot(kBoneWeightSlotId, kSlotBoneWeightName, kSlotBoneWeightName,
                SlotType.Input, Vector3.zero, ShaderStageCapability.Vertex));*/
            
            AddSlot(new PositionMaterialSlot(kPositionSlotId, kSlotPositionName, kSlotPositionName,
                CoordinateSpace.Object, ShaderStageCapability.Vertex));
            AddSlot(new NormalMaterialSlot(kNormalSlotId, kSlotNormalName, kSlotNormalName, CoordinateSpace.Object,
                ShaderStageCapability.Vertex));
            AddSlot(new TangentMaterialSlot(kTangentSlotId, kSlotTangentName, kSlotTangentName, CoordinateSpace.Object,
                ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kPositionOutputSlotId, kOutputSlotPositionName, kOutputSlotPositionName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kNormalOutputSlotId, kOutputSlotNormalName, kOutputSlotNormalName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kTangentOutputSlotId, kOutputSlotTangentName, kOutputSlotTangentName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            RemoveSlotsNameNotMatching(new[]
            {
                kTexSlotId, 
                kPixelCountPerFrameSlotId, 
                
                //kBoneIndexSlotId, kBoneWeightSlotId, 
                
                kPositionSlotId, 
                kNormalSlotId, 
                kTangentSlotId, 
                kPositionOutputSlotId, 
                kNormalOutputSlotId,
                kTangentOutputSlotId
            });
        }

        public bool RequiresVertexSkinning(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return true;
        }

        /*public bool RequiresMeshUV(UVChannel channel, ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            switch (channel)
            {
                case UVChannel.UV2:
                case UVChannel.UV3:
                    return true;
            }

            return false;
        }*/

        public NeededCoordinateSpace RequiresPosition(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public NeededCoordinateSpace RequiresNormal(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public NeededCoordinateSpace RequiresTangent(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            /*properties.AddShaderProperty(new Vector1ShaderProperty()
            {
                displayName = "Animated Skin Pixel Count Per Frame",
                overrideReferenceName = "_AnimatedSkinPixelCountPerFrame",
                //overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.UnityPerMaterial,
                hidden = false,
                value = 0
            });
            
            properties.AddShaderProperty(new Texture2DShaderProperty()
            {
                displayName = "Animated Skin Tex",
                overrideReferenceName = "_AnimatedSkinTex",
                generatePropertyBlock = false,
                //hlslDeclarationOverride = HLSLDeclaration.UnityPerMaterial,
                defaultType = Texture2DShaderProperty.DefaultType.White,
                //value = 0
            });*/

            base.CollectShaderProperties(properties, generationMode);
        }

        public void GenerateNodeCode(ShaderStringBuilder sb, GenerationMode generationMode)
        {
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kPositionOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kNormalOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kTangentOutputSlotId));
            if (generationMode == GenerationMode.ForReals)
            {
                sb.AppendLine($"{GetFunctionName()}(" +
                              $"{GetSlotValue(kTexSlotId, generationMode)}, " +
                              $"(uint)({GetSlotValue(kPixelCountPerFrameSlotId, generationMode)}), " +
                              $"IN.BoneIndices, " +
                              $"IN.BoneWeights, " +
                              //$"{GetSlotValue(kBoneIndexSlotId, generationMode)}, " +
                              //$"{GetSlotValue(kBoneWeightSlotId, generationMode)}, " +
                              
                              $"{GetSlotValue(kPositionSlotId, generationMode)}, " +
                              $"{GetSlotValue(kNormalSlotId, generationMode)}, " +
                              $"{GetSlotValue(kTangentSlotId, generationMode)}, " +
                              $"{GetVariableNameForSlot(kPositionOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kNormalOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kTangentOutputSlotId)});");
            }
        }

        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction("AnimatedSkin", sb =>
            {
                sb.AppendLine("UNITY_INSTANCING_BUFFER_START(AnimatedSkin)");
                using (sb.IndentScope())
                {
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _AnimatedSkinFrameCountPerSecond)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _AnimatedSkinFrameCount)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _AnimatedSkinStartFrame)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _AnimatedSkinOffsetSeconds)");
                }
                sb.AppendLine("UNITY_INSTANCING_BUFFER_END(AnimatedSkin)");
            });
            
            registry.ProvideFunction("AnimatedSkinGetUV", sb =>
            {
                sb.AppendLine($"float4 AnimatedSkinGetUV(uint index, float4 texelSize)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("uint z = (uint)texelSize.z;");
                    sb.AppendLine("uint row = index / z;");
                    sb.AppendLine("uint col = index % z;");
                    sb.AppendLine("return float4(col / texelSize.z, row / texelSize.w, 0, 0);");
                }

                sb.AppendLine("}");
            });
            
            registry.ProvideFunction("AnimatedSkinGetMatrix", sb =>
            {
                sb.AppendLine($"float3x4 AnimatedSkinGetMatrix(uint startIndex, uint boneIndex, UnityTexture2D map)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    //sb.AppendLine("#if (SHADER_TARGET >= 41)");
                    sb.AppendLine("uint matrixIndex = startIndex + boneIndex * 3;");
                    sb.AppendLine("float4 row0 = tex2Dlod(map, AnimatedSkinGetUV(matrixIndex + 0, map.texelSize));");
                    sb.AppendLine("float4 row1 = tex2Dlod(map, AnimatedSkinGetUV(matrixIndex + 1, map.texelSize));");
                    sb.AppendLine("float4 row2 = tex2Dlod(map, AnimatedSkinGetUV(matrixIndex + 2, map.texelSize));");
                    //sb.AppendLine("#else");
                    //sb.AppendLine("float4 row0 = float4(1.0f, 0, 0, 0);");
                    //sb.AppendLine("float4 row1 = float4(0, 1.0f, 0, 0);");
                    //sb.AppendLine("float4 row2 = float4(0, 0, 1.0f, 0);");
                    //sb.AppendLine("#endif");
                    
                    sb.AppendLine("return float3x4(row0, row1, row2);");
                }

                sb.AppendLine("}");
            });

            registry.ProvideFunction(GetFunctionName(), sb =>
            {
                sb.AppendLine($"void {GetFunctionName()}(" +
                              "UnityTexture2D map, " +
                              "uint pixelCountPerFrame, " +
                              "uint4 indices, " +
                              //"$precision4 indices, " +
                              "$precision4 weights, " +
                              "$precision3 positionIn, " +
                              "$precision3 normalIn, " +
                              "$precision3 tangentIn, " +
                              "out $precision3 positionOut, " +
                              "out $precision3 normalOut, " +
                              "out $precision3 tangentOut)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("positionOut = 0;");
                    sb.AppendLine("normalOut = 0;");
                    sb.AppendLine("tangentOut = 0;");

                    sb.AppendLine(
                        "uint offsetFrame = (uint)((_Time.y + UNITY_ACCESS_INSTANCED_PROP(AnimatedSkin, _AnimatedSkinOffsetSeconds)) * asuint(UNITY_ACCESS_INSTANCED_PROP(AnimatedSkin, _AnimatedSkinFrameCountPerSecond)));");
                    sb.AppendLine(
                        "uint currentFrame = asuint(UNITY_ACCESS_INSTANCED_PROP(AnimatedSkin, _AnimatedSkinStartFrame)) + offsetFrame % asuint(UNITY_ACCESS_INSTANCED_PROP(AnimatedSkin, _AnimatedSkinFrameCount));");
                    sb.AppendLine(
                        "uint clampedIndex = currentFrame * pixelCountPerFrame;");
                    
                    /*sb.AppendLine(
                        "uint offsetFrame = (uint)((_Time.y + 0.0f) * 30);");
                    sb.AppendLine(
                        "uint currentFrame = 0 + offsetFrame % 1;");
                    sb.AppendLine(
                        "uint clampedIndex = currentFrame * 243;");*/

                    sb.AppendLine(
                        "float totalWeight = 0.0f, weight;");

                    sb.AppendLine("for (int i = 0; i < 3; ++i)");
                    sb.AppendLine("{");
                    using (sb.IndentScope())
                    {
                        sb.AppendLine("$precision3x4 skinMatrix = AnimatedSkinGetMatrix(clampedIndex, indices[i], map);");
                        sb.AppendLine("$precision3 vtransformed = mul(skinMatrix, $precision4(positionIn, 1));");
                        sb.AppendLine("$precision3 ntransformed = mul(skinMatrix, $precision4(normalIn, 0));");
                        sb.AppendLine("$precision3 ttransformed = mul(skinMatrix, $precision4(tangentIn, 0));");
                        sb.AppendLine("");
                        sb.AppendLine("weight = weights[i];");
                        sb.AppendLine("positionOut += vtransformed * weight;");
                        sb.AppendLine("normalOut   += ntransformed * weight;");
                        sb.AppendLine("tangentOut  += ttransformed * weight;");
                        sb.AppendLine("totalWeight += weight;");
                    }
                    sb.AppendLine("}");
                    
                    sb.AppendLine("$precision3x4 skinMatrix = AnimatedSkinGetMatrix(clampedIndex, indices.w, map);");
                    sb.AppendLine("$precision3 vtransformed = mul(skinMatrix, $precision4(positionIn, 1));");
                    sb.AppendLine("$precision3 ntransformed = mul(skinMatrix, $precision4(normalIn, 0));");
                    sb.AppendLine("$precision3 ttransformed = mul(skinMatrix, $precision4(tangentIn, 0));");
                    sb.AppendLine("");
                    sb.AppendLine("weight = 1.0 - totalWeight;");
                    sb.AppendLine("positionOut += vtransformed * weight;");
                    sb.AppendLine("normalOut   += ntransformed * weight;");
                    sb.AppendLine("tangentOut  += ttransformed * weight;");
                    
                    //sb.AppendLine("positionOut = positionIn;");
                    //sb.AppendLine("normalOut   = normalIn;");
                    //sb.AppendLine("tangentOut  = tangentIn;");
                }

                sb.AppendLine("}");
            });
        }

        string GetFunctionName()
        {
            return "AnimatedSkin_$precision";
        }
    }
}