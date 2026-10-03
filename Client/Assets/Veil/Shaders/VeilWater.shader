// Stylized water. A baked shore map (_ShoreTex: 0 = at the shore, 1 = deep, from WorldBuilder) drives
// clear turquoise shallows -> deep blue, animated shore foam and incoming foam lines — no depth texture needed
// (phones). Surface: gentle vertex waves, analytic ripple normals, sun glints and a sky-tinted fresnel.
Shader "Veil/Water"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.03, 0.27, 0.55, 1)
        _MidColor ("Mid", Color) = (0.07, 0.55, 0.78, 1)
        _ShallowColor ("Shallow", Color) = (0.36, 0.9, 0.86, 1)
        _FoamColor ("Foam", Color) = (0.96, 1, 1, 1)
        _SkyColor ("Sky Reflection", Color) = (0.75, 0.88, 1, 1)
        _WaveHeight ("Wave Height", Float) = 0.05
        _ShoreTex ("Shore Map", 2D) = "white" {}
        _ShoreRect ("Shore Rect (minX, minZ, size, on)", Vector) = (0, 0, 1, 0)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _MidColor, _ShallowColor, _FoamColor, _SkyColor;
                float _WaveHeight;
                float4 _ShoreRect;
                float4 _ShoreTex_ST;
            CBUFFER_END
            TEXTURE2D(_ShoreTex); SAMPLER(sampler_ShoreTex);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; };

            float Depth01(float3 ws)
            {
                if (_ShoreRect.w < 0.5) return 1;
                float2 uv = (ws.xz - _ShoreRect.xy) / _ShoreRect.z;
                if (any(uv < 0) || any(uv > 1)) return 1;
                return SAMPLE_TEXTURE2D_LOD(_ShoreTex, sampler_ShoreTex, uv, 0).r;
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float t = _Time.y;
                ws.y += (sin(ws.x * 0.45 + t * 1.2) + cos(ws.z * 0.5 + t * 1.0)) * _WaveHeight;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.positionWS.xz;
                float d = Depth01(i.positionWS);

                // ripple normal from a few travelling sine waves (cheap, tiles nowhere)
                float2 g = 0;
                g += cos(dot(p, float2(0.83, 0.55)) * 1.9 + t * 1.7) * float2(0.83, 0.55) * 1.0;
                g += cos(dot(p, float2(-0.42, 0.91)) * 2.7 + t * 2.1) * float2(-0.42, 0.91) * 0.7;
                g += cos(dot(p, float2(0.97, -0.24)) * 4.1 + t * 2.9) * float2(0.97, -0.24) * 0.45;
                g += cos(dot(p, float2(-0.7, -0.7)) * 6.3 + t * 3.6) * float2(-0.7, -0.7) * 0.3;
                half3 n = normalize(half3(-g.x * 0.09, 1, -g.y * 0.09));

                // colour by depth
                half3 col = lerp(_ShallowColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.35, d));
                col = lerp(col, _DeepColor.rgb, smoothstep(0.3, 1.0, d));

                // light + fresnel sky + sun glints
                Light sun = GetMainLight();
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndl = saturate(dot(n, sun.direction));
                col *= 0.82 + 0.25 * ndl;
                half fres = pow(1 - saturate(dot(n, v)), 4);
                col = lerp(col, _SkyColor.rgb, fres * 0.55);
                half3 h = normalize(sun.direction + v);
                half spec = pow(saturate(dot(n, h)), 220) * 2.2;
                col += sun.color * spec;

                // soft caustic shimmer in the shallows
                float caus = sin(p.x * 1.7 + g.y * 1.5 + t) * sin(p.y * 1.9 - g.x * 1.5 - t * 0.8);
                col += _FoamColor.rgb * smoothstep(0.55, 0.95, caus) * (1 - smoothstep(0.0, 0.3, d)) * 0.12;

                // shore foam: a white edge + foam lines rolling in towards the beach
                float edge = 1 - smoothstep(0.01, 0.035 + 0.012 * sin(p.x * 0.8 + p.y * 0.6 + t * 1.3), d);
                float lines = smoothstep(0.82, 0.97, sin(d * 70 + t * 2.2 + g.x * 0.6)) * (1 - smoothstep(0.03, 0.1, d));
                float foam = saturate(edge + lines * 0.7);
                col = lerp(col, _FoamColor.rgb, foam * 0.9);

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
