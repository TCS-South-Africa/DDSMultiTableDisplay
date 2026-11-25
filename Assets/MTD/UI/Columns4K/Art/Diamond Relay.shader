Shader "MTD/Columns/Diamond Relay"
{
    Properties
    {
        [PerRendererData] _MainTex ("White", 2D) = "white" {}
        _Elapsed ("Seconds since page presentation", Float) = 0
        _Slots ("Columns", Float) = 6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Elapsed, _Slots;
            struct v2f { float4 pos : SV_POSITION; float2 p : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.p = (v.texcoord.xy - .5) * float2(3820,88);
                return o;
            }
            float star(float2 p)
            {
                p = abs(p);
                return .7*exp2(-dot(p,p)/36) + .48*exp2(-p.x/19-p.y/1.3)
                    + .52*exp2(-p.x/1.4-p.y/11) + .14*exp2(-dot(p,p)/210);
            }
            float4 frag(v2f i) : SV_Target
            {
                float x = abs(i.p.x), y = abs(i.p.y);
                float u = saturate(_Elapsed/1.28);
                float head = 1910*u*u*(3-2*u);
                float wake = head-x;
                float pitch = 3820/_Slots;
                float joint = floor(x/pitch+.5)*pitch;
                float dx = x-joint;
                // Inverse smoothstep gives the instant the travelling light reached this facet.
                float arrival = 1.28*(.5-sin(asin(1-2*saturate(joint/1910))/3));
                float age = _Elapsed-arrival;
                float catchLight = smoothstep(-.045,.055,age)*(1-smoothstep(.105,.54,age));
                catchLight *= 1-step(1909,joint);
                float beam = smoothstep(-3,2,wake)*exp2(-y*y/2.7);
                float wakeLight = exp2(-max(0,wake)/83)*smoothstep(-2,1,wake)*exp2(-y*y/22);
                float headLight = star(float2(wake,i.p.y))*(1-smoothstep(1.28,1.61,_Elapsed));
                float facet = saturate(1-abs(dx)/27-y/18);
                float face = smoothstep(0,.07,facet) * (dx*i.p.y>0 ? .54 : .22);
                // The arcs pick up the four rounded frame corners beside each diamond.
                float2 corner = max(float2(30-abs(dx),30-y),0);
                float edge = (length(corner)-21)/2.2;
                float rim = exp2(-edge*edge-abs(dx)/65-y/29);
                float facetLight = catchLight*(star(float2(dx,i.p.y)) + face + .8*rim);
                float fade = smoothstep(0,.12,_Elapsed)*(1-smoothstep(1.37,2.24,_Elapsed));
                float3 gold = float3(1,.55,.17)*(.43*beam+.58*wakeLight+.55*facetLight);
                return float4((gold+float3(1,.88,.61)*(headLight+.56*facetLight))*fade,0);
            }
            ENDCG
        }
    }
}
