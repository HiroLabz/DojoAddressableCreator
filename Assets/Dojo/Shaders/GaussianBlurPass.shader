// One axis of a separable Gaussian blur, run as a Graphics.Blit between render textures.
//
// Separable because a true 2D Gaussian of radius r costs r² taps, while doing it as a horizontal
// pass then a vertical pass costs 2r for the same result. That is the whole reason a wide, smooth
// blur is affordable at all — and why the single-pass kernel this replaces looked like a smudge.
//
// The five taps are a nine-tap Gaussian collapsed onto bilinear samples: each off-centre fetch sits
// between two texels at the weighted midpoint, so the hardware's own filtering does the pairing for
// free. Iterating this a few times with a growing offset widens the kernel far past nine taps while
// keeping the falloff smooth.
Shader "Hidden/Dojo/GaussianBlurPass"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            Name "GaussianBlurAxis"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            // sampler_LinearClamp is declared by URP's Core.hlsl — redeclaring it is an error.
            TEXTURE2D(_MainTex);

            float4 _MainTex_TexelSize;

            // (1,0) for the horizontal pass, (0,1) for the vertical one, scaled by how far this
            // iteration should reach.
            float2 _BlurAxis;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy * _BlurAxis;

                // Offsets and weights of a 9-tap Gaussian folded into 5 bilinear fetches.
                const float o1 = 1.3846153846;
                const float o2 = 3.2307692308;
                const float w0 = 0.2270270270;
                const float w1 = 0.3162162162;
                const float w2 = 0.0702702703;

                float4 sum = SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv) * w0;

                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, saturate(input.uv + texel * o1)) * w1;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, saturate(input.uv - texel * o1)) * w1;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, saturate(input.uv + texel * o2)) * w2;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, saturate(input.uv - texel * o2)) * w2;

                return sum;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
