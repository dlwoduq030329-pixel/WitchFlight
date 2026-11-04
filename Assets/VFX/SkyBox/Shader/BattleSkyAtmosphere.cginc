#ifndef WITCHFLIGHT_BATTLE_SKY_ATMOSPHERE
#define WITCHFLIGHT_BATTLE_SKY_ATMOSPHERE
float4 _CloudSeaColor;
float _CloudSeaHeight, _CloudSeaSoftness, _CloudSeaDetail;
float _RainbowIntensity, _RainbowHeading, _RainbowElevation, _RainbowRadius, _RainbowWidth;
float _CumulusClouds, _CumulusBankCount, _CumulusWidth, _CumulusBaseElevation, _CumulusTowerHeight;
float4 _CumulusShadowColor;

float BattleCloudHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}
float BattleCloudNoise(float2 p)
{
    float2 cell = floor(p), f = frac(p);
    f = f*f*(3.0-2.0*f);
    return lerp(lerp(BattleCloudHash(cell), BattleCloudHash(cell+float2(1,0)), f.x),
        lerp(BattleCloudHash(cell+float2(0,1)), BattleCloudHash(cell+1), f.x), f.y);
}
void BattleCloudLobe(float2 p, float2 center, float2 radius, inout float shape, inout float2 normal)
{
    float2 q = (p-center)/radius;
    float d = length(q);
    // Smooth union also blends the lighting normal: a hard min leaves visible
    // straight seams where neighbouring puffs meet.
    float blend = saturate(0.5+0.5*(shape-d)/0.28);
    shape = lerp(shape,d,blend)-0.28*blend*(1.0-blend);
    normal = lerp(normal,q,blend);
}

// Angular banks with rounded lobes: the silhouette rises vertically instead of
// projecting a flat noise sheet toward the horizon. No volumetric marching.
float4 BattleCumulusClouds(float3 rd, float daylight, float twilight, float time)
{
    float4 result = 0;
    if (rd.y < 0.015 || rd.y > 0.94 || _CloudDensity < 0.001 || _CloudCoverage < 0.001) return result;
    float elevation = asin(clamp(rd.y,-1.0,1.0));
    float horizontalLength = max(length(rd.xz),0.01);
    float2 horizontal = rd.xz / horizontalLength;
    int count = (int)clamp(floor(_CumulusBankCount+0.5),4.0,12.0);
    [loop] for (int bank=0;bank<count;bank++)
    {
        float seed = BattleCloudHash(float2(bank,37));
        float heading = (bank+0.13+(seed-0.5)*0.24) * 6.2831853/count
            + radians(_WindDirection) + time*_WindSpeed*0.000012;
        float2 forward = float2(sin(heading),cos(heading));
        float front = dot(horizontal,forward);
        if(front < 0.65) continue;
        float width = tan(radians(_CumulusWidth)) * lerp(0.76,1.15,seed)
            * lerp(0.6,1.3,_CloudCoverage);
        float height = radians(_CumulusTowerHeight)*lerp(0.66,1.18,seed);
        float baseHeight = radians(_CumulusBaseElevation) + (seed-0.5)*0.20;
        float2 p = float2(dot(horizontal,float2(forward.y,-forward.x))/front/max(width,0.02),
            (elevation-baseHeight)/max(height,0.02));
        if(abs(p.x)>1.4 || p.y < -0.45 || p.y>1.3) continue;
        p.x *= seed < 0.5 ? -1.0 : 1.0;
        // A broad flattened base with an uneven crown of six rising puffs.
        float shape = 10;
        float2 normal = 0;
        BattleCloudLobe(p,float2(0,-0.01),float2(1.08,0.25),shape,normal);
        BattleCloudLobe(p,float2(-0.78,0.12),float2(0.34,0.28),shape,normal);
        BattleCloudLobe(p,float2(-0.46,0.28+seed*0.11),float2(0.36,0.36),shape,normal);
        BattleCloudLobe(p,float2(-0.12,0.55+seed*0.12),float2(0.36,0.44),shape,normal);
        BattleCloudLobe(p,float2(0.24,0.36),float2(0.4,0.4),shape,normal);
        BattleCloudLobe(p,float2(0.59,0.21),float2(0.36,0.3),shape,normal);
        BattleCloudLobe(p,float2(0.88,0.06),float2(0.28,0.22),shape,normal);
        float2 uv = p*float2(11,13)+bank*17.3+float2(time*_Evolution*0.002,0);
        float detail = BattleCloudNoise(uv)*0.55+BattleCloudNoise(uv*2.07)*0.30+BattleCloudNoise(uv*4.13)*0.15;
        shape += (detail-0.5)*lerp(0.08,0.26,_CloudDetail);
        float softness = max(0.035,_CloudSoftness*1.6)+fwidth(shape);
        float mask = 1.0-smoothstep(0.91-softness,1.03+softness,shape);
        float alpha = mask*saturate(_CloudDensity)*smoothstep(0.015,0.10,rd.y);
        float light = saturate(0.24 + smoothstep(-0.15,0.62,p.y)*0.25
            + sqrt(saturate(1.0-shape*shape))*0.24 + normal.y*0.24 - normal.x*0.08
            +(detail-0.5)*0.26 + (BattleCloudNoise(uv*0.37)-0.5)*0.20);
        float3 shade = _CumulusShadowColor.rgb*lerp(1.2,0.7,saturate(_CloudShadow));
        float3 cloud = lerp(shade,float3(1.65,1.65,1.6),light)*_CloudTint.rgb;
        cloud = lerp(cloud,cloud*_TwilightColor.rgb,twilight*0.35);
        cloud *= lerp(0.02,1.0,daylight);
        // Premultiplied accumulation keeps overlapping puffs continuous.
        result.rgb = cloud*alpha+result.rgb*(1.0-alpha);
        result.a = alpha+result.a*(1.0-alpha);
    }
    result.rgb /= max(result.a,0.0001);
    return result;
}

float3 BattleSkyAtmosphere(float3 sky, float3 rd, float daylight, float time)
{
    // A direction-only cloud sea stays continuous across the horizon, at every
    // player position. Two texture samples, no extra cameras or ray marching.
    float2 uv = rd.xz / max(0.22, abs(rd.y) + 0.22);
    uv = uv * 0.4 + float2(time * 0.00035, -time * 0.00018);
    float body = tex2D(_CloudMap, uv).r;
    float detail = tex2D(_CloudMap, uv * 2.13 + 0.37).g;
    float edge = _CloudSeaHeight + (body - 0.5) * _CloudSeaDetail;
    float coverage = 1.0 - smoothstep(edge - _CloudSeaSoftness, edge + _CloudSeaSoftness, rd.y);
    coverage = max(coverage, 1.0 - smoothstep(0.01, 0.10, abs(rd.y)));
    // Flatten the colour immediately at the horizon to match the distant ocean
    // fog; keep soft cloud shading below it and along the upper bank.
    float relief = smoothstep(0.08, 0.4, abs(rd.y));
    float shade = (body * 0.7 + detail * 0.3 - 0.5) * 0.22 * relief;
    float3 sea = _CloudSeaColor.rgb * (1.0 + shade) * lerp(0.035, 1.0, daylight);
    // This colour is also used by the scene fog. Undo the sky's exposure/tone
    // curve here so the ocean and sky meet at the same displayed horizon tint.
    sea = lerp(sea, -log(max(1.0 - min(sea, 0.98), 0.02)), step(0.5, _ToneMap));
    sea /= max(_Exposure, 0.001);
    sky = lerp(sky, sea, coverage);

    if (_RainbowIntensity > 0.001)
    {
        float heading = radians(_RainbowHeading);
        float elevation = radians(_RainbowElevation);
        float3 center = float3(sin(heading) * cos(elevation), sin(elevation), cos(heading) * cos(elevation));
        float angle = acos(clamp(dot(rd, center), -1.0, 1.0));
        float width = max(radians(_RainbowWidth), 0.001);
        float band = (angle - radians(_RainbowRadius)) / width + 0.5;
        // Violet on the inside, red on the outside, with soft spectral borders.
        float3 spectrum = saturate(abs(frac((1.0 - saturate(band)) * 0.76 + float3(0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0) - 1.0);
        float feather = max(fwidth(band) * 1.5, 0.12);
        float alpha = smoothstep(0.0, feather, band) * (1.0 - smoothstep(1.0 - feather, 1.0, band));
        alpha *= smoothstep(-0.015, 0.10, rd.y) * daylight;
        sky = lerp(sky, spectrum * 1.15 + 0.12, saturate(alpha * _RainbowIntensity));
    }
    return sky;
}
#endif
