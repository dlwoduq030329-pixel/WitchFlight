Shader "Hidden/WitchFlight/EyePaletteTint"
{
    Properties
    {
        _MainTex ("Eye texture", 2D) = "white" {}
        _PaletteColor ("Iris color", Color) = (1,1,1,1)
        _PupilOnly ("Four-white eyes", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _PaletteColor;
            float _PupilOnly;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv);
                float high = max(source.r, max(source.g, source.b));
                float low = min(source.r, min(source.g, source.b));
                float saturation = (high - low) / max(high, 0.0001);
                // Keep sclera, highlights, dark outlines and other expression atlas regions.
                float iris = step(0.12, high) * step(0.2, saturation);
                float pupil = 1 - step(0.12, high);
                float mask = lerp(iris, pupil, _PupilOnly);
                float shade = lerp(lerp(0.55, 1, high), 1, _PupilOnly);
                source.rgb = lerp(source.rgb, _PaletteColor.rgb * shade, mask);
                return source;
            }
            ENDCG
        }
    }
}
