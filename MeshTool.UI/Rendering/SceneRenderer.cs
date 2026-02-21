using Avalonia.OpenGL;
using Silk.NET.OpenGL;
using System;
using System.Runtime.InteropServices;
using TerrainTool.Data;
using Silk.NET.Maths;
using MeshTool.UI.Controls;

namespace MeshTool.UI.Rendering
{
    public class SceneRenderer
    {
        private GL _gl;
        private OpenGlViewport _viewport;
        private uint _vaoPoints, _vboInstances;
        private uint _vaoSurfels, _vboSurfelVerts;
        private uint _shaderProgramPoints, _shaderProgramSurfels, _shaderProgramRayAccum, _shaderProgramRayReveal, _shaderProgramGridAccum, _shaderProgramGridReveal, _shaderProgramComposite, _shaderProgramMesh, _shaderProgramAxes;
        private uint _vaoRays, _vboRays;
        private uint _vaoMesh, _vboMesh;
        private uint _vaoGrid, _vboGrid;
        private uint _vaoAxes, _vboAxes;
        private int _meshVertexCount;

        private int _pointCapacity;
        private int _rayCapacity; // Number of lines capacity
        private float _avgDistance = 1.0f;

        private int _surfelVertexCount;

        private uint _msaaFbo;
        private uint _msaaAccumFbo;
        private uint _msaaRevealFbo;
        private uint _msaaColor;
        private uint _msaaAccum;
        private uint _msaaReveal;
        private uint _msaaDepth;
        private uint _resolveFbo;
        private uint _resolveColor;
        private uint _resolveDepth;
        private uint _oitAccumResolveFbo;
        private uint _oitRevealResolveFbo;
        private uint _oitAccumColor;
        private uint _oitRevealColor;
        private int _msaaWidth;
        private int _msaaHeight;
        private int _msaaSamples = 4;
        private float _latestSpawnTime = 0f;

        public Vector3D<float>? HoveredCoordinate { get; set; }
        public float GridPlaneY { get; set; } = 0.0f;

        public SceneRenderer(GlInterface glInterface, OpenGlViewport viewport)
        {
            _viewport = viewport;
            _gl = GL.GetApi(glInterface.GetProcAddress);
        }

        private delegate void glClearDepthfDelegate(float depth);
        private glClearDepthfDelegate? _glClearDepthf;

        public unsafe void Init()
        {
            _viewport.OnLog?.Invoke($"[GL] Version: {_gl.GetStringS(StringName.Version)}");
            _viewport.OnLog?.Invoke($"[GL] Renderer: {_gl.GetStringS(StringName.Renderer)}");
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Greater);

            try
            {
                _gl.ClearDepth(0.0);
            }
            catch (Exception)
            {
                _viewport.OnLog?.Invoke("[GL] ClearDepth failed, falling back to glClearDepthf via context.");
                if (_gl.Context.TryGetProcAddress("glClearDepthf", out var ptr))
                {
                    _viewport.OnLog?.Invoke("[GL] Successfully found glClearDepthf via context.");
                    _glClearDepthf = Marshal.GetDelegateForFunctionPointer<glClearDepthfDelegate>(ptr);
                    _glClearDepthf(0.0f);
                }
                else
                {
                    _viewport.OnLog?.Invoke("[GL] Failed to find glClearDepthf via context.");
                }
            }

            try
            {
                int maxSamples = _gl.GetInteger(GLEnum.MaxSamples);
                _msaaSamples = Math.Min(4, maxSamples);
                if (_msaaSamples > 1)
                {
                    _gl.Enable(EnableCap.Multisample);
                    _viewport.OnLog?.Invoke($"[GL] MSAA Enabled with {_msaaSamples} samples.");
                }
                else
                {
                    _viewport.OnLog?.Invoke($"[GL] MSAA not supported (MaxSamples = {maxSamples}).");
                }
            }
            catch (Exception ex)
            {
                _viewport.OnLog?.Invoke($"[GL] MSAA check failed: {ex.Message}");
            }

            InitShaders();
            InitBuffers();
        }

        private unsafe void InitShaders()
        {
            // --- POINT SHADER ---
            string vsPoint = @"#version 300 es
                precision highp float;
                layout (location = 0) in vec3 aPos;
                layout (location = 1) in vec3 aNormal;
                uniform mat4 uView;
                uniform mat4 uProjection;
                out vec3 Normal;
                void main() {
                    gl_Position = uProjection * uView * vec4(aPos, 1.0);
                    // Reverse Z: map z from [-1, 1] to [1, 0] for gl_FragDepth
                    // gl_Position.z is in clip space, after perspective divide it will be in NDC [-1, 1]
                    // But OpenGL expects depth in [0, 1]. We want near=1, far=0.
                    // The projection matrix already maps near to 1 and far to -1 in NDC.
                    // So we need to map NDC [-1, 1] to depth [0, 1].
                    // Actually, gl_Position.z / gl_Position.w is NDC.
                    // OpenGL maps NDC z to depth using: depth = (z_ndc + 1) / 2
                    // If z_ndc is 1 (near), depth = 1. If z_ndc is -1 (far), depth = 0.
                    // This is exactly what we want for reverse Z!
                    gl_PointSize = 4.0;
                    Normal = aNormal;
                }";
            string fsPoint = @"#version 300 es
                precision highp float;
                in vec3 Normal;
                out vec4 FragColor;
                void main() {
                    vec3 n = length(Normal) > 0.0001 ? normalize(Normal) : vec3(0.0, 1.0, 0.0);
                    vec3 lightDir = normalize(vec3(0.35, 1.0, 0.25));
                    float lambert = max(dot(n, lightDir), 0.2);
                    vec3 base = mix(vec3(0.7, 0.8, 1.0), abs(n), 0.65);
                    FragColor = vec4(base * lambert, 1.0);
                }";
            _shaderProgramPoints = CreateProgram(vsPoint, fsPoint);

            // --- SURFEL SHADER (Instanced) ---
            string vsSurfel = @"#version 300 es
                precision highp float;
                layout (location = 0) in vec3 aVertex; // Unit circle vertex
                layout (location = 1) in vec3 iPos;    // Instance Position
                layout (location = 2) in vec3 iNormal; // Instance Normal
                layout (location = 3) in float iSpawnTime; // Instance Spawn Time
                
                uniform mat4 uView;
                uniform mat4 uProjection;
                uniform float uScale;
                uniform float uCurrentTime;
                uniform vec3 uHoveredPos;
                uniform float uHasHovered;

                out vec3 Normal;
                out vec3 Color;

                void main() {
                    vec3 norm = normalize(iNormal);
                    vec3 helper = abs(norm.y) < 0.999 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
                    vec3 tangent = normalize(cross(helper, norm));
                    vec3 bitangent = normalize(cross(norm, tangent));
                    
                    // aVertex is unit disk in XZ plane, so X->tangent and Z->bitangent
                    vec3 worldPos = iPos + (tangent * aVertex.x + bitangent * aVertex.z) * uScale;
                    
                    gl_Position = uProjection * uView * vec4(worldPos, 1.0);
                    // Reverse Z: gl_Position.z is mapped to [1, 0] depth
                    Normal = norm;
                    
                    float age = uCurrentTime - iSpawnTime;
                    if (uHasHovered > 0.5 && length(iPos - uHoveredPos) < 0.001) {
                        Color = vec3(1.0, 0.0, 1.0); // Magenta
                    } else if (iSpawnTime <= 0.0 || age > 5.0 || age < 0.0) {
                        Color = vec3(0.0, 0.7, 1.0); // Cyan
                    } else {
                        float t = age / 5.0;
                        Color = mix(vec3(1.0, 0.0, 1.0), vec3(0.0, 0.7, 1.0), t); // Magenta to Cyan
                    }
                }";
            string fsSurfel = @"#version 300 es
                precision highp float;
                in vec3 Normal;
                in vec3 Color;
                out vec4 FragColor;
                void main() {
                    vec3 lightDir = normalize(vec3(1.0, 2.0, 1.0));
                    float diff = max(dot(Normal, lightDir), 0.0);
                    vec3 diffuse = diff * Color + vec3(0.1, 0.1, 0.3);
                    FragColor = vec4(diffuse, 1.0);
                }";
            _shaderProgramSurfels = CreateProgram(vsSurfel, fsSurfel);

            // --- RAY SHADERS (Weighted Blended OIT) ---
            string vsRay = @"#version 300 es
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
                    float distFade = 1.0 - smoothstep(500.0, 20000.0, dist);
                    Alpha = baseAlpha * distFade;
                }";
            string fsRayAccum = @"#version 300 es
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
            string fsRayReveal = @"#version 300 es
                precision highp float;
                in float Alpha;
                out vec4 FragColor;
                void main() {
                    FragColor = vec4(Alpha, Alpha, Alpha, Alpha);
                }";
            _shaderProgramRayAccum = CreateProgram(vsRay, fsRayAccum);
            _shaderProgramRayReveal = CreateProgram(vsRay, fsRayReveal);

            string vsComposite = @"#version 300 es
                precision highp float;
                layout (location = 0) in vec3 aPos;
                out vec2 v_uv;
                void main() {
                    gl_Position = vec4(aPos, 1.0);
                    v_uv = aPos.xy * 0.5 + 0.5;
                }";
            string fsComposite = @"#version 300 es
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
            _shaderProgramComposite = CreateProgram(vsComposite, fsComposite);

            // --- AXES SHADER ---
            string vsAxes = @"#version 300 es
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
            string fsAxes = @"#version 300 es
                precision highp float;
                in vec3 Color;
                out vec4 FragColor;
                void main() {
                    FragColor = vec4(Color, 1.0);
                }";
            _shaderProgramAxes = CreateProgram(vsAxes, fsAxes);

            // --- MESH SHADER ---
            string vsMesh = @"#version 300 es
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
            string fsMesh = @"#version 300 es
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
            _shaderProgramMesh = CreateProgram(vsMesh, fsMesh);

            // --- GRID SHADER ---
            string vsGrid = @"#version 300 es
                precision highp float;
                layout (location = 0) in vec3 aPos;
                out vec2 v_uv;
                void main() {
                    gl_Position = vec4(aPos, 1.0);
                    v_uv = aPos.xy;
                }";
            string fsGridCommon = @"
                precision highp float;
                in vec2 v_uv;
                uniform mat4 uView;
                uniform mat4 uProjection;
                uniform vec3 uCameraPos;
                uniform float uGridPlaneY;
                uniform float uGridSpacingMinor;
                uniform float uGridSpacingMajor0;
                uniform float uGridSpacingMajor1;
                uniform vec2 uGridPhaseMinor;
                uniform vec2 uGridPhaseMajor0;
                uniform vec2 uGridPhaseMajor1;
                uniform float uGridFade;
                uniform float uGridFadeStart;
                uniform float uGridFadeEnd;
                float GridLineAA(vec2 localXZ, float spacing, vec2 phase, float lineWidthPx) {
                    vec2 uv = (localXZ + phase) / spacing;
                    vec2 deriv = max(fwidth(uv), vec2(1e-6));
                    vec2 distToLine = abs(fract(uv - 0.5) - 0.5) / deriv;
                    float lineDist = min(distToLine.x, distToLine.y);
                    return 1.0 - smoothstep(lineWidthPx, lineWidthPx + 1.0, lineDist);
                }
";

            string fsGridAccum = @"#version 300 es
" + fsGridCommon + @"
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

                    vec4 clip_space_pos = uProjection * vec4(hitPosView, 1.0);
                    if (clip_space_pos.w <= 0.0) discard;
                    float ndc_z = clip_space_pos.z / clip_space_pos.w;
                    gl_FragDepth = clamp((ndc_z + 1.0) * 0.5, 0.0, 1.0);

                    float fade = clamp(uGridFade, 0.0, 1.0);
                    float widthPx = 1.0;
                    float minor = GridLineAA(localXZ, uGridSpacingMinor, uGridPhaseMinor, widthPx);
                    float major0 = GridLineAA(localXZ, uGridSpacingMajor0, uGridPhaseMajor0, widthPx);
                    float major1 = GridLineAA(localXZ, uGridSpacingMajor1, uGridPhaseMajor1, widthPx);

                    float wMinor = 0.08;
                    float wMajor = 0.30;
                    float aMinor = minor * ((1.0 - fade) * wMinor);
                    float aMajor0 = major0 * (((1.0 - fade) * wMajor) + (fade * wMinor));
                    float aMajor1 = major1 * (fade * wMajor);
                    float gridAlpha = clamp(aMinor + aMajor0 + aMajor1, 0.0, 0.55);

                    vec3 gridColor = vec3(0.4, 0.4, 0.4);
                    vec4 finalColor = vec4(gridColor, gridAlpha);

                    float horizonFade = smoothstep(0.01, 0.03, absRayY);
                    finalColor.a *= horizonFade;
                    if (finalColor.a < 0.001) discard;

                    float z = gl_FragDepth;
                    float weight = clamp(finalColor.a * 1e4 * pow(z, 4.0), 1e-2, 3e3);
                    FragColor = vec4(finalColor.rgb * finalColor.a * weight, finalColor.a * weight);
                }";

            string fsGridReveal = @"#version 300 es
" + fsGridCommon + @"
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

                    vec4 clip_space_pos = uProjection * vec4(hitPosView, 1.0);
                    if (clip_space_pos.w <= 0.0) discard;
                    float ndc_z = clip_space_pos.z / clip_space_pos.w;
                    gl_FragDepth = clamp((ndc_z + 1.0) * 0.5, 0.0, 1.0);

                    float fade = clamp(uGridFade, 0.0, 1.0);
                    float widthPx = 1.0;
                    float minor = GridLineAA(localXZ, uGridSpacingMinor, uGridPhaseMinor, widthPx);
                    float major0 = GridLineAA(localXZ, uGridSpacingMajor0, uGridPhaseMajor0, widthPx);
                    float major1 = GridLineAA(localXZ, uGridSpacingMajor1, uGridPhaseMajor1, widthPx);

                    float wMinor = 0.08;
                    float wMajor = 0.30;
                    float aMinor = minor * ((1.0 - fade) * wMinor);
                    float aMajor0 = major0 * (((1.0 - fade) * wMajor) + (fade * wMinor));
                    float aMajor1 = major1 * (fade * wMajor);
                    float gridAlpha = clamp(aMinor + aMajor0 + aMajor1, 0.0, 0.55);

                    vec4 finalColor = vec4(1.0, 1.0, 1.0, gridAlpha);

                    float horizonFade = smoothstep(0.01, 0.03, absRayY);
                    finalColor.a *= horizonFade;
                    if (finalColor.a < 0.001) discard;

                    FragColor = vec4(finalColor.a, finalColor.a, finalColor.a, finalColor.a);
                }";

            _shaderProgramGridAccum = CreateProgram(vsGrid, fsGridAccum);
            _shaderProgramGridReveal = CreateProgram(vsGrid, fsGridReveal);
        }

        private unsafe uint CreateProgram(string vsSource, string fsSource)
        {
            uint vs = _gl.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(vs, vsSource);
            _gl.CompileShader(vs);
            _gl.GetShader(vs, ShaderParameterName.CompileStatus, out int vStatus);
            if (vStatus == 0)
            {
                string log = _gl.GetShaderInfoLog(vs);
                _viewport.OnLog?.Invoke($"[VS ERROR] {log}");
            }

            uint fs = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(fs, fsSource);
            _gl.CompileShader(fs);
            _gl.GetShader(fs, ShaderParameterName.CompileStatus, out int fStatus);
            if (fStatus == 0)
            {
                string log = _gl.GetShaderInfoLog(fs);
                _viewport.OnLog?.Invoke($"[FS ERROR] {log}");
            }

            uint program = _gl.CreateProgram();
            _gl.AttachShader(program, vs);
            _gl.AttachShader(program, fs);
            _gl.LinkProgram(program);
            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int lStatus);
            if (lStatus == 0)
            {
                string log = _gl.GetProgramInfoLog(program);
                _viewport.OnLog?.Invoke($"[LINK ERROR] {log}");
            }

            _gl.DeleteShader(vs);
            _gl.DeleteShader(fs);

            return program;
        }

        private unsafe void InitBuffers()
        {
            _vboInstances = _gl.GenBuffer();

            // 1. Points VAO
            _vaoPoints = _gl.GenVertexArray();
            _gl.BindVertexArray(_vaoPoints);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            // 2. Surfels VAO
            _vaoSurfels = _gl.GenVertexArray();
            _vboSurfelVerts = _gl.GenBuffer();

            // Create unit circle in XZ plane (Y up)
            int segments = 16;
            _surfelVertexCount = segments * 3; // Triangles from center
            float[] surfelVerts = new float[_surfelVertexCount * 3];
            for (int i = 0; i < segments; i++)
            {
                float a1 = (float)i / segments * 2.0f * MathF.PI;
                float a2 = (float)(i + 1) / segments * 2.0f * MathF.PI;

                int idx = i * 9;
                // Center
                surfelVerts[idx + 0] = 0f; surfelVerts[idx + 1] = 0f; surfelVerts[idx + 2] = 0f;
                // V1
                surfelVerts[idx + 3] = MathF.Cos(a1); surfelVerts[idx + 4] = 0f; surfelVerts[idx + 5] = MathF.Sin(a1);
                // V2
                surfelVerts[idx + 6] = MathF.Cos(a2); surfelVerts[idx + 7] = 0f; surfelVerts[idx + 8] = MathF.Sin(a2);
            }

            _gl.BindVertexArray(_vaoSurfels);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboSurfelVerts);
            fixed (float* v = surfelVerts)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(surfelVerts.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribDivisor(1, 1); // Instanced

            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribDivisor(2, 1); // Instanced

            _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribDivisor(3, 1); // Instanced

            // 3. Rays VAO
            _vaoRays = _gl.GenVertexArray();
            _vboRays = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoRays);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);

            // 4. Mesh VAO
            _vaoMesh = _gl.GenVertexArray();
            _vboMesh = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoMesh);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboMesh);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            // 5. Grid VAO (Full screen quad)
            _vaoGrid = _gl.GenVertexArray();
            _vboGrid = _gl.GenBuffer();
            float[] gridVerts = {
                -1f,  1f, 0f,
                -1f, -1f, 0f,
                 1f, -1f, 0f,
                -1f,  1f, 0f,
                 1f, -1f, 0f,
                 1f,  1f, 0f
            };
            _gl.BindVertexArray(_vaoGrid);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboGrid);
            fixed (float* v = gridVerts)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(gridVerts.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            // 6. Axes VAO
            _vaoAxes = _gl.GenVertexArray();
            _vboAxes = _gl.GenBuffer();
            float[] axesVerts = {
                // X axis (Red)
                0f, 0f, 0f,  1f, 0f, 0f,
                10000f, 0f, 0f,  1f, 0f, 0f,
                // Y axis (Green)
                0f, 0f, 0f,  0f, 1f, 0f,
                0f, 10000f, 0f,  0f, 1f, 0f,
                // Z axis (Blue)
                0f, 0f, 0f,  0f, 0f, 1f,
                0f, 0f, 10000f,  0f, 0f, 1f
            };
            _gl.BindVertexArray(_vaoAxes);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboAxes);
            fixed (float* v = axesVerts)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(axesVerts.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
        }

        public void Deinit()
        {
            if (_msaaFbo != 0) _gl.DeleteFramebuffer(_msaaFbo);
            if (_msaaAccumFbo != 0) _gl.DeleteFramebuffer(_msaaAccumFbo);
            if (_msaaRevealFbo != 0) _gl.DeleteFramebuffer(_msaaRevealFbo);
            if (_msaaColor != 0) _gl.DeleteRenderbuffer(_msaaColor);
            if (_msaaAccum != 0) _gl.DeleteRenderbuffer(_msaaAccum);
            if (_msaaReveal != 0) _gl.DeleteRenderbuffer(_msaaReveal);
            if (_msaaDepth != 0) _gl.DeleteRenderbuffer(_msaaDepth);

            if (_resolveFbo != 0) _gl.DeleteFramebuffer(_resolveFbo);
            if (_resolveColor != 0) _gl.DeleteTexture(_resolveColor);
            if (_resolveDepth != 0) _gl.DeleteTexture(_resolveDepth);
            if (_oitAccumResolveFbo != 0) _gl.DeleteFramebuffer(_oitAccumResolveFbo);
            if (_oitRevealResolveFbo != 0) _gl.DeleteFramebuffer(_oitRevealResolveFbo);
            if (_oitAccumColor != 0) _gl.DeleteTexture(_oitAccumColor);
            if (_oitRevealColor != 0) _gl.DeleteTexture(_oitRevealColor);

            _gl.DeleteVertexArray(_vaoPoints);
            _gl.DeleteVertexArray(_vaoSurfels);
            _gl.DeleteVertexArray(_vaoRays);
            _gl.DeleteVertexArray(_vaoMesh);
            _gl.DeleteVertexArray(_vaoGrid);
            _gl.DeleteVertexArray(_vaoAxes);
            _gl.DeleteBuffer(_vboInstances);
            _gl.DeleteBuffer(_vboSurfelVerts);
            _gl.DeleteBuffer(_vboRays);
            _gl.DeleteBuffer(_vboMesh);
            _gl.DeleteBuffer(_vboGrid);
            _gl.DeleteBuffer(_vboAxes);
            _gl.DeleteProgram(_shaderProgramPoints);
            _gl.DeleteProgram(_shaderProgramSurfels);
            _gl.DeleteProgram(_shaderProgramRayAccum);
            _gl.DeleteProgram(_shaderProgramRayReveal);
            _gl.DeleteProgram(_shaderProgramGridAccum);
            _gl.DeleteProgram(_shaderProgramGridReveal);
            _gl.DeleteProgram(_shaderProgramComposite);
            _gl.DeleteProgram(_shaderProgramMesh);
            _gl.DeleteProgram(_shaderProgramAxes);
            _gl.Dispose();
        }

        private Vertex[]? _pendingPoints;
        private TerrainTool.Data.Ray[]? _pendingRays;
        private System.Collections.Generic.List<TerrainTool.Data.Triangle>? _pendingMesh;
        private System.Collections.Generic.List<Vertex> _pendingAppendPointsList = new System.Collections.Generic.List<Vertex>();
        private System.Collections.Generic.List<TerrainTool.Data.Ray> _pendingAppendRaysList = new System.Collections.Generic.List<TerrainTool.Data.Ray>();
        private float _pendingAvgDistance;
        private bool _dataDirty = false;
        private bool _appendDirty = false;
        private bool _meshDirty = false;

        private int _pointCount;
        private int _rayCount;
        private int _missRayCount;

        public unsafe void UpdateMesh(System.Collections.Generic.List<TerrainTool.Data.Triangle>? triangles)
        {
            _pendingMesh = triangles;
            _meshDirty = true;
        }

        public unsafe void UpdateData(Vertex[] points, TerrainTool.Data.Ray[] rays, float avgDistance)
        {
            _pendingPoints = points;
            _pendingRays = rays;
            _pendingAvgDistance = avgDistance;
            _dataDirty = true;
            _appendDirty = false; // Override any pending appends
            _pendingAppendPointsList.Clear();
            _pendingAppendRaysList.Clear();
            UpdateLatestSpawnTime(points, rays);
        }

        public unsafe void AppendData(Vertex[]? newPoints, TerrainTool.Data.Ray[]? newMisses, float avgDistance)
        {
            if (_dataDirty) return; // If a full update is pending, ignore appends

            if (newPoints != null)
            {
                _pendingAppendPointsList.AddRange(newPoints);
                UpdateLatestSpawnTime(newPoints, null);
            }
            if (newMisses != null)
            {
                _pendingAppendRaysList.AddRange(newMisses);
                UpdateLatestSpawnTime(null, newMisses);
            }
            _pendingAvgDistance = avgDistance;
            _appendDirty = true;
        }

        private void UpdateLatestSpawnTime(Vertex[]? points, TerrainTool.Data.Ray[]? rays)
        {
            if (points != null)
            {
                foreach (var p in points)
                {
                    if (p.SpawnTime > _latestSpawnTime) _latestSpawnTime = p.SpawnTime;
                }
            }
            if (rays != null)
            {
                foreach (var r in rays)
                {
                    if (r.SpawnTime > _latestSpawnTime) _latestSpawnTime = r.SpawnTime;
                }
            }
        }

        public bool HasActiveAnimations()
        {
            float currentTime = (float)(Environment.TickCount64 - TerrainTool.IO.LogParser.AppStartTime) / 1000.0f;
            return (currentTime - _latestSpawnTime) < 5.0f;
        }

        public struct Frustum
        {
            public Vector4D<float>[] Planes;

            public Frustum(Matrix4X4<float> vp)
            {
                Planes = new Vector4D<float>[6];
                Planes[0] = new Vector4D<float>(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
                Planes[1] = new Vector4D<float>(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
                Planes[2] = new Vector4D<float>(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
                Planes[3] = new Vector4D<float>(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
                Planes[4] = new Vector4D<float>(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43);
                Planes[5] = new Vector4D<float>(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);

                for (int i = 0; i < 6; i++)
                {
                    float length = MathF.Sqrt(Planes[i].X * Planes[i].X + Planes[i].Y * Planes[i].Y + Planes[i].Z * Planes[i].Z);
                    Planes[i].X /= length;
                    Planes[i].Y /= length;
                    Planes[i].Z /= length;
                    Planes[i].W /= length;
                }
            }

            public bool Contains(Vector3D<float> point, float radius)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (Planes[i].X * point.X + Planes[i].Y * point.Y + Planes[i].Z * point.Z + Planes[i].W <= -radius)
                        return false;
                }
                return true;
            }
        }

        private unsafe void ApplyPendingData()
        {
            if (_dataDirty)
            {
                _dataDirty = false;
                if (_pendingPoints == null || _pendingRays == null) return;

                Vertex[] points = _pendingPoints;
                TerrainTool.Data.Ray[] rays = _pendingRays;
                _avgDistance = _pendingAvgDistance;

                _pointCount = points.Length;
                _missRayCount = rays.Length;
                _rayCount = _missRayCount + _pointCount;

                if (_pointCount > _pointCapacity)
                {
                    _pointCapacity = Math.Max(_pointCapacity * 2, _pointCount + 10000);
                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_pointCapacity * 7 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
                }

                if (_pointCount > 0)
                {
                    Console.WriteLine($"[GL] Uploading {_pointCount} points to GPU...");
                    float[] vertices = new float[_pointCount * 7];
                    for (int i = 0; i < _pointCount; i++)
                    {
                        float nx = (float)points[i].Normal.X;
                        float ny = (float)points[i].Normal.Y;
                        float nz = (float)points[i].Normal.Z;
                        float nLen = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (nLen > 0.00001f)
                        {
                            nx /= nLen;
                            ny /= nLen;
                            nz /= nLen;
                        }
                        else
                        {
                            nx = 0f;
                            ny = 1f;
                            nz = 0f;
                        }

                        vertices[i * 7 + 0] = (float)points[i].Position.X;
                        vertices[i * 7 + 1] = (float)points[i].Position.Y;
                        vertices[i * 7 + 2] = (float)points[i].Position.Z;
                        vertices[i * 7 + 3] = nx;
                        vertices[i * 7 + 4] = ny;
                        vertices[i * 7 + 5] = nz;
                        vertices[i * 7 + 6] = points[i].SpawnTime;
                    }

                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                    fixed (float* v = vertices)
                    {
                        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertices.Length * sizeof(float)), v);
                    }
                }

                if (_rayCount > _rayCapacity)
                {
                    _rayCapacity = Math.Max(_rayCapacity * 2, _rayCount + 10000);
                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_rayCapacity * 14 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
                }

                if (_rayCount > 0)
                {
                    float[] rayData = new float[_rayCount * 14]; // 2 verts * 7 floats

                    // Miss rays (Red)
                    for (int i = 0; i < rays.Length; i++)
                    {
                        int idx = i * 14;
                        rayData[idx + 0] = (float)rays[i].Start.X; rayData[idx + 1] = (float)rays[i].Start.Y; rayData[idx + 2] = (float)rays[i].Start.Z;
                        rayData[idx + 3] = 1f; rayData[idx + 4] = 0f; rayData[idx + 5] = 0f;
                        rayData[idx + 6] = rays[i].SpawnTime;

                        rayData[idx + 7] = (float)rays[i].End.X; rayData[idx + 8] = (float)rays[i].End.Y; rayData[idx + 9] = (float)rays[i].End.Z;
                        rayData[idx + 10] = 1f; rayData[idx + 11] = 0f; rayData[idx + 12] = 0f;
                        rayData[idx + 13] = rays[i].SpawnTime;
                    }

                    // Point normals (Yellow)
                    int offset = rays.Length * 14;
                    float normalLen = _avgDistance * 1.5f;
                    for (int i = 0; i < _pointCount; i++)
                    {
                        int idx = offset + i * 14;
                        float px = (float)points[i].Position.X;
                        float py = (float)points[i].Position.Y;
                        float pz = (float)points[i].Position.Z;

                        float nx = (float)points[i].Normal.X;
                        float ny = (float)points[i].Normal.Y;
                        float nz = (float)points[i].Normal.Z;
                        float nLen = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (nLen > 0.00001f)
                        {
                            nx /= nLen;
                            ny /= nLen;
                            nz /= nLen;
                        }
                        else
                        {
                            nx = 0f;
                            ny = 1f;
                            nz = 0f;
                        }

                        rayData[idx + 0] = px; rayData[idx + 1] = py; rayData[idx + 2] = pz;
                        rayData[idx + 3] = 1f; rayData[idx + 4] = 1f; rayData[idx + 5] = 0f; // Yellow
                        rayData[idx + 6] = 0f; // No animation for normals

                        rayData[idx + 7] = px + nx * normalLen; rayData[idx + 8] = py + ny * normalLen; rayData[idx + 9] = pz + nz * normalLen;
                        rayData[idx + 10] = 1f; rayData[idx + 11] = 1f; rayData[idx + 12] = 0f; // Yellow
                        rayData[idx + 13] = 0f; // No animation for normals
                    }

                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                    fixed (float* v = rayData)
                    {
                        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(rayData.Length * sizeof(float)), v);
                    }
                }
            }
            else if (_appendDirty)
            {
                _appendDirty = false;

                Vertex[] newPoints = _pendingAppendPointsList.ToArray();
                TerrainTool.Data.Ray[] newMisses = _pendingAppendRaysList.ToArray();
                _pendingAppendPointsList.Clear();
                _pendingAppendRaysList.Clear();

                _avgDistance = _pendingAvgDistance;

                int addedPoints = newPoints.Length;
                int addedMisses = newMisses.Length;
                int addedRays = addedMisses + addedPoints; // Each point adds a normal ray

                if (addedPoints > 0)
                {
                    int newPointCount = _pointCount + addedPoints;
                    if (newPointCount > _pointCapacity)
                    {
                        // Reallocate and copy old data
                        int newCapacity = Math.Max(_pointCapacity * 2, newPointCount + 10000);
                        uint newVbo = _gl.GenBuffer();
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, newVbo);
                        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(newCapacity * 7 * sizeof(float)), null, BufferUsageARB.DynamicDraw);

                        _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _vboInstances);
                        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, newVbo);
                        _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, 0, (nuint)(_pointCount * 7 * sizeof(float)));

                        _gl.DeleteBuffer(_vboInstances);
                        _vboInstances = newVbo;
                        _pointCapacity = newCapacity;

                        // Re-bind VAOs to new VBO
                        _gl.BindVertexArray(_vaoPoints);
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
                        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(3 * sizeof(float)));

                        _gl.BindVertexArray(_vaoSurfels);
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
                        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(3 * sizeof(float)));
                        _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(6 * sizeof(float)));
                    }

                    float[] vertices = new float[addedPoints * 7];
                    for (int i = 0; i < addedPoints; i++)
                    {
                        float nx = (float)newPoints![i].Normal.X;
                        float ny = (float)newPoints[i].Normal.Y;
                        float nz = (float)newPoints[i].Normal.Z;
                        float nLen = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (nLen > 0.00001f)
                        {
                            nx /= nLen;
                            ny /= nLen;
                            nz /= nLen;
                        }
                        else
                        {
                            nx = 0f;
                            ny = 1f;
                            nz = 0f;
                        }

                        vertices[i * 7 + 0] = (float)newPoints[i].Position.X;
                        vertices[i * 7 + 1] = (float)newPoints[i].Position.Y;
                        vertices[i * 7 + 2] = (float)newPoints[i].Position.Z;
                        vertices[i * 7 + 3] = nx;
                        vertices[i * 7 + 4] = ny;
                        vertices[i * 7 + 5] = nz;
                        vertices[i * 7 + 6] = newPoints[i].SpawnTime;
                    }

                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                    fixed (float* v = vertices)
                    {
                        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(_pointCount * 7 * sizeof(float)), (nuint)(vertices.Length * sizeof(float)), v);
                    }
                    _pointCount = newPointCount;
                }

                if (addedRays > 0)
                {
                    int newRayCount = _rayCount + addedRays;
                    if (newRayCount > _rayCapacity)
                    {
                        int newCapacity = Math.Max(_rayCapacity * 2, newRayCount + 10000);
                        uint newVbo = _gl.GenBuffer();
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, newVbo);
                        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(newCapacity * 14 * sizeof(float)), null, BufferUsageARB.DynamicDraw);

                        _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _vboRays);
                        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, newVbo);
                        _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, 0, (nuint)(_rayCount * 14 * sizeof(float)));

                        _gl.DeleteBuffer(_vboRays);
                        _vboRays = newVbo;
                        _rayCapacity = newCapacity;

                        _gl.BindVertexArray(_vaoRays);
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
                        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(3 * sizeof(float)));
                        _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(6 * sizeof(float)));
                    }

                    float[] rayData = new float[addedRays * 14];
                    int offset = 0;

                    if (addedMisses > 0)
                    {
                        for (int i = 0; i < addedMisses; i++)
                        {
                            int idx = offset + i * 14;
                            rayData[idx + 0] = (float)newMisses![i].Start.X; rayData[idx + 1] = (float)newMisses[i].Start.Y; rayData[idx + 2] = (float)newMisses[i].Start.Z;
                            rayData[idx + 3] = 1f; rayData[idx + 4] = 0f; rayData[idx + 5] = 0f;
                            rayData[idx + 6] = newMisses[i].SpawnTime;

                            rayData[idx + 7] = (float)newMisses[i].End.X; rayData[idx + 8] = (float)newMisses[i].End.Y; rayData[idx + 9] = (float)newMisses[i].End.Z;
                            rayData[idx + 10] = 1f; rayData[idx + 11] = 0f; rayData[idx + 12] = 0f;
                            rayData[idx + 13] = newMisses[i].SpawnTime;
                        }
                        offset += addedMisses * 14;
                    }

                    if (addedPoints > 0)
                    {
                        float normalLen = _avgDistance * 1.5f;
                        for (int i = 0; i < addedPoints; i++)
                        {
                            int idx = offset + i * 14;
                            float px = (float)newPoints![i].Position.X;
                            float py = (float)newPoints[i].Position.Y;
                            float pz = (float)newPoints[i].Position.Z;

                            float nx = (float)newPoints[i].Normal.X;
                            float ny = (float)newPoints[i].Normal.Y;
                            float nz = (float)newPoints[i].Normal.Z;
                            float nLen = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                            if (nLen > 0.00001f)
                            {
                                nx /= nLen;
                                ny /= nLen;
                                nz /= nLen;
                            }
                            else
                            {
                                nx = 0f;
                                ny = 1f;
                                nz = 0f;
                            }

                            rayData[idx + 0] = px; rayData[idx + 1] = py; rayData[idx + 2] = pz;
                            rayData[idx + 3] = 1f; rayData[idx + 4] = 1f; rayData[idx + 5] = 0f;
                            rayData[idx + 6] = 0f;

                            rayData[idx + 7] = px + nx * normalLen; rayData[idx + 8] = py + ny * normalLen; rayData[idx + 9] = pz + nz * normalLen;
                            rayData[idx + 10] = 1f; rayData[idx + 11] = 1f; rayData[idx + 12] = 0f;
                            rayData[idx + 13] = 0f;
                        }
                    }

                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                    fixed (float* v = rayData)
                    {
                        _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(_rayCount * 14 * sizeof(float)), (nuint)(rayData.Length * sizeof(float)), v);
                    }
                    _rayCount = newRayCount;
                    _missRayCount += addedMisses;
                }
            }

            if (_meshDirty)
            {
                _meshDirty = false;
                if (_pendingMesh != null)
                {
                    _meshVertexCount = _pendingMesh.Count * 3;
                    if (_meshVertexCount > 0)
                    {
                        float[] meshData = new float[_meshVertexCount * 6];
                        for (int i = 0; i < _pendingMesh.Count; i++)
                        {
                            var t = _pendingMesh[i];
                            var edge1 = t.B.Position - t.A.Position;
                            var edge2 = t.C.Position - t.A.Position;
                            var n = edge1.Cross(edge2);
                            double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                            if (len > 1e-7)
                            {
                                n.X /= len;
                                n.Y /= len;
                                n.Z /= len;
                            }
                            else
                            {
                                n = new TerrainTool.Data.Vector3(0, 1, 0);
                            }

                            meshData[i * 18 + 0] = (float)t.A.Position.X;
                            meshData[i * 18 + 1] = (float)t.A.Position.Y;
                            meshData[i * 18 + 2] = (float)t.A.Position.Z;
                            meshData[i * 18 + 3] = (float)n.X;
                            meshData[i * 18 + 4] = (float)n.Y;
                            meshData[i * 18 + 5] = (float)n.Z;

                            meshData[i * 18 + 6] = (float)t.B.Position.X;
                            meshData[i * 18 + 7] = (float)t.B.Position.Y;
                            meshData[i * 18 + 8] = (float)t.B.Position.Z;
                            meshData[i * 18 + 9] = (float)n.X;
                            meshData[i * 18 + 10] = (float)n.Y;
                            meshData[i * 18 + 11] = (float)n.Z;

                            meshData[i * 18 + 12] = (float)t.C.Position.X;
                            meshData[i * 18 + 13] = (float)t.C.Position.Y;
                            meshData[i * 18 + 14] = (float)t.C.Position.Z;
                            meshData[i * 18 + 15] = (float)n.X;
                            meshData[i * 18 + 16] = (float)n.Y;
                            meshData[i * 18 + 17] = (float)n.Z;
                        }

                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboMesh);
                        fixed (float* v = meshData)
                        {
                            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(meshData.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
                        }
                    }
                }
                else
                {
                    _meshVertexCount = 0;
                }
            }
        }

        public unsafe void Render(int fb, Avalonia.Size bounds)
        {
            ApplyPendingData();

            int width = (int)bounds.Width;
            int height = (int)bounds.Height;

            if (width != _msaaWidth || height != _msaaHeight)
            {
                if (_msaaFbo != 0) _gl.DeleteFramebuffer(_msaaFbo);
                if (_msaaAccumFbo != 0) _gl.DeleteFramebuffer(_msaaAccumFbo);
                if (_msaaRevealFbo != 0) _gl.DeleteFramebuffer(_msaaRevealFbo);
                if (_msaaColor != 0) _gl.DeleteRenderbuffer(_msaaColor);
                if (_msaaAccum != 0) _gl.DeleteRenderbuffer(_msaaAccum);
                if (_msaaReveal != 0) _gl.DeleteRenderbuffer(_msaaReveal);
                if (_msaaDepth != 0) _gl.DeleteRenderbuffer(_msaaDepth);

                if (_resolveFbo != 0) _gl.DeleteFramebuffer(_resolveFbo);
                if (_resolveColor != 0) _gl.DeleteTexture(_resolveColor);
                if (_resolveDepth != 0) _gl.DeleteTexture(_resolveDepth);
                if (_oitAccumResolveFbo != 0) _gl.DeleteFramebuffer(_oitAccumResolveFbo);
                if (_oitRevealResolveFbo != 0) _gl.DeleteFramebuffer(_oitRevealResolveFbo);
                if (_oitAccumColor != 0) _gl.DeleteTexture(_oitAccumColor);
                if (_oitRevealColor != 0) _gl.DeleteTexture(_oitRevealColor);

                _msaaWidth = width;
                _msaaHeight = height;

                // 1. MSAA FBO (Renderbuffers)
                _msaaFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

                _msaaColor = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColor);
                _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.Rgba8, (uint)width, (uint)height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaColor);

                _msaaAccum = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaAccum);
                _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.Rgba16f, (uint)width, (uint)height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, RenderbufferTarget.Renderbuffer, _msaaAccum);

                _msaaReveal = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaReveal);
                _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.Rgba8, (uint)width, (uint)height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2, RenderbufferTarget.Renderbuffer, _msaaReveal);

                _msaaDepth = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepth);
                _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.DepthComponent32f, (uint)width, (uint)height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);

                var status1 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status1 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] MSAA FBO incomplete: {status1}");

                // 2. Resolve FBO (Textures)
                _resolveFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFbo);

                _resolveColor = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _resolveColor);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _resolveColor, 0);

                _resolveDepth = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _resolveDepth);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32f, (uint)width, (uint)height, 0, PixelFormat.DepthComponent, PixelType.Float, null);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _resolveDepth, 0);

                var status2 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status2 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] Resolve FBO incomplete: {status2}");

                // 3. OIT resolve textures/FBOs (single sample)
                _oitAccumResolveFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _oitAccumResolveFbo);

                _oitAccumColor = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _oitAccumColor);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.HalfFloat, null);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _oitAccumColor, 0);

                var status3 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status3 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] OIT Accum Resolve FBO incomplete: {status3}");

                _oitRevealResolveFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _oitRevealResolveFbo);

                _oitRevealColor = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _oitRevealColor);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _oitRevealColor, 0);

                var status4 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status4 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] OIT Reveal Resolve FBO incomplete: {status4}");

                // 4. MSAA OIT FBOs (single color attachment each, shared depth)
                _msaaAccumFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaAccumFbo);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaAccum);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);
                var status5 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status5 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] MSAA Accum FBO incomplete: {status5}");

                _msaaRevealFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaRevealFbo);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaReveal);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);
                var status6 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status6 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] MSAA Reveal FBO incomplete: {status6}");
            }

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
            _gl.Viewport(0, 0, (uint)width, (uint)height);
            _gl.ClearColor(0.15f, 0.15f, 0.15f, 1.0f);
            if (_glClearDepthf != null) _glClearDepthf(0.0f);
            else _gl.ClearDepth(0.0);
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Greater);

            bool hasAnyGeometry = _pointCount > 0 || _rayCount > 0 || _meshVertexCount > 0;
            if (!hasAnyGeometry && !_viewport.ShowGrid)
            {
                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
                _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                return;
            }

            var view = _viewport.Camera.GetViewMatrix();
            var proj = _viewport.Camera.GetProjectionMatrix((float)width, (float)height);
            var vp = view * proj;

            float currentTime = (float)(Environment.TickCount64 - TerrainTool.IO.LogParser.AppStartTime) / 1000.0f;

            // 1. Draw Points
            if (_viewport.ShowPoints && _pointCount > 0)
            {
                _gl.UseProgram(_shaderProgramPoints);
                SetUniforms(_shaderProgramPoints, view, proj);

                _gl.BindVertexArray(_vaoPoints);
                _gl.PointSize(4.0f);

                // Force unbind element array buffer in case it was bound elsewhere
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);

                _gl.DrawArrays(PrimitiveType.Points, 0, (uint)_pointCount);
            }

            // 2. Draw Surfels
            if (_viewport.ShowSurfels && _pointCount > 0)
            {
                _gl.UseProgram(_shaderProgramSurfels);
                SetUniforms(_shaderProgramSurfels, view, proj);

                int scaleLoc = _gl.GetUniformLocation(_shaderProgramSurfels, "uScale");
                _gl.Uniform1(scaleLoc, _avgDistance * 0.5f * _viewport.SurfelScale);

                int timeLoc = _gl.GetUniformLocation(_shaderProgramSurfels, "uCurrentTime");
                _gl.Uniform1(timeLoc, currentTime);

                int hasHoveredLoc = _gl.GetUniformLocation(_shaderProgramSurfels, "uHasHovered");
                int hoveredPosLoc = _gl.GetUniformLocation(_shaderProgramSurfels, "uHoveredPos");
                if (HoveredCoordinate.HasValue)
                {
                    _gl.Uniform1(hasHoveredLoc, 1.0f);
                    _gl.Uniform3(hoveredPosLoc, (float)HoveredCoordinate.Value.X, (float)HoveredCoordinate.Value.Y, (float)HoveredCoordinate.Value.Z);
                }
                else
                {
                    _gl.Uniform1(hasHoveredLoc, 0.0f);
                }

                _gl.BindVertexArray(_vaoSurfels);
                _gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, (uint)_surfelVertexCount, (uint)_pointCount);
            }

            // 3. Draw Mesh
            if (_viewport.ShowMesh && _meshVertexCount > 0)
            {
                _gl.UseProgram(_shaderProgramMesh);
                SetUniforms(_shaderProgramMesh, view, proj);

                _gl.BindVertexArray(_vaoMesh);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_meshVertexCount);
            }

            // 4. Draw Axes
            if (_viewport.ShowGrid)
            {
                _gl.UseProgram(_shaderProgramAxes);
                SetUniforms(_shaderProgramAxes, view, proj);

                _gl.BindVertexArray(_vaoAxes);
                _gl.DrawArrays(PrimitiveType.Lines, 0, 6);
            }

            _gl.BindVertexArray(0);

            bool hasMissRays = _viewport.ShowMissRays && _missRayCount > 0;
            bool hasNormalRays = _viewport.ShowNormalRays && _pointCount > 0;
            bool hasRays = hasMissRays || hasNormalRays;
            bool hasWboit = hasRays || _viewport.ShowGrid;
            if (hasWboit)
            {
                var camPos = _viewport.Camera.Position;
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Greater);
                _gl.DepthMask(false);
                _gl.Enable(EnableCap.Blend);

                // OIT pass A (accumulation) in dedicated MSAA FBO
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaAccumFbo);
                _gl.ClearColor(0f, 0f, 0f, 0f);
                _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);

                if (_viewport.ShowGrid)
                {
                    _gl.UseProgram(_shaderProgramGridAccum);
                    SetGridUniforms(_shaderProgramGridAccum, view, proj, camPos);
                    _gl.BindVertexArray(_vaoGrid);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }

                if (hasRays)
                {
                    _gl.UseProgram(_shaderProgramRayAccum);
                    SetUniforms(_shaderProgramRayAccum, view, proj);
                    int timeLocAccum = _gl.GetUniformLocation(_shaderProgramRayAccum, "uCurrentTime");
                    _gl.Uniform1(timeLocAccum, currentTime);
                    _gl.Uniform3(_gl.GetUniformLocation(_shaderProgramRayAccum, "uCameraPos"), camPos.X, camPos.Y, camPos.Z);
                    _gl.BindVertexArray(_vaoRays);
                    if (hasMissRays)
                    {
                        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(_missRayCount * 2));
                    }
                    if (hasNormalRays)
                    {
                        _gl.DrawArrays(PrimitiveType.Lines, _missRayCount * 2, (uint)(_pointCount * 2));
                    }
                }

                // OIT pass B (revealage) in dedicated MSAA FBO
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaRevealFbo);
                _gl.ClearColor(1f, 1f, 1f, 1f);
                _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                _gl.BlendFunc(BlendingFactor.Zero, BlendingFactor.OneMinusSrcAlpha);

                if (_viewport.ShowGrid)
                {
                    _gl.UseProgram(_shaderProgramGridReveal);
                    SetGridUniforms(_shaderProgramGridReveal, view, proj, camPos);
                    _gl.BindVertexArray(_vaoGrid);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }

                if (hasRays)
                {
                    _gl.UseProgram(_shaderProgramRayReveal);
                    SetUniforms(_shaderProgramRayReveal, view, proj);
                    int timeLocReveal = _gl.GetUniformLocation(_shaderProgramRayReveal, "uCurrentTime");
                    _gl.Uniform1(timeLocReveal, currentTime);
                    _gl.Uniform3(_gl.GetUniformLocation(_shaderProgramRayReveal, "uCameraPos"), camPos.X, camPos.Y, camPos.Z);
                    _gl.BindVertexArray(_vaoRays);
                    if (hasMissRays)
                    {
                        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(_missRayCount * 2));
                    }
                    if (hasNormalRays)
                    {
                        _gl.DrawArrays(PrimitiveType.Lines, _missRayCount * 2, (uint)(_pointCount * 2));
                    }
                }

                _gl.Disable(EnableCap.Blend);
                _gl.DepthMask(true);
            }

            // Resolve MSAA opaque color/depth to resolve textures
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFbo);
            _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);

            if (hasWboit)
            {
                // Resolve MSAA OIT attachments to single-sample OIT textures.
                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaAccumFbo);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _oitAccumResolveFbo);
                _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaRevealFbo);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _oitRevealResolveFbo);
                _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

                // Composite opaque + transparent into swapchain framebuffer.
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                _gl.Viewport(0, 0, (uint)width, (uint)height);
                _gl.Disable(EnableCap.DepthTest);

                _gl.UseProgram(_shaderProgramComposite);
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, _resolveColor);
                _gl.Uniform1(_gl.GetUniformLocation(_shaderProgramComposite, "uOpaqueColor"), 0);

                _gl.ActiveTexture(TextureUnit.Texture1);
                _gl.BindTexture(TextureTarget.Texture2D, _oitAccumColor);
                _gl.Uniform1(_gl.GetUniformLocation(_shaderProgramComposite, "uAccumColor"), 1);

                _gl.ActiveTexture(TextureUnit.Texture2);
                _gl.BindTexture(TextureTarget.Texture2D, _oitRevealColor);
                _gl.Uniform1(_gl.GetUniformLocation(_shaderProgramComposite, "uRevealColor"), 2);

                _gl.BindVertexArray(_vaoGrid);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

                _gl.ActiveTexture(TextureUnit.Texture2);
                _gl.BindTexture(TextureTarget.Texture2D, 0);
                _gl.ActiveTexture(TextureUnit.Texture1);
                _gl.BindTexture(TextureTarget.Texture2D, 0);
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, 0);
                _gl.Enable(EnableCap.DepthTest);
            }
            else
            {
                // Blit Resolve FBO to Default FBO.
                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _resolveFbo);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
                _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            }
        }

        private static float PositiveModulo(float value, float modulus)
        {
            if (modulus <= 0.0f)
            {
                return 0.0f;
            }

            float result = value % modulus;
            return result < 0.0f ? result + modulus : result;
        }

        private unsafe void SetUniforms(uint program, Matrix4X4<float> view, Matrix4X4<float> proj)
        {
            int viewLoc = _gl.GetUniformLocation(program, "uView");
            _gl.UniformMatrix4(viewLoc, 1, false, (float*)&view);

            int projLoc = _gl.GetUniformLocation(program, "uProjection");
            _gl.UniformMatrix4(projLoc, 1, false, (float*)&proj);
        }

        private void SetGridUniforms(uint program, Matrix4X4<float> view, Matrix4X4<float> proj, Vector3D<float> camPos)
        {
            SetUniforms(program, view, proj);
            _gl.Uniform3(_gl.GetUniformLocation(program, "uCameraPos"), camPos.X, camPos.Y, camPos.Z);
            _gl.Uniform1(_gl.GetUniformLocation(program, "uGridPlaneY"), GridPlaneY);

            double camHeight = Math.Max(Math.Abs((double)(camPos.Y - GridPlaneY)), 0.0001);
            double lod = Math.Log10(Math.Max(camHeight, 1.0));
            double expBase = Math.Floor(lod);
            float fade = (float)(lod - expBase);
            float spacingMajor0 = (float)Math.Pow(10.0, expBase);
            float spacingMajor1 = spacingMajor0 * 10.0f;
            float spacingMinor = spacingMajor0 * 0.1f;

            float phaseMinorX = PositiveModulo(camPos.X, spacingMinor);
            float phaseMinorZ = PositiveModulo(camPos.Z, spacingMinor);
            float phaseMajor0X = PositiveModulo(camPos.X, spacingMajor0);
            float phaseMajor0Z = PositiveModulo(camPos.Z, spacingMajor0);
            float phaseMajor1X = PositiveModulo(camPos.X, spacingMajor1);
            float phaseMajor1Z = PositiveModulo(camPos.Z, spacingMajor1);

            _gl.Uniform1(_gl.GetUniformLocation(program, "uGridSpacingMinor"), spacingMinor);
            _gl.Uniform1(_gl.GetUniformLocation(program, "uGridSpacingMajor0"), spacingMajor0);
            _gl.Uniform1(_gl.GetUniformLocation(program, "uGridSpacingMajor1"), spacingMajor1);
            _gl.Uniform2(_gl.GetUniformLocation(program, "uGridPhaseMinor"), phaseMinorX, phaseMinorZ);
            _gl.Uniform2(_gl.GetUniformLocation(program, "uGridPhaseMajor0"), phaseMajor0X, phaseMajor0Z);
            _gl.Uniform2(_gl.GetUniformLocation(program, "uGridPhaseMajor1"), phaseMajor1X, phaseMajor1Z);
            _gl.Uniform1(_gl.GetUniformLocation(program, "uGridFade"), fade);

            float baseFadeStart = 85000.0f;
            float camHeightFadeStart = Math.Abs(camPos.Y - GridPlaneY) * 12.0f;
            float fadeStart = MathF.Max(baseFadeStart, camHeightFadeStart);
            _gl.Uniform1(_gl.GetUniformLocation(program, "uGridFadeStart"), fadeStart);
            _gl.Uniform1(_gl.GetUniformLocation(program, "uGridFadeEnd"), fadeStart * 1.15f);
        }

    }
}
