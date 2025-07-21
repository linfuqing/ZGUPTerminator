#ifndef CAUSTICS_UTILITY_INCLUDED
#define CAUSTICS_UTILITY_INCLUDED

//TEXTURE2D(_CausticsTex); SAMPLER(sampler_CausticsTex);

float2 Panner(float2 uv, float2 offset, float tiling)
{
	return  _Time.y * offset + uv * tiling;
}

/*half3 TexCaustics(float2 uv, float mipLod)
{
	float2 normal = _CausticsDistortionStrength * SampleNormal(uv * _CausticsDistortionScale, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), 1.0f).xz;

	float2 uv1 = normal * _CausticsST1.y + Panner(uv, _CausticsST1.zw, _CausticsST1.x);// float4((normal * _CausticsST1.y + Panner(uv, _CausticsST1.zw, _CausticsST1.x)), 0.0f, mipLod);
	float2 uv2 = normal * _CausticsST2.y + Panner(uv, _CausticsST2.zw, _CausticsST2.x);// float4((normal * _CausticsST2.y + Panner(uv, _CausticsST2.zw, _CausticsST2.x)), 0.0f, mipLod);

	return SAMPLE_TEXTURE2D_LOD(_CausticsTex, sampler_CausticsTex, uv1, mipLod).xyz + SAMPLE_TEXTURE2D_LOD(_CausticsTex, sampler_CausticsTex, uv2, mipLod).xyz;
}*/

half3 RGBSplit(UnityTexture2D causticsTex, float2 uv, float mipLod, float split)
{
	float4 uvr = float4(uv + float2(split, split), 0, mipLod);
	float4 uvg = float4(uv + float2(split, -split), 0, mipLod);
	float4 uvb = float4(uv + float2(-split, -split), 0, mipLod);

	half3 r = tex2Dlod(causticsTex, uvr);
	half3 g = tex2Dlod(causticsTex, uvg);
	half3 b = tex2Dlod(causticsTex, uvb);

	return (r + g + b) / 3.0f;//half3(r, g, b);
}

half3 TexCaustics(UnityTexture2D causticsTex, float4 st1, float4 st2, float2 uv, float mipLod)
{
	float2 uv1 = Panner(uv, st1.zw, st1.x);
	float2 uv2 = Panner(uv, st2.zw, st2.x);

	half3 texture1 = RGBSplit(causticsTex, uv1, mipLod, st1.y);
	half3 texture2 = RGBSplit(causticsTex, uv2, mipLod, st2.y);

	half3 textureCombined = min(texture1, texture2);

	return textureCombined;
}

half3 ApplyCaustics(
	float focalDepth, 
	float invDepthOfField, 
	float strength, 
	half3 lightColor,
	half3 lightDirectionWS,
	float3 scenePos, 
	float4 st1,
	float4 st2,
	UnityTexture2D causticsTex)
{
	// Compute mip index manually, with bias based on sea floor depth. We compute it manually because if it is computed automatically it produces ugly patches
	// where samples are stretched/dilated. The bias is to give a focusing effect to caustics - they are sharpest at a particular depth. This doesn't work amazingly
	// well and could be replaced.
	float mipLod = abs(scenePos.y - focalDepth) * invDepthOfField;
	// project along light dir, but multiply by a fudge factor reduce the angle bit - compensates for fact that in real life
	// caustics come from many directions and don't exhibit such a strong directonality
	float2 surfacePosXZ = scenePos.xz + lightDirectionWS.xz * (scenePos.y / (4.0 * lightDirectionWS.y));
	
	//return causticsStrength * tex2Dlod(_MainTex, uv).xyz;
	return strength * lightColor * TexCaustics(causticsTex, st1, st2, surfacePosXZ, mipLod);
}

void ApplyCaustics_float(
	float focalDepth, 
	float invDepthOfField, 
	float strength, 
	half3 lightColor,
	half3 lightDirectionWS,
	float3 scenePos, 
	float4 st1,
	float4 st2,
	UnityTexture2D causticsTex,
	out float3 result)
{
	result = ApplyCaustics(
		focalDepth,
		invDepthOfField,
		strength,
		lightColor,
		lightDirectionWS,
		scenePos,
		st1,
		st2,
		causticsTex);
}

void ApplyCaustics_half(
	float focalDepth, 
	float invDepthOfField, 
	float strength, 
	half3 lightColor,
	half3 lightDirectionWS,
	float3 scenePos, 
	float4 st1,
	float4 st2,
	UnityTexture2D causticsTex,
	out half3 result)
{
	result = ApplyCaustics(
		focalDepth,
		invDepthOfField,
		strength,
		lightColor,
		lightDirectionWS,
		scenePos,
		st1,
		st2,
		causticsTex);
}
#endif
