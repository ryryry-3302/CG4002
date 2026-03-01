Shader "Hidden/BatonVisual"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" }
        Cull Off

        // Pass 1: Depth-only — writes to z-buffer, renders nothing visible.
        // This is what makes the baton occlude AR objects behind it.
        Pass
        {
            ColorMask 0
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }

        // Pass 2: Visual — renders the baton with vertex colors + cylindrical shading.
        Pass
        {
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = i.color;
                float cx = abs(i.uv.x - 0.5) * 2.0;
                float shade = 1.0 - cx * cx * 0.4;
                float spec = pow(max(0, 1.0 - cx * 1.8), 8.0) * 0.25;
                col.rgb *= shade;
                col.rgb += spec;
                return col;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
