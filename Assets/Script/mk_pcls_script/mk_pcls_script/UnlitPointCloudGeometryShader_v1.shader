Shader "Custom/PointCloudGeometryShader"
{
    Properties
    {
        _PointSize ("Point Size (world units)", Range(0.0005, 0.1)) = 0.003
        _SoftEdge  ("Soft Edge Width", Range(0.0, 0.5)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom
            #pragma target 4.0

            #include "UnityCG.cginc"

            float _PointSize;
            float _SoftEdge;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
            };

            struct v2g
            {
                float4 worldPos : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            struct g2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv    : TEXCOORD0;  // [-1,1] for circle masking
            };

            // Vertex Shader — pass world position to geometry stage
            v2g vert(appdata v)
            {
                v2g o;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.color = v.color;
                return o;
            }

            // Geometry Shader — expand each point into a camera-facing circular disc (4 verts, 1 strip)
            [maxvertexcount(4)]
            void geom(point v2g p[1], inout TriangleStream<g2f> triStream)
            {
                float3 worldCenter = p[0].worldPos.xyz;

                // Camera right & up vectors in world space
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 camUp    = normalize(UNITY_MATRIX_V[1].xyz);

                float half = _PointSize * 0.5;

                // UV corners for circle test
                float2 uvs[4] = {
                    float2(-1, -1),
                    float2( 1, -1),
                    float2(-1,  1),
                    float2( 1,  1)
                };

                // World-space offsets for a camera-facing quad
                float3 offsets[4] = {
                    (-camRight - camUp) * half,
                    ( camRight - camUp) * half,
                    (-camRight + camUp) * half,
                    ( camRight + camUp) * half
                };

                for (int i = 0; i < 4; i++)
                {
                    g2f o;
                    float3 wpos = worldCenter + offsets[i];
                    o.pos   = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                    o.color = p[0].color;
                    o.uv    = uvs[i];
                    triStream.Append(o);
                }
            }

            // Fragment Shader — circular disc with optional soft edge
            fixed4 frag(g2f i) : SV_Target
            {
                float dist = length(i.uv);  // distance from centre [0..√2]

                // Hard discard outside unit circle
                if (dist > 1.0)
                    discard;

                // Soft edge: smoothstep fade near the rim
                float alpha = 1.0 - smoothstep(1.0 - _SoftEdge, 1.0, dist);

                fixed4 col = i.color;
                col.a = alpha;
                return col;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}