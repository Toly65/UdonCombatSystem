Shader "Custom/RedDotSight"
{
    Properties
    {
        // --- Dot ---
        _DotColor       ("Dot Color",        Color)  = (1, 0.05, 0.05, 1)
        _DotSize        ("Dot Size",         Range(0.001, 0.1)) = 0.012
        _DotSharpness   ("Dot Sharpness",    Range(0.0, 1.0))   = 0.85
        // 0 = filled circle, 1 = ring, 2 = crosshair, 3 = dot + ring
        _ReticleType    ("Reticle Type (0=Dot 1=Ring 2=Cross 3=Dot+Ring)", Int) = 0

        // --- Ring (used when type = 1 or 3) ---
        _RingRadius     ("Ring Radius",      Range(0.01, 0.5))  = 0.08
        _RingThickness  ("Ring Thickness",   Range(0.001, 0.05)) = 0.005

        // --- Crosshair (used when type = 2) ---
        _CrossLength    ("Cross Length",     Range(0.01, 0.5))  = 0.06
        _CrossThickness ("Cross Thickness",  Range(0.001, 0.05)) = 0.004
        _CrossGap       ("Cross Gap",        Range(0.0, 0.1))   = 0.01

        // --- Glow ---
        _GlowColor      ("Glow Color",       Color)  = (1, 0.1, 0.0, 1)
        _GlowRadius     ("Glow Radius",      Range(0.0, 0.3))   = 0.035
        _GlowIntensity  ("Glow Intensity",   Range(0.0, 8.0))   = 2.5
        _GlowFalloff    ("Glow Falloff",     Range(0.5, 6.0))   = 2.0

        // --- Aspect ---
        // Set to match your quad's world-space aspect ratio so the dot stays round.
        _AspectRatio    ("Aspect Ratio (W/H)", Float) = 1.0

        // --- Parallax / Collimation ---
        // Positive values make the reticle shift with view angle like a distant projected dot.
        _ParallaxAmount ("Parallax Amount", Range(-2.0, 2.0)) = 0.2
        _ParallaxBoost  ("Parallax Boost", Range(0.25, 8.0)) = 2.5
        _ParallaxDepth  ("Parallax Depth Bias", Range(0.01, 1.0)) = 0.08
        _UseMeterParallax   ("Use Meter Parallax (0/1)", Range(0.0, 1.0)) = 1.0
        _ParallaxDistanceM  ("Parallax Distance (Meters)", Range(1.0, 500.0)) = 50.0
        _ParallaxRefDistanceM ("Parallax Reference (Meters)", Range(1.0, 500.0)) = 50.0
        _ScaleCompensation  ("Scale Compensation", Range(0.0, 2.0)) = 1.0

        // --- Projected Emitter (recommended red-dot mode) ---
        _UseProjectedEmitter ("Use Projected Emitter (0/1)", Range(0.0, 1.0)) = 1.0
        _VirtualDistanceM    ("Virtual Image Distance (Meters)", Range(1.0, 2000.0)) = 100.0
        _ProjectionDirection ("Projection Direction (+1 or -1)", Range(-1.0, 1.0)) = 1.0
        _EmitterOffset       ("Emitter Offset (Local XY)", Vector) = (0, 0, 0, 0)
        _LensHalfSizeX       ("Lens Half Size X (Local)", Float) = 0.5
        _LensHalfSizeY       ("Lens Half Size Y (Local)", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        // Pass 1: write lens shape into stencil only.
        Pass
        {
            Name "StencilMask"
            ZWrite Off
            Cull Off
            ColorMask 0

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vertMask
            #pragma fragment fragMask
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vertMask(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 fragMask(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }

        // Pass 2: draw reticle additively, only where stencil mask exists.
        Pass
        {
            Name "Reticle"
            Blend One One
            ZWrite Off
            Cull Off

            Stencil
            {
                Ref 1
                Comp Equal
                Pass Keep
            }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #include "UnityCG.cginc"

            // ---- properties ----
            fixed4  _DotColor;
            float   _DotSize;
            float   _DotSharpness;
            int     _ReticleType;

            float   _RingRadius;
            float   _RingThickness;

            float   _CrossLength;
            float   _CrossThickness;
            float   _CrossGap;

            fixed4  _GlowColor;
            float   _GlowRadius;
            float   _GlowIntensity;
            float   _GlowFalloff;

            float   _AspectRatio;
            float   _ParallaxAmount;
            float   _ParallaxBoost;
            float   _ParallaxDepth;
            float   _UseMeterParallax;
            float   _ParallaxDistanceM;
            float   _ParallaxRefDistanceM;
            float   _ScaleCompensation;
            float   _UseProjectedEmitter;
            float   _VirtualDistanceM;
            float   _ProjectionDirection;
            float4  _EmitterOffset;
            float   _LensHalfSizeX;
            float   _LensHalfSizeY;

            // ---- structs ----
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float3 viewDirObj : TEXCOORD1;
                float  invAvgWorldScaleXY : TEXCOORD2;
                float3 localPos : TEXCOORD3;
            };

            // ---- vertex ----
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Remap [0,1] → [-1,1], then correct aspect ratio.
                float2 uv = v.uv * 2.0 - 1.0;
                uv.x *= _AspectRatio;
                o.uv = uv;
                o.viewDirObj = ObjSpaceViewDir(v.vertex);
                o.localPos = v.vertex.xyz;

                // Keep parallax behavior consistent when the lens mesh is uniformly scaled up/down.
                float worldScaleX = length(float3(unity_ObjectToWorld._m00,
                                                  unity_ObjectToWorld._m10,
                                                  unity_ObjectToWorld._m20));
                float worldScaleY = length(float3(unity_ObjectToWorld._m01,
                                                  unity_ObjectToWorld._m11,
                                                  unity_ObjectToWorld._m21));
                float avgWorldScaleXY = max((worldScaleX + worldScaleY) * 0.5, 0.0001);
                o.invAvgWorldScaleXY = 1.0 / avgWorldScaleXY;
                return o;
            }

            // ---- SDF helpers ----

            // Returns distance from point p to a filled circle of radius r.
            float sdCircle(float2 p, float r)
            {
                return length(p) - r;
            }

            // Returns distance from point p to a ring (annulus).
            float sdRing(float2 p, float radius, float thickness)
            {
                return abs(length(p) - radius) - thickness * 0.5;
            }

            // Returns distance from point p to a crosshair with a centre gap.
            float sdCross(float2 p, float halfLen, float halfThick, float gap)
            {
                float2 ap = abs(p);
                // Horizontal bar (outside gap)
                float hBar = max(ap.y - halfThick,
                                 max(gap - ap.x, ap.x - halfLen));
                // Vertical bar (outside gap)
                float vBar = max(ap.x - halfThick,
                                 max(gap - ap.y, ap.y - halfLen));
                return min(hBar, vBar);
            }

            // Smooth antialiased coverage from an SDF value.
            // edgeWidth controls the AA fringe in UV space.
            float sdfCoverage(float d, float edgeWidth)
            {
                return 1.0 - smoothstep(-edgeWidth, edgeWidth, d);
            }

            // ---- fragment ----
            fixed4 frag(v2f i) : SV_Target
            {
                float safeHalfX = max(_LensHalfSizeX, 0.0001);
                float safeHalfY = max(_LensHalfSizeY, 0.0001);
                float2 pBase = float2(i.localPos.x / safeHalfX,
                                      i.localPos.y / safeHalfY);
                pBase.x *= _AspectRatio;

                // View-angle-driven offset that emulates a collimated reticle.
                float3 viewDir = normalize(i.viewDirObj);
                float viewZ = max(abs(viewDir.z), _ParallaxDepth);
                float manualScale = _ParallaxAmount * _ParallaxBoost;
                float meterScale = _ParallaxAmount * _ParallaxBoost *
                                   (max(_ParallaxRefDistanceM, 0.001) / max(_ParallaxDistanceM, 0.001));
                float parallaxScale = lerp(manualScale, meterScale, saturate(_UseMeterParallax));
                float scaleMul = lerp(1.0, i.invAvgWorldScaleXY, saturate(_ScaleCompensation));
                parallaxScale *= scaleMul;
                // Invert sign so positive amount matches the common "dot floats opposite glass motion" expectation.
                float2 parallaxOffset = -(viewDir.xy / viewZ) * parallaxScale;

                // Projected emitter model: compute where the eye->virtual-image ray crosses the lens plane (z=0 in local space).
                float3 eyeObj = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float dirSign = (_ProjectionDirection >= 0.0) ? 1.0 : -1.0;
                float3 virtualObj = float3(_EmitterOffset.x,
                                           _EmitterOffset.y,
                                           _VirtualDistanceM * dirSign);
                float denom = virtualObj.z - eyeObj.z;
                float tProj = (abs(denom) > 1e-5) ? (-eyeObj.z / denom) : 0.0;
                float2 centerLocal = eyeObj.xy + tProj * (virtualObj.xy - eyeObj.xy);
                float2 centerNorm = float2(centerLocal.x / safeHalfX,
                                           centerLocal.y / safeHalfY);
                centerNorm.x *= _AspectRatio;

                float2 pParallax = pBase + parallaxOffset;
                float2 pProjected = pBase - centerNorm;
                float2 p = lerp(pParallax, pProjected, saturate(_UseProjectedEmitter));

                // Pixel-size in UV space — used for AA fringe.
                // fwidth gives us half the screen-space derivative magnitude.
                float fw = fwidth(length(p)) * 0.5;
                float aa = max(fw, 0.0002);

                // --- Sharpness knob ---
                // _DotSharpness=1 → hair-thin AA fringe; =0 → wider soft edge.
                aa = lerp(aa * 8.0, aa, _DotSharpness);

                float reticleMask = 0.0;

                // --- Dot (type 0 or 3) ---
                if (_ReticleType == 0 || _ReticleType == 3)
                {
                    float dDot = sdCircle(p, _DotSize);
                    reticleMask = max(reticleMask, sdfCoverage(dDot, aa));
                }

                // --- Ring (type 1 or 3) ---
                if (_ReticleType == 1 || _ReticleType == 3)
                {
                    float dRing = sdRing(p, _RingRadius, _RingThickness);
                    reticleMask = max(reticleMask, sdfCoverage(dRing, aa));
                }

                // --- Crosshair (type 2) ---
                if (_ReticleType == 2)
                {
                    float dCross = sdCross(p,
                                           _CrossLength   * 0.5,
                                           _CrossThickness * 0.5,
                                           _CrossGap);
                    reticleMask = max(reticleMask, sdfCoverage(dCross, aa));
                }

                // --- Glow ---
                // Glow is driven by the nearest reticle feature's SDF.
                float nearestSDF = 1e9;

                if (_ReticleType == 0 || _ReticleType == 3)
                    nearestSDF = min(nearestSDF, sdCircle(p, _DotSize));
                if (_ReticleType == 1 || _ReticleType == 3)
                    nearestSDF = min(nearestSDF, sdRing(p, _RingRadius, _RingThickness));
                if (_ReticleType == 2)
                    nearestSDF = min(nearestSDF, sdCross(p,
                                                         _CrossLength   * 0.5,
                                                         _CrossThickness * 0.5,
                                                         _CrossGap));

                // Glow only outside the solid reticle (nearestSDF > 0).
                float glowDist = max(0.0, nearestSDF);
                float glow = 0.0;
                if (_GlowRadius > 0.0001)
                {
                    float t = saturate(glowDist / _GlowRadius);
                    glow = _GlowIntensity * pow(1.0 - t, _GlowFalloff);
                }

                // --- Combine ---
                fixed4 col  = _DotColor  * reticleMask;
                fixed4 glowCol = _GlowColor * glow;

                // Additive blend: glow fills in behind the solid dot.
                fixed4 outCol = col + glowCol * (1.0 - reticleMask);

                // Alpha drives additive intensity — keep it non-zero where visible.
                outCol.a = saturate(reticleMask + glow * 0.3);

                return outCol;
            }
            ENDCG
        }
    }

    FallBack Off
}
