// Adds a planar reflection to the surfaces PlanarFloorReflection tags: drawn over them a second
// time, additively, with the mirrored camera's picture sampled at the pixel's own screen position.
// The floors' own materials are left alone - this only adds light on top of what they drew.
Shader "Hidden/Dojo/PlanarReflectionOverlay"
{
    Properties
    {
        _PlanarIntensity("Intensity", Range(0, 1)) = 0.25
        _PlanarFresnel("Fresnel boost", Range(0, 4)) = 1
        _PlanarBlur("Blur (mip)", Range(0, 6)) = 1.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+1" }

        Pass
        {
            Name "PlanarReflectionOverlay"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PlanarReflectionTex);
            SAMPLER(sampler_PlanarReflectionTex);

            CBUFFER_START(UnityPerMaterial)
                half _PlanarIntensity;
                half _PlanarFresnel;
                half _PlanarBlur;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.screenPos = ComputeScreenPos(position.positionCS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);

                // Only the tops of the tiles face the mirror; their edges get nothing.
                half facing = saturate(normal.y * 4 - 3);

                float2 uv = input.screenPos.xy / input.screenPos.w;
                half3 reflection = SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv, _PlanarBlur).rgb;

                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half grazing = pow(1 - saturate(dot(normal, view)), 5);
                half amount = _PlanarIntensity * (1 + _PlanarFresnel * grazing) * facing;

                return half4(reflection * amount, 0);
            }
            ENDHLSL
        }
    }
}
