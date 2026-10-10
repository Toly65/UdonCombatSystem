// Fullscreen low-health vignette for VRChat worlds (works in VR and desktop, no post-processing).
// Multiplies a damage texture (white center = untouched, grey/veined edges = darkened) over the screen;
// defaults to the legacy VRChat combat system's damage texture from the SDK.
// Put it on a Cube mesh (no collider) scaled huge so it's never frustum-culled; the vertex shader
// ignores the geometry and maps one face's UVs straight onto the screen.
Shader "UCS/DamageVignette"
{
    Properties
    {
        _MainTex ("Damage Texture (white = clear)", 2D) = "white" {}
        _Intensity ("Intensity (0 = full health, 1 = near death)", Range(0, 1)) = 0
        _Contrast ("Darkness (texture power)", Range(1, 4)) = 2
        _ZoomAtLowIntensity ("Zoom At Low Intensity (smaller = only the very edges)", Range(0.1, 1)) = 0.45
        _MaskStart ("Edge Mask Start (0 = whole screen, 0.9 = only corners)", Range(0, 0.95)) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Overlay+100" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend DstColor Zero // multiply: white leaves the view alone, darker darkens it

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;
            float _Contrast;
            float _ZoomAtLowIntensity;
            float _MaskStart;
            // set globally by VRChat
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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.uv = v.uv;
                // keep only the cube's +Z face, and never draw into mirrors or the VRChat photo camera
                bool keep = v.normal.z > 0.5 && _VRChatMirrorMode == 0 && _VRChatCameraMode == 0 && _Intensity > 0.001;
                // that face's 0..1 UVs become the whole screen (per eye in VR); everything else goes off-screen
                o.pos = keep ? float4(v.uv * 2 - 1, 0.5, 1) : float4(2, 2, 2, 1);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // texture stretched to the screen (per-eye views in VR are near square anyway)
                // low intensity: zoomed into the clear center, so only a hint of the edges shows;
                // high intensity: the whole frame closes in
                float zoom = lerp(_ZoomAtLowIntensity, 1, _Intensity);
                float2 tuv = (i.uv - 0.5) * zoom + 0.5;
                fixed3 tex = pow(tex2D(_MainTex, tuv).rgb, _Contrast);
                // radial mask so the texture's flat grey only darkens toward the edges, not the whole view
                float edge = smoothstep(_MaskStart, 1.0, length(i.uv * 2 - 1));
                fixed3 col = lerp(fixed3(1, 1, 1), tex, edge * saturate(_Intensity * 2));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
