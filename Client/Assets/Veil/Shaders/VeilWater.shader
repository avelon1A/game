// Stylized animated water: vertex waves, fresnel tint and scrolling foam bands.
Shader "Veil/Water"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.08, 0.45, 0.75, 1)
        _ShallowColor ("Shallow", Color) = (0.3, 0.85, 0.95, 1)
        _FoamColor ("Foam", Color) = (0.9, 1, 1, 1)
        _WaveHeight ("Wave Height", Float) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _FoamColor;
                float _WaveHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                ws.y += sin(ws.x * 0.7 + _Time.y * 1.6) * _WaveHeight + cos(ws.z * 0.9 + _Time.y * 1.3) * _WaveHeight;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float w = sin(i.positionWS.x * 1.3 + t * 1.1) * 0.5 + sin(i.positionWS.z * 1.7 - t * 0.9) * 0.5;
                float bands = smoothstep(0.82, 0.95, sin(i.positionWS.x * 0.9 + i.positionWS.z * 0.6 + w * 1.5 + t * 0.6));
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = pow(1 - saturate(viewDir.y), 2);
                half3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(0.35 + w * 0.25 + fres * 0.6));
                col = lerp(col, _FoamColor.rgb, bands * 0.55);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
