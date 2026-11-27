#ifndef WF_TREE_BEND_INCLUDED
#define WF_TREE_BEND_INCLUDED
float4 _TreeRoot;
float4 _TreeUpHeight;
float4 _TreeBendSettings; // top displacement / height, frequency, phase, root fixed fraction
float4 _TreeWind; // additional strength, accumulated phase
void WFTreeBend(inout float3 positionOS, inout float3 normalOS)
{
    if (_TreeUpHeight.w <= 0) return;
    float3 up = normalize(_TreeUpHeight.xyz);
    float3 p = TransformObjectToWorld(positionOS);
    float height = max(.01, _TreeUpHeight.w);
    float h = dot(p - _TreeRoot.xyz, up) / height;
    float pin = _TreeBendSettings.w;
    float t = saturate((h-pin) / max(.01,1-pin));
    float phase = (_Time.y + _TreeWind.y) * _TreeBendSettings.y + _TreeBendSettings.z;
    float wave = sin(phase) * .75 + sin(phase * 1.73 + .6) * .25;
    float3 dir = float3(1,0,.35);
    dir = normalize(dir - up * dot(dir,up) + float3(.0001,0,0));
    float amplitude = height * _TreeBendSettings.x * max(1,_TreeWind.x) * wave;
    float3 n = TransformObjectToWorldNormal(normalOS);
    float slope = (h > pin && h < 1) ? amplitude * 2*t / (height * max(.01,1-pin)) : 0;
    n = normalize(n - up * (slope * dot(n,dir)));
    positionOS = TransformWorldToObject(p + dir * (amplitude*t*t));
    normalOS = TransformWorldToObjectNormal(n);
}
#endif
