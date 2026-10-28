Shader "WitchFlight/Battle Cloud Sky"
{
    Properties
    {
        [Header(Rounded skybox cumulus)]
        [ToggleUI] _CumulusClouds ("Use Cumulus Clouds", Float) = 1
        _CumulusBankCount ("Cloud Bank Count", Range(4,12)) = 10
        _CumulusWidth ("Cloud Bank Half Width (degrees)", Range(10,40)) = 18
        _CumulusBaseElevation ("Cloud Base Elevation (degrees)", Range(2,35)) = 16
        _CumulusTowerHeight ("Cloud Tower Height (degrees)", Range(6,40)) = 17
        _CumulusShadowColor ("Cloud Blue Underside", Color) = (0.48,0.65,0.84,1)
        [Header(Cloud sea and distant horizon)]
        _CloudSeaColor ("Cloud Sea Color", Color) = (0.62,0.72,0.86,1)
        _CloudSeaHeight ("Cloud Sea Height", Range(-0.1,0.3)) = 0.055
        _CloudSeaSoftness ("Cloud Sea Edge Softness", Range(0.01,0.25)) = 0.09
        _CloudSeaDetail ("Cloud Sea Edge Variation", Range(0,0.25)) = 0.08
        [Header(Rainbow visible from every map position)]
        _RainbowIntensity ("Rainbow Intensity (0 disables)", Range(0,1)) = 0.48
        _RainbowHeading ("Rainbow Heading", Range(0,360)) = 25
        _RainbowElevation ("Rainbow Center Elevation", Range(-30,30)) = -4
        _RainbowRadius ("Rainbow Angular Radius", Range(10,70)) = 42
        _RainbowWidth ("Rainbow Angular Width", Range(0.2,8)) = 2.8
        _MaterialTimeHours ("Material Time (hours)", Range(0,24)) = 13
        [HideInInspector] _UseSyncedTime ("Udon clock active", Float) = 0
        [HideInInspector] _SkyTimeHours ("Udon sky hour", Float) = 13
        [HideInInspector] _SkyAnimationSeconds ("Udon animation seconds", Float) = 0

        _SkyRotation ("Sky Rotation", Range(0,360)) = 0
        _OrbitTilt ("Orbit Tilt", Range(-70,70)) = 25
        _Exposure ("Exposure", Range(0,5)) = 1.4
        [Toggle] _ToneMap ("Internal Tone Mapping", Float) = 1
        _TwilightWidth ("Dawn / Dusk Transition Width", Range(0,3)) = 1
        _Haze ("Haze", Range(0,2)) = 0.35
        _DayZenith ("Day Zenith", Color) = (0.13,0.42,0.85,1)
        _DayHorizon ("Day Horizon", Color) = (0.68,0.81,0.96,1)
        _NightZenith ("Night Zenith", Color) = (0.0015,0.003,0.012,1)
        _NightHorizon ("Night Horizon", Color) = (0.014,0.023,0.048,1)
        _TwilightColor ("Twilight Color", Color) = (1.0,0.24,0.06,1)
        _GroundColor ("Below Horizon", Color) = (0.19,0.23,0.29,1)
        [ToggleUI] _ShowSun ("Show Sun", Float) = 1
        [HDR] _SunColor ("Sun Color", Color) = (1,0.96,0.88,1)
        _SunSize ("Sun Angular Radius (degrees)", Range(0.05,3)) = 0.27
        _SunIntensity ("Sun Intensity", Range(0,30)) = 12
        [ToggleUI] _ShowMoon ("Show Moon", Float) = 1
        _MoonTex ("Moon Surface", 2D) = "white" {}
        [HDR] _MoonColor ("Moon Color", Color) = (0.78,0.85,1,1)
        _MoonSize ("Moon Angular Radius (degrees)", Range(0.1,8)) = 0.9
        _MoonIntensity ("Moon Intensity", Range(0,5)) = 1.2
        _MoonPhase ("Moon Phase (0 new - 0.5 full)", Range(0,1)) = 0.5
        _MoonOrbitOffset ("Moon Orbit Offset (degrees)", Range(-180,180)) = 0
        [ToggleUI] _ShowStars ("Show Stars", Float) = 1
        _StarIntensity ("Star Intensity", Range(0,5)) = 1.2
        _StarDensity ("Star Density", Range(0,1)) = 0.45
        _StarTwinkle ("Star Twinkle", Range(0,1)) = 0.15
        _CloudMap ("Packed Cloud Map", 2D) = "gray" {}
        _CloudCoverage ("Cloud Coverage", Range(0,1)) = 0.55
        _Overcast ("Overcast", Range(0,1)) = 0
        _CloudDensity ("Cloud Density", Range(0,3)) = 1.1
        _CloudScale ("Cloud Feature Size (meters)", Range(100,8000)) = 1800
        _CloudBase ("Cloud Base (meters)", Range(100,6000)) = 1200
        _WindDirection ("Wind Direction (degrees)", Range(0,360)) = 25
        _WindSpeed ("Wind Speed (meters per second)", Range(0,100)) = 14
        _Evolution ("Cloud Evolution", Range(0,3)) = 0.45
        _CloudDetail ("Edge Detail", Range(0,1)) = 0.55
        _CloudSoftness ("Cloud Edge Softness", Range(0.015,0.25)) = 0.045
        _SecondLayer ("Second Cloud Layer", Range(0,1)) = 0.7
        _CloudShadow ("Cloud Self Shadow", Range(0,3)) = 1.2
        _CloudSilver ("Silver Lining", Range(0,5)) = 1.5
        _CloudAmbient ("Cloud Ambient Light", Range(0,2)) = 1
        _CloudTint ("Cloud Tint", Color) = (1,1,1,1)
        _HighClouds ("High Thin Clouds", Range(0,1)) = 0.3
    }
    SubShader
    {
        // Mesh skies render after Unity's skybox, before normal transparent objects.
        Tags { "Queue"="Geometry+501" "RenderType"="Background" "PreviewType"="Skybox" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex CQ_MultiSkyboxVert
            #pragma fragment CQ_MultiSkyboxFrag
            #pragma multi_compile_instancing
            #define WITCHFLIGHT_CLOUD_SEA 1
            #include "CQ_MultiSkybox_Clouds.cginc"
            ENDCG
        }
    }
    Fallback Off
}
