Shader "Unlit/Terrain"
{
    Properties
    {
        heightmap ("Heightmap", 2D) = "white" {}
    }
      SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            sampler2D heightmap;
            uniform int border;
            uniform float size;
            uniform float tsize;
						uniform float2 offset;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float2 uv = v.uv.xy * (size / tsize) + (border / size);
                v.vertex.y += tex2Dlod(heightmap, float4(uv, 0, 0)).x * 32;
                o.uv = v.uv.xy;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 off = float2(1.0, 0) / size;
								float2 uv = i.uv.xy * (size / tsize) + (border / size);
                float h = tex2D(heightmap, float4(uv, 0, 0)).x;
                float l = tex2D(heightmap, float4(uv - off.xy, 0, 0)).x;
                float r = tex2D(heightmap, float4(uv + off.xy, 0, 0)).x;
                float u = tex2D(heightmap, float4(uv + off.yx, 0, 0)).x;
                float d = tex2D(heightmap, float4(uv - off.yx, 0, 0)).x;
                
                float dx1 = h - l;
                float dx2 = h - r;
                float dy1 = h - u;
                float dy2 = h - d;

                float dp = off.x*2;

                float3 t1 = float3(-dp, dx1, 0);
                float3 t2 = float3(dp, dx2, 0);
                float3 b1 = float3(0, dy1, dp);
                float3 b2 = float3(0, dy2, -dp);

                float3 e1 = cross(t1, b1);
                float3 e2 = cross(t2, b2);

                float3 n = normalize(e1 + e2);

                float3 L = normalize(float3(1, 1, 0));
                float light = saturate(dot(L, n));
                float3 color = float3(38, 84, 33) / 255.0;
                if (h > 0.4) color = float3(1, 1, 1);
                color = lerp(color, (133, 112, 37) / 255.0, 1-n.y)*2;
                color *= (light + 0.01);
								return float4(color, 1);

                //

								//i.uv.xy += offset;
								//float2 p2d = i.uv.xy;
								//float3 n = 0;
								//float nf = 1.0;
								//float na = 0.6;
								//for (int s = 0; s < 2; s++) {
								//	n += noised(p2d * nf) * na * float3(1.0, nf, nf);
								//	na *= 0.5;
								//	nf *= 2.0;
								//}

								//float3 p = float3(i.uv.x, 0, i.uv.y);
								//float eps = 0.001;
								//float3 x = float3(1, 0, 0);
								//float3 y = float3(0, 1, 0);
								//float3 z = float3(0, 0, 1);
								//float h = baseHeight(p);
								//float a = baseHeight(p + x*eps);
								//float b = baseHeight(p + y*eps);
								//float c = baseHeight(p + z*eps);

								//float3 grad = float3(a - h, b - h, c - h);

								///*float dx1 = h - l;
								//float dx2 = r - h;
								//float dy1 = u - h;
								//float dy2 = h - d;

								//float3 t1 = (dx1 * float3(0, 1, 0) - tangent * eps);
								//float3 t2 = (dx2 * float3(0, 1, 0) + tangent * eps);
								//float3 b1 = (dy1 * float3(0, 1, 0) + bitangent * eps);
								//float3 b2 = (dy2 * float3(0, 1, 0) - bitangent * eps);*/
								//if (i.uv.x < 4) {
								//	return float4(normalize(grad), 1);
								//}
								//else {
								//	return float4(n.y, 0, n.z, 1);
								//}

								//
            }
            ENDCG
        }
    }
}
