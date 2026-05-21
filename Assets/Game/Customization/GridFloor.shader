// Anti-aliased infinite grid for the customize-scene preview floor.
// Uses world-space XZ coordinates so a single large quad reads as a tiled
// grid regardless of the quad's own UVs / tiling. Derivatives (fwidth) keep
// the line width consistent at any view angle / distance.
//
// Properties are tuned for the customize scene (charcoal background, half-
// strength grey lines, 0.5m cells fading out at 8m) but every value is
// editable from the material inspector.
Shader "CathayCrossing/GridFloor"
{
    Properties
    {
        _BgColor("Background", Color) = (0.04, 0.04, 0.06, 1)
        _LineColor("Line", Color) = (0.45, 0.45, 0.52, 1)
        _CellSize("Cell Size (units)", Float) = 0.5
        _LineWidth("Line Width (pixels)", Float) = 1.2
        _FadeDistance("Fade Distance", Float) = 8.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BgColor;
                float4 _LineColor;
                float _CellSize;
                float _LineWidth;
                float _FadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs v = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = v.positionCS;
                OUT.positionWS  = v.positionWS;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // World-XZ → grid coordinate.
                float2 uv = IN.positionWS.xz / max(_CellSize, 1e-4);
                // Distance from nearest grid line, normalised to pixel width.
                float2 grid = abs(frac(uv - 0.5) - 0.5) / fwidth(uv);
                float minDistInPixels = min(grid.x, grid.y);
                // Line mask: 1 on the line, fading off over ~_LineWidth pixels.
                float lineMask = 1.0 - min(minDistInPixels / max(_LineWidth, 0.1), 1.0);

                // Radial fade from origin so the grid sinks into the background
                // toward the edges instead of clipping at the quad bounds.
                float dist = length(IN.positionWS.xz);
                float fade = saturate(1.0 - dist / max(_FadeDistance, 0.01));
                lineMask *= fade;

                float3 col = lerp(_BgColor.rgb, _LineColor.rgb, lineMask);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
