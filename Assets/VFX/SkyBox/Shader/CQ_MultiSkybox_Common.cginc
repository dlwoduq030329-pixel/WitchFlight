#ifndef CQ_MULTISKYBOX_COMMON_INCLUDED
#define CQ_MULTISKYBOX_COMMON_INCLUDED
#include "UnityCG.cginc"

#define CQ_MULTISKYBOX_PI 3.14159265359
#define CQ_MULTISKYBOX_TAU 6.28318530718
sampler2D _MoonTex;
float _ShowSun, _ShowMoon, _ShowStars;
float _MaterialTimeHours, _UseSyncedTime;
float _SkyTimeHours, _SkyAnimationSeconds, _SkyRotation, _OrbitTilt;
float _Exposure, _ToneMap, _Haze, _TwilightWidth;
float4 _DayZenith, _DayHorizon, _NightZenith, _NightHorizon, _TwilightColor, _GroundColor;
float _SunSize, _SunIntensity, _MoonSize, _MoonIntensity, _MoonPhase, _MoonOrbitOffset;
float4 _SunColor, _MoonColor;
float _StarIntensity, _StarDensity, _StarTwinkle;
float _CloudCoverage, _Overcast, _CloudDensity, _CloudScale, _CloudBase;
float _WindDirection, _WindSpeed, _Evolution, _CloudDetail, _CloudShadow, _CloudSilver;
float _CloudAmbient, _HighClouds;
float4 _CloudTint;

struct CQ_MultiSkyboxAppData
{
    float4 vertex : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct CQ_MultiSkyboxVaryings
{
    float4 position : SV_POSITION;
    float3 direction : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};
CQ_MultiSkyboxVaryings CQ_MultiSkyboxVert(CQ_MultiSkyboxAppData v)
{
    CQ_MultiSkyboxVaryings o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(CQ_MultiSkyboxVaryings, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.position = UnityObjectToClipPos(v.vertex);
    // Match the ray at each covered pixel, independent of the mesh's UVs,
    // pivot, scale or distance. Do not normalize before perspective interpolation.
    // Unity supplies a camera-centred view transform for its native skybox draw;
    // ordinary MeshRenderers supply the actual model-view transform instead.
    float3 viewPosition = UnityObjectToViewPos(v.vertex.xyz);
    float3 viewDirection = lerp(viewPosition, float3(0,0,-1), unity_OrthoParams.w);
    o.direction = mul((float3x3)UNITY_MATRIX_I_V, viewDirection);
    // Keep the mesh's screen coverage but place its depth at the background.
    // UNITY_MATRIX_I_V is eye-specific after UNITY_SETUP_INSTANCE_ID in SPI.
    #if defined(UNITY_REVERSED_Z)
        o.position.z = 0.0;
    #else
        o.position.z = o.position.w;
    #endif
    return o;
}
float CQ_MultiSkyboxClock()
{
    return _SkyAnimationSeconds;
}
float CQ_MultiSkyboxCloudClock()
{
    // Fixed sky hour does not freeze clouds. Only unsynchronized clouds use
    // Unity's local seconds; active Udon retains its shared animation clock.
    return _UseSyncedTime > 0.5 ? _SkyAnimationSeconds : _Time.y + _SkyAnimationSeconds;
}
float3 CQ_MultiSkyboxRotateY(float3 p, float angle)
{
    float s, c; sincos(angle, s, c);
    return float3(c*p.x + s*p.z, p.y, -s*p.x + c*p.z);
}
float CQ_MultiSkyboxHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
// Scale appearance transitions without changing the shared hour or celestial orbit.
float CQ_MultiSkyboxTransition(float low, float high, float height)
{
    float width = clamp(_TwilightWidth, 0.0, 3.0);
    float soft = smoothstep(low, high, height / max(width, 0.0001));
    // Explicit zero avoids equal smoothstep edges and defines an instant transition.
    return width > 0.0 ? soft : step(0.0, height);
}
void CQ_MultiSkyboxCelestials(float clockTime, out float3 sun, out float3 moon, out float daylight, out float twilight)
{
    float hours = _UseSyncedTime > 0.5 ? _SkyTimeHours : _MaterialTimeHours;
    float a = (frac(hours/24.0) - 0.25)*CQ_MULTISKYBOX_TAU;
    float tilt = radians(_OrbitTilt);
    sun = float3(cos(a), sin(a)*cos(tilt), sin(a)*sin(tilt));
    float ma = a + CQ_MULTISKYBOX_PI + radians(_MoonOrbitOffset);
    moon = float3(cos(ma), sin(ma)*cos(tilt), sin(ma)*sin(tilt));
    daylight = CQ_MultiSkyboxTransition(-0.12, 0.16, sun.y);
    float width = clamp(_TwilightWidth, 0.0, 3.0);
    twilight = width > 0.0 ? exp(-abs(sun.y)*12.0 / max(width, 0.0001)) : 0.0;
}
float3 CQ_MultiSkyboxSunlight(float3 sun, float twilight)
{
    return _SunColor.rgb * lerp(float3(1,1,1), _TwilightColor.rgb, twilight * 0.85);
}
float3 CQ_MultiSkyboxStars(float3 rd, float clockTime)
{
    // 3D lattice avoids longitude seams and pinching at the poles.
    float3 p = CQ_MultiSkyboxRotateY(rd, clockTime * 0.00001) * 190.0;
    float3 id = floor(p);
    float h = CQ_MultiSkyboxHash(id);
    float3 local = frac(p)-0.5;
    float footprint = max(length(fwidth(p)), 0.015);
    float radius = lerp(0.045, 0.13, CQ_MultiSkyboxHash(id+7.1));
    float aa = max(footprint*0.6, 0.01);
    float star = 1.0 - smoothstep(max(radius-aa,0.0), radius+aa, length(local));
    star *= step(1.0-_StarDensity*0.045, h);
    // Preserve approximate star energy when a star is smaller than a pixel.
    star *= saturate(radius*radius/max(footprint*footprint*0.18,0.00001));
    star *= lerp(1.0, 0.75+0.25*sin(clockTime*1.4+h*130.0), _StarTwinkle);
    return star * lerp(float3(0.65,0.79,1),float3(1,0.84,0.64),CQ_MultiSkyboxHash(id+15.0));
}
float3 CQ_MultiSkybox(float3 rd, float3 sun, float3 moon, float daylight, float twilight, float clockTime)
{
    float elevation = max(rd.y, 0.0);
    float horizon = exp(-elevation*4.0);
    float3 zenith = lerp(_NightZenith.rgb, _DayZenith.rgb, daylight);
    float3 horizonColor = lerp(_NightHorizon.rgb, _DayHorizon.rgb, daylight);
    float3 color = lerp(zenith, horizonColor, horizon);
    float sunDot = dot(rd,sun);
    float glow = pow(saturate(sunDot*0.5+0.5),8.0);
    color = lerp(color, _TwilightColor.rgb*(0.3+0.65*glow), twilight*horizon*(0.25+0.65*glow));
    if (_ShowSun > 0.5)
        color += CQ_MultiSkyboxSunlight(sun,twilight)*pow(saturate(sunDot),64.0)*0.12*daylight;
    color = lerp(color, horizonColor, saturate(_Haze*0.35)*exp(-elevation*7.0));
    float overcast = saturate(_Overcast);
    float3 greySky = lerp(float3(0.006,0.009,0.015),float3(0.3,0.34,0.4),daylight);
    color = lerp(color, greySky*(0.72+0.28*horizon), overcast*0.87);

    float starVisibility = (1.0-CQ_MultiSkyboxTransition(-0.22,-0.045,sun.y))*smoothstep(0.0,0.15,rd.y);
    float3 stars = 0.0;
    UNITY_BRANCH
    if (_ShowStars > 0.5)
        stars = CQ_MultiSkyboxStars(rd,clockTime)*_StarIntensity*starVisibility;

    UNITY_BRANCH
    if (_ShowSun > 0.5)
    {
        float sunRadius = radians(max(_SunSize,0.01));
        float sunDistance = length(rd-sun);
        float sunAA = max(fwidth(sunDistance), 0.000025);
        float sunDisk = 1.0-smoothstep(sunRadius-sunAA,sunRadius+sunAA,sunDistance);
        color += sunDisk * CQ_MultiSkyboxSunlight(sun,twilight)*_SunIntensity*smoothstep(-0.015,0.012,sun.y);
    }

    UNITY_BRANCH
    if (_ShowMoon > 0.5)
    {
        float3 reference = abs(moon.y) > 0.98 ? float3(1,0,0) : float3(0,1,0);
        float3 right = normalize(cross(reference,moon));
        float3 up = cross(moon,right);
        float radius = sin(radians(max(_MoonSize,0.02)));
        float2 m = float2(dot(rd,right),dot(rd,up))/radius;
        float r2 = dot(m,m);
        float edgeAA = max(fwidth(r2),0.001);
        float moonMask = (1.0-smoothstep(1.0-edgeAA,1.0+edgeAA,r2))*step(0.0,dot(rd,moon));
        float3 normal = normalize(float3(m,sqrt(saturate(1.0-r2))));
        float phase = _MoonPhase*CQ_MULTISKYBOX_TAU;
        float3 moonLight = float3(sin(phase),0.0,-cos(phase));
        float illumination = saturate(dot(normal,moonLight));
        float2 moonUV = float2(atan2(normal.x,max(normal.z,0.00001))/CQ_MULTISKYBOX_TAU+0.5,
                              asin(clamp(normal.y,-1.0,1.0))/CQ_MULTISKYBOX_PI+0.5);
        float3 albedo = tex2D(_MoonTex,moonUV).rgb;
        float moonVisibility = smoothstep(-0.02,0.01,rd.y)*lerp(1.0,0.16,daylight);
        // The unlit part of the lunar disc occludes stars, including at new moon.
        stars *= 1.0-moonMask;
        color += stars;
        float3 lunarSurface = albedo * _MoonColor.rgb * _MoonIntensity * (illumination+0.018);
        color = lerp(color, color*lerp(0.05,0.82,daylight)+lunarSurface,moonMask*moonVisibility);
        float lunarGlow = exp(-length(rd-moon)/max(radius*3.0,0.001));
        color += _MoonColor.rgb*lunarGlow*_MoonIntensity*0.025*(1.0-daylight)*pow(sin(phase*0.5),2.0);
    }
    else
    {
        color += stars;
    }
    return color;
}
float3 CQ_MultiSkyboxFinish(float3 color, float3 rd, float daylight)
{
    float3 ground = _GroundColor.rgb*lerp(0.035,0.5,daylight);
    color = lerp(ground,color,smoothstep(-0.045,0.006,rd.y));
    color = max(color,0.0)*_Exposure;
    color = lerp(color,1.0-exp(-color),step(0.5,_ToneMap));
    #if defined(UNITY_COLORSPACE_GAMMA)
    color = LinearToGammaSpace(color);
    #endif
    return color;
}
#endif
