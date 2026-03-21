namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Contains OpenGL shader source code for all renderers.
    /// </summary>
    public static class ShaderSource
    {
        /// <summary>
        /// Vertex shader for point rendering.
        /// </summary>
        public const string PointVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in float aSelected;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            out vec3 WorldPos;
            out vec3 Normal;
            out float Selected;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
                // Reverse Z
                gl_PointSize = 4.0;
                WorldPos = aPos;
                Normal = aNormal;
                Selected = aSelected;
            }";

        /// <summary>
        /// Fragment shader for point rendering.
        /// </summary>
        public const string PointFragment = @"#version 300 es
            precision highp float;
            in vec3 WorldPos;
            in vec3 Normal;
            in float Selected;
            
            uniform float uUseDynamicColor;
            uniform float uWorldMinY;
            uniform float uWorldMaxY;
            
            out vec4 FragColor;

            vec3 colormap(float t) {
                const vec3 c0 = vec3(0.277, 0.005, 0.334);
                const vec3 c1 = vec3(0.198, 0.410, 0.551);
                const vec3 c2 = vec3(0.122, 0.638, 0.518);
                const vec3 c3 = vec3(0.395, 0.812, 0.347);
                const vec3 c4 = vec3(0.993, 0.906, 0.144);
                
                if (t < 0.25) return mix(c0, c1, t / 0.25);
                if (t < 0.5) return mix(c1, c2, (t - 0.25) / 0.25);
                if (t < 0.75) return mix(c2, c3, (t - 0.5) / 0.25);
                return mix(c3, c4, (t - 0.75) / 0.25);
            }

            void main() {
                vec3 n = length(Normal) > 0.0001 ? normalize(Normal) : vec3(0.0, 1.0, 0.0);
                vec3 lightDir = normalize(vec3(0.35, 1.0, 0.25));
                float lambert = max(dot(n, lightDir), 0.2);
                
                vec3 base;
                if (Selected > 0.5) {
                    base = vec3(1.0, 0.62, 0.12);
                } else if (uUseDynamicColor > 0.5) {
                    float range = max(0.001, uWorldMaxY - uWorldMinY);
                    float normalizedHeight = clamp((WorldPos.y - uWorldMinY) / range, 0.0, 1.0);
                    base = colormap(normalizedHeight);
                } else {
                    base = mix(vec3(0.7, 0.8, 1.0), abs(n), 0.65);
                }
                
                FragColor = vec4(base * lambert, 1.0);
            }";

        /// <summary>
        /// Vertex shader for surfel rendering.
        /// </summary>
        public const string SurfelVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aVertex;
            layout (location = 1) in vec3 iPos;
            layout (location = 2) in vec3 iNormal;
            layout (location = 3) in float iSpawnTime;
            layout (location = 4) in float iSelected;
            uniform mat4 uView;
            uniform mat4 uProjection;
            uniform float uScale;
            uniform float uCurrentTime;
            uniform vec3 uHoveredPos;
            uniform float uHasHovered;
            uniform float uUseDynamicColor;
            uniform float uWorldMinY;
            uniform float uWorldMaxY;
            
            out vec3 Normal;
            out vec3 Color;

            vec3 colormap(float t) {
                const vec3 c0 = vec3(0.277, 0.005, 0.334);
                const vec3 c1 = vec3(0.198, 0.410, 0.551);
                const vec3 c2 = vec3(0.122, 0.638, 0.518);
                const vec3 c3 = vec3(0.395, 0.812, 0.347);
                const vec3 c4 = vec3(0.993, 0.906, 0.144);

                if (t < 0.25) return mix(c0, c1, t / 0.25);
                if (t < 0.5) return mix(c1, c2, (t - 0.25) / 0.25);
                if (t < 0.75) return mix(c2, c3, (t - 0.5) / 0.25);
                return mix(c3, c4, (t - 0.75) / 0.25);
            }
            
            void main() {
                vec3 norm = normalize(iNormal);
                float s = norm.z >= 0.0 ? 1.0 : -1.0;
                float a = -1.0 / (s + norm.z);
                float b = norm.x * norm.y * a;
                vec3 tangent = normalize(vec3(1.0 + s * norm.x * norm.x * a, s * b, -s * norm.x));
                vec3 bitangent = normalize(vec3(b, s + norm.y * norm.y * a, -norm.y));

                vec3 worldPos = iPos + (tangent * aVertex.x + bitangent * aVertex.z) * uScale;
                gl_Position = uProjection * uView * vec4(worldPos, 1.0);
                Normal = norm;

                float age = uCurrentTime - iSpawnTime;
                if (iSelected > 0.5) {
                    Color = vec3(1.0, 0.62, 0.12);
                } else if (uHasHovered > 0.5 && length(iPos - uHoveredPos) < 0.001) {
                    Color = vec3(1.0, 0.0, 1.0);
                } else if (uUseDynamicColor > 0.5) {
                    float range = max(0.001, uWorldMaxY - uWorldMinY);
                    float normalizedHeight = clamp((iPos.y - uWorldMinY) / range, 0.0, 1.0);
                    Color = colormap(normalizedHeight);
                } else if (iSpawnTime <= 0.0 || age > 5.0 || age < 0.0) {
                    Color = vec3(0.0, 0.7, 1.0);
                } else {
                    float t = age / 5.0;
                    Color = mix(vec3(1.0, 0.0, 1.0), vec3(0.0, 0.7, 1.0), t);
                }
            }";

        /// <summary>
        /// Fragment shader for surfel rendering.
        /// </summary>
        public const string SurfelFragment = @"#version 300 es
            precision highp float;
            in vec3 WorldPos;
            in vec3 Normal;
            in float SpawnTime;
            in float Selected;
            in float Highlight;
            
            uniform float uUseDynamicColor;
            uniform float uWorldMinY;
            uniform float uWorldMaxY;
            
            out vec4 FragColor;
            
            vec3 colormap(float t) {
                const vec3 c0 = vec3(0.277, 0.005, 0.334);
                const vec3 c1 = vec3(0.198, 0.410, 0.551);
                const vec3 c2 = vec3(0.122, 0.638, 0.518);
                const vec3 c3 = vec3(0.395, 0.812, 0.347);
                const vec3 c4 = vec3(0.993, 0.906, 0.144);
                
                if (t < 0.25) return mix(c0, c1, t / 0.25);
                if (t < 0.5) return mix(c1, c2, (t - 0.25) / 0.25);
                if (t < 0.75) return mix(c2, c3, (t - 0.5) / 0.25);
                return mix(c3, c4, (t - 0.75) / 0.25);
            }

            void main() {
                vec3 n = length(Normal) > 0.0001 ? normalize(Normal) : vec3(0.0, 1.0, 0.0);
                vec3 lightDir = normalize(vec3(0.35, 1.0, 0.25));
                float lambert = max(dot(n, lightDir), 0.2);
                
                vec3 base;
                if (Selected > 0.5) {
                    base = vec3(1.0, 0.3, 0.2);
                } else if (Highlight > 0.0) {
                    base = mix(vec3(0.4, 0.6, 0.9), vec3(1.0, 1.0, 0.5), Highlight);
                } else if (uUseDynamicColor > 0.5) {
                    float range = max(uWorldMaxY - uWorldMinY, 0.001);
                    float t = clamp((WorldPos.y - uWorldMinY) / range, 0.0, 1.0);
                    base = colormap(t);
                } else {
                    base = vec3(0.4, 0.6, 0.9);
                }
                
                FragColor = vec4(base * lambert, 1.0);
            }";

        /// <summary>
        /// Vertex shader for ray rendering.
        /// </summary>
        public const string RayVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aColor;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            out vec3 Color;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
                Color = aColor;
            }";

        /// <summary>
        /// Fragment shader for ray rendering.
        /// </summary>
        public const string RayFragment = @"#version 300 es
            precision highp float;
            in vec3 Color;
            out vec4 FragColor;
            void main() {
                FragColor = vec4(Color, 1.0);
            }";

        /// <summary>
        /// Vertex shader for mesh rendering.
        /// </summary>
        public const string MeshVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            out vec3 Normal;
            out vec3 WorldPos;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
                Normal = aNormal;
                WorldPos = aPos;
            }";

        /// <summary>
        /// Fragment shader for mesh rendering.
        /// </summary>
        public const string MeshFragment = @"#version 300 es
            precision highp float;
            in vec3 Normal;
            in vec3 WorldPos;
            out vec4 FragColor;
            
            void main() {
                vec3 n = length(Normal) > 0.0001 ? normalize(Normal) : vec3(0.0, 1.0, 0.0);
                vec3 lightDir = normalize(vec3(0.35, 1.0, 0.25));
                float lambert = max(dot(n, lightDir), 0.3);
                vec3 base = vec3(0.6, 0.7, 0.8);
                FragColor = vec4(base * lambert, 1.0);
            }";

        /// <summary>
        /// Vertex shader for grid rendering.
        /// </summary>
        public const string GridVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
            }";

        /// <summary>
        /// Fragment shader for grid rendering.
        /// </summary>
        public const string GridFragment = @"#version 300 es
            precision highp float;
            out vec4 FragColor;
            uniform vec3 uColor;
            void main() {
                FragColor = vec4(uColor, 1.0);
            }";

        /// <summary>
        /// Vertex shader for axis rendering.
        /// </summary>
        public const string AxesVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aColor;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            out vec3 Color;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
                Color = aColor;
            }";

        /// <summary>
        /// Fragment shader for axis rendering.
        /// </summary>
        public const string AxesFragment = @"#version 300 es
            precision highp float;
            in vec3 Color;
            out vec4 FragColor;
            void main() {
                FragColor = vec4(Color, 1.0);
            }";

        /// <summary>
        /// Vertex shader for flat color rendering.
        /// </summary>
        public const string FlatColorVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
            }";

        /// <summary>
        /// Fragment shader for flat color rendering.
        /// </summary>
        public const string FlatColorFragment = @"#version 300 es
            precision highp float;
            out vec4 FragColor;
            uniform vec4 uColor;
            void main() {
                FragColor = uColor;
            }";

        /// <summary>
        /// Vertex shader for order-independent transparency accumulation.
        /// </summary>
        public const string OitAccumVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
            }";

        /// <summary>
        /// Fragment shader for order-independent transparency accumulation.
        /// </summary>
        public const string OitAccumFragment = @"#version 300 es
            precision highp float;
            out vec4 AccumColor;
            out float AccumAlpha;
            uniform vec4 uColor;
            
            void main() {
                AccumColor = vec4(uColor.rgb * uColor.a, uColor.a);
                AccumAlpha = uColor.a;
            }";

        /// <summary>
        /// Vertex shader for order-independent transparency reveal.
        /// </summary>
        public const string OitRevealVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            uniform mat4 uView;
            uniform mat4 uProjection;
            
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
            }";

        /// <summary>
        /// Fragment shader for order-independent transparency reveal.
        /// </summary>
        public const string OitRevealFragment = @"#version 300 es
            precision highp float;
            out vec4 RevealColor;
            uniform vec4 uColor;
            
            void main() {
                RevealColor = vec4(uColor.rgb, 1.0 - uColor.a);
            }";

        /// <summary>
        /// Vertex shader for composite rendering.
        /// </summary>
        public const string CompositeVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec2 aPos;
            out vec2 TexCoord;
            void main() {
                gl_Position = vec4(aPos, 0.0, 1.0);
                TexCoord = (aPos + 1.0) * 0.5;
            }";

        /// <summary>
        /// Fragment shader for composite rendering.
        /// </summary>
        public const string CompositeFragment = @"#version 300 es
            precision highp float;
            in vec2 TexCoord;
            out vec4 FragColor;
            uniform sampler2D uColorTex;
            uniform sampler2D uAccumTex;
            uniform sampler2D uRevealTex;
            
            void main() {
                vec4 color = texture(uColorTex, TexCoord);
                vec4 accum = texture(uAccumTex, TexCoord);
                float reveal = texture(uRevealTex, TexCoord).r;
                
                // Reverse Z: depth is 1 at near, 0 at far
                float depth = color.a;
                vec3 finalColor = color.rgb;
                
                // Blend OIT
                float alpha = 1.0 - reveal;
                if (alpha > 0.0) {
                    finalColor = finalColor * (1.0 - alpha) + accum.rgb / max(accum.a, 0.001) * alpha;
                }
                
                FragColor = vec4(finalColor, 1.0);
                gl_FragDepth = 1.0 - depth;
            }";

        // =====================================================================
        // ADVANCED SHADERS FOR SCENE RENDERER
        // =====================================================================

        /// <summary>
        /// Surfel fragment shader with spawn time animation and dynamic coloring.
        /// </summary>
        public const string SurfelFragmentAdvanced = @"#version 300 es
            precision highp float;
            in vec3 WorldPos;
            in vec3 Normal;
            in float SpawnTime;
            in float Selected;
            in float Highlight;
            
            uniform float uUseDynamicColor;
            uniform float uWorldMinY;
            uniform float uWorldMaxY;
            uniform float uCurrentTime;
            
            out vec4 FragColor;
            
            vec3 colormap(float t) {
                const vec3 c0 = vec3(0.277, 0.005, 0.334);
                const vec3 c1 = vec3(0.198, 0.410, 0.551);
                const vec3 c2 = vec3(0.122, 0.638, 0.518);
                const vec3 c3 = vec3(0.395, 0.812, 0.347);
                const vec3 c4 = vec3(0.993, 0.906, 0.144);
                
                if (t < 0.25) return mix(c0, c1, t / 0.25);
                if (t < 0.5) return mix(c1, c2, (t - 0.25) / 0.25);
                if (t < 0.75) return mix(c2, c3, (t - 0.5) / 0.25);
                return mix(c3, c4, (t - 0.75) / 0.25);
            }
            
            void main() {
                vec3 n = length(Normal) > 0.0001 ? normalize(Normal) : vec3(0.0, 1.0, 0.0);
                vec3 lightDir = normalize(vec3(0.35, 1.0, 0.25));
                float lambert = max(dot(n, lightDir), 0.2);
                
                vec3 base;
                if (Selected > 0.5) {
                    base = vec3(1.0, 0.3, 0.2);
                } else if (Highlight > 0.0) {
                    base = mix(vec3(0.4, 0.6, 0.9), vec3(1.0, 1.0, 0.5), Highlight);
                } else if (uUseDynamicColor > 0.5) {
                    float range = max(uWorldMaxY - uWorldMinY, 0.001);
                    float t = clamp((WorldPos.y - uWorldMinY) / range, 0.0, 1.0);
                    base = colormap(t);
                } else {
                    base = vec3(0.4, 0.6, 0.9);
                }
                
                FragColor = vec4(base * lambert, 1.0);
            }";

        /// <summary>
        /// Ray vertex shader for OIT rendering with spawn time.
        /// </summary>
        public const string RayOitVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aBaseColor;
            layout (location = 2) in float aSpawnTime;
            uniform mat4 uView;
            uniform mat4 uProjection;
            uniform float uCurrentTime;
            uniform vec3 uCameraPos;
            out vec3 Color;
            out float Alpha;
            out float IsMissRay;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);

                float age = uCurrentTime - aSpawnTime;
                bool missRay = aBaseColor.r > 0.5 && aBaseColor.g < 0.5;
                IsMissRay = missRay ? 1.0 : 0.0;
                if (aSpawnTime <= 0.0 || age > 5.0 || age < 0.0) {
                    Color = aBaseColor;
                } else {
                    float t = age / 5.0;
                    if (missRay) {
                        Color = mix(vec3(1.0, 0.45, 0.45), vec3(1.0, 0.22, 0.22), t);
                    } else {
                        Color = aBaseColor;
                    }
                }

                float baseAlpha = missRay ? 0.75 : 0.45;
                float dist = length(aPos - uCameraPos);
                float distFade = missRay ? 1.0 : (1.0 - smoothstep(500.0, 20000.0, dist));
                Alpha = baseAlpha * distFade;
            }";

        /// <summary>
        /// Ray fragment shader for OIT accumulation.
        /// </summary>
        public const string RayOitAccumFragment = @"#version 300 es
            precision highp float;
            in vec3 Color;
            in float Alpha;
            in float IsMissRay;
            out vec4 FragColor;
            void main() {
                vec3 c = Color;
                if (IsMissRay > 0.5) {
                    c = vec3(1.0, 0.22, 0.22);
                }

                float z = gl_FragCoord.z;
                float weight = clamp(Alpha * 1e4 * pow(z, 4.0), 1e-2, 3e3);
                FragColor = vec4(c * Alpha * weight, Alpha * weight);
            }";

        /// <summary>
        /// Ray fragment shader for OIT reveal.
        /// </summary>
        public const string RayOitRevealFragment = @"#version 300 es
            precision highp float;
            in float Alpha;
            out vec4 FragColor;
            void main() {
                FragColor = vec4(Alpha, Alpha, Alpha, Alpha);
            }";

        /// <summary>
        /// Density points vertex shader with fade.
        /// </summary>
        public const string DensityPointsVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aColor;
            uniform mat4 uView;
            uniform mat4 uProjection;
            uniform float uPointSize;
            out vec3 Color;
            out vec3 WorldPos;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
                gl_PointSize = uPointSize;
                Color = aColor;
                WorldPos = aPos;
            }";

        /// <summary>
        /// Density points fragment shader with distance-based fade.
        /// </summary>
        public const string DensityPointsFragment = @"#version 300 es
            precision highp float;
            in vec3 Color;
            in vec3 WorldPos;
            uniform vec2 uCameraXZ;
            uniform float uFadeRadius;
            uniform float uFadeBand;
            uniform float uEnableFade;
            out vec4 FragColor;
            void main() {
                float alpha = 1.0;
                if (uEnableFade > 0.5) {
                    float distXZ = distance(WorldPos.xz, uCameraXZ);
                    float fadeStart = max(0.0, uFadeRadius - uFadeBand);
                    alpha = 1.0 - smoothstep(fadeStart, uFadeRadius, distXZ);
                    if (alpha <= 0.001) {
                        discard;
                    }
                }
                FragColor = vec4(Color, alpha);
            }";

        /// <summary>
        /// Gizmo solid fragment shader with lighting.
        /// </summary>
        public const string GizmoSolidFragment = @"#version 300 es
            precision highp float;
            in vec3 vNormalWorld;
            in vec3 vColor;
            in float vAlpha;
            out vec4 FragColor;
            void main() {
                if (vAlpha < 0.999) discard;
                vec3 n = normalize(vNormalWorld);
                vec3 lightDir = normalize(vec3(0.35, 0.85, 0.4));
                float diff = max(dot(n, lightDir), 0.2);
                vec3 c = clamp(vColor * diff, 0.0, 1.0);
                FragColor = vec4(c, 1.0);
            }";

        /// <summary>
        /// Gizmo accumulation fragment shader for OIT.
        /// </summary>
        public const string GizmoAccumFragment = @"#version 300 es
            precision highp float;
            in vec3 vNormalWorld;
            in vec3 vColor;
            in float vAlpha;
            out vec4 FragColor;
            void main() {
                if (vAlpha >= 0.999) discard;
                vec3 n = normalize(vNormalWorld);
                vec3 lightDir = normalize(vec3(0.35, 0.85, 0.4));
                float diff = max(dot(n, lightDir), 0.2);
                vec3 c = clamp(vColor * diff, 0.0, 1.0);
                float alpha = clamp(vAlpha, 0.0, 1.0);
                if (alpha < 0.001) discard;

                float z = gl_FragCoord.z;
                float weight = clamp(alpha * 1e4 * pow(z, 4.0), 1e-2, 3e3);
                FragColor = vec4(c * alpha * weight, alpha * weight);
            }";

        /// <summary>
        /// Gizmo reveal fragment shader for OIT.
        /// </summary>
        public const string GizmoRevealFragment = @"#version 300 es
            precision highp float;
            in float vAlpha;
            out vec4 FragColor;
            void main() {
                if (vAlpha >= 0.999) discard;
                float alpha = clamp(vAlpha, 0.0, 1.0);
                if (alpha < 0.001) discard;
                FragColor = vec4(alpha, alpha, alpha, alpha);
            }";

        /// <summary>
        /// Grid vertex shader for ray casting.
        /// </summary>
        public const string GridRaycastVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            out vec2 v_uv;
            void main() {
                gl_Position = vec4(aPos, 1.0);
                v_uv = aPos.xy * 0.5 + 0.5;
            }";

        /// <summary>
        /// Grid vertex shader that preserves clip-space coordinates for ray casting.
        /// </summary>
        public const string GridClipVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            out vec2 v_uv;
            void main() {
                gl_Position = vec4(aPos, 1.0);
                v_uv = aPos.xy;
            }";

        /// <summary>
        /// Common grid fragment shader code for ray casting.
        /// </summary>
        public const string GridRaycastCommon = @"
            precision highp float;
            in vec2 v_uv;
            uniform mat4 uView;
            uniform mat4 uProjection;
            uniform vec3 uCameraPos;
            uniform float uGridPlaneY;
            const int GRID_TIER_COUNT = 14;
            const float GRID_MIN_SPACING = 0.001;
            uniform float uGridCameraHeight;
            uniform float uGridFadeStart;
            uniform float uGridFadeEnd;
            float GridFootprint(vec2 localXZ, float spacing, vec2 phase) {
                vec2 uv = (localXZ + phase) / spacing;
                vec2 deriv = max(fwidth(uv), vec2(1e-6));
                return max(deriv.x, deriv.y);
            }

            float GridLineAA(vec2 localXZ, float spacing, vec2 phase, float lineWidthPx) {
                vec2 uv = (localXZ + phase) / spacing;
                vec2 deriv = max(fwidth(uv), vec2(1e-6));
                vec2 distToLine = abs(fract(uv - 0.5) - 0.5) / deriv;
                float lineDist = min(distToLine.x, distToLine.y);
                float aaWidth = 1.0 + clamp(max(deriv.x, deriv.y) * 0.85, 0.0, 2.5);
                return 1.0 - smoothstep(lineWidthPx, lineWidthPx + aaWidth, lineDist);
            }

            float GridFadeOut(float footprint, float startFootprint, float endFootprint) {
                return 1.0 - smoothstep(startFootprint, endFootprint, footprint);
            }

            float GridHeightLogRatio(float cameraHeight, float spacing) {
                float ratio = cameraHeight / max(spacing, 1e-4);
                return log2(max(ratio, 1e-6)) / log2(10.0);
            }

            float GridHeightWeight(float cameraHeight, float spacing) {
                float logRatio = GridHeightLogRatio(cameraHeight, spacing);
                float coarseFadeIn = smoothstep(-3.3, -1.4, logRatio);
                float fineFadeOut = 1.0 - smoothstep(0.9, 2.1, logRatio);
                return coarseFadeIn * fineFadeOut;
            }

            float GridTierStrength(float cameraHeight, float spacing, float horizonBlend) {
                float logRatio = GridHeightLogRatio(cameraHeight, spacing);
                float coarseFactor = 1.0 - smoothstep(-0.2, 1.2, logRatio);
                float nearStrength = mix(0.026, 0.108, coarseFactor);
                float horizonStrength = mix(nearStrength * 0.28, nearStrength, horizonBlend);
                return horizonStrength;
            }

            float GridTierWidth(float cameraHeight, float spacing, float widthScale) {
                float logRatio = GridHeightLogRatio(cameraHeight, spacing);
                float coarseFactor = 1.0 - smoothstep(-0.2, 1.2, logRatio);
                return mix(0.30, 0.96, coarseFactor) * widthScale;
            }

            float GridDistanceWeight(float hitDistance, float spacing) {
                float distanceRatio = hitDistance / max(spacing, 1e-4);
                return 1.0 - smoothstep(5.0, 30.0, distanceRatio);
            }
        ";

        /// <summary>
        /// Grid accumulation fragment shader for OIT with ray casting.
        /// </summary>
        public const string GridOitAccumFragment = @"#version 300 es
" + GridRaycastCommon + @"
            out vec4 FragColor;
            void main() {
                mat4 viewInv = inverse(uView);
                mat4 projInv = inverse(uProjection);

                vec4 nearViewH = projInv * vec4(v_uv.x, v_uv.y, 1.0, 1.0);
                vec4 farViewH = projInv * vec4(v_uv.x, v_uv.y, 0.0, 1.0);
                if (abs(nearViewH.w) < 0.000001 || abs(farViewH.w) < 0.000001) discard;
                vec3 nearView = nearViewH.xyz / nearViewH.w;
                vec3 farView = farViewH.xyz / farViewH.w;
                vec3 rayDirView = normalize(farView - nearView);
                vec3 rayDirWorld = normalize(mat3(viewInv) * rayDirView);

                float rayY = rayDirWorld.y;
                float safeRayY = abs(rayY) < 0.000001 ? (rayY < 0.0 ? -0.000001 : 0.000001) : rayY;
                float absRayY = abs(safeRayY);

                float t = (uGridPlaneY - uCameraPos.y) / safeRayY;
                if (t <= 0.0) discard;

                vec3 hitPosView = rayDirView * t;
                vec2 localXZ = rayDirWorld.xz * t;
                vec2 worldXZ = localXZ + uCameraPos.xz;

                vec4 clip_space_pos = uProjection * vec4(hitPosView, 1.0);
                if (clip_space_pos.w <= 0.0) discard;
                float ndc_z = clip_space_pos.z / clip_space_pos.w;
                gl_FragDepth = clamp((ndc_z + 1.0) * 0.5, 0.0, 1.0);

                float hitDistance = length(localXZ);
                float horizonBlend = smoothstep(0.028, 0.18, absRayY);
                float distanceFade = 1.0 - smoothstep(uGridFadeStart, uGridFadeEnd, hitDistance);
                float widthScale = mix(0.18, 1.0, horizonBlend);

                float gridAlpha = 0.0;
                for (int i = 0; i < GRID_TIER_COUNT; ++i) {
                    float spacing = GRID_MIN_SPACING * pow(10.0, float(i));
                    vec2 phase = vec2(0.0);
                    float aliasStart = 0.85 + (0.08 * float(i));
                    float aliasEnd = aliasStart + 1.0;
                    float aliasWeight = GridFadeOut(GridFootprint(worldXZ, spacing, phase), aliasStart, aliasEnd);
                    float distanceWeight = GridDistanceWeight(hitDistance, spacing);
                    float heightWeight = GridHeightWeight(uGridCameraHeight, spacing);
                    float line = GridLineAA(worldXZ, spacing, phase, GridTierWidth(uGridCameraHeight, spacing, widthScale));
                    float tierAlpha = line * aliasWeight * distanceWeight * heightWeight * GridTierStrength(uGridCameraHeight, spacing, horizonBlend);
                    gridAlpha += tierAlpha;
                }
                gridAlpha = clamp(gridAlpha * 1.12, 0.0, 0.27);

                vec3 gridColor = vec3(0.40, 0.40, 0.41);
                vec4 finalColor = vec4(gridColor, gridAlpha);

                float horizonFade = smoothstep(0.012, 0.16, absRayY);
                finalColor.a *= horizonFade * distanceFade;
                if (finalColor.a < 0.001) discard;

                float z = gl_FragDepth;
                float weight = clamp(finalColor.a * 1e4 * pow(z, 4.0), 1e-2, 3e3);
                FragColor = vec4(finalColor.rgb * finalColor.a * weight, finalColor.a * weight);
            }";

        /// <summary>
        /// Grid reveal fragment shader for OIT with ray casting.
        /// </summary>
        public const string GridOitRevealFragment = @"#version 300 es
" + GridRaycastCommon + @"
            out vec4 FragColor;
            void main() {
                mat4 viewInv = inverse(uView);
                mat4 projInv = inverse(uProjection);

                vec4 nearViewH = projInv * vec4(v_uv.x, v_uv.y, 1.0, 1.0);
                vec4 farViewH = projInv * vec4(v_uv.x, v_uv.y, 0.0, 1.0);
                if (abs(nearViewH.w) < 0.000001 || abs(farViewH.w) < 0.000001) discard;
                vec3 nearView = nearViewH.xyz / nearViewH.w;
                vec3 farView = farViewH.xyz / farViewH.w;
                vec3 rayDirView = normalize(farView - nearView);
                vec3 rayDirWorld = normalize(mat3(viewInv) * rayDirView);

                float rayY = rayDirWorld.y;
                float safeRayY = abs(rayY) < 0.000001 ? (rayY < 0.0 ? -0.000001 : 0.000001) : rayY;
                float absRayY = abs(safeRayY);

                float t = (uGridPlaneY - uCameraPos.y) / safeRayY;
                if (t <= 0.0) discard;

                vec3 hitPosView = rayDirView * t;
                vec2 localXZ = rayDirWorld.xz * t;
                vec2 worldXZ = localXZ + uCameraPos.xz;

                vec4 clip_space_pos = uProjection * vec4(hitPosView, 1.0);
                if (clip_space_pos.w <= 0.0) discard;
                float ndc_z = clip_space_pos.z / clip_space_pos.w;
                gl_FragDepth = clamp((ndc_z + 1.0) * 0.5, 0.0, 1.0);

                float hitDistance = length(localXZ);
                float horizonBlend = smoothstep(0.028, 0.18, absRayY);
                float distanceFade = 1.0 - smoothstep(uGridFadeStart, uGridFadeEnd, hitDistance);
                float widthScale = mix(0.18, 1.0, horizonBlend);

                float gridAlpha = 0.0;
                for (int i = 0; i < GRID_TIER_COUNT; ++i) {
                    float spacing = GRID_MIN_SPACING * pow(10.0, float(i));
                    vec2 phase = vec2(0.0);
                    float aliasStart = 0.85 + (0.08 * float(i));
                    float aliasEnd = aliasStart + 1.0;
                    float aliasWeight = GridFadeOut(GridFootprint(worldXZ, spacing, phase), aliasStart, aliasEnd);
                    float distanceWeight = GridDistanceWeight(hitDistance, spacing);
                    float heightWeight = GridHeightWeight(uGridCameraHeight, spacing);
                    float line = GridLineAA(worldXZ, spacing, phase, GridTierWidth(uGridCameraHeight, spacing, widthScale));
                    float tierAlpha = line * aliasWeight * distanceWeight * heightWeight * GridTierStrength(uGridCameraHeight, spacing, horizonBlend);
                    gridAlpha += tierAlpha;
                }
                gridAlpha = clamp(gridAlpha * 1.12, 0.0, 0.27);

                vec4 finalColor = vec4(1.0, 1.0, 1.0, gridAlpha);

                float horizonFade = smoothstep(0.012, 0.16, absRayY);
                finalColor.a *= horizonFade * distanceFade;
                if (finalColor.a < 0.001) discard;

                FragColor = vec4(finalColor.a, finalColor.a, finalColor.a, finalColor.a);
            }";

        /// <summary>
        /// Flat color fragment shader for OIT accumulation.
        /// </summary>
        public const string FlatOitAccumFragment = @"#version 300 es
            precision highp float;
            out vec4 FragColor;
            uniform vec4 uColor;
            void main() {
                float alpha = clamp(uColor.a, 0.0, 1.0);
                if (alpha < 0.001) discard;

                float z = gl_FragCoord.z;
                float weight = clamp(alpha * 1e4 * pow(z, 4.0), 1e-2, 3e3);
                FragColor = vec4(uColor.rgb * alpha * weight, alpha * weight);
            }";

        /// <summary>
        /// Flat color fragment shader for OIT reveal.
        /// </summary>
        public const string FlatOitRevealFragment = @"#version 300 es
            precision highp float;
            out vec4 FragColor;
            uniform vec4 uColor;
            void main() {
                float alpha = clamp(uColor.a, 0.0, 1.0);
                if (alpha < 0.001) discard;
                FragColor = vec4(alpha, alpha, alpha, alpha);
            }";

        /// <summary>
        /// Mesh vertex shader.
        /// </summary>
        public const string MeshVertexSimple = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            uniform mat4 uView;
            uniform mat4 uProjection;
            out vec3 Normal;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
                Normal = aNormal;
            }";

        /// <summary>
        /// Mesh fragment shader.
        /// </summary>
        public const string MeshFragmentSimple = @"#version 300 es
            precision highp float;
            in vec3 Normal;
            out vec4 FragColor;
            void main() {
                vec3 n = normalize(Normal);
                vec3 lightDir = normalize(vec3(0.5, 1.0, 0.5));
                float diff = max(dot(n, lightDir), 0.2);
                vec3 color = vec3(0.8, 0.8, 0.8) * diff;
                FragColor = vec4(color, 1.0);
            }";

        /// <summary>
        /// Gizmo solid vertex shader.
        /// </summary>
        public const string GizmoSolidVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in vec3 aColor;
            layout (location = 3) in float aAlpha;
            uniform mat4 uView;
            uniform mat4 uProjection;
            out vec3 vNormalWorld;
            out vec3 vColor;
            out float vAlpha;
            void main() {
                gl_Position = uProjection * uView * vec4(aPos, 1.0);
                vNormalWorld = normalize(aNormal);
                vColor = aColor;
                vAlpha = aAlpha;
            }";

        /// <summary>
        /// Composite fragment shader for OIT.
        /// </summary>
        public const string CompositeFragmentOit = @"#version 300 es
            precision highp float;
            in vec2 v_uv;
            uniform sampler2D uOpaqueColor;
            uniform sampler2D uAccumColor;
            uniform sampler2D uRevealColor;
            out vec4 FragColor;
            void main() {
                vec3 opaque = texture(uOpaqueColor, v_uv).rgb;
                vec4 accum = texture(uAccumColor, v_uv);
                float reveal = clamp(texture(uRevealColor, v_uv).r, 0.0, 1.0);
                vec3 trans = accum.rgb / max(accum.a, 1e-5);
                vec3 outColor = trans * (1.0 - reveal) + opaque * reveal;
                FragColor = vec4(outColor, 1.0);
            }";
    }
}
