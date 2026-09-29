// Bright cartoon sky gradient with a soft sun and stylised cloud bands.
Shader "Veil/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.2, 0.45, 0.95, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.88, 1, 1)
        _BottomColor ("Bottom", Color) = (0.55, 0.72, 0.9, 1)
        _SunColor ("Sun", Color) = (1, 0.95, 0.8, 1)
        _SunDir ("Sun Direction", Vector) = (0.4, 0.55, 0.3, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half4 _SunColor;
                float4 _SunDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                half3 col = y > 0 ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(y), 0.55)) : lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-y * 3));
                // clouds
                float a = atan2(d.z, d.x);
                float c = sin(a * 7 + _Time.y * 0.02) * 0.5 + sin(a * 13 - _Time.y * 0.015) * 0.3 + sin(a * 3) * 0.4;
                float band = smoothstep(0.02, 0.1, y) * smoothstep(0.32, 0.12, y);
                float cloud = smoothstep(0.25, 0.55, c * 0.5 + 0.5 - (y - 0.12) * 1.8) * band;
                col = lerp(col, half3(1, 1, 1), cloud * 0.75);
                // sun
                float3 sd = normalize(_SunDir.xyz);
                float s = saturate(dot(d, sd));
                col += _SunColor.rgb * (pow(s, 600) * 4 + pow(s, 12) * 0.25);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
