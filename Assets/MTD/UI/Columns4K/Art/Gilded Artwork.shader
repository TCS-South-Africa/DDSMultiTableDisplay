Shader "MTD/Columns/Gilded Artwork"
{
    Properties { [PerRendererData] _MainTex ("Artwork", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Header Sheen.cginc"
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float sheen : TEXCOORD1; fixed4 color : COLOR; };
            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.sheen = MtdSheenCoordinate(o.pos);
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 paint = tex2D(_MainTex,i.uv) * i.color;
                paint.rgb = MtdGoldSheen(paint.rgb,i.sheen);
                return paint;
            }
            ENDCG
        }
    }
}
