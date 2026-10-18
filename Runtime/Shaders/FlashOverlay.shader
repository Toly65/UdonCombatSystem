// Fullscreen flashbang whiteout for VRChat worlds. Same screen mapping as UCS/DamageVignette: put it on a
// Cube mesh (no collider) scaled huge so it's never frustum-culled; the +Z face's UVs become the whole
// screen per eye, so it holds up at any FOV. Skipped in mirrors and the VRChat photo camera.
Shader "UCS/FlashOverlay"
{
    Properties
    {
        _Color ("Flash Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Overlay+110" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _VRChatMirrorMode;
            float _VRChatCameraMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                bool keep = v.normal.z > 0.5 && _VRChatMirrorMode == 0 && _VRChatCameraMode == 0 && _Intensity > 0.001;
                o.pos = keep ? float4(v.uv * 2 - 1, 0.5, 1) : float4(2, 2, 2, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // eased so the tail of the fade lingers as a haze rather than snapping off
                return fixed4(_Color.rgb, _Color.a * saturate(_Intensity * (2 - _Intensity)));
            }
            ENDCG
        }
    }
}
