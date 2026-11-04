#ifndef CQ_MULTISKYBOX_CLOUDS_INCLUDED
#define CQ_MULTISKYBOX_CLOUDS_INCLUDED
#include "CQ_MultiSkybox_Common.cginc"
sampler2D _CloudMap;
float _SecondLayer, _CloudSoftness;
#if defined(WITCHFLIGHT_CLOUD_SEA)
#include "BattleSkyAtmosphere.cginc"
#endif

// Analytic projected cloud sheets. No depth traversal or ray marching.
// Each layer uses exactly three texture samples: body, erosion and light offset.
float4 CQ_MultiSkyboxCloudLayer(float3 rd, float3 sun, float3 moon, float daylight,
                     float twilight, float clockTime, float layer)
{
    float projection = max(rd.y+0.17,0.17);
    // Position and wind displacement must use the same world-to-UV scale.
    // The old 0.018 wind factor reduced the advertised speed by ~10.6 times.
    float worldToUV = 0.19/max(_CloudScale,100.0);
    float2 uv = rd.xz/projection * _CloudBase*worldToUV;
    float windAngle = radians(_WindDirection);
    float2 wind = float2(cos(windAngle),sin(windAngle));
    float rate = clockTime*_WindSpeed*worldToUV;
    uv = uv*lerp(1.0,1.41,layer) + layer*float2(0.37,0.69);
    uv -= wind*rate*lerp(1.0,0.68,layer);
    float4 body = tex2D(_CloudMap,uv);
    float2 evolve = float2(0.0011,-0.0007)*clockTime*_Evolution;
    float detail = tex2D(_CloudMap,uv*2.03+evolve+layer*0.13).g;
    float coverage = saturate(_CloudCoverage+_Overcast*0.35-layer*0.07);
    float threshold = lerp(0.88,0.17,coverage);
    float erosion = (detail-0.5)*_CloudDetail*0.18;
    float signal = body.r + (body.b-0.5)*0.10*(1.0-_Overcast)-erosion;
    float softness = max(_CloudSoftness,0.015)+fwidth(signal);
    float mask = smoothstep(threshold-softness,threshold+softness+0.17,signal);
    float opticalDepth = mask*_CloudDensity*lerp(4.0,2.5,layer);
    float alpha = (1.0-exp(-opticalDepth))*smoothstep(0.0,0.085,rd.y);
    alpha *= step(0.001,coverage)*lerp(1.0,_SecondLayer,layer);

    float3 lightDirection = daylight>0.15 ? sun : moon;
    float2 offset = lightDirection.xz*0.012;
    float neighbor = tex2D(_CloudMap,uv+offset).r;
    float relief = clamp((body.r-neighbor)*13.0,-1.0,1.0);
    float shadow = saturate(0.50-relief*0.55+mask*0.24-lightDirection.y*0.22);
    float3 ambient = lerp(float3(0.013,0.022,0.043),float3(0.52,0.65,0.83),daylight);
    ambient *= _CloudAmbient*lerp(0.70,0.44,mask);
    float sunAmount = CQ_MultiSkyboxTransition(-0.10,0.15,sun.y);
    float moonAmount = (1.0-daylight)*smoothstep(-0.03,0.12,moon.y)*0.13*_MoonIntensity;
    float3 source = CQ_MultiSkyboxSunlight(sun,twilight)*sunAmount+_MoonColor.rgb*moonAmount;
    float forward = pow(saturate(dot(rd,lightDirection)),10.0);
    float silver = _CloudSilver*forward*pow(1.0-mask,2.0);
    float lighting = exp(-shadow*_CloudShadow)*(0.68+silver);
    float3 cloud = (ambient+source*lighting)*_CloudTint.rgb*lerp(1.0,0.68,_Overcast);
    return float4(cloud,alpha);
}

float4 CQ_MultiSkyboxFrag(CQ_MultiSkyboxVaryings i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float3 rd = CQ_MultiSkyboxRotateY(normalize(i.direction),radians(_SkyRotation));
    float clockTime = CQ_MultiSkyboxClock();
    float cloudTime = CQ_MultiSkyboxCloudClock();
    float3 sun,moon; float daylight,twilight;
    CQ_MultiSkyboxCelestials(clockTime,sun,moon,daylight,twilight);
    float3 color = CQ_MultiSkybox(rd,sun,moon,daylight,twilight,clockTime);
    #if !defined(CQ_MULTISKYBOX_LITE)
    if (_HighClouds>0.001)
    {
        float2 highUV = rd.xz/max(rd.y+0.22,0.22)*float2(0.22,0.63);
        float angle = radians(_WindDirection);
        // Cirrus remains slower than the lower sheet, with the same wind calibration.
        highUV -= float2(cos(angle),sin(angle))*cloudTime*_WindSpeed*0.000095;
        float cirrus = tex2D(_CloudMap,highUV).a;
        float alpha = smoothstep(0.48,0.76,cirrus)*_HighClouds*0.4*smoothstep(0.0,0.12,rd.y);
        float3 highColor = lerp(float3(0.026,0.043,0.071),float3(0.75,0.83,0.94),daylight);
        highColor = lerp(highColor,_TwilightColor.rgb,twilight*0.60);
        color = lerp(color,highColor,alpha);
    }
    #endif
    #if defined(WITCHFLIGHT_CLOUD_SEA)
    if (_CumulusClouds > 0.5)
    {
        float4 cumulus = BattleCumulusClouds(rd,daylight,twilight,cloudTime);
        cumulus.rgb = lerp(cumulus.rgb,color,saturate(_Haze*0.14/(rd.y*rd.y+0.4)));
        color = lerp(color,cumulus.rgb,cumulus.a);
    }
    else
    #endif
    if (_CloudDensity>0.001 && _CloudCoverage+_Overcast>0.001)
    {
        #if !defined(CQ_MULTISKYBOX_LITE)
        if (_SecondLayer>0.001)
        {
            float4 upper = CQ_MultiSkyboxCloudLayer(rd,sun,moon,daylight,twilight,cloudTime,1.0);
            upper.rgb = lerp(upper.rgb,color,saturate(_Haze*0.28/(rd.y*rd.y+0.4)));
            color = lerp(color,upper.rgb,upper.a);
        }
        #endif
        float4 lower = CQ_MultiSkyboxCloudLayer(rd,sun,moon,daylight,twilight,cloudTime,0.0);
        lower.rgb = lerp(lower.rgb,color,saturate(_Haze*0.18/(rd.y*rd.y+0.4)));
        color = lerp(color,lower.rgb,lower.a);
    }
    #if defined(WITCHFLIGHT_CLOUD_SEA)
    color = BattleSkyAtmosphere(color, rd, daylight, cloudTime);
    #endif
    return float4(CQ_MultiSkyboxFinish(color,rd,daylight),1.0);
}
#endif
