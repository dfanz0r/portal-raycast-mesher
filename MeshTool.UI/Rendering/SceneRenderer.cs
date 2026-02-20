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
        private uint _shaderProgramPoints, _shaderProgramSurfels, _shaderProgramRays;
        private uint _vaoRays, _vboRays;

        private int _pointCount;
        private int _rayCount; // Number of lines (2 verts each)
        private int _pointCapacity;
        private int _rayCapacity; // Number of lines capacity
        private float _avgDistance = 1.0f;

        private int _surfelVertexCount;

        private uint _msaaFbo;
        private uint _msaaColor;
        private uint _msaaDepth;
        private uint _resolveFbo;
        private uint _resolveColor;
        private uint _resolveDepth;
        private int _msaaWidth;
        private int _msaaHeight;
        private int _msaaSamples = 4;
        private bool _msaaSupported = true;

        public SceneRenderer(GlInterface glInterface, OpenGlViewport viewport)
        {
            _viewport = viewport;
            _gl = GL.GetApi(glInterface.GetProcAddress);
        }

        public unsafe void Init()
        {
            _viewport.OnLog?.Invoke($"[GL] Version: {_gl.GetStringS(StringName.Version)}");
            _viewport.OnLog?.Invoke($"[GL] Renderer: {_gl.GetStringS(StringName.Renderer)}");
            _gl.Enable(EnableCap.DepthTest);

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
                    _msaaSupported = false;
                    _viewport.OnLog?.Invoke($"[GL] MSAA not supported (MaxSamples = {maxSamples}).");
                }
            }
            catch (Exception ex)
            {
                _msaaSupported = false;
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
                    Normal = norm;
                    
                    float age = uCurrentTime - iSpawnTime;
                    if (iSpawnTime <= 0.0 || age > 5.0 || age < 0.0) {
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

            // --- RAY/NORMAL SHADER ---
            string vsRay = @"#version 300 es
                precision highp float;
                layout (location = 0) in vec3 aPos;
                layout (location = 1) in vec3 aBaseColor;
                layout (location = 2) in float aSpawnTime;
                uniform mat4 uView;
                uniform mat4 uProjection;
                uniform float uCurrentTime;
                out vec3 Color;
                void main() {
                    gl_Position = uProjection * uView * vec4(aPos, 1.0);
                    
                    float age = uCurrentTime - aSpawnTime;
                    if (aSpawnTime <= 0.0 || age > 5.0 || age < 0.0) {
                        Color = aBaseColor;
                    } else {
                        float t = age / 5.0;
                        // If it's a miss ray (base color is red), fade from white to red
                        if (aBaseColor.r > 0.5 && aBaseColor.g < 0.5) {
                            Color = mix(vec3(1.0, 1.0, 1.0), aBaseColor, t);
                        } else {
                            Color = aBaseColor; // Normals stay yellow
                        }
                    }
                }";
            string fsRay = @"#version 300 es
                precision highp float;
                in vec3 Color;
                out vec4 FragColor;
                void main() {
                    FragColor = vec4(Color, 1.0);
                }";
            _shaderProgramRays = CreateProgram(vsRay, fsRay);
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

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
        }

        public void Deinit()
        {
            if (_msaaFbo != 0) _gl.DeleteFramebuffer(_msaaFbo);
            if (_msaaColor != 0) _gl.DeleteRenderbuffer(_msaaColor);
            if (_msaaDepth != 0) _gl.DeleteRenderbuffer(_msaaDepth);

            if (_resolveFbo != 0) _gl.DeleteFramebuffer(_resolveFbo);
            if (_resolveColor != 0) _gl.DeleteTexture(_resolveColor);
            if (_resolveDepth != 0) _gl.DeleteTexture(_resolveDepth);

            _gl.DeleteVertexArray(_vaoPoints);
            _gl.DeleteVertexArray(_vaoSurfels);
            _gl.DeleteVertexArray(_vaoRays);
            _gl.DeleteBuffer(_vboInstances);
            _gl.DeleteBuffer(_vboSurfelVerts);
            _gl.DeleteBuffer(_vboRays);
            _gl.DeleteProgram(_shaderProgramPoints);
            _gl.DeleteProgram(_shaderProgramSurfels);
            _gl.DeleteProgram(_shaderProgramRays);
            _gl.Dispose();
        }

        private Vertex[]? _pendingPoints;
        private TerrainTool.Data.Ray[]? _pendingRays;
        private System.Collections.Generic.List<Vertex> _pendingAppendPointsList = new System.Collections.Generic.List<Vertex>();
        private System.Collections.Generic.List<TerrainTool.Data.Ray> _pendingAppendRaysList = new System.Collections.Generic.List<TerrainTool.Data.Ray>();
        private float _pendingAvgDistance;
        private bool _dataDirty = false;
        private bool _appendDirty = false;

        public unsafe void UpdateData(Vertex[] points, TerrainTool.Data.Ray[] rays, float avgDistance)
        {
            _pendingPoints = points;
            _pendingRays = rays;
            _pendingAvgDistance = avgDistance;
            _dataDirty = true;
            _appendDirty = false; // Override any pending appends
            _pendingAppendPointsList.Clear();
            _pendingAppendRaysList.Clear();
        }

        public unsafe void AppendData(Vertex[]? newPoints, TerrainTool.Data.Ray[]? newMisses, float avgDistance)
        {
            if (_dataDirty) return; // If a full update is pending, ignore appends

            if (newPoints != null) _pendingAppendPointsList.AddRange(newPoints);
            if (newMisses != null) _pendingAppendRaysList.AddRange(newMisses);
            _pendingAvgDistance = avgDistance;
            _appendDirty = true;
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
                _rayCount = rays.Length + _pointCount;

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
                if (_msaaColor != 0) _gl.DeleteRenderbuffer(_msaaColor);
                if (_msaaDepth != 0) _gl.DeleteRenderbuffer(_msaaDepth);

                if (_resolveFbo != 0) _gl.DeleteFramebuffer(_resolveFbo);
                if (_resolveColor != 0) _gl.DeleteTexture(_resolveColor);
                if (_resolveDepth != 0) _gl.DeleteTexture(_resolveDepth);

                _msaaWidth = width;
                _msaaHeight = height;

                // 1. MSAA FBO (Renderbuffers)
                _msaaFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

                _msaaColor = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColor);
                _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.Rgba8, (uint)width, (uint)height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaColor);

                _msaaDepth = _gl.GenRenderbuffer();
                _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepth);
                _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.DepthComponent24, (uint)width, (uint)height);
                _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);

                var status1 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status1 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] MSAA FBO incomplete: {status1}");

                // 2. Resolve FBO (Textures)
                _resolveFbo = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFbo);

                _resolveColor = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _resolveColor);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _resolveColor, 0);

                _resolveDepth = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _resolveDepth);
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24, (uint)width, (uint)height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _resolveDepth, 0);

                var status2 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status2 != GLEnum.FramebufferComplete) _viewport.OnLog?.Invoke($"[GL ERROR] Resolve FBO incomplete: {status2}");
            }

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
            _gl.Viewport(0, 0, (uint)width, (uint)height);
            _gl.ClearColor(0.15f, 0.15f, 0.15f, 1.0f);
            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

            if (_pointCount == 0 && _rayCount == 0)
            {
                _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
                _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
                _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                return;
            }

            var view = _viewport.Camera.GetViewMatrix();
            var proj = _viewport.Camera.GetProjectionMatrix((float)width, (float)height);
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

                _gl.BindVertexArray(_vaoSurfels);
                _gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, (uint)_surfelVertexCount, (uint)_pointCount);
            }

            // 3. Draw Rays & Normals
            if (_viewport.ShowRays && _rayCount > 0)
            {
                _gl.UseProgram(_shaderProgramRays);
                SetUniforms(_shaderProgramRays, view, proj);

                int timeLoc = _gl.GetUniformLocation(_shaderProgramRays, "uCurrentTime");
                _gl.Uniform1(timeLoc, currentTime);

                _gl.BindVertexArray(_vaoRays);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(_rayCount * 2));
            }

            _gl.BindVertexArray(0);

            // Resolve MSAA to Resolve FBO
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFbo);
            _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);

            // Read depth at mouse position to find hovered coordinate from the Resolve FBO
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _resolveFbo);
            int mx = (int)_viewport.LastMousePosition.X;
            int my = (int)(height - _viewport.LastMousePosition.Y); // OpenGL Y is inverted

            if (mx >= 0 && mx < width && my >= 0 && my < height)
            {
                float depth;
                _gl.ReadPixels(mx, my, 1, 1, PixelFormat.DepthComponent, PixelType.Float, &depth);

                if (depth < 1.0f) // 1.0 is clear depth
                {
                    // Unproject
                    var ndc = new Vector3D<float>((mx / (float)width) * 2.0f - 1.0f,
                                                  (my / (float)height) * 2.0f - 1.0f,
                                                  depth * 2.0f - 1.0f);

                    Matrix4X4.Invert(view * proj, out var invViewProj);
                    var worldPos4 = Vector4D.Transform(new Vector4D<float>(ndc.X, ndc.Y, ndc.Z, 1.0f), invViewProj);
                    var worldPos = new Vector3D<float>(worldPos4.X / worldPos4.W, worldPos4.Y / worldPos4.W, worldPos4.Z / worldPos4.W);

                    _viewport.UpdateHoveredCoordinate(worldPos);
                }
                else
                {
                    _viewport.UpdateHoveredCoordinate(null);
                }
            }
            else
            {
                _viewport.UpdateHoveredCoordinate(null);
            }

            // Blit Resolve FBO to Default FBO
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _resolveFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)fb);
            _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        }

        private unsafe void SetUniforms(uint program, Matrix4X4<float> view, Matrix4X4<float> proj)
        {
            int viewLoc = _gl.GetUniformLocation(program, "uView");
            _gl.UniformMatrix4(viewLoc, 1, false, (float*)&view);

            int projLoc = _gl.GetUniformLocation(program, "uProjection");
            _gl.UniformMatrix4(projLoc, 1, false, (float*)&proj);
        }
    }
}
