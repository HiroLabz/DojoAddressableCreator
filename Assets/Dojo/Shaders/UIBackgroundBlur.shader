// Blurs a captured image of whatever was behind this UI graphic.
//
// The backdrop is supplied by UIBackdropBlur, which renders the scene into a texture the moment the
// dialog opens. It is NOT _CameraOpaqueTexture: that global is only bound while the render loop is
// running, and a Screen Space Overlay canvas draws after the loop has finished — so an overlay
// dialog sampling it gets nothing. A captured texture works under any canvas mode, and needs no
// "Opaque Texture" tick on the pipeline asset.
//
// A frozen backdrop is also the right behaviour for a modal: the world is not meant to carry on
// moving underneath a question the player has to answer.
Shader "Dojo/UI/BackgroundBlur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _BackdropTex ("Backdrop (set at runtime)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // How strong the blur is lives on the UIBackdropBlur component, not here: the blurring
        // happens in its capture passes, and a radius slider on this material would do nothing.
        _Darken ("Darken", Range(0, 1)) = 0.35
        _Saturation ("Saturation", Range(0, 2)) = 1

        // Written by Unity's UI mask system. Present so this material can sit inside a Mask.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIBackgroundBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // sampler_LinearClamp comes from URP's Core.hlsl — declaring it again is a redefinition.
            TEXTURE2D(_BackdropTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _Darken;
                float  _Saturation;
            CBUFFER_END

            float4 _BackdropTex_TexelSize;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.screenPos.xy / input.screenPos.w;

                // A single fetch. The blur already happened: UIBackdropBlur runs a separable
                // Gaussian over the captured backdrop before handing it here, so doing kernel work
                // again at UI draw time would only cost fill rate and soften it twice.
                float3 sum = SAMPLE_TEXTURE2D(_BackdropTex, sampler_LinearClamp, saturate(uv)).rgb;

                // Desaturate and darken before tinting, so the panel's own colours stay legible
                // against whatever happened to be on screen.
                float luma = dot(sum, float3(0.2126, 0.7152, 0.0722));
                sum = lerp(luma.xxx, sum, _Saturation);
                sum *= (1.0 - _Darken);

                // The graphic's own sprite and vertex colour decide the shape and the fade, so the
                // dialog can animate this in with a CanvasGroup like any other UI element.
                half4 mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half alpha = mask.a * input.color.a * _Color.a;

                return float4(sum * _Color.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
