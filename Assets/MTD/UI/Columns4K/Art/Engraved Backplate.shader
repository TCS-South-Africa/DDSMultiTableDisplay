Shader "MTD/Columns/Engraved Backplate"
{
    Properties { [PerRendererData] _MainTex ("Engraving relief", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 tint : COLOR; };
            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.tint = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float slot = floor(i.uv.y/2);
                float2 uv = i.uv-float2(0,2*slot);
                float2 relief = tex2D(_MainTex,uv).rg;
                float travel = frac((_Time.y-slot*.63)/10.4)*5.0-.58;
                float distance = (uv.x+.18*(1-uv.y)-travel)/.18;
                float rake = exp2(-distance*distance);
                float highlight = relief.r*(.051+.245*rake);
                float shadow = relief.g*(.26+.35*rake);
                float alpha = (highlight+shadow*(1-highlight))*i.tint.a;
                return fixed4(i.tint.rgb*highlight*i.tint.a, alpha);
            }
            ENDCG
        }
    }
}
