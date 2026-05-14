Shader "Custom/PointCloud"
{
    Properties
    {
        [HideInInspector] _PclSize ("Point Cloud Size", Vector) = (4, 0, 0, 0)
        [HideInInspector] _ClippingMode ("Clipping Mode", Int) = 0
        [HideInInspector] _MapAreaMin ("Map Area Min", Vector) = (0, 0, 0, 0)
        [HideInInspector] _MapAreaMax ("Map Area Max", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "../AlphaClipping.hlsl"

            struct appdata
            {
                // vertex defines the coordinates of a unit square, and its purpose is to specify
                // the relative offset of each vertex within the billboard. It needs to work together
                // with the billboard’s size, since the actual billboard size depends on whether this
                // offset calculation is done in local space or in clip space.
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct VS_output
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float4 color: COLOR;
            };

            // Instance data
            struct InstanceData
            {
                float3 relativePosition;
                uint rgba;
            };
            StructuredBuffer<InstanceData> _InstanceBuffer;

            float4x4 _LocalToWorld;
            float4x4 _WorldToLocal;
            float4 _PclSize;
            int _ClippingMode;
            float3 _MapAreaMin;
            float3 _MapAreaMax;

            float4 UnpackColor(uint packedColor) {
                float a = ((packedColor >> 24) & 0x000000FF) / 255.0;
                float b = ((packedColor >> 16) & 0x000000FF) / 255.0;
                float g = ((packedColor >> 8) & 0x000000FF) / 255.0;
                float r = (packedColor & 0x000000FF) / 255.0;
                return float4(r, g, b, a);
            }

            VS_output vert(appdata v)
            {
                InitIndirectDrawArgs(0);

                VS_output o;

                InstanceData instanceData = _InstanceBuffer[v.instanceID];

                // This is a combined parameter related to the size of the point.
                // The first component {x} defines the size type (fixed=0, splat=1).
                // The second component {y} defines the size space (screenSpace=0, worldSpace=1).
                // The last two components {zw} defines the size of points (sizeX, sizeY).
                // The last two "zw" represent sizes in screen space in pixels, while in world space they are in meters.
                bool useWorldSpace = _PclSize.y > 0.5;
                if (useWorldSpace)
                {
                    float4 centerWorld = mul(_LocalToWorld, float4(instanceData.relativePosition, 1.0));
                    float4 centerVS = mul(UNITY_MATRIX_V, centerWorld);
                    float4 centerCS = mul(UNITY_MATRIX_P, centerVS);
                    o.worldPosition = centerWorld.xyz;

                    // Compute clip-space position for +1 view-space offset in X/Y.
                    // This helps derive how many NDC units (i.e., screen pixels when scaled by w and ScreenParams)
                    // correspond to one unit in view space along X and Y.
                    float4 projXYClip = mul(UNITY_MATRIX_P, centerVS + float4(1.0, 1.0, 0.0, 0.0));
                    // pixScale: NDC-space scale per +1 view-space unit in X/Y
                    float2 pixScaleNDC = float2(abs(centerCS.x / centerCS.w - projXYClip.x / projXYClip.w), abs(centerCS.y / centerCS.w - projXYClip.y / projXYClip.w));

                    float2 ptSizeNDC = _PclSize.xx * pixScaleNDC;

                    // Max/min billboard size in pixels (converted to NDC via 2/screen):
                    // max: 32 pixels wide/high, min: 4 pixels (ensures visibility and avoids oversized splats)
                    float2 maxSizeNDC = 16.0 * (2.0 / _ScreenParams.xy);
                    float2 minSizeNDC = 2.0 * (2.0 / _ScreenParams.xy);
                    if (ptSizeNDC.x > maxSizeNDC.x || ptSizeNDC.y > maxSizeNDC.y)
                    {
                        ptSizeNDC = maxSizeNDC;
                    }

                    if (ptSizeNDC.x < minSizeNDC.x || ptSizeNDC.y < minSizeNDC.y)
                    {
                        ptSizeNDC = minSizeNDC;
                    }

                    // Apply NDC offset and reconstruct clip-space position
                    o.position = centerCS / centerCS.w;
                    o.position.xy += float2(v.vertex.x * ptSizeNDC.x, -v.vertex.y * ptSizeNDC.y);
                }
                else
                {
                    // Screen space
                    float3 relativePosition = instanceData.relativePosition;
                    float4 worldPos = mul(_LocalToWorld, float4(relativePosition, 1.0));
                    float4 clipPos = mul(UNITY_MATRIX_VP, worldPos);
                    o.worldPosition = worldPos.xyz;

                    float2 pixelToClip = (2.0 / _ScreenParams.xy) * clipPos.w;

                    // v.vertex defines a unit square with x/y in screen space coordinates
                    clipPos.xy += float2(v.vertex.x * _PclSize.x, -v.vertex.y * _PclSize.x) * pixelToClip;

                    o.position = clipPos;
                }

                o.uv = float2(v.vertex.x, v.vertex.y) * 2.0;
                o.color = UnpackColor(instanceData.rgba);

                return o;
            }

            fixed4 frag(VS_output i) : SV_Target
            {
                float clippingAlpha = 1.0;
                AlphaClipping_float(i.worldPosition, _WorldToLocal, _ClippingMode, _MapAreaMin, _MapAreaMax, clippingAlpha);
                clip(clippingAlpha - 0.5);

                float radius = length(i.uv);
                clip(1 - radius);

                return i.color;
            }
            ENDHLSL
        }
    }
}
