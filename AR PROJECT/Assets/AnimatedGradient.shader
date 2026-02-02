Shader "Unlit/AnimatedGradient"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        // Default to Deep Royal Blue -> Dark Violet (Compliments Gold)
        _Color1 ("Color 1", Color) = (0.02, 0.05, 0.2, 1)    // Deep Navy
        _Color2 ("Color 2", Color) = (0.2, 0.0, 0.2, 1)      // Deep Purple
        _Speed ("Animation Speed", Range(0, 5)) = 0.5
        _Angle ("Angle", Range(0, 360)) = 45
        
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _WriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _ReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    
    SubShader
    {
        Tags
        { 
            "RenderType"="Opaque" 
            "Queue"="Transparent" 
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color1;
            fixed4 _Color2;
            float _Speed;
            float _Angle;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Convert angle to radians
                float rad = _Angle * 0.0174532925;
                float cosAngle = cos(rad);
                float sinAngle = sin(rad);

                // Rotate UVs
                float2 uv = i.uv - 0.5;
                float2 rotatedUV;
                rotatedUV.x = uv.x * cosAngle - uv.y * sinAngle;
                rotatedUV.y = uv.x * sinAngle + uv.y * cosAngle;
                rotatedUV += 0.5;

                // Create moving sine wave pattern
                float time = _Time.y * _Speed;
                float t = sin(rotatedUV.x * 3.14 + time) * 0.5 + 0.5;

                // Blend colors
                fixed4 c = lerp(_Color1, _Color2, t);
                
                // Apply UI alpha (IMPORTANT for UI to work)
                c.a *= i.color.a;

                return c;
            }
            ENDCG
        }
    }
}

