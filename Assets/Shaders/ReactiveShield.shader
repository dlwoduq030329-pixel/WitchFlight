Shader "WitchFlight/VFX/Reactive Shield"
{
    Properties
    {
        [HDR] _EnergyColor("Energy", Color) = (.12,1.5,2.5,1)
        [HDR] _ImpactColor("Impact", Color) = (4,.28,.08,1)
        _SurfaceOpacity("Surface opacity", Range(0,1)) = .08
        _EdgeWidth("Edge width", Range(.002,.15)) = .025
        _AmbientEnergy("Ambient packets", Range(0,2)) = .35
        _ShieldRadius("Radius", Float) = 2
        _Displacement("Fragment travel", Float) = .38
        _FragmentRotation("Fragment rotation", Float) = .42
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "ReactiveShield"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _EnergyColor, _ImpactColor;
                float _SurfaceOpacity, _EdgeWidth, _AmbientEnergy, _ShieldRadius, _Displacement, _FragmentRotation;
            CBUFFER_END
            float4 _ShieldHits[8];
            float4 _ShieldTimings[8];
            struct Attributes
            {
                float4 positionOS:POSITION;float3 normalOS:NORMAL;
                float3 bary:TEXCOORD0;float4 cell:TEXCOORD1;float4 shard:TEXCOORD2;float4 edges:TEXCOORD3;
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;
                float3 bary:TEXCOORD2;float4 edges:TEXCOORD3;float3 energy:TEXCOORD4;
            };
            float3 Rotate(float3 v,float3 axis,float angle)
            {
                float s,c;sincos(angle,s,c);return v*c+cross(axis,v)*s+axis*dot(axis,v)*(1-c);
            }
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float seed=frac(v.shard.w), damage=0, ripple=0;
                float3 displacement=0;
                [unroll] for(int i=0;i<8;i++)
                {
                    float4 timing=_ShieldTimings[i];
                    float age=(_Time.y-timing.x)/max(.1,timing.z);
                    float active=timing.w*step(0,age)*(1-step(1,age));
                    float distanceToHit=distance(v.cell.xyz,_ShieldHits[i].xyz);
                    float reach=max(.02,timing.y);
                    float falloff=saturate(1-distanceToHit/reach);falloff*=falloff;
                    float envelope=smoothstep(0,.09,age)*(1-smoothstep(.2,1,age));
                    float force=active*falloff*envelope*_ShieldHits[i].w;
                    damage=max(damage,force);
                    float3 scatter=normalize(v.cell.xyz+float3(sin(seed*91),cos(seed*71),sin(seed*53))*.35);
                    displacement+=scatter*force*_Displacement;
                    float wave=1-smoothstep(.025,.11,abs(distanceToHit-age*reach*2));
                    ripple=max(ripple,active*wave*(1-age)*_ShieldHits[i].w);
                }
                // Bound overlapping impacts so rapid fire cannot throw fragments out of the mesh bounds.
                float travel=length(displacement);displacement*=min(1,(_Displacement*2)/max(.0001,travel));
                float3 axis=normalize(cross(v.cell.xyz,float3(.17+seed,.83,.41)) + float3(.001,0,0));
                float angle=min(damage,2)*_FragmentRotation*(seed>.5?1:-1);
                float3 p=(v.shard.xyz+Rotate(v.positionOS.xyz-v.shard.xyz,axis,angle))*_ShieldRadius+displacement;
                o.positionWS=TransformObjectToWorld(p);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(Rotate(v.normalOS,axis,angle));
                o.bary=v.bary;o.edges=v.edges;
                float packet=pow(saturate(sin(_Time.y*1.9+v.cell.w*19)),28)*step(.65,frac(v.cell.w));
                o.energy=float3(saturate(damage),ripple,packet*_AmbientEnergy);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float3 width=max(fwidth(i.bary)*1.2,_EdgeWidth.xxx);
                float3 lines=1-smoothstep(0,width,i.bary);
                float3 visibleEdges=max(i.edges.xyz,i.energy.x.xxx);
                float edge=max(max(lines.x*visibleEdges.x,lines.y*visibleEdges.y),lines.z*visibleEdges.z);
                edge=max(edge,i.edges.w);
                float rim=pow(1-saturate(abs(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS)))),3);
                float hit=saturate(i.energy.x+i.energy.y*.75);
                float3 color=lerp(_EnergyColor.rgb,_ImpactColor.rgb,hit);
                float intensity=.16+edge*.85+rim*.45+i.energy.z+i.energy.y*1.3+i.energy.x;
                float alpha=saturate(_SurfaceOpacity+edge*.42+rim*.2+i.energy.x*.48+i.energy.y*.25+i.energy.z*.25);
                return half4(color*intensity,alpha);
            }
            ENDHLSL
        }
    }
}
