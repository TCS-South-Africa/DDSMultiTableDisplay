Shader "MTD/Columns/Film Lights"
{
    Properties
    {
        [PerRendererData] _MainTex ("White", 2D) = "white" {}
        _Burst ("Winning sparks", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Burst;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float alpha : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.alpha = v.color.a;
                return o;
            }
            float2 tile(float2 uv, float index, float2 grid, float2 pixels)
            {
                float2 cell = float2(fmod(index, grid.x), grid.y - 1 - floor(index / grid.x));
                return (cell + (saturate(uv) * (pixels - 1) + .5) / pixels) / grid;
            }
            float borderLight(float2 p, float time, float phase)
            {
                // Measure across and along the painted frame. The whole streak bends
                // around the arc, including the part still on the incoming edge.
                const float radius = 20;
                const float2 straight = float2(309,168.5) - radius;
                const float arc = radius * 1.57079633;
                const float quarter = straight.x + arc + straight.y;
                float2 q = abs(p) - straight;
                float normal = length(max(q,0)) + min(max(q.x,q.y),0) - radius;
                if (abs(normal) > 16) return 0;
                float along;
                if (all(q > 0)) along = straight.x + radius * atan2(q.x,q.y);
                else if (q.x > q.y) along = straight.x + arc + straight.y - abs(p.y);
                else along = abs(p.x);
                if (p.y < 0) along = 2 * quarter - along;
                if (p.x < 0) along = -along;
                float period = 2 * quarter; // two opposing highlights
                float travel = time * 83 - 234 + phase * 725;
                float d = (frac((along - travel) / period + .5) - .5) * period;
                float core = exp2(-d*d/850 - normal*normal/9);
                float halo = exp2(-d*d/2100 - normal*normal/35);
                return .74 * core + .24 * halo;
            }

            float hash(float2 p)
            {
                float3 h = frac(float3(p.xyx) * float3(.1031,.1030,.0973));
                h += dot(h, h.yzx + 33.33);
                return frac((h.x+h.y)*h.z);
            }

            float motes(float2 p, float time, float2 spacing, float speed, float seed)
            {
                float lane = floor(p.x / spacing.x);
                float drift = speed * lerp(.72,1.38,hash(float2(lane,seed)));
                float y = p.y / spacing.y - fmod(time * drift / spacing.y,64);
                float row = floor(y);
                float2 id = float2(lane + seed, frac(row / 64) * 64);
                float size = hash(id);
                float phase = hash(id + 4.91);
                float2 centre = float2(.5 + .25*(phase-.5) + .10*sin(time*1.6+phase*32),
                                       .5 + .27*(hash(id+8.13)-.5));
                float2 delta = (float2(frac(p.x/spacing.x),frac(y)) - centre) * spacing;
                float dist2 = dot(delta,delta);
                float r = lerp(.85,3.8,size*size);
                float pulse = .75 + .25*sin(time*(2+phase*3)+size*40);
                return pulse * (.86*exp2(-dist2/(r*r)) + .19*exp2(-dist2/(6*r*r)));
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float mode = floor(i.uv.x * .5);
                float2 uv = i.uv - 2 * floor(i.uv * .5);
                if (mode >= 3)
                {
                    float light = tex2D(_Burst, tile(uv, mode-3, float2(8,6), float2(256,256))).r;
                    return float4(light * float3(1,.96,.89), i.alpha);
                }
                float2 location = (uv - .5) * float2(640,364);
                float phase = floor(i.uv.y * .5);
                float glint = borderLight(location,_Time.y,phase);
                float3 colour = glint * float3(1,.78,.22);
                if (mode > 0)
                {
                    // Four columns widen the artwork. Emit round motes at the
                    // same physical density as the six-column design.
                    float2 field = location;
                    field.x *= fwidth(location.y) / max(fwidth(location.x),.0001);
                    float2 edge = float2(295,155) - abs(location);
                    float fade = smoothstep(0,14,min(edge.x,edge.y));
                    if (fade > 0)
                    {
                        float t = _Time.y + phase*2.71 + mode*5.2;
                        float far = motes(field,t,float2(41,57),59,mode*11);
                        float near = motes(field+17,t,float2(83,90),122,mode*19);
                        float3 hue = mode < 2 ? float3(.85,.016,.96) : float3(1,.55,.028);
                        colour += fade * (hue*(.62*far + 1.2*near) + float3(.6,.46,.32)*near*near*.25);
                    }
                }
                return float4(colour, i.alpha);
            }
            ENDCG
        }
    }
}
