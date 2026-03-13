using Avalonia.OpenGL;
using Silk.NET.OpenGL;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using MeshTool.Core.Data;
using Silk.NET.Maths;
using MeshTool.UI.Controls;
using MeshTool.UI.Models;

namespace MeshTool.UI.Rendering
{
    public class SceneRenderer
    {
        private const int FineDensityPreviewTargetPoints = 36000;
        private const float FineDensityPreviewAdjustRate = 0.35f;
        private const float ScanDensityRebuildMoveThreshold = 24f;
        private float _fineDensityPreviewRadius = 3200f;
        private GL _gl;
        private OpenGlViewport _viewport;
        private uint _vaoPoints, _vboInstances;
        private uint _vaoSurfels, _vboSurfelVerts;
        private ShaderProgram? _shaderProgramPoints, _shaderProgramSurfels, _shaderProgramRayAccum, _shaderProgramRayReveal, _shaderProgramGridAccum, _shaderProgramGridReveal, _shaderProgramComposite, _shaderProgramMesh, _shaderProgramAxes, _shaderProgramGizmoSolid, _shaderProgramDensityPoints, _shaderProgramFlatColor, _shaderProgramGizmoAccum, _shaderProgramGizmoReveal, _shaderProgramFlatAccum, _shaderProgramFlatReveal;
        private uint _vaoRays, _vboRays;
        private uint _vaoMesh, _vboMesh;
        private uint _vaoGrid, _vboGrid;
        private uint _vaoAxes, _vboAxes;
        private uint _vaoScanVolume, _vboScanVolume;
        private int _scanVolumeVertexCount;
        private uint _vaoScanHandles, _vboScanHandles;
        private int _scanHandleVertexCount;
        private uint _vaoScanDensity, _vboScanDensity;
        private int _scanDensityVertexCount;
        private int _scanDensityBroadCount;
        private uint _vaoSelectionFill, _vboSelectionFill;
        private int _selectionFillVertexCount;
        private bool _scanDensityBufferValid;
        private ScanVolumeSettings _lastDensityScanVolume;
        private float _lastDensityFineTargetStep = -1f;
        private float _lastDensityGridPlaneY = float.NaN;
        private Vector3D<float> _lastDensityCameraPos;
        private int _meshVertexCount;

        private int _pointCapacity;
        private int _rayCapacity; // Number of lines capacity
        private float _avgDistance = 1.0f;

        private int _surfelVertexCount;

        private OitFramebufferManager? _framebufferManager;
        private float _latestSpawnTime = 0f;

        public Vector3D<float>? HoveredCoordinate { get; set; }
        public float GridPlaneY { get; set; } = 0.0f;
        public bool ShowScanVolume { get; set; } = true;
        public bool ShowScanHandles { get; set; } = true;
        public bool ShowScanDensityPreview { get; set; } = true;
        public float ScanFineTargetStep { get; set; } = 24f;
        public bool UseDynamicColorMapping { get; set; } = false;
        private ScanVolumeSettings _scanVolume = ScanVolumeSettings.Default;
        private int _hoverScanHandle;
        private int _activeScanHandle;
        private float _minPointY = float.MaxValue;
        private float _maxPointY = float.MinValue;
        private bool _showSelectionBox;
        private Vector3D<float> _selectionStartWorld;
        private Vector3D<float> _selectionEndWorld;
        private float _selectionYBottom;
        private float _selectionYTop;
        private Vector4D<float>[] _selectionAreas = Array.Empty<Vector4D<float>>();
        private float _selectionAreasPlaneY;

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

            _framebufferManager = new OitFramebufferManager(_gl, 4, _viewport.OnLog);

            InitShaders();
            InitBuffers();
        }

        private unsafe void InitShaders()
        {
            // --- POINT SHADER ---
            _shaderProgramPoints = new ShaderProgram(_gl, "Point", ShaderSource.PointVertex, ShaderSource.PointFragment, _viewport.OnLog);

            // --- SURFEL SHADER (Instanced) ---
            string vsSurfel = @"#version 300 es
                precision highp float;
                layout (location = 0) in vec3 aVertex; // Unit circle vertex
                layout (location = 1) in vec3 iPos;    // Instance Position
                layout (location = 2) in vec3 iNormal; // Instance Normal
                layout (location = 3) in float iSpawnTime; // Instance Spawn Time
                layout (location = 4) in float iSelected; // Selection flag
                
                uniform mat4 uView;
                uniform mat4 uProjection;
                uniform float uScale;
                uniform float uCurrentTime;
                uniform vec3 uHoveredPos;
                uniform float uHasHovered;
                uniform float uUseDynamicColor; // Optional Color Mapping Toggle
                uniform float uWorldMinY; // Bounds minimum Y
                uniform float uWorldMaxY; // Bounds maximum Y

                out vec3 Normal;
                out vec3 Color;

                // Simple viridis approximation
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
                    
                    // aVertex is unit disk in XZ plane, so X->tangent and Z->bitangent
                    vec3 worldPos = iPos + (tangent * aVertex.x + bitangent * aVertex.z) * uScale;
                    
                    gl_Position = uProjection * uView * vec4(worldPos, 1.0);
                    // Reverse Z: gl_Position.z is mapped to [1, 0] depth
                    Normal = norm;
                    
                    float age = uCurrentTime - iSpawnTime;
                    if (iSelected > 0.5) {
                        Color = vec3(1.0, 0.62, 0.12); // Selected
                    } else if (uHasHovered > 0.5 && length(iPos - uHoveredPos) < 0.001) {
                        Color = vec3(1.0, 0.0, 1.0); // Magenta
                    } else if (uUseDynamicColor > 0.5) {
                        // Semantic Color Mapping Mode (Height-Based Viridis)
                        float range = max(0.001, uWorldMaxY - uWorldMinY);
                        float normalizedHeight = clamp((iPos.y - uWorldMinY) / range, 0.0, 1.0);
                        Color = colormap(normalizedHeight);
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
            _shaderProgramSurfels = new ShaderProgram(_gl, "Surfel", vsSurfel, fsSurfel, _viewport.OnLog);

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
                    float distFade = missRay ? 1.0 : (1.0 - smoothstep(500.0, 20000.0, dist));
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
            _shaderProgramRayAccum = new ShaderProgram(_gl, "RayAccum", vsRay, fsRayAccum, _viewport.OnLog);
            _shaderProgramRayReveal = new ShaderProgram(_gl, "RayReveal", vsRay, fsRayReveal, _viewport.OnLog);

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
            _shaderProgramComposite = new ShaderProgram(_gl, "Composite", vsComposite, fsComposite, _viewport.OnLog);

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
                    gl_PointSize = 3.5;
                    Color = aColor;
                }";
            string fsAxes = @"#version 300 es
                precision highp float;
                in vec3 Color;
                out vec4 FragColor;
                void main() {
                    FragColor = vec4(Color, 1.0);
                }";
            _shaderProgramAxes = new ShaderProgram(_gl, "Axes", vsAxes, fsAxes, _viewport.OnLog);

            string vsDensityPoints = @"#version 300 es
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
            string fsDensityPoints = @"#version 300 es
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
            _shaderProgramDensityPoints = new ShaderProgram(_gl, "DensityPoints", vsDensityPoints, fsDensityPoints, _viewport.OnLog);

            string vsGizmoSolid = @"#version 300 es
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
            string fsGizmoSolid = @"#version 300 es
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
            _shaderProgramGizmoSolid = new ShaderProgram(_gl, "GizmoSolid", vsGizmoSolid, fsGizmoSolid, _viewport.OnLog);

            string fsGizmoAccum = @"#version 300 es
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
            string fsGizmoReveal = @"#version 300 es
                precision highp float;
                in float vAlpha;
                out vec4 FragColor;
                void main() {
                    if (vAlpha >= 0.999) discard;
                    float alpha = clamp(vAlpha, 0.0, 1.0);
                    if (alpha < 0.001) discard;
                    FragColor = vec4(alpha, alpha, alpha, alpha);
                }";
            _shaderProgramGizmoAccum = new ShaderProgram(_gl, "GizmoAccum", vsGizmoSolid, fsGizmoAccum, _viewport.OnLog);
            _shaderProgramGizmoReveal = new ShaderProgram(_gl, "GizmoReveal", vsGizmoSolid, fsGizmoReveal, _viewport.OnLog);

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
            _shaderProgramMesh = new ShaderProgram(_gl, "Mesh", vsMesh, fsMesh, _viewport.OnLog);

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

            _shaderProgramGridAccum = new ShaderProgram(_gl, "GridAccum", vsGrid, fsGridAccum, _viewport.OnLog);
            _shaderProgramGridReveal = new ShaderProgram(_gl, "GridReveal", vsGrid, fsGridReveal, _viewport.OnLog);

            string vsFlatColor = @"#version 300 es
                precision highp float;
                layout (location = 0) in vec3 aPos;
                uniform mat4 uView;
                uniform mat4 uProjection;
                void main() {
                    gl_Position = uProjection * uView * vec4(aPos, 1.0);
                }";
            string fsFlatColor = @"#version 300 es
                precision highp float;
                uniform vec4 uColor;
                out vec4 FragColor;
                void main() {
                    FragColor = uColor;
                }";
            _shaderProgramFlatColor = new ShaderProgram(_gl, "FlatColor", vsFlatColor, fsFlatColor, _viewport.OnLog);

            string fsFlatAccum = @"#version 300 es
                precision highp float;
                uniform vec4 uColor;
                out vec4 FragColor;
                void main() {
                    float alpha = clamp(uColor.a, 0.0, 1.0);
                    if (alpha < 0.001) discard;

                    float z = gl_FragCoord.z;
                    float weight = clamp(alpha * 1e4 * pow(z, 4.0), 1e-2, 3e3);
                    FragColor = vec4(uColor.rgb * alpha * weight, alpha * weight);
                }";
            string fsFlatReveal = @"#version 300 es
                precision highp float;
                uniform vec4 uColor;
                out vec4 FragColor;
                void main() {
                    float alpha = clamp(uColor.a, 0.0, 1.0);
                    if (alpha < 0.001) discard;
                    FragColor = vec4(alpha, alpha, alpha, alpha);
                }";
            _shaderProgramFlatAccum = new ShaderProgram(_gl, "FlatAccum", vsFlatColor, fsFlatAccum, _viewport.OnLog);
            _shaderProgramFlatReveal = new ShaderProgram(_gl, "FlatReveal", vsFlatColor, fsFlatReveal, _viewport.OnLog);
        }

        private unsafe void SetUniforms(ShaderProgram program, Matrix4X4<float> view, Matrix4X4<float> proj)
        {
            program.SetViewProjection(view, proj);
        }

        private unsafe void InitBuffers()
        {
            _vboInstances = _gl.GenBuffer();

            // 1. Points VAO
            _vaoPoints = _gl.GenVertexArray();
            _gl.BindVertexArray(_vaoPoints);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(7 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);

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
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribDivisor(1, 1); // Instanced

            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribDivisor(2, 1); // Instanced

            _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribDivisor(3, 1); // Instanced

            _gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(7 * sizeof(float)));
            _gl.EnableVertexAttribArray(4);
            _gl.VertexAttribDivisor(4, 1); // Instanced

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

            // 8. Solid gizmo handles VAO (dynamic triangle list)
            _vaoScanHandles = _gl.GenVertexArray();
            _vboScanHandles = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoScanHandles);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboScanHandles);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(4096 * 10 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)(9 * sizeof(float)));
            _gl.EnableVertexAttribArray(3);

            // 9. Scan density preview VAO (dynamic points)
            _vaoScanDensity = _gl.GenVertexArray();
            _vboScanDensity = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoScanDensity);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboScanDensity);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(400000 * 6 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            // 7. Scan volume gizmo VAO (dynamic line list)
            _vaoScanVolume = _gl.GenVertexArray();
            _vboScanVolume = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoScanVolume);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboScanVolume);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(256 * 6 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            // 10. Selection fill VAO (dynamic planar quad triangles)
            _vaoSelectionFill = _gl.GenVertexArray();
            _vboSelectionFill = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoSelectionFill);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboSelectionFill);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(6 * 3 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
        }

        public void Deinit()
        {
            _framebufferManager?.Dispose();

            _gl.DeleteVertexArray(_vaoPoints);
            _gl.DeleteVertexArray(_vaoSurfels);
            _gl.DeleteVertexArray(_vaoRays);
            _gl.DeleteVertexArray(_vaoMesh);
            _gl.DeleteVertexArray(_vaoGrid);
            _gl.DeleteVertexArray(_vaoAxes);
            _gl.DeleteVertexArray(_vaoScanVolume);
            _gl.DeleteVertexArray(_vaoScanHandles);
            _gl.DeleteVertexArray(_vaoScanDensity);
            _gl.DeleteVertexArray(_vaoSelectionFill);
            _gl.DeleteBuffer(_vboInstances);
            _gl.DeleteBuffer(_vboSurfelVerts);
            _gl.DeleteBuffer(_vboRays);
            _gl.DeleteBuffer(_vboMesh);
            _gl.DeleteBuffer(_vboGrid);
            _gl.DeleteBuffer(_vboAxes);
            _gl.DeleteBuffer(_vboScanVolume);
            _gl.DeleteBuffer(_vboScanHandles);
            _gl.DeleteBuffer(_vboScanDensity);
            _gl.DeleteBuffer(_vboSelectionFill);
            _shaderProgramPoints?.Dispose();
            _shaderProgramSurfels?.Dispose();
            _shaderProgramRayAccum?.Dispose();
            _shaderProgramRayReveal?.Dispose();
            _shaderProgramGridAccum?.Dispose();
            _shaderProgramGridReveal?.Dispose();
            _shaderProgramComposite?.Dispose();
            _shaderProgramMesh?.Dispose();
            _shaderProgramAxes?.Dispose();
            _shaderProgramGizmoSolid?.Dispose();
            _shaderProgramDensityPoints?.Dispose();
            _shaderProgramFlatColor?.Dispose();
            _shaderProgramGizmoAccum?.Dispose();
            _shaderProgramGizmoReveal?.Dispose();
            _shaderProgramFlatAccum?.Dispose();
            _shaderProgramFlatReveal?.Dispose();
            _gl.Dispose();
        }

        private MeshTool.Core.Data.Vertex[]? _pendingPoints;
        private MeshTool.Core.Data.Ray[]? _pendingRays;
        private System.Collections.Generic.List<MeshTool.Core.Data.Triangle>? _pendingMesh;
        private float[]? _pendingMeshRawBuffer;
        private int _pendingMeshRawVertexCount;
        private System.Collections.Generic.List<MeshTool.Core.Data.Vertex> _pendingAppendPointsList = new System.Collections.Generic.List<MeshTool.Core.Data.Vertex>();
        private System.Collections.Generic.List<MeshTool.Core.Data.Ray> _pendingAppendRaysList = new System.Collections.Generic.List<MeshTool.Core.Data.Ray>();
        private float _pendingAvgDistance;
        private bool _dataDirty = false;
        private bool _appendDirty = false;
        private bool _meshDirty = false;
        private bool _meshRawDirty = false;
        private readonly object _pendingLock = new object();

        private int _pointCount;
        private int _rayCount;
        private int _missRayCount;
        private readonly System.Collections.Generic.List<Vertex> _allPoints = new System.Collections.Generic.List<Vertex>();
        private readonly System.Collections.Generic.HashSet<int> _selectedPointIndices = new System.Collections.Generic.HashSet<int>();
        private int[]? _pendingSelectedPointIndices;
        private bool _selectionDirty;

        public bool IsMeshUpdatePending
        {
            get { lock (_pendingLock) return _meshDirty || _meshRawDirty; }
        }

        public unsafe void UpdateMesh(System.Collections.Generic.List<MeshTool.Core.Data.Triangle>? triangles)
        {
            lock (_pendingLock)
            {
                _pendingMesh = triangles;
                _meshDirty = true;
                _meshRawDirty = false;
            }
        }

        public unsafe void UpdateMeshRaw(float[] buffer, int vertexCount)
        {
            lock (_pendingLock)
            {
                if (_pendingMeshRawBuffer != null)
                {
                    ArrayPool<float>.Shared.Return(_pendingMeshRawBuffer);
                }
                _pendingMeshRawBuffer = buffer;
                _pendingMeshRawVertexCount = vertexCount;
                _meshRawDirty = true;
                _meshDirty = false;
            }
        }

        public unsafe void UpdateData(Vertex[] points, MeshTool.Core.Data.Ray[] rays, float avgDistance)
        {
            lock (_pendingLock)
            {
                _pendingPoints = points;
                _pendingRays = rays;
                _pendingAvgDistance = avgDistance;
                _dataDirty = true;
                _appendDirty = false; // Override any pending appends
                _pendingAppendPointsList.Clear();
                _pendingAppendRaysList.Clear();
            }
            UpdateLatestSpawnTime(points, rays);
        }

        public unsafe void AppendData(Vertex[]? newPoints, MeshTool.Core.Data.Ray[]? newMisses, float avgDistance)
        {
            lock (_pendingLock)
            {
                if (_dataDirty) return; // If a full update is pending, ignore appends

                if (newPoints != null)
                {
                    _pendingAppendPointsList.AddRange(newPoints);
                }
                if (newMisses != null)
                {
                    _pendingAppendRaysList.AddRange(newMisses);
                }
                _pendingAvgDistance = avgDistance;
                _appendDirty = true;
            }

            UpdateLatestSpawnTime(newPoints, newMisses);
        }

        public void UpdateScanVolume(ScanVolumeSettings settings)
        {
            _scanVolume = settings.Sanitize();
        }

        public void UpdateScanHandleState(int hoverHandle, int activeHandle)
        {
            _hoverScanHandle = hoverHandle;
            _activeScanHandle = activeHandle;
        }

        public void UpdateSelectedPointIndices(int[] indices)
        {
            lock (_pendingLock)
            {
                _pendingSelectedPointIndices = indices;
                _selectionDirty = true;
            }
        }

        public void UpdateSelectionBox(bool show, Vector3D<float> startWorld, Vector3D<float> endWorld, float yBottom, float yTop)
        {
            _showSelectionBox = show;
            _selectionStartWorld = startWorld;
            _selectionEndWorld = endWorld;
            _selectionYBottom = MathF.Min(yBottom, yTop);
            _selectionYTop = MathF.Max(yBottom, yTop);
        }

        public void UpdateSelectionAreas(Vector4D<float>[] areas, float planeY)
        {
            _selectionAreas = areas ?? Array.Empty<Vector4D<float>>();
            _selectionAreasPlaneY = planeY;
        }

        private void UpdateLatestSpawnTime(Vertex[]? points, MeshTool.Core.Data.Ray[]? rays)
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
            float currentTime = (float)(Environment.TickCount64 - MeshTool.Core.IO.LogParser.AppStartTime) / 1000.0f;
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
            Vertex[]? pendingPoints = null;
            MeshTool.Core.Data.Ray[]? pendingRays = null;
            Vertex[] newPoints = Array.Empty<Vertex>();
            MeshTool.Core.Data.Ray[] newMisses = Array.Empty<MeshTool.Core.Data.Ray>();
            System.Collections.Generic.List<MeshTool.Core.Data.Triangle>? pendingMesh = null;
            float pendingAvgDistance = 0f;

            bool doFullUpdate = false;
            bool doAppend = false;
            bool doMesh = false;
            bool doSelectionUpdate = false;
            int[]? selectedIndices = null;

            lock (_pendingLock)
            {
                if (_dataDirty)
                {
                    _dataDirty = false;
                    pendingPoints = _pendingPoints;
                    pendingRays = _pendingRays;
                    pendingAvgDistance = _pendingAvgDistance;
                    doFullUpdate = pendingPoints != null && pendingRays != null;
                }
                else if (_appendDirty)
                {
                    _appendDirty = false;
                    newPoints = _pendingAppendPointsList.ToArray();
                    newMisses = _pendingAppendRaysList.ToArray();
                    _pendingAppendPointsList.Clear();
                    _pendingAppendRaysList.Clear();
                    pendingAvgDistance = _pendingAvgDistance;
                    doAppend = true;
                }

                if (_selectionDirty)
                {
                    _selectionDirty = false;
                    selectedIndices = _pendingSelectedPointIndices ?? Array.Empty<int>();
                    doSelectionUpdate = true;
                }

                if (_meshDirty)
                {
                    _meshDirty = false;
                    pendingMesh = _pendingMesh;
                    doMesh = true;
                }
                else if (_meshRawDirty)
                {
                    _meshRawDirty = false;

                    if (_pendingMeshRawBuffer != null)
                    {
                        _meshVertexCount = _pendingMeshRawVertexCount;
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboMesh);
                        fixed (float* v = _pendingMeshRawBuffer)
                        {
                            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_meshVertexCount * 6 * sizeof(float)), v, BufferUsageARB.StaticDraw);
                        }
                        ArrayPool<float>.Shared.Return(_pendingMeshRawBuffer);
                        _pendingMeshRawBuffer = null;
                    }
                }
            }

            if (doFullUpdate)
            {
                Vertex[] points = pendingPoints!;
                MeshTool.Core.Data.Ray[] rays = pendingRays!;
                _avgDistance = pendingAvgDistance;

                _pointCount = points.Length;
                _missRayCount = rays.Length;
                _rayCount = _missRayCount + _pointCount;

                if (_pointCount > _pointCapacity)
                {
                    _pointCapacity = Math.Max(_pointCapacity * 2, _pointCount + 10000);
                    _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_pointCapacity * 8 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
                }

                if (_pointCount > 0)
                {
                    Console.WriteLine($"[GL] Uploading {_pointCount} points to GPU...");
                    int pointFloatCount = _pointCount * 8;
                    float[] vertices = ArrayPool<float>.Shared.Rent(pointFloatCount);
                    try
                    {
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

                            vertices[i * 8 + 0] = (float)points[i].Position.X;
                            vertices[i * 8 + 1] = (float)points[i].Position.Y;
                            vertices[i * 8 + 2] = (float)points[i].Position.Z;
                            vertices[i * 8 + 3] = nx;
                            vertices[i * 8 + 4] = ny;
                            vertices[i * 8 + 5] = nz;
                            vertices[i * 8 + 6] = points[i].SpawnTime;
                            vertices[i * 8 + 7] = _selectedPointIndices.Contains(i) ? 1f : 0f;

                            if (points[i].Position.Y < _minPointY) _minPointY = (float)points[i].Position.Y;
                            if (points[i].Position.Y > _maxPointY) _maxPointY = (float)points[i].Position.Y;
                        }

                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                        fixed (float* v = vertices)
                        {
                            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(pointFloatCount * sizeof(float)), v);
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(vertices);
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
                    int rayFloatCount = _rayCount * 14;
                    float[] rayData = ArrayPool<float>.Shared.Rent(rayFloatCount);
                    try
                    {
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

                        // Point normals (Yellow) - fixed length for stable visualization.
                        int offset = rays.Length * 14;
                        const float normalLen = 300.0f;
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
                            rayData[idx + 3] = 1f; rayData[idx + 4] = 1f; rayData[idx + 5] = 0f;
                            rayData[idx + 6] = 0f;

                            rayData[idx + 7] = px + nx * normalLen; rayData[idx + 8] = py + ny * normalLen; rayData[idx + 9] = pz + nz * normalLen;
                            rayData[idx + 10] = 1f; rayData[idx + 11] = 1f; rayData[idx + 12] = 0f;
                            rayData[idx + 13] = 0f;
                        }

                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                        fixed (float* v = rayData)
                        {
                            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(rayFloatCount * sizeof(float)), v);
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(rayData);
                    }
                }

                _allPoints.Clear();
                _allPoints.AddRange(points);
            }
            else if (doAppend)
            {
                _avgDistance = pendingAvgDistance;

                int oldPointCount = _pointCount;
                int oldMissRayCount = _missRayCount;
                int oldRayCount = _rayCount;
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
                        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(newCapacity * 8 * sizeof(float)), null, BufferUsageARB.DynamicDraw);

                        _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _vboInstances);
                        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, newVbo);
                        _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, 0, (nuint)(_pointCount * 8 * sizeof(float)));

                        _gl.DeleteBuffer(_vboInstances);
                        _vboInstances = newVbo;
                        _pointCapacity = newCapacity;

                        // Re-bind VAOs to new VBO
                        _gl.BindVertexArray(_vaoPoints);
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
                        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
                        _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(7 * sizeof(float)));

                        _gl.BindVertexArray(_vaoSurfels);
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
                        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
                        _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
                        _gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(7 * sizeof(float)));
                    }

                    int pointFloatCount = addedPoints * 8;
                    float[] vertices = ArrayPool<float>.Shared.Rent(pointFloatCount);
                    try
                    {
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

                            int pointIndex = _pointCount + i;
                            vertices[i * 8 + 0] = (float)newPoints[i].Position.X;
                            vertices[i * 8 + 1] = (float)newPoints[i].Position.Y;
                            vertices[i * 8 + 2] = (float)newPoints[i].Position.Z;
                            vertices[i * 8 + 3] = nx;
                            vertices[i * 8 + 4] = ny;
                            vertices[i * 8 + 5] = nz;
                            vertices[i * 8 + 6] = newPoints[i].SpawnTime;
                            vertices[i * 8 + 7] = _selectedPointIndices.Contains(pointIndex) ? 1f : 0f;

                            if (newPoints[i].Position.Y < _minPointY) _minPointY = (float)newPoints[i].Position.Y;
                            if (newPoints[i].Position.Y > _maxPointY) _maxPointY = (float)newPoints[i].Position.Y;
                        }

                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                        fixed (float* v = vertices)
                        {
                            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(_pointCount * 8 * sizeof(float)), (nuint)(pointFloatCount * sizeof(float)), v);
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(vertices);
                    }
                    _pointCount = newPointCount;
                    _allPoints.AddRange(newPoints);
                }

                if (addedRays > 0)
                {
                    int newRayCount = oldRayCount + addedRays;
                    if (newRayCount > _rayCapacity)
                    {
                        int newCapacity = Math.Max(_rayCapacity * 2, newRayCount + 10000);
                        uint newVbo = _gl.GenBuffer();
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, newVbo);
                        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(newCapacity * 14 * sizeof(float)), null, BufferUsageARB.DynamicDraw);

                        _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _vboRays);
                        _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, newVbo);
                        _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, 0, (nuint)(oldRayCount * 14 * sizeof(float)));

                        _gl.DeleteBuffer(_vboRays);
                        _vboRays = newVbo;
                        _rayCapacity = newCapacity;

                        _gl.BindVertexArray(_vaoRays);
                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
                        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(3 * sizeof(float)));
                        _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)(6 * sizeof(float)));
                    }

                    int bytesPerRay = 14 * sizeof(float);
                    if (addedMisses > 0 && oldPointCount > 0)
                    {
                        uint tempVbo = _gl.GenBuffer();
                        int normalBytes = oldPointCount * bytesPerRay;
                        try
                        {
                            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, tempVbo);
                            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)normalBytes, null, BufferUsageARB.DynamicDraw);

                            nint oldNormalsOffset = (nint)(oldMissRayCount * bytesPerRay);
                            _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _vboRays);
                            _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, tempVbo);
                            _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, oldNormalsOffset, 0, (nuint)normalBytes);

                            nint shiftedNormalsOffset = (nint)((oldMissRayCount + addedMisses) * bytesPerRay);
                            _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, tempVbo);
                            _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, _vboRays);
                            _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, shiftedNormalsOffset, (nuint)normalBytes);
                        }
                        finally
                        {
                            _gl.DeleteBuffer(tempVbo);
                        }
                    }

                    if (addedMisses > 0)
                    {
                        int missFloatCount = addedMisses * 14;
                        float[] missData = ArrayPool<float>.Shared.Rent(missFloatCount);
                        try
                        {
                            for (int i = 0; i < addedMisses; i++)
                            {
                                int idx = i * 14;
                                missData[idx + 0] = (float)newMisses![i].Start.X; missData[idx + 1] = (float)newMisses[i].Start.Y; missData[idx + 2] = (float)newMisses[i].Start.Z;
                                missData[idx + 3] = 1f; missData[idx + 4] = 0f; missData[idx + 5] = 0f;
                                missData[idx + 6] = newMisses[i].SpawnTime;

                                missData[idx + 7] = (float)newMisses[i].End.X; missData[idx + 8] = (float)newMisses[i].End.Y; missData[idx + 9] = (float)newMisses[i].End.Z;
                                missData[idx + 10] = 1f; missData[idx + 11] = 0f; missData[idx + 12] = 0f;
                                missData[idx + 13] = newMisses[i].SpawnTime;
                            }

                            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                            fixed (float* v = missData)
                            {
                                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(oldMissRayCount * bytesPerRay), (nuint)(missFloatCount * sizeof(float)), v);
                            }
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(missData);
                        }
                    }

                    if (addedPoints > 0)
                    {
                        int normalFloatCount = addedPoints * 14;
                        float[] rayData = ArrayPool<float>.Shared.Rent(normalFloatCount);
                        try
                        {
                            const float normalLen = 300.0f;
                            for (int i = 0; i < addedPoints; i++)
                            {
                                int idx = i * 14;
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

                            int oldNormalCount = oldPointCount;
                            int normalInsertRayIndex = oldMissRayCount + addedMisses + oldNormalCount;
                            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboRays);
                            fixed (float* v = rayData)
                            {
                                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(normalInsertRayIndex * bytesPerRay), (nuint)(normalFloatCount * sizeof(float)), v);
                            }
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(rayData);
                        }
                    }

                    _rayCount = newRayCount;
                    _missRayCount = oldMissRayCount + addedMisses;
                }

            }

            if (doSelectionUpdate)
            {
                _selectedPointIndices.Clear();
                if (selectedIndices != null)
                {
                    for (int i = 0; i < selectedIndices.Length; i++)
                    {
                        int idx = selectedIndices[i];
                        if (idx >= 0 && idx < _pointCount)
                        {
                            _selectedPointIndices.Add(idx);
                        }
                    }
                }

                if (_pointCount > 0)
                {
                    int pointFloatCount = _pointCount * 8;
                    float[] vertices = ArrayPool<float>.Shared.Rent(pointFloatCount);
                    try
                    {
                        for (int i = 0; i < _pointCount; i++)
                        {
                            var p = _allPoints[i];
                            float nx = (float)p.Normal.X;
                            float ny = (float)p.Normal.Y;
                            float nz = (float)p.Normal.Z;
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

                            vertices[i * 8 + 0] = (float)p.Position.X;
                            vertices[i * 8 + 1] = (float)p.Position.Y;
                            vertices[i * 8 + 2] = (float)p.Position.Z;
                            vertices[i * 8 + 3] = nx;
                            vertices[i * 8 + 4] = ny;
                            vertices[i * 8 + 5] = nz;
                            vertices[i * 8 + 6] = p.SpawnTime;
                            vertices[i * 8 + 7] = _selectedPointIndices.Contains(i) ? 1f : 0f;
                        }

                        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboInstances);
                        fixed (float* v = vertices)
                        {
                            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(pointFloatCount * sizeof(float)), v);
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(vertices);
                    }
                }
            }

            if (doMesh)
            {
                if (pendingMesh != null)
                {
                    _meshVertexCount = pendingMesh.Count * 3;
                    if (_meshVertexCount > 0)
                    {
                        int meshFloatCount = _meshVertexCount * 6;
                        float[] meshData = ArrayPool<float>.Shared.Rent(meshFloatCount);
                        try
                        {
                            for (int i = 0; i < pendingMesh.Count; i++)
                            {
                                var t = pendingMesh[i];
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
                                    n = new MeshTool.Core.Data.Vector3(0, 1, 0);
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
                                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(meshFloatCount * sizeof(float)), v, BufferUsageARB.StaticDraw);
                            }
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(meshData);
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

            _framebufferManager?.EnsureSize(width, height);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebufferManager!.MsaaFbo);
            _gl.Viewport(0, 0, (uint)width, (uint)height);
            _gl.ClearColor(0.15f, 0.15f, 0.15f, 1.0f);
            if (_glClearDepthf != null) _glClearDepthf(0.0f);
            else _gl.ClearDepth(0.0);
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Greater);

            bool hasAnyGeometry = _pointCount > 0 || _rayCount > 0 || _meshVertexCount > 0 || _showSelectionBox || _selectionAreas.Length > 0;
            if (!hasAnyGeometry && !_viewport.ShowGrid)
            {
                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebufferManager.MsaaFbo);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
                _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                return;
            }

            var view = _viewport.Camera.GetViewMatrix();
            var proj = _viewport.Camera.GetProjectionMatrix((float)width, (float)height);
            var vp = view * proj;

            float currentTime = (float)(Environment.TickCount64 - MeshTool.Core.IO.LogParser.AppStartTime) / 1000.0f;

            // 1. Draw Points
            if (_viewport.ShowPoints && _pointCount > 0)
            {
                _gl.UseProgram(_shaderProgramPoints!.Handle);
                SetUniforms(_shaderProgramPoints, view, proj);

                _gl.BindVertexArray(_vaoPoints);
                _gl.PointSize(4.0f);

                // Force unbind element array buffer in case it was bound elsewhere
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);

                int dynColLocP = _gl.GetUniformLocation(_shaderProgramPoints.Handle, "uUseDynamicColor");
                _gl.Uniform1(dynColLocP, UseDynamicColorMapping ? 1.0f : 0.0f);

                int minLocP = _gl.GetUniformLocation(_shaderProgramPoints.Handle, "uWorldMinY");
                int maxLocP = _gl.GetUniformLocation(_shaderProgramPoints.Handle, "uWorldMaxY");
                _gl.Uniform1(minLocP, _minPointY);
                _gl.Uniform1(maxLocP, _maxPointY);

                _gl.DrawArrays(PrimitiveType.Points, 0, (uint)_pointCount);
            }

            // 2. Draw Surfels
            if (_viewport.ShowSurfels && _pointCount > 0)
            {
                _gl.UseProgram(_shaderProgramSurfels!.Handle);
                SetUniforms(_shaderProgramSurfels, view, proj);

                int scaleLoc = _gl.GetUniformLocation(_shaderProgramSurfels.Handle, "uScale");
                _gl.Uniform1(scaleLoc, _avgDistance * 0.5f * _viewport.SurfelScale);

                int timeLoc = _gl.GetUniformLocation(_shaderProgramSurfels.Handle, "uCurrentTime");
                _gl.Uniform1(timeLoc, currentTime);

                int hasHoveredLoc = _gl.GetUniformLocation(_shaderProgramSurfels.Handle, "uHasHovered");
                int hoveredPosLoc = _gl.GetUniformLocation(_shaderProgramSurfels.Handle, "uHoveredPos");
                if (HoveredCoordinate.HasValue)
                {
                    _gl.Uniform1(hasHoveredLoc, 1.0f);
                    _gl.Uniform3(hoveredPosLoc, (float)HoveredCoordinate.Value.X, (float)HoveredCoordinate.Value.Y, (float)HoveredCoordinate.Value.Z);
                }
                else
                {
                    _gl.Uniform1(hasHoveredLoc, 0.0f);
                }

                int dynColLoc = _gl.GetUniformLocation(_shaderProgramSurfels.Handle, "uUseDynamicColor");
                _gl.Uniform1(dynColLoc, UseDynamicColorMapping ? 1.0f : 0.0f);

                int minLoc = _gl.GetUniformLocation(_shaderProgramSurfels.Handle, "uWorldMinY");
                int maxLoc = _gl.GetUniformLocation(_shaderProgramSurfels.Handle, "uWorldMaxY");
                _gl.Uniform1(minLoc, _minPointY);
                _gl.Uniform1(maxLoc, _maxPointY);

                _gl.BindVertexArray(_vaoSurfels);
                _gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, (uint)_surfelVertexCount, (uint)_pointCount);
            }

            // 3. Draw Mesh
            if (_viewport.ShowMesh && _meshVertexCount > 0)
            {
                _gl.UseProgram(_shaderProgramMesh!.Handle);
                SetUniforms(_shaderProgramMesh, view, proj);

                _gl.BindVertexArray(_vaoMesh);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_meshVertexCount);
            }

            // 4. Draw Axes
            if (_viewport.ShowGrid)
            {
                _gl.UseProgram(_shaderProgramAxes!.Handle);
                SetUniforms(_shaderProgramAxes, view, proj);

                _gl.BindVertexArray(_vaoAxes);
                _gl.DrawArrays(PrimitiveType.Lines, 0, 6);
            }

            if (ShowScanVolume || ShowScanDensityPreview || _showSelectionBox || _selectionAreas.Length > 0)
            {
                if (ShowScanVolume)
                {
                    UpdateScanVolumeBuffer();
                    if (ShowScanHandles)
                    {
                        UpdateScanHandleBuffer();
                    }
                }
                if (ShowScanDensityPreview)
                {
                    UpdateScanDensityBuffer();
                }
                if (_showSelectionBox || _selectionAreas.Length > 0 || _selectionFillVertexCount > 0)
                {
                    UpdateSelectionFillBuffer();
                }
                if (_scanDensityVertexCount > 0 || (ShowScanVolume && _scanVolumeVertexCount > 0) || _selectionFillVertexCount > 0)
                {
                    if (ShowScanDensityPreview && _scanDensityVertexCount > 0)
                    {
                        _gl.Enable(EnableCap.DepthTest);
                        _gl.DepthFunc(DepthFunction.Greater);
                        _gl.DepthMask(false);
                        _gl.Enable(EnableCap.Blend);
                        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                        _gl.UseProgram(_shaderProgramDensityPoints!.Handle);
                        SetUniforms(_shaderProgramDensityPoints, view, proj);
                        _gl.BindVertexArray(_vaoScanDensity);
                        var camPosPreview = _viewport.Camera.Position;
                        int camLoc = _gl.GetUniformLocation(_shaderProgramDensityPoints.Handle, "uCameraXZ");
                        int radiusLoc = _gl.GetUniformLocation(_shaderProgramDensityPoints.Handle, "uFadeRadius");
                        int bandLoc = _gl.GetUniformLocation(_shaderProgramDensityPoints.Handle, "uFadeBand");
                        int fadeEnableLoc = _gl.GetUniformLocation(_shaderProgramDensityPoints.Handle, "uEnableFade");
                        _gl.Uniform2(camLoc, camPosPreview.X, camPosPreview.Z);
                        float fadeBand = Math.Clamp(_fineDensityPreviewRadius * 0.22f, 260f, 1100f);
                        _gl.Uniform1(radiusLoc, _fineDensityPreviewRadius);
                        _gl.Uniform1(bandLoc, fadeBand);
                        int psLoc = _gl.GetUniformLocation(_shaderProgramDensityPoints.Handle, "uPointSize");
                        if (_scanDensityBroadCount > 0)
                        {
                            _gl.Uniform1(fadeEnableLoc, 0.0f);
                            _gl.Uniform1(psLoc, 3.5f);
                            _gl.DrawArrays(PrimitiveType.Points, 0, (uint)_scanDensityBroadCount);
                        }
                        int fineCount = _scanDensityVertexCount - _scanDensityBroadCount;
                        if (fineCount > 0)
                        {
                            _gl.Uniform1(fadeEnableLoc, 1.0f);
                            _gl.Uniform1(psLoc, 2.0f);
                            _gl.DrawArrays(PrimitiveType.Points, _scanDensityBroadCount, (uint)fineCount);
                        }
                        _gl.Disable(EnableCap.Blend);
                        _gl.DepthMask(true);
                    }

                    _gl.Enable(EnableCap.DepthTest);
                    _gl.DepthFunc(DepthFunction.Greater);

                    if (ShowScanVolume && ShowScanHandles && _scanHandleVertexCount > 0)
                    {
                        _gl.DepthMask(true);
                        _gl.Disable(EnableCap.Blend);
                        _gl.Disable(EnableCap.CullFace);
                        _gl.UseProgram(_shaderProgramGizmoSolid!.Handle);
                        SetUniforms(_shaderProgramGizmoSolid, view, proj);
                        _gl.BindVertexArray(_vaoScanHandles);
                        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_scanHandleVertexCount);
                    }

                    if (ShowScanVolume && _scanVolumeVertexCount > 0)
                    {
                        _gl.DepthMask(false);
                        _gl.UseProgram(_shaderProgramAxes!.Handle);
                        SetUniforms(_shaderProgramAxes, view, proj);
                        _gl.BindVertexArray(_vaoScanVolume);
                        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_scanVolumeVertexCount);
                        _gl.DepthMask(true);
                    }
                }
            }

            _gl.BindVertexArray(0);

            bool hasMissRays = _viewport.ShowMissRays && _missRayCount > 0;
            bool hasNormalRays = _viewport.ShowNormalRays && _pointCount > 0;
            bool hasRays = hasMissRays || hasNormalRays;
            bool hasSelectionFill = _selectionFillVertexCount > 0;
            bool hasScanHandlePlanes = ShowScanVolume && ShowScanHandles && _scanHandleVertexCount > 0;
            bool hasWboit = hasRays || _viewport.ShowGrid || hasSelectionFill || hasScanHandlePlanes;

            if (hasWboit)
            {
                var camPos = _viewport.Camera.Position;
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Greater);
                _gl.DepthMask(false);
                _gl.Enable(EnableCap.Blend);

                // OIT pass A (accumulation) in dedicated MSAA FBO
                _framebufferManager.BindMsaaAccumFramebuffer();
                _gl.ClearColor(0f, 0f, 0f, 0f);
                _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);

                if (_viewport.ShowGrid)
                {
                    _gl.UseProgram(_shaderProgramGridAccum!.Handle);
                    SetGridUniforms(_shaderProgramGridAccum.Handle, view, proj, camPos);
                    _gl.BindVertexArray(_vaoGrid);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }

                if (hasRays)
                {
                    _gl.UseProgram(_shaderProgramRayAccum!.Handle);
                    SetUniforms(_shaderProgramRayAccum, view, proj);
                    int timeLocAccum = _gl.GetUniformLocation(_shaderProgramRayAccum.Handle, "uCurrentTime");
                    _gl.Uniform1(timeLocAccum, currentTime);
                    _gl.Uniform3(_gl.GetUniformLocation(_shaderProgramRayAccum.Handle, "uCameraPos"), camPos.X, camPos.Y, camPos.Z);
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

                if (hasSelectionFill)
                {
                    _gl.UseProgram(_shaderProgramFlatAccum!.Handle);
                    SetUniforms(_shaderProgramFlatAccum, view, proj);
                    int colorLocAccum = _gl.GetUniformLocation(_shaderProgramFlatAccum.Handle, "uColor");
                    _gl.Uniform4(colorLocAccum, 0.88f, 0.42f, 1.0f, 0.22f);
                    _gl.Disable(EnableCap.CullFace);
                    _gl.BindVertexArray(_vaoSelectionFill);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_selectionFillVertexCount);
                }

                if (hasScanHandlePlanes)
                {
                    _gl.UseProgram(_shaderProgramGizmoAccum!.Handle);
                    SetUniforms(_shaderProgramGizmoAccum, view, proj);
                    _gl.Disable(EnableCap.CullFace);
                    _gl.BindVertexArray(_vaoScanHandles);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_scanHandleVertexCount);
                }

                // OIT pass B (revealage) in dedicated MSAA FBO
                _framebufferManager.BindMsaaRevealFramebuffer();
                _gl.ClearColor(1f, 1f, 1f, 1f);
                _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                _gl.BlendFunc(BlendingFactor.Zero, BlendingFactor.OneMinusSrcAlpha);

                if (_viewport.ShowGrid)
                {
                    _gl.UseProgram(_shaderProgramGridReveal!.Handle);
                    SetGridUniforms(_shaderProgramGridReveal.Handle, view, proj, camPos);
                    _gl.BindVertexArray(_vaoGrid);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }

                if (hasRays)
                {
                    _gl.UseProgram(_shaderProgramRayReveal!.Handle);
                    SetUniforms(_shaderProgramRayReveal, view, proj);
                    int timeLocReveal = _gl.GetUniformLocation(_shaderProgramRayReveal.Handle, "uCurrentTime");
                    _gl.Uniform1(timeLocReveal, currentTime);
                    _gl.Uniform3(_gl.GetUniformLocation(_shaderProgramRayReveal.Handle, "uCameraPos"), camPos.X, camPos.Y, camPos.Z);
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

                if (hasSelectionFill)
                {
                    _gl.UseProgram(_shaderProgramFlatReveal!.Handle);
                    SetUniforms(_shaderProgramFlatReveal, view, proj);
                    int colorLocReveal = _gl.GetUniformLocation(_shaderProgramFlatReveal.Handle, "uColor");
                    _gl.Uniform4(colorLocReveal, 0.88f, 0.42f, 1.0f, 0.22f);
                    _gl.Disable(EnableCap.CullFace);
                    _gl.BindVertexArray(_vaoSelectionFill);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_selectionFillVertexCount);
                }

                if (hasScanHandlePlanes)
                {
                    _gl.UseProgram(_shaderProgramGizmoReveal!.Handle);
                    SetUniforms(_shaderProgramGizmoReveal, view, proj);
                    _gl.Disable(EnableCap.CullFace);
                    _gl.BindVertexArray(_vaoScanHandles);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_scanHandleVertexCount);
                }

                _gl.Disable(EnableCap.Blend);
                _gl.DepthMask(true);

                // Resolve MSAA OIT attachments to single-sample OIT textures.
                _framebufferManager.ResolveBuffers();
            }
            else
            {
                // Resolve MSAA opaque color/depth to resolve textures
                _framebufferManager.ResolveBuffers();
            }

            if (hasWboit)
            {

                // Composite opaque + transparent into swapchain framebuffer.
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                _gl.Viewport(0, 0, (uint)width, (uint)height);
                _gl.Disable(EnableCap.DepthTest);

                _gl.UseProgram(_shaderProgramComposite!.Handle);
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, _framebufferManager.ResolveColorTexture);
                _gl.Uniform1(_gl.GetUniformLocation(_shaderProgramComposite.Handle, "uOpaqueColor"), 0);

                _gl.ActiveTexture(TextureUnit.Texture1);
                _gl.BindTexture(TextureTarget.Texture2D, _framebufferManager.OitAccumTexture);
                _gl.Uniform1(_gl.GetUniformLocation(_shaderProgramComposite.Handle, "uAccumColor"), 1);

                _gl.ActiveTexture(TextureUnit.Texture2);
                _gl.BindTexture(TextureTarget.Texture2D, _framebufferManager.OitRevealTexture);
                _gl.Uniform1(_gl.GetUniformLocation(_shaderProgramComposite.Handle, "uRevealColor"), 2);

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
                _framebufferManager.BlitToFramebuffer((uint)fb);
            }
        }

        private unsafe void UpdateScanVolumeBuffer()
        {
            var s = _scanVolume.Sanitize();
            float[] data = ScanVolumeGeometryBuilder.BuildScanVolumeLineVertices(
                s,
                _hoverScanHandle,
                _activeScanHandle,
                ShowScanHandles,
                _showSelectionBox,
                _selectionStartWorld,
                _selectionEndWorld,
                _selectionYBottom,
                _selectionYTop);
            _scanVolumeVertexCount = data.Length / 6;

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboScanVolume);
            fixed (float* v = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), v, BufferUsageARB.DynamicDraw);
            }
        }

        private unsafe void UpdateSelectionFillBuffer()
        {
            _selectionFillVertexCount = 0;
            if (!_showSelectionBox && _selectionAreas.Length == 0)
            {
                return;
            }

            var verts = new System.Collections.Generic.List<float>((_selectionAreas.Length + (_showSelectionBox ? 1 : 0)) * 18);
            float y = _selectionAreasPlaneY + 0.05f;

            void AddArea(float minX, float maxX, float minZ, float maxZ)
            {
                if ((maxX - minX) < 0.001f || (maxZ - minZ) < 0.001f)
                {
                    return;
                }

                verts.Add(minX); verts.Add(y); verts.Add(minZ);
                verts.Add(maxX); verts.Add(y); verts.Add(minZ);
                verts.Add(maxX); verts.Add(y); verts.Add(maxZ);
                verts.Add(minX); verts.Add(y); verts.Add(minZ);
                verts.Add(maxX); verts.Add(y); verts.Add(maxZ);
                verts.Add(minX); verts.Add(y); verts.Add(maxZ);
            }

            for (int i = 0; i < _selectionAreas.Length; i++)
            {
                var a = _selectionAreas[i];
                AddArea(a.X, a.Y, a.Z, a.W);
            }

            if (_showSelectionBox)
            {
                float minX = MathF.Min(_selectionStartWorld.X, _selectionEndWorld.X);
                float maxX = MathF.Max(_selectionStartWorld.X, _selectionEndWorld.X);
                float minZ = MathF.Min(_selectionStartWorld.Z, _selectionEndWorld.Z);
                float maxZ = MathF.Max(_selectionStartWorld.Z, _selectionEndWorld.Z);
                AddArea(minX, maxX, minZ, maxZ);
            }

            if (verts.Count == 0)
            {
                return;
            }

            _selectionFillVertexCount = verts.Count / 3;
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboSelectionFill);
            var data = verts.ToArray();
            fixed (float* v = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), v, BufferUsageARB.DynamicDraw);
            }
        }

        private unsafe void UpdateScanHandleBuffer()
        {
            var s = _scanVolume.Sanitize();
            float[] data = ScanVolumeGeometryBuilder.BuildScanHandleSolidVertices(s, _hoverScanHandle, _activeScanHandle, _viewport.Camera.Position);
            _scanHandleVertexCount = data.Length / 10;

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboScanHandles);
            fixed (float* v = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), v, BufferUsageARB.DynamicDraw);
            }
        }

        private unsafe void UpdateScanDensityBuffer()
        {
            var s = _scanVolume.Sanitize();
            if (!ShouldRebuildScanDensity(s))
            {
                return;
            }

            var density = ScanVolumeGeometryBuilder.BuildScanDensityVertices(s, GridPlaneY, ScanFineTargetStep, _viewport.Camera.Position, ref _fineDensityPreviewRadius);
            float[] data = density.Vertices;
            _scanDensityVertexCount = data.Length / 6;
            _scanDensityBroadCount = density.BroadCount;

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboScanDensity);
            fixed (float* v = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), v, BufferUsageARB.DynamicDraw);
            }

            _scanDensityBufferValid = true;
            _lastDensityScanVolume = s;
            _lastDensityFineTargetStep = ScanFineTargetStep;
            _lastDensityGridPlaneY = GridPlaneY;
            _lastDensityCameraPos = _viewport.Camera.Position;
        }

        private bool ShouldRebuildScanDensity(ScanVolumeSettings s)
        {
            if (!_scanDensityBufferValid)
            {
                return true;
            }

            if (!_lastDensityScanVolume.Equals(s))
            {
                return true;
            }

            if (MathF.Abs(_lastDensityFineTargetStep - ScanFineTargetStep) > 0.01f)
            {
                return true;
            }

            if (MathF.Abs(_lastDensityGridPlaneY - GridPlaneY) > 0.01f)
            {
                return true;
            }

            var cam = _viewport.Camera.Position;
            float dx = cam.X - _lastDensityCameraPos.X;
            float dz = cam.Z - _lastDensityCameraPos.Z;
            return (dx * dx) + (dz * dz) >= (ScanDensityRebuildMoveThreshold * ScanDensityRebuildMoveThreshold);
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
