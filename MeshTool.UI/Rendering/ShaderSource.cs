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
                    base = vec3(1.0, 0.3, 0.2);
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
        /// Vertex shader for surfel rendering.
        /// </summary>
        public const string SurfelVertex = @"#version 300 es
            precision highp float;
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in float aSpawnTime;
            layout (location = 3) in float aSelected;
            uniform mat4 uView;
            uniform mat4 uProjection;
            uniform float uAvgDistance;
            uniform float uScale;
            uniform float uLatestSpawnTime;
            uniform float uAnimationDuration;
            
            out vec3 WorldPos;
            out vec3 Normal;
            out float SpawnTime;
            out float Selected;
            out float Highlight;
            
            void main() {
                WorldPos = aPos;
                Normal = aNormal;
                SpawnTime = aSpawnTime;
                Selected = aSelected;
                
                // Highlight newly spawned points
                Highlight = 0.0;
                if (uAnimationDuration > 0.0 && uLatestSpawnTime > 0.0) {
                    float age = uLatestSpawnTime - SpawnTime;
                    if (age >= 0.0 && age < uAnimationDuration) {
                        Highlight = 1.0 - (age / uAnimationDuration);
                    }
                }
                
                // Billboard: quad facing camera
                vec3 camRight = vec3(uView[0][0], uView[1][0], uView[2][0]);
                vec3 camUp = vec3(uView[0][1], uView[1][1], uView[2][1]);
                
                float size = uAvgDistance * uScale * 0.5;
                vec3 pos = aPos;
                
                int vertId = gl_VertexID % 6;
                vec2 offset = vec2(0.0);
                if (vertId == 0 || vertId == 5) offset = vec2(-1.0, -1.0);
                else if (vertId == 1) offset = vec2(1.0, -1.0);
                else if (vertId == 2 || vertId == 3) offset = vec2(1.0, 1.0);
                else offset = vec2(-1.0, 1.0);
                
                pos += camRight * offset.x * size + camUp * offset.y * size;
                gl_Position = uProjection * uView * vec4(pos, 1.0);
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
    }
}