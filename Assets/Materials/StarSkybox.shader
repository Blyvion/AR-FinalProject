Shader "Skybox/ProceduralStars"
{
    Properties
    {
        _StarDensity    ("Star Density",      Range(100, 3000)) = 1000
        _StarSize       ("Star Size",         Range(0.001, 0.05)) = 0.012
        _StarBrightness ("Star Brightness",   Range(0.5, 10.0)) = 4.0
        _TwinkleSpeed   ("Twinkle Speed",     Range(0, 5)) = 1.5
        _TwinkleAmount  ("Twinkle Amount",    Range(0, 0.5)) = 0.2
        _SpaceColor     ("Deep Space Color",  Color) = (0.0, 0.0, 0.015, 1)
        _NebulaStrength ("Milky Way Strength",Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #include "UnityCG.cginc"

            float   _StarDensity;
            float   _StarSize;
            float   _StarBrightness;
            float   _TwinkleSpeed;
            float   _TwinkleAmount;
            fixed4  _SpaceColor;
            float   _NebulaStrength;

            // --------------- data types ---------------

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // --------------- hash / noise helpers ---------------

            // Returns a value in [0,1) given a 3-D integer lattice point.
            float hash1(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // 2-D variant used in the star grid.
            float hash1_2(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float2 hash2_2(float2 p)
            {
                return float2(hash1_2(p), hash1_2(p + 37.13));
            }

            // Value noise on a 3-D lattice (used for the Milky Way nebula).
            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(lerp(hash1(i + float3(0,0,0)), hash1(i + float3(1,0,0)), f.x),
                         lerp(hash1(i + float3(0,1,0)), hash1(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash1(i + float3(0,0,1)), hash1(i + float3(1,0,1)), f.x),
                         lerp(hash1(i + float3(0,1,1)), hash1(i + float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            // Fractional Brownian motion – gives the cloudy nebula look.
            float fbm(float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                float3 shift = float3(100, 100, 100);
                for (int i = 0; i < 5; i++)
                {
                    v += a * vnoise(p);
                    p  = p * 2.0 + shift;
                    a *= 0.5;
                }
                return v;
            }

            // --------------- star rendering ---------------
            // Maps the unit direction to an equirectangular UV, then uses a
            // Voronoi-style cell grid to place one star per cell (with some
            // cells left empty).  Returns accumulated star brightness.
            float stars(float3 dir, float density, float size)
            {
                const float PI = 3.14159265;

                float2 uv;
                uv.x = atan2(dir.z, dir.x) / (2.0 * PI) + 0.5;
                uv.y = asin(clamp(dir.y, -1.0, 1.0)) / PI + 0.5;

                float2 scaled  = uv * density;
                float2 cellID  = floor(scaled);
                float2 cellUV  = frac(scaled);

                float result = 0.0;

                [unroll]
                for (int cy = -1; cy <= 1; cy++)
                {
                    [unroll]
                    for (int cx = -1; cx <= 1; cx++)
                    {
                        float2 neighbor = float2(cx, cy);
                        float2 id       = cellID + neighbor;

                        float2 rnd      = hash2_2(id);

                        // Only ~40 % of cells actually contain a star.
                        float presence  = step(0.6, rnd.x);

                        // Random position of the star inside its cell.
                        float2 starPos  = rnd;
                        float2 toStar   = neighbor + starPos - cellUV;
                        float  dist     = length(toStar);

                        // Brightness varies per star.
                        float brightness = pow(hash1_2(id * 7.39 + 0.5), 2.5) + 0.1;

                        // Per-star twinkle phase.
                        float phase   = hash1_2(id * 13.71) * 6.28318;
                        float twinkle = 1.0 + _TwinkleAmount
                                            * sin(_Time.y * _TwinkleSpeed + phase);

                        // Smooth circular glow.
                        float glow = max(0.0, 1.0 - dist / max(size, 0.0001));
                        glow = pow(glow, 2.5);

                        result += glow * brightness * twinkle * presence;
                    }
                }
                return result;
            }

            // --------------- vertex ---------------

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // --------------- fragment ---------------

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // --- base deep-space colour ---
                fixed4 col = _SpaceColor;

                // --- Milky Way band ---
                // A tilted band of diffuse nebula haze.
                float3 mwAxis = normalize(float3(0.25, 1.0, 0.35));
                float  band   = abs(dot(dir, mwAxis));
                band = 1.0 - smoothstep(0.0, 0.65, band);

                float nebula = fbm(dir * 1.4) * fbm(dir * 2.2 + 4.3);
                col.rgb += nebula * _NebulaStrength * band
                           * fixed3(0.09, 0.05, 0.14);          // purple-ish haze
                col.rgb += nebula * _NebulaStrength * 0.25
                           * fixed3(0.02, 0.04, 0.12);          // faint blue overall

                // --- three layers of stars (bright+sparse / medium / dim+dense) ---
                float starField = 0.0;
                starField += stars(dir, _StarDensity * 0.35, _StarSize * 1.6);        // large bright
                starField += stars(dir, _StarDensity,        _StarSize)       * 0.7;  // standard
                starField += stars(dir, _StarDensity * 2.5,  _StarSize * 0.4) * 0.3;  // faint background

                col.rgb += starField * _StarBrightness;

                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
