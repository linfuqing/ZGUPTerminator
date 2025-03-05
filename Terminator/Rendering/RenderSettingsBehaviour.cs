using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZG
{
    public class RenderSettingsBehaviour : MonoBehaviour
    {
        [Flags]
        public enum AlwaysUpdate
        {
            AmbientEquatorColor = 0x0001,
            AmbientGroundColor = 0x0002,
            AmbientIntensity = 0x0004,
            AmbientLight = 0x0008,
            AmbientSkyColor = 0x0010,
            FlareFadeSpeed = 0x0020,
            FlareStrength = 0x0040,
            FogColor = 0x0080,
            FogDensity = 0x0100,
            FogEndDistance = 0x0200,
            FogStartDistance = 0x0400,
            HaloStrength = 0x0800,
            ReflectionBounces = 0x1000,
            ReflectionIntensity = 0x2000
        }

        [Serializable]
        public struct Data
        {
            public Color ambientEquatorColor;
            public Color ambientGroundColor;
            public float ambientIntensity;
            public Color ambientLight;
            public Color ambientSkyColor;
            public float flareFadeSpeed;
            public float flareStrength;
            public Color fogColor;
            public float fogDensity;
            public float fogEndDistance;
            public float fogStartDistance;
            public float haloStrength;
            public int reflectionBounces;
            public float reflectionIntensity;
        }

        [Mask]
        public AlwaysUpdate alwaysUpdate;

        public Data data;
        private Data __data;

        void Start()
        {
            __data = data;
        }

        void Update()
        {
            if ((alwaysUpdate & AlwaysUpdate.AmbientEquatorColor) == AlwaysUpdate.AmbientEquatorColor || data.ambientEquatorColor != __data.ambientEquatorColor)
                RenderSettings.ambientEquatorColor = __data.ambientEquatorColor = data.ambientEquatorColor;

            if ((alwaysUpdate & AlwaysUpdate.AmbientGroundColor) == AlwaysUpdate.AmbientGroundColor || data.ambientGroundColor != __data.ambientGroundColor)
                RenderSettings.ambientGroundColor = __data.ambientGroundColor = data.ambientGroundColor;

            if ((alwaysUpdate & AlwaysUpdate.AmbientIntensity) == AlwaysUpdate.AmbientIntensity || data.ambientIntensity != __data.ambientIntensity)
                RenderSettings.ambientIntensity = __data.ambientIntensity = data.ambientIntensity;

            if ((alwaysUpdate & AlwaysUpdate.AmbientLight) == AlwaysUpdate.AmbientLight || data.ambientLight != __data.ambientLight)
                RenderSettings.ambientLight = __data.ambientLight = data.ambientLight;

            if ((alwaysUpdate & AlwaysUpdate.AmbientSkyColor) == AlwaysUpdate.AmbientSkyColor || data.ambientSkyColor != __data.ambientSkyColor)
                RenderSettings.ambientSkyColor = __data.ambientSkyColor = data.ambientSkyColor;

            if ((alwaysUpdate & AlwaysUpdate.FlareFadeSpeed) == AlwaysUpdate.FlareFadeSpeed || data.flareFadeSpeed != __data.flareFadeSpeed)
                RenderSettings.flareFadeSpeed = __data.flareFadeSpeed = data.flareFadeSpeed;

            if ((alwaysUpdate & AlwaysUpdate.FlareStrength) == AlwaysUpdate.FlareStrength || data.flareStrength != __data.flareStrength)
                RenderSettings.flareStrength = __data.flareStrength = data.flareStrength;

            if ((alwaysUpdate & AlwaysUpdate.FogColor) == AlwaysUpdate.FogColor || data.fogColor != __data.fogColor)
                RenderSettings.fogColor = __data.fogColor = data.fogColor;

            if ((alwaysUpdate & AlwaysUpdate.FogDensity) == AlwaysUpdate.FogDensity || data.fogDensity != __data.fogDensity)
                RenderSettings.fogDensity = __data.fogDensity = data.fogDensity;

            if ((alwaysUpdate & AlwaysUpdate.FogEndDistance) == AlwaysUpdate.FogEndDistance || data.fogEndDistance != __data.fogEndDistance)
                RenderSettings.fogEndDistance = __data.fogEndDistance = data.fogEndDistance;

            if ((alwaysUpdate & AlwaysUpdate.FogStartDistance) == AlwaysUpdate.FogStartDistance || data.fogStartDistance != __data.fogStartDistance)
                RenderSettings.fogStartDistance = __data.fogStartDistance = data.fogStartDistance;

            if ((alwaysUpdate & AlwaysUpdate.HaloStrength) == AlwaysUpdate.HaloStrength || data.haloStrength != __data.haloStrength)
                RenderSettings.haloStrength = __data.haloStrength = data.haloStrength;

            if ((alwaysUpdate & AlwaysUpdate.ReflectionBounces) == AlwaysUpdate.ReflectionBounces || data.reflectionBounces != __data.reflectionBounces)
                RenderSettings.reflectionBounces = __data.reflectionBounces = data.reflectionBounces;

            if ((alwaysUpdate & AlwaysUpdate.ReflectionIntensity) == AlwaysUpdate.ReflectionIntensity || data.reflectionIntensity != __data.reflectionIntensity)
                RenderSettings.reflectionIntensity = __data.reflectionIntensity = data.reflectionIntensity;
        }
    }
}