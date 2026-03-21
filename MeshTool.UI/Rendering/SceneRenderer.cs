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
        private const int AxesVertexStride = 6 * sizeof(float);
        private float _fineDensityPreviewRadius = 3200f;
        private GL _gl;
        private OpenGlViewport _viewport;
        private ShaderProgram? _shaderProgramAxes;
        private uint _vaoRays;
        private uint _vaoAxes, _vboAxes;
        private VertexArray? _axesBuffer;
        private PointRenderer? _pointRenderer;
        private RayRenderer? _rayRenderer;
        private GridRenderer? _gridRenderer;
        private MeshRenderer? _meshRenderer;
        private VolumeRenderer? _volumeRenderer;
        private int _meshVertexCount;

        private int _pointCapacity;
        private int _rayCapacity; // Number of lines capacity
        private float _avgDistance = 1.0f;

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
            _pointRenderer = new PointRenderer(_gl, _viewport.OnLog);
            _gridRenderer = new GridRenderer(_gl, _viewport.OnLog);
            _rayRenderer = new RayRenderer(_gl, _viewport.OnLog);
            _meshRenderer = new MeshRenderer(_gl, _viewport.OnLog);
            _volumeRenderer = new VolumeRenderer(_gl, _viewport, _viewport.OnLog);

            InitShaders();
            InitBuffers();
        }

        private unsafe void InitShaders()
        {
            // --- AXES SHADER ---
            string vsAxes = ShaderSource.AxesVertex;
            string fsAxes = ShaderSource.AxesFragment;
            _shaderProgramAxes = new ShaderProgram(_gl, "Axes", vsAxes, fsAxes, _viewport.OnLog);

        }

        private unsafe void InitBuffers()
        {
            // Rays VAO
            _vaoRays = _gl.GenVertexArray();
            _rayRenderer!.ConfigureVao(_vaoRays);

            // Axes VAO
            _axesBuffer = new VertexArray(_gl, AxesVertexStride);
            _vaoAxes = _axesBuffer.VaoHandle;
            _vboAxes = _axesBuffer.VboHandle;
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
            _axesBuffer.UploadData(axesVerts, 6, BufferUsageARB.StaticDraw);
            _axesBuffer.SetAttribute(0, 3, VertexAttribPointerType.Float, 0);
            _axesBuffer.SetAttribute(1, 3, VertexAttribPointerType.Float, 3 * sizeof(float));

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _gl.BindVertexArray(0);
        }

        public void Deinit()
        {
            _framebufferManager?.Dispose();
            _pointRenderer?.Dispose();
            _gridRenderer?.Dispose();
            _rayRenderer?.Dispose();
            _meshRenderer?.Dispose();
            _volumeRenderer?.Dispose();
            _axesBuffer?.Dispose();

            _gl.DeleteVertexArray(_vaoRays);
            _vaoAxes = 0;
            _vboAxes = 0;
            _shaderProgramAxes?.Dispose();
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
                        _meshRenderer!.UploadRaw(_pendingMeshRawBuffer, _meshVertexCount);
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
                    _pointRenderer!.EnsureCapacity(_pointCapacity);
                }

                if (_pointCount > 0)
                {
                    _pointRenderer!.UploadPoints(points, _pointCount, _selectedPointIndices, ref _minPointY, ref _maxPointY);
                }

                if (_rayCount > _rayCapacity)
                {
                    _rayCapacity = Math.Max(_rayCapacity * 2, _rayCount + 10000);
                    if (_rayRenderer!.EnsureCapacity(_rayCapacity))
                    {
                        _rayRenderer.ConfigureVao(_vaoRays);
                    }
                }

                if (_rayCount > 0)
                {
                    _rayRenderer!.UploadFull(points, rays, _pointCount, _rayCount);
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
                        _pointRenderer!.EnsureCapacity(newCapacity);
                        _pointCapacity = newCapacity;
                    }

                    _pointRenderer!.AppendPoints(newPoints, _pointCount, _selectedPointIndices, ref _minPointY, ref _maxPointY);
                    _pointCount = newPointCount;
                    _allPoints.AddRange(newPoints);
                }

                if (addedRays > 0)
                {
                    int newRayCount = oldRayCount + addedRays;
                    if (newRayCount > _rayCapacity)
                    {
                        int newCapacity = Math.Max(_rayCapacity * 2, newRayCount + 10000);
                        if (_rayRenderer!.EnsureCapacity(newCapacity))
                        {
                            _rayRenderer.ConfigureVao(_vaoRays);
                        }
                        _rayCapacity = newCapacity;
                    }

                    if (addedMisses > 0 && oldPointCount > 0)
                    {
                        _rayRenderer!.ShiftExistingNormalRays(oldMissRayCount, oldPointCount, addedMisses);
                    }

                    if (addedMisses > 0)
                    {
                        _rayRenderer!.UploadMissRays(newMisses, oldMissRayCount);
                    }

                    if (addedPoints > 0)
                    {
                        int oldNormalCount = oldPointCount;
                        int normalInsertRayIndex = oldMissRayCount + addedMisses + oldNormalCount;
                        _rayRenderer!.UploadNormalRays(newPoints, normalInsertRayIndex);
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
                    _pointRenderer!.UploadSelectionState(_allPoints, _pointCount, _selectedPointIndices);
                }
            }

            if (doMesh)
            {
                if (pendingMesh != null)
                {
                    _meshRenderer!.UploadTriangles(pendingMesh, out _meshVertexCount);
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
                _pointRenderer!.RenderPoints(view, proj, _pointCount, UseDynamicColorMapping, _minPointY, _maxPointY);
            }

            // 2. Draw Surfels
            if (_viewport.ShowSurfels && _pointCount > 0)
            {
                _pointRenderer!.RenderSurfels(view, proj, _pointCount, _avgDistance, _viewport.SurfelScale, currentTime, HoveredCoordinate, UseDynamicColorMapping, _minPointY, _maxPointY);
            }

            // 3. Draw Mesh
            if (_viewport.ShowMesh && _meshVertexCount > 0)
            {
                _meshRenderer!.Render(view, proj, _meshVertexCount);
            }

            // 4. Draw Axes
            if (_viewport.ShowGrid)
            {
                _gl.UseProgram(_shaderProgramAxes!.Handle);
                _shaderProgramAxes.SetViewProjection(view, proj);

                _gl.BindVertexArray(_vaoAxes);
                _gl.DrawArrays(PrimitiveType.Lines, 0, 6);
            }

            if (ShowScanVolume || ShowScanDensityPreview || _showSelectionBox || _selectionAreas.Length > 0)
            {
                if (ShowScanVolume)
                {
                    _volumeRenderer!.UpdateScanVolumeBuffer(_scanVolume, _hoverScanHandle, _activeScanHandle, ShowScanHandles, _showSelectionBox, _selectionStartWorld, _selectionEndWorld, _selectionYBottom, _selectionYTop);
                    if (ShowScanHandles)
                    {
                        _volumeRenderer.UpdateScanHandleBuffer(_scanVolume, _hoverScanHandle, _activeScanHandle);
                    }
                }
                if (ShowScanDensityPreview)
                {
                    _volumeRenderer!.UpdateScanDensityBuffer(_scanVolume, GridPlaneY, ScanFineTargetStep, ref _fineDensityPreviewRadius);
                }
                if (_showSelectionBox || _selectionAreas.Length > 0 || _volumeRenderer!.SelectionFillVertexCount > 0)
                {
                    _volumeRenderer!.UpdateSelectionFillBuffer(_showSelectionBox, _selectionAreas, _selectionAreasPlaneY, _selectionStartWorld, _selectionEndWorld);
                }
                if (_volumeRenderer.ScanDensityVertexCount > 0 || (ShowScanVolume && _volumeRenderer.ScanVolumeVertexCount > 0) || _volumeRenderer.SelectionFillVertexCount > 0)
                {
                    _volumeRenderer.RenderOpaque(view, proj, ShowScanDensityPreview, ShowScanVolume, ShowScanHandles, _fineDensityPreviewRadius);
                }
            }

            _gl.BindVertexArray(0);

            bool hasMissRays = _viewport.ShowMissRays && _missRayCount > 0;
            bool hasNormalRays = _viewport.ShowNormalRays && _pointCount > 0;
            bool hasRays = hasMissRays || hasNormalRays;
            bool hasSelectionFill = _volumeRenderer!.SelectionFillVertexCount > 0;
            bool hasScanHandlePlanes = ShowScanVolume && ShowScanHandles && _volumeRenderer.ScanHandleVertexCount > 0;
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
                    _gridRenderer!.RenderAccum(view, proj, camPos, GridPlaneY);
                }

                if (hasRays)
                {
                    _rayRenderer!.RenderAccum(view, proj, camPos, currentTime, _vaoRays, _missRayCount, _pointCount, hasMissRays, hasNormalRays);
                }

                _volumeRenderer.RenderAccum(view, proj, hasSelectionFill, hasScanHandlePlanes);

                // OIT pass B (revealage) in dedicated MSAA FBO
                _framebufferManager.BindMsaaRevealFramebuffer();
                _gl.ClearColor(1f, 1f, 1f, 1f);
                _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                _gl.BlendFunc(BlendingFactor.Zero, BlendingFactor.OneMinusSrcAlpha);

                if (_viewport.ShowGrid)
                {
                    _gridRenderer!.RenderReveal(view, proj, camPos, GridPlaneY);
                }

                if (hasRays)
                {
                    _rayRenderer!.RenderReveal(view, proj, camPos, currentTime, _vaoRays, _missRayCount, _pointCount, hasMissRays, hasNormalRays);
                }

                _volumeRenderer.RenderReveal(view, proj, hasSelectionFill, hasScanHandlePlanes);

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

                _gridRenderer!.RenderComposite(
                    _framebufferManager.ResolveColorTexture,
                    _framebufferManager.OitAccumTexture,
                    _framebufferManager.OitRevealTexture);
                _gl.Enable(EnableCap.DepthTest);
            }
            else
            {
                // Blit Resolve FBO to Default FBO.
                _framebufferManager.BlitToFramebuffer((uint)fb);
            }
        }

    }
}
