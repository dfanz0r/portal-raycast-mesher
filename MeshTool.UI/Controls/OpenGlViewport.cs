using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using MeshTool.UI.Rendering;
using MeshTool.UI.Models;

namespace MeshTool.UI.Controls
{
    public class OpenGlViewport : OpenGlControlBase
    {
        private enum ScanHandleKind
        {
            None,
            MoveCenter,
            SizeXPos,
            SizeXNeg,
            SizeZPos,
            SizeZNeg,
            YTop,
            YBottom,
            RotateYaw,
            MoveXAxis,
            MoveZAxis
        }

        private SceneRenderer? _renderer;
        private readonly HashSet<Key> _pressedKeys = new HashSet<Key>();
        private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
        private long _lastFrameTicks;
        private TopLevel? _topLevel;
        private Point _lastGlobalMousePos;

        public Camera Camera { get; } = new Camera();

        // Toggles & properties bound to UI
        public bool ShowPoints { get; set; } = true;
        public bool ShowSurfels { get; set; } = true;
        public bool ShowMissRays { get; set; } = false;
        public bool ShowNormalRays { get; set; } = false;
        public bool ShowMesh { get; set; } = false;
        public bool ShowGrid { get; set; } = true;
        public float SurfelScale { get; set; } = 1.0f;
        public bool ShowScanVolume { get; set; } = true;
        public bool ShowScanHandles { get; set; } = true;
        public bool ScanVolumeEditEnabled { get; set; } = true;
        public bool ShowScanDensityPreview { get; set; } = true;
        public float ScanFineTargetStep { get; set; } = 24f;
        public bool UseDynamicColorMapping { get; set; } = false;
        public bool PointSelectionModeEnabled { get; set; } = false;
        public ScanVolumeSettings ScanVolume { get; private set; } = ScanVolumeSettings.Default;
        public int HoverScanHandleId => (int)_hoverScanHandle;
        public int ActiveScanHandleId => (int)_activeScanHandle;
        public int SelectedPointCount => _selectedPointIndices.Count;
        public Action<string>? OnLog { get; set; }
        public Action<Vector3D<float>?>? OnHoveredCoordinateChanged { get; set; }
        public Action<ScanVolumeSettings>? OnScanVolumeChanged { get; set; }
        public Action<int>? OnSelectionCountChanged { get; set; }
        public Action? OnDeleteSelectionRequested { get; set; }
        public Action? OnToggleSelectionModeRequested { get; set; }

        public Action<float>? OnMoveSpeedChanged { get; set; }

        public Point LastMousePosition => _lastMousePos;

        public OpenGlViewport()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        protected override void OnOpenGlInit(GlInterface gl)
        {
            base.OnOpenGlInit(gl);
            try
            {
                _renderer = new SceneRenderer(gl, this);
                _renderer.Init();
                _renderer.GridPlaneY = _gridPlaneY;
                _renderer.ShowScanVolume = ShowScanVolume;
                _renderer.ShowScanHandles = ShowScanHandles;
                _renderer.ShowScanDensityPreview = ShowScanDensityPreview;
                _renderer.ScanFineTargetStep = ScanFineTargetStep;
                _renderer.UpdateScanVolume(ScanVolume);
                _renderer.UpdateScanHandleState(HoverScanHandleId, ActiveScanHandleId);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[GL ERROR] {ex.Message}");
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel != null)
            {
                _topLevel.KeyDown += OnTopLevelKeyDown;
                _topLevel.KeyUp += OnTopLevelKeyUp;
                _topLevel.PointerPressed += OnTopLevelPointerPressed;
                _topLevel.PointerMoved += OnTopLevelPointerMoved;
                _topLevel.PointerReleased += OnTopLevelPointerReleased;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_topLevel != null)
            {
                _topLevel.KeyDown -= OnTopLevelKeyDown;
                _topLevel.KeyUp -= OnTopLevelKeyUp;
                _topLevel.PointerPressed -= OnTopLevelPointerPressed;
                _topLevel.PointerMoved -= OnTopLevelPointerMoved;
                _topLevel.PointerReleased -= OnTopLevelPointerReleased;
                _topLevel = null;
            }

            _pressedKeys.Clear();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            _renderer?.Deinit();
            base.OnOpenGlDeinit(gl);
        }

        private bool _hoverDirty = true;
        private bool _initialScanCameraFramed = false;
        private readonly HashSet<int> _selectedPointIndices = new HashSet<int>();
        private readonly List<Vector4D<float>> _selectionAreas = new List<Vector4D<float>>();
        private bool _isPointSelecting = false;
        private bool _selectionWorldValid = false;
        private Vector3D<float> _selectionStartWorld;
        private Vector3D<float> _selectionEndWorld;
        private Vector3D<float> _selectionEndWorldTarget;
        private bool _selectionHasTarget = false;

        public void ClearPointSelection()
        {
            _isPointSelecting = false;
            _selectionWorldValid = false;
            _selectionHasTarget = false;
            _selectedPointIndices.Clear();
            _selectionAreas.Clear();
            _renderer?.UpdateSelectionAreas(Array.Empty<Vector4D<float>>(), _gridPlaneY);
            _renderer?.UpdateSelectedPointIndices(Array.Empty<int>());
            OnSelectionCountChanged?.Invoke(0);
            Invalidate();
        }

        public (double X, double Y, double Z)[] GetSelectedPointPositions()
        {
            if (_selectedPointIndices.Count == 0) return Array.Empty<(double X, double Y, double Z)>();

            var list = new List<(double X, double Y, double Z)>(_selectedPointIndices.Count);
            foreach (int idx in _selectedPointIndices)
            {
                if (idx >= 0 && idx < _points.Count)
                {
                    var p = _points[idx].Position;
                    list.Add((p.X, p.Y, p.Z));
                }
            }
            return list.ToArray();
        }

        public int[] GetSelectedPointIndices()
        {
            if (_selectedPointIndices.Count == 0) return Array.Empty<int>();
            var arr = new int[_selectedPointIndices.Count];
            _selectedPointIndices.CopyTo(arr);
            Array.Sort(arr);
            return arr;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
        }

        private void ApplyPointSelectionWorld(bool append)
        {
            if (!append)
            {
                _selectedPointIndices.Clear();
            }

            if (!_selectionWorldValid)
            {
                _renderer?.UpdateSelectedPointIndices(_selectedPointIndices.Count == 0 ? Array.Empty<int>() : new List<int>(_selectedPointIndices).ToArray());
                OnSelectionCountChanged?.Invoke(_selectedPointIndices.Count);
                Invalidate();
                return;
            }

            float minX = MathF.Min(_selectionStartWorld.X, _selectionEndWorld.X);
            float maxX = MathF.Max(_selectionStartWorld.X, _selectionEndWorld.X);
            float minZ = MathF.Min(_selectionStartWorld.Z, _selectionEndWorld.Z);
            float maxZ = MathF.Max(_selectionStartWorld.Z, _selectionEndWorld.Z);

            float spanX = maxX - minX;
            float spanZ = maxZ - minZ;
            bool clickPick = spanX < 0.02f && spanZ < 0.02f;

            if (clickPick)
            {
                float pickRadius = ComputeSelectionPickRadius(_lastMousePos);
                float pickRadiusSq = pickRadius * pickRadius;
                int nearestIdx = -1;
                float nearestDistSq = float.MaxValue;

                for (int i = 0; i < _points.Count; i++)
                {
                    var p = _points[i].Position;
                    float dx = (float)p.X - _selectionEndWorld.X;
                    float dz = (float)p.Z - _selectionEndWorld.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 <= pickRadiusSq && d2 < nearestDistSq)
                    {
                        nearestDistSq = d2;
                        nearestIdx = i;
                    }
                }

                if (nearestIdx >= 0)
                {
                    _selectedPointIndices.Add(nearestIdx);
                }

                _renderer?.UpdateSelectedPointIndices(_selectedPointIndices.Count == 0 ? Array.Empty<int>() : new List<int>(_selectedPointIndices).ToArray());
                OnSelectionCountChanged?.Invoke(_selectedPointIndices.Count);
                Invalidate();
                return;
            }

            for (int i = 0; i < _points.Count; i++)
            {
                var p = _points[i].Position;
                float px = (float)p.X;
                float pz = (float)p.Z;

                if (px < minX || px > maxX || pz < minZ || pz > maxZ) continue;
                _selectedPointIndices.Add(i);
            }

            _renderer?.UpdateSelectedPointIndices(_selectedPointIndices.Count == 0 ? Array.Empty<int>() : new List<int>(_selectedPointIndices).ToArray());
            OnSelectionCountChanged?.Invoke(_selectedPointIndices.Count);
            Invalidate();
        }

        private float ComputeSelectionPickRadius(Point screenPos)
        {
            if (!TryProjectPointerToPlane(_gridPlaneY, screenPos, out var p0))
            {
                return 0.5f;
            }

            var pX = screenPos + new Point(1, 0);
            var pY = screenPos + new Point(0, 1);

            float metersPerPixel = 0.0f;
            if (TryProjectPointerToPlane(_gridPlaneY, pX, out var px))
            {
                float dx = px.X - p0.X;
                float dz = px.Z - p0.Z;
                metersPerPixel = MathF.Max(metersPerPixel, MathF.Sqrt(dx * dx + dz * dz));
            }
            if (TryProjectPointerToPlane(_gridPlaneY, pY, out var py))
            {
                float dx = py.X - p0.X;
                float dz = py.Z - p0.Z;
                metersPerPixel = MathF.Max(metersPerPixel, MathF.Sqrt(dx * dx + dz * dz));
            }

            if (metersPerPixel <= 0.0f)
            {
                return 0.5f;
            }

            return Math.Clamp(metersPerPixel * 6.0f, 0.15f, 6.0f);
        }

        private void FrameCameraToScanVolume()
        {
            var s = ScanVolume.Sanitize();
            float midY = (s.YTop + s.YBottom) * 0.5f;
            float halfY = MathF.Max(10f, (s.YTop - s.YBottom) * 0.5f);
            Camera.FocusOnBounds(
                new Vector3D<float>(s.CenterX, midY, s.CenterZ),
                new Vector3D<float>(s.SizeX * 0.5f, halfY, s.SizeZ * 0.5f));
            _hoverDirty = true;
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            long now = _frameTimer.ElapsedTicks;
            float dt = _lastFrameTicks == 0 ? 0.016f : (float)(now - _lastFrameTicks) / Stopwatch.Frequency;
            if (dt > 0.1f) dt = 0.1f;
            _lastFrameTicks = now;

            if (!_initialScanCameraFramed)
            {
                FrameCameraToScanVolume();
                _initialScanCameraFramed = true;
            }

            bool cameraMoved = UpdateCameraMovement(dt);
            bool cameraLerped = Camera.UpdateLerp(dt);
            cameraMoved = cameraMoved || cameraLerped;

            if (_isPointSelecting && _selectionWorldValid && _selectionHasTarget)
            {
                _selectionEndWorld = _selectionEndWorldTarget;
            }

            if (_renderer != null)
            {
                _renderer.UseDynamicColorMapping = UseDynamicColorMapping;
                _renderer.ShowScanVolume = ShowScanVolume;
                _renderer.ShowScanHandles = ShowScanHandles;
                _renderer.ShowScanDensityPreview = ShowScanDensityPreview;
                _renderer.ScanFineTargetStep = ScanFineTargetStep;
                _renderer.UpdateScanVolume(ScanVolume);
                _renderer.UpdateScanHandleState(HoverScanHandleId, ActiveScanHandleId);
                _renderer.UpdateSelectionBox(
                    _isPointSelecting && _selectionWorldValid,
                    _selectionStartWorld,
                    _selectionEndWorld,
                    _gridPlaneY,
                    _gridPlaneY);
                _renderer.UpdateSelectionAreas(_selectionAreas.ToArray(), _gridPlaneY);
            }
            _renderer?.Render(fb, Bounds.Size);

            if (_hoverDirty)
            {
                UpdateHoveredPoint();
                _hoverDirty = false;
            }

            bool hasAnimations = _renderer != null && _renderer.HasActiveAnimations();

            if (cameraMoved || cameraLerped || hasAnimations || _pressedKeys.Count > 0 || (_isPointSelecting && _selectionHasTarget))
            {
                RequestNextFrameRendering();
            }
            else
            {
                _lastFrameTicks = 0;
            }
        }

        private void UpdateHoveredPoint()
        {
            if (_points.Count == 0)
            {
                UpdateHoveredCoordinate(null);
                return;
            }

            var ray = Camera.GetRay((float)_lastMousePos.X, (float)_lastMousePos.Y, (float)Bounds.Width, (float)Bounds.Height);

            float tMin = 0f;
            float tMax;
            if (_hasPointBounds)
            {
                if (!RayIntersectsAabb(ray.Origin, ray.Direction, _pointsMin, _pointsMax, out tMin, out tMax))
                {
                    UpdateHoveredCoordinate(null);
                    return;
                }
                tMin = MathF.Max(0f, tMin);
            }
            else
            {
                tMax = 5000f;
            }

            var candidates = new HashSet<MeshTool.Core.Data.Vertex>();
            float step = MathF.Max(_hoverCellSize * 0.75f, 0.25f);
            for (float t = tMin; t <= tMax; t += step)
            {
                var sample = ray.Origin + ray.Direction * t;
                var cell = Quantize(sample);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var key = (cell.X + dx, cell.Y + dy, cell.Z + dz);
                            if (_hoverGrid.TryGetValue(key, out var bucket))
                            {
                                for (int i = 0; i < bucket.Count; i++)
                                {
                                    candidates.Add(bucket[i]);
                                }
                            }
                        }
                    }
                }

                if (candidates.Count > 10000) break;
            }

            if (candidates.Count == 0)
            {
                UpdateHoveredCoordinate(null);
                return;
            }

            float maxAngleCos = MathF.Cos(0.01f); // ~0.57 degrees cone
            float bestDepth = float.MaxValue;
            Silk.NET.Maths.Vector3D<float>? bestPoint = null;

            foreach (var p in candidates)
            {
                var toPoint = new Silk.NET.Maths.Vector3D<float>((float)p.Position.X, (float)p.Position.Y, (float)p.Position.Z) - ray.Origin;
                float depth = Silk.NET.Maths.Vector3D.Dot(toPoint, ray.Direction);

                if (depth > 0 && depth < bestDepth)
                {
                    float distSq = toPoint.LengthSquared;
                    if (distSq < 1e-9f) continue;
                    float cosTheta = depth / MathF.Sqrt(distSq);

                    if (cosTheta > maxAngleCos)
                    {
                        bestDepth = depth;
                        bestPoint = new Silk.NET.Maths.Vector3D<float>((float)p.Position.X, (float)p.Position.Y, (float)p.Position.Z);
                    }
                }
            }

            UpdateHoveredCoordinate(bestPoint);
        }

        public void Invalidate()
        {
            RequestNextFrameRendering();
        }

        public void ReframeToScanVolume()
        {
            FrameCameraToScanVolume();
            _initialScanCameraFramed = true;
            Invalidate();
        }

        public void SetScanVolume(ScanVolumeSettings settings, bool raiseChanged = true)
        {
            ScanVolume = settings.Sanitize();
            if (raiseChanged)
            {
                OnScanVolumeChanged?.Invoke(ScanVolume);
            }
            Invalidate();
        }

        private Vector3D<float>? _hoveredCoordinate;
        private List<MeshTool.Core.Data.Vertex> _points = new List<MeshTool.Core.Data.Vertex>();
        private readonly Dictionary<(int X, int Y, int Z), List<MeshTool.Core.Data.Vertex>> _hoverGrid = new Dictionary<(int X, int Y, int Z), List<MeshTool.Core.Data.Vertex>>();
        private float _hoverCellSize = 1.0f;
        private Vector3D<float> _pointsMin;
        private Vector3D<float> _pointsMax;
        private bool _hasPointBounds = false;

        public void UpdateHoveredCoordinate(Vector3D<float>? coord)
        {
            if (_hoveredCoordinate != coord)
            {
                _hoveredCoordinate = coord;
                if (_renderer != null)
                {
                    _renderer.HoveredCoordinate = coord;
                }
                OnHoveredCoordinateChanged?.Invoke(coord);
            }
        }

        private bool _cameraInitialized = false;
        private float _gridPlaneY = 0.0f;

        public void LoadMesh(List<MeshTool.Core.Data.Triangle>? triangles)
        {
            _renderer?.UpdateMesh(triangles);
            Invalidate();
        }

        public void LoadMeshRaw(float[] buffer, int vertexCount)
        {
            _renderer?.UpdateMeshRaw(buffer, vertexCount);
            Invalidate();
        }

        public bool IsMeshUpdatePending => _renderer?.IsMeshUpdatePending ?? false;

        public void LoadData(MeshTool.Core.Data.Vertex[] points, MeshTool.Core.Data.Ray[] rays, float avgDistance, bool resetCamera = true)
        {
            _points.Clear();
            _points.AddRange(points);
            _selectedPointIndices.Clear();
            _selectionAreas.Clear();
            _renderer?.UpdateSelectionAreas(Array.Empty<Vector4D<float>>(), _gridPlaneY);
            _renderer?.UpdateSelectedPointIndices(Array.Empty<int>());
            OnSelectionCountChanged?.Invoke(0);
            RebuildHoverGrid(points, avgDistance);
            _renderer?.UpdateData(points, rays, avgDistance);

            // Keep grid anchored to world origin so it matches axis references.
            _gridPlaneY = 0.0f;
            if (_renderer != null)
            {
                _renderer.GridPlaneY = _gridPlaneY;
            }

            if ((resetCamera || !_cameraInitialized) && points.Length > 0)
            {
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                foreach (var p in points)
                {
                    if (p.Position.X < minX) minX = (float)p.Position.X;
                    if (p.Position.X > maxX) maxX = (float)p.Position.X;
                    if (p.Position.Y < minY) minY = (float)p.Position.Y;
                    if (p.Position.Y > maxY) maxY = (float)p.Position.Y;
                    if (p.Position.Z < minZ) minZ = (float)p.Position.Z;
                    if (p.Position.Z > maxZ) maxZ = (float)p.Position.Z;
                }

                var center = new Silk.NET.Maths.Vector3D<float>((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
                var extents = new Silk.NET.Maths.Vector3D<float>(maxX - minX, maxY - minY, maxZ - minZ);

                Camera.FocusOnBounds(center, extents);
                _cameraInitialized = true;
                OnMoveSpeedChanged?.Invoke(Camera.MoveSpeed);
                OnLog?.Invoke("[CAM] Free-cam controls: hold LMB/RMB + move to look, WASD/arrows move, Space/Ctrl up/down, Shift sprint, Wheel speed.");
            }

            _hoverDirty = true;
            Invalidate();
        }

        public void AppendData(MeshTool.Core.Data.Vertex[]? newPoints, MeshTool.Core.Data.Ray[]? newMisses, float avgDistance)
        {
            if (newPoints != null)
            {
                _points.AddRange(newPoints);
                AppendHoverGrid(newPoints, avgDistance);
            }
            _renderer?.AppendData(newPoints, newMisses, avgDistance);
            _hoverDirty = true;
            Invalidate();
        }

        private void RebuildHoverGrid(MeshTool.Core.Data.Vertex[] points, float avgDistance)
        {
            _hoverGrid.Clear();
            _hoverCellSize = MathF.Max(avgDistance * 2.0f, 0.25f);
            _hasPointBounds = false;

            for (int i = 0; i < points.Length; i++)
            {
                AddPointToHoverGrid(points[i]);
            }
        }

        private void AppendHoverGrid(MeshTool.Core.Data.Vertex[] points, float avgDistance)
        {
            // Keep a stable cell size for incremental appends. Changing it here would
            // invalidate previously inserted bucket keys unless we rebuild the whole grid.
            if (_hoverGrid.Count == 0)
            {
                _hoverCellSize = MathF.Max(avgDistance * 2.0f, 0.25f);
            }
            for (int i = 0; i < points.Length; i++)
            {
                AddPointToHoverGrid(points[i]);
            }
        }

        private void AddPointToHoverGrid(MeshTool.Core.Data.Vertex p)
        {
            var pos = new Vector3D<float>((float)p.Position.X, (float)p.Position.Y, (float)p.Position.Z);

            if (!_hasPointBounds)
            {
                _pointsMin = pos;
                _pointsMax = pos;
                _hasPointBounds = true;
            }
            else
            {
                _pointsMin = new Vector3D<float>(MathF.Min(_pointsMin.X, pos.X), MathF.Min(_pointsMin.Y, pos.Y), MathF.Min(_pointsMin.Z, pos.Z));
                _pointsMax = new Vector3D<float>(MathF.Max(_pointsMax.X, pos.X), MathF.Max(_pointsMax.Y, pos.Y), MathF.Max(_pointsMax.Z, pos.Z));
            }

            var key = Quantize(pos);
            if (!_hoverGrid.TryGetValue(key, out var bucket))
            {
                bucket = new List<MeshTool.Core.Data.Vertex>();
                _hoverGrid[key] = bucket;
            }
            bucket.Add(p);
        }

        private (int X, int Y, int Z) Quantize(Vector3D<float> p)
        {
            return (
                (int)MathF.Floor(p.X / _hoverCellSize),
                (int)MathF.Floor(p.Y / _hoverCellSize),
                (int)MathF.Floor(p.Z / _hoverCellSize));
        }

        private static bool RayIntersectsAabb(Vector3D<float> origin, Vector3D<float> dir, Vector3D<float> bmin, Vector3D<float> bmax, out float tmin, out float tmax)
        {
            tmin = float.NegativeInfinity;
            tmax = float.PositiveInfinity;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
                float mn = axis == 0 ? bmin.X : axis == 1 ? bmin.Y : bmin.Z;
                float mx = axis == 0 ? bmax.X : axis == 1 ? bmax.Y : bmax.Z;

                if (MathF.Abs(d) < 1e-8f)
                {
                    if (o < mn || o > mx) return false;
                    continue;
                }

                float inv = 1.0f / d;
                float t1 = (mn - o) * inv;
                float t2 = (mx - o) * inv;
                if (t1 > t2)
                {
                    float tmp = t1;
                    t1 = t2;
                    t2 = tmp;
                }

                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmax < tmin) return false;
            }

            return tmax >= 0f;
        }

        // Camera Controls
        private Point _lastMousePos;
        private bool _isLooking = false;
        private bool _isScanDragging = false;
        private bool _isScanAxisMove = false;
        private bool _isScanRotating = false;
        private bool _isScanScaling = false;
        private bool _isScanHeightAdjust = false;
        private ScanHandleKind _hoverScanHandle = ScanHandleKind.None;
        private ScanHandleKind _activeScanHandle = ScanHandleKind.None;
        private Vector3D<float> _scanDragOffset;
        private Point _scanRotateStart;
        private float _scanYawStart;
        private float _scanRotateStartAngle;
        private Vector3D<float> _scanMoveAxisWorld;
        private float _scanAxisPlaneY;
        private float _scanAxisStartCoord;
        private float _scanAxisStartCenterX;
        private float _scanAxisStartCenterZ;
        private bool _scanAxisDragReady;
        private float _scanScaleStartX;
        private float _scanScaleStartZ;
        private Point _scanScaleStartMouse;
        private float _scanHeightStartTop;
        private float _scanHeightStartBottom;

        private bool TryProjectPointerToPlane(float planeY, Point localPos, out Vector3D<float> point)
        {
            point = default;
            if (Bounds.Width <= 1 || Bounds.Height <= 1)
            {
                return false;
            }

            var ray = Camera.GetRay((float)localPos.X, (float)localPos.Y, (float)Bounds.Width, (float)Bounds.Height);
            if (MathF.Abs(ray.Direction.Y) < 1e-5f)
            {
                return false;
            }

            float t = (planeY - Camera.Position.Y) / ray.Direction.Y;
            if (t <= 0)
            {
                return false;
            }

            point = Camera.Position + ray.Direction * t;
            return true;
        }

        private Point ClampPointerToViewport(Point p)
        {
            return new Point(
                Math.Clamp(p.X, 0.0, Math.Max(0.0, Bounds.Width - 1.0)),
                Math.Clamp(p.Y, 0.0, Math.Max(0.0, Bounds.Height - 1.0)));
        }

        private void BeginPointSelection(Point local)
        {
            var p = ClampPointerToViewport(local);
            _lastMousePos = p;
            _isPointSelecting = true;
            _selectionWorldValid = TryProjectPointerToPlane(_gridPlaneY, p, out _selectionStartWorld);
            _selectionEndWorld = _selectionStartWorld;
            _selectionEndWorldTarget = _selectionStartWorld;
            _selectionHasTarget = _selectionWorldValid;
            Invalidate();
        }

        private void UpdatePointSelection(Point local)
        {
            var p = ClampPointerToViewport(local);
            _lastMousePos = p;
            if (TryProjectPointerToPlane(_gridPlaneY, p, out var projected))
            {
                _selectionEndWorldTarget = projected;
                _selectionEndWorld = projected;
                _selectionWorldValid = true;
                _selectionHasTarget = true;
            }
            Invalidate();
        }

        private void EndPointSelection(Point local, KeyModifiers keyModifiers)
        {
            var p = ClampPointerToViewport(local);
            _lastMousePos = p;
            if (TryProjectPointerToPlane(_gridPlaneY, p, out var projectedEnd))
            {
                _selectionEndWorldTarget = projectedEnd;
                _selectionEndWorld = projectedEnd;
                _selectionWorldValid = true;
                _selectionHasTarget = true;
            }

            _isPointSelecting = false;
            bool append = keyModifiers.HasFlag(KeyModifiers.Shift) || _pressedKeys.Contains(Key.LeftShift) || _pressedKeys.Contains(Key.RightShift);

            if (!append)
            {
                _selectionAreas.Clear();
            }
            if (_selectionWorldValid)
            {
                float minX = MathF.Min(_selectionStartWorld.X, _selectionEndWorld.X);
                float maxX = MathF.Max(_selectionStartWorld.X, _selectionEndWorld.X);
                float minZ = MathF.Min(_selectionStartWorld.Z, _selectionEndWorld.Z);
                float maxZ = MathF.Max(_selectionStartWorld.Z, _selectionEndWorld.Z);
                if ((maxX - minX) > 0.001f && (maxZ - minZ) > 0.001f)
                {
                    _selectionAreas.Add(new Vector4D<float>(minX, maxX, minZ, maxZ));
                }
            }
            _renderer?.UpdateSelectionAreas(_selectionAreas.ToArray(), _gridPlaneY);

            ApplyPointSelectionWorld(append);
        }

        private static Vector2D<float> WorldToScanLocal(ScanVolumeSettings s, Vector3D<float> p)
        {
            float yaw = s.YawDegrees * (MathF.PI / 180f);
            float cos = MathF.Cos(yaw);
            float sin = MathF.Sin(yaw);
            float dx = p.X - s.CenterX;
            float dz = p.Z - s.CenterZ;
            float lx = dx * cos + dz * sin;
            float lz = -dx * sin + dz * cos;
            return new Vector2D<float>(lx, lz);
        }

        private static void GetScanAxes(ScanVolumeSettings s, out Vector3D<float> xAxis, out Vector3D<float> zAxis)
        {
            float yaw = s.YawDegrees * (MathF.PI / 180f);
            xAxis = Silk.NET.Maths.Vector3D.Normalize(new Vector3D<float>(MathF.Cos(yaw), 0f, MathF.Sin(yaw)));
            zAxis = Silk.NET.Maths.Vector3D.Normalize(new Vector3D<float>(-MathF.Sin(yaw), 0f, MathF.Cos(yaw)));
        }

        private static bool TryGetScanHandlePosition(ScanVolumeSettings s, ScanHandleKind handle, out Vector3D<float> p)
        {
            GetScanAxes(s, out var xAxis, out var zAxis);
            float hx = s.SizeX * 0.5f;
            float hz = s.SizeZ * 0.5f;
            float midY = (s.YTop + s.YBottom) * 0.5f;
            var centerMid = new Vector3D<float>(s.CenterX, midY, s.CenterZ);
            float moveOffset = MathF.Max(20f, MathF.Min(hx, hz) * 0.35f);
            float rotateRadius = Math.Clamp(MathF.Min(hx, hz) * 0.25f, 30f, 600f);
            float rotateHandleOffset = hx + MathF.Max(40f, MathF.Min(hx, hz) * 0.35f);

            p = handle switch
            {
                ScanHandleKind.MoveCenter => centerMid,
                ScanHandleKind.MoveXAxis => centerMid + xAxis * moveOffset,
                ScanHandleKind.MoveZAxis => centerMid + zAxis * moveOffset,
                ScanHandleKind.SizeXPos => centerMid + xAxis * hx,
                ScanHandleKind.SizeXNeg => centerMid - xAxis * hx,
                ScanHandleKind.SizeZPos => centerMid + zAxis * hz,
                ScanHandleKind.SizeZNeg => centerMid - zAxis * hz,
                ScanHandleKind.YTop => new Vector3D<float>(s.CenterX, s.YTop, s.CenterZ),
                ScanHandleKind.YBottom => new Vector3D<float>(s.CenterX, s.YBottom, s.CenterZ),
                ScanHandleKind.RotateYaw => centerMid + xAxis * rotateHandleOffset,
                _ => default
            };

            return handle != ScanHandleKind.None;
        }

        private bool TryProjectWorldToScreen(Vector3D<float> world, out Vector2D<float> screen)
        {
            screen = default;
            if (Bounds.Width <= 1 || Bounds.Height <= 1) return false;

            var view = Camera.GetViewMatrix();
            var proj = Camera.GetProjectionMatrix((float)Bounds.Width, (float)Bounds.Height);
            var clip = Vector4D.Transform(new Vector4D<float>(world.X, world.Y, world.Z, 1f), view * proj);
            if (MathF.Abs(clip.W) < 1e-6f) return false;

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            screen = new Vector2D<float>(
                (ndcX * 0.5f + 0.5f) * (float)Bounds.Width,
                (1f - (ndcY * 0.5f + 0.5f)) * (float)Bounds.Height);
            return true;
        }

        private bool TryGetAxisScreenDirection(Vector3D<float> origin, Vector3D<float> axis, float axisWorldLen, out Vector2D<float> dir, out float worldPerPixel)
        {
            dir = default;
            worldPerPixel = 1f;

            if (!TryProjectWorldToScreen(origin, out var s0)) return false;
            if (!TryProjectWorldToScreen(origin + axis * axisWorldLen, out var s1)) return false;

            var v = s1 - s0;
            float len = v.Length;
            if (len < 1e-3f) return false;

            dir = v / len;
            worldPerPixel = axisWorldLen / len;
            return true;
        }

        private ScanHandleKind PickScanHandle(Point pointer)
        {
            if (Bounds.Width <= 1 || Bounds.Height <= 1) return ScanHandleKind.None;

            var p = ClampPointerToViewport(pointer);
            var ray = Camera.GetRay((float)p.X, (float)p.Y, (float)Bounds.Width, (float)Bounds.Height);
            var s = ScanVolume.Sanitize();

            float hx = s.SizeX * 0.5f;
            float hz = s.SizeZ * 0.5f;
            float midY = (s.YTop + s.YBottom) * 0.5f;

            static bool TryRaySphere(Vector3D<float> origin, Vector3D<float> dir, Vector3D<float> center, float radius, out float t)
            {
                t = float.MaxValue;
                var oc = origin - center;
                float b = Silk.NET.Maths.Vector3D.Dot(oc, dir);
                float c = Silk.NET.Maths.Vector3D.Dot(oc, oc) - radius * radius;
                float h = b * b - c;
                if (h < 0f) return false;
                float srt = MathF.Sqrt(h);
                float t0 = -b - srt;
                float t1 = -b + srt;
                if (t0 > 1e-5f) { t = t0; return true; }
                if (t1 > 1e-5f) { t = t1; return true; }
                return false;
            }

            static bool TryRayPlaneRect(
                Vector3D<float> origin,
                Vector3D<float> dir,
                Vector3D<float> center,
                Vector3D<float> normal,
                Vector3D<float> axisU,
                Vector3D<float> axisV,
                float halfU,
                float halfV,
                out float t)
            {
                t = float.MaxValue;
                float denom = Silk.NET.Maths.Vector3D.Dot(dir, normal);
                if (MathF.Abs(denom) < 1e-6f) return false;
                float hitT = Silk.NET.Maths.Vector3D.Dot(center - origin, normal) / denom;
                if (hitT <= 1e-5f) return false;

                var hit = origin + dir * hitT;
                var d = hit - center;
                float u = Silk.NET.Maths.Vector3D.Dot(d, axisU);
                float v = Silk.NET.Maths.Vector3D.Dot(d, axisV);
                if (MathF.Abs(u) > halfU || MathF.Abs(v) > halfV) return false;

                t = hitT;
                return true;
            }

            float yaw = s.YawDegrees * (MathF.PI / 180f);
            var xAxisW = Silk.NET.Maths.Vector3D.Normalize(new Vector3D<float>(MathF.Cos(yaw), 0f, MathF.Sin(yaw)));
            var zAxisW = Silk.NET.Maths.Vector3D.Normalize(new Vector3D<float>(-MathF.Sin(yaw), 0f, MathF.Cos(yaw)));
            var yAxisW = new Vector3D<float>(0f, 1f, 0f);
            var centerMid = new Vector3D<float>(s.CenterX, midY, s.CenterZ);

            float moveOffset = MathF.Max(20f, MathF.Min(hx, hz) * 0.35f);
            float rotateHandleOffset = hx + MathF.Max(40f, MathF.Min(hx, hz) * 0.35f);
            float distToCamera = (centerMid - Camera.Position).Length;
            float baseSize = Math.Clamp(distToCamera * 0.02f, 0.5f, 400f);

            var hRotateCenter = centerMid;
            var hRotateYaw = centerMid + xAxisW * rotateHandleOffset;
            var hMoveX = centerMid + xAxisW * moveOffset;
            var hMoveZ = centerMid + zAxisW * moveOffset;

            var hXPos = centerMid + xAxisW * hx;
            var hXNeg = centerMid - xAxisW * hx;
            var hZPos = centerMid + zAxisW * hz;
            var hZNeg = centerMid - zAxisW * hz;
            var hTop = new Vector3D<float>(s.CenterX, s.YTop, s.CenterZ);
            var hBottom = new Vector3D<float>(s.CenterX, s.YBottom, s.CenterZ);

            float ySpan = MathF.Max(8f, s.YTop - s.YBottom);
            float sharedFaceMin = MathF.Min(MathF.Min(2f * hx, 2f * hz), ySpan);
            float sharedSquareHalf = MathF.Max(24f, sharedFaceMin / 3f);
            float faceOffset = 0.75f;

            var xPosCenter = hXPos - xAxisW * faceOffset;
            var xNegCenter = hXNeg + xAxisW * faceOffset;
            var zPosCenter = hZPos - zAxisW * faceOffset;
            var zNegCenter = hZNeg + zAxisW * faceOffset;
            var topCenter = hTop - yAxisW * faceOffset;
            var bottomCenter = hBottom + yAxisW * faceOffset;

            bool IsFaceVisible(Vector3D<float> faceCenter, Vector3D<float> outwardNormal)
            {
                var toCam = Camera.Position - faceCenter;
                return Silk.NET.Maths.Vector3D.Dot(outwardNormal, toCam) > 0.0f;
            }

            ScanHandleKind bestHandle = ScanHandleKind.None;
            float bestT = float.MaxValue;
            int bestPriority = int.MaxValue;

            void Consider(ScanHandleKind k, float t, int priority)
            {
                if (t <= 1e-5f) return;
                if (priority < bestPriority || (priority == bestPriority && t < bestT))
                {
                    bestPriority = priority;
                    bestT = t;
                    bestHandle = k;
                }
            }

            if (TryRaySphere(ray.Origin, ray.Direction, hRotateCenter, baseSize * 0.95f, out float tRotateCenter))
                Consider(ScanHandleKind.MoveCenter, tRotateCenter, 0);
            if (TryRaySphere(ray.Origin, ray.Direction, hRotateYaw, baseSize * 0.95f, out float tRotateYaw))
                Consider(ScanHandleKind.RotateYaw, tRotateYaw, 0);

            float moveScale = 0.95f;
            float moveRadius = baseSize * 0.75f * moveScale;
            var moveXTip = hMoveX + xAxisW * (baseSize * 1.1f * moveScale);
            var moveZTip = hMoveZ + zAxisW * (baseSize * 1.1f * moveScale);
            if (TryRaySphere(ray.Origin, ray.Direction, hMoveX, moveRadius, out float tMoveX0))
                Consider(ScanHandleKind.MoveXAxis, tMoveX0, 0);
            if (TryRaySphere(ray.Origin, ray.Direction, moveXTip, moveRadius * 0.7f, out float tMoveX1))
                Consider(ScanHandleKind.MoveXAxis, tMoveX1, 0);
            if (TryRaySphere(ray.Origin, ray.Direction, hMoveZ, moveRadius, out float tMoveZ0))
                Consider(ScanHandleKind.MoveZAxis, tMoveZ0, 0);
            if (TryRaySphere(ray.Origin, ray.Direction, moveZTip, moveRadius * 0.7f, out float tMoveZ1))
                Consider(ScanHandleKind.MoveZAxis, tMoveZ1, 0);

            if (IsFaceVisible(xPosCenter, xAxisW) && TryRayPlaneRect(ray.Origin, ray.Direction, xPosCenter, xAxisW, zAxisW, yAxisW, sharedSquareHalf, sharedSquareHalf, out float tXPos))
                Consider(ScanHandleKind.SizeXPos, tXPos, 1);
            if (IsFaceVisible(xNegCenter, -xAxisW) && TryRayPlaneRect(ray.Origin, ray.Direction, xNegCenter, -xAxisW, zAxisW, yAxisW, sharedSquareHalf, sharedSquareHalf, out float tXNeg))
                Consider(ScanHandleKind.SizeXNeg, tXNeg, 1);
            if (IsFaceVisible(zPosCenter, zAxisW) && TryRayPlaneRect(ray.Origin, ray.Direction, zPosCenter, zAxisW, xAxisW, yAxisW, sharedSquareHalf, sharedSquareHalf, out float tZPos))
                Consider(ScanHandleKind.SizeZPos, tZPos, 1);
            if (IsFaceVisible(zNegCenter, -zAxisW) && TryRayPlaneRect(ray.Origin, ray.Direction, zNegCenter, -zAxisW, xAxisW, yAxisW, sharedSquareHalf, sharedSquareHalf, out float tZNeg))
                Consider(ScanHandleKind.SizeZNeg, tZNeg, 1);
            if (IsFaceVisible(topCenter, yAxisW) && TryRayPlaneRect(ray.Origin, ray.Direction, topCenter, yAxisW, xAxisW, zAxisW, sharedSquareHalf, sharedSquareHalf, out float tTop))
                Consider(ScanHandleKind.YTop, tTop, 1);
            if (IsFaceVisible(bottomCenter, -yAxisW) && TryRayPlaneRect(ray.Origin, ray.Direction, bottomCenter, -yAxisW, xAxisW, zAxisW, sharedSquareHalf, sharedSquareHalf, out float tBottom))
                Consider(ScanHandleKind.YBottom, tBottom, 1);

            return bestHandle;
        }

        private bool StartScanHandleManipulation(Point pointerPos)
        {
            _activeScanHandle = _hoverScanHandle != ScanHandleKind.None ? _hoverScanHandle : PickScanHandle(pointerPos);
            if (_activeScanHandle == ScanHandleKind.None)
            {
                return false;
            }

            float planeY = _gridPlaneY;
            if (_activeScanHandle == ScanHandleKind.RotateYaw)
            {
                _isScanRotating = true;
                _scanRotateStart = pointerPos;
                _scanYawStart = ScanVolume.YawDegrees;
                if (TryProjectPointerToPlane(planeY, pointerPos, out var hitRotate))
                {
                    _scanRotateStartAngle = MathF.Atan2(hitRotate.Z - ScanVolume.CenterZ, hitRotate.X - ScanVolume.CenterX);
                }
                else
                {
                    _scanRotateStartAngle = 0f;
                }
            }
            else if (_activeScanHandle == ScanHandleKind.MoveCenter && TryProjectPointerToPlane(planeY, pointerPos, out var hitMove))
            {
                _isScanDragging = true;
                _scanDragOffset = new Vector3D<float>(ScanVolume.CenterX - hitMove.X, 0f, ScanVolume.CenterZ - hitMove.Z);
            }
            else if (_activeScanHandle == ScanHandleKind.MoveXAxis || _activeScanHandle == ScanHandleKind.MoveZAxis)
            {
                _isScanAxisMove = true;
                var s = ScanVolume.Sanitize();
                GetScanAxes(s, out var xAxis, out var zAxis);
                _scanMoveAxisWorld = _activeScanHandle == ScanHandleKind.MoveXAxis ? xAxis : zAxis;
                _scanAxisPlaneY = (s.YTop + s.YBottom) * 0.5f;
                _scanAxisStartCenterX = s.CenterX;
                _scanAxisStartCenterZ = s.CenterZ;
                _scanAxisDragReady = false;

                if (TryProjectPointerToPlane(_scanAxisPlaneY, pointerPos, out var hitAxis))
                {
                    var startCenter = new Vector3D<float>(_scanAxisStartCenterX, _scanAxisPlaneY, _scanAxisStartCenterZ);
                    _scanAxisStartCoord = Silk.NET.Maths.Vector3D.Dot(hitAxis - startCenter, _scanMoveAxisWorld);
                    _scanAxisDragReady = true;
                }
            }
            else if (_activeScanHandle == ScanHandleKind.SizeXPos || _activeScanHandle == ScanHandleKind.SizeXNeg ||
                     _activeScanHandle == ScanHandleKind.SizeZPos || _activeScanHandle == ScanHandleKind.SizeZNeg)
            {
                _isScanScaling = true;
                _scanScaleStartX = ScanVolume.SizeX;
                _scanScaleStartZ = ScanVolume.SizeZ;
                _scanScaleStartMouse = pointerPos;
            }
            else if (_activeScanHandle == ScanHandleKind.YTop || _activeScanHandle == ScanHandleKind.YBottom)
            {
                _isScanHeightAdjust = true;
                _scanHeightStartTop = ScanVolume.YTop;
                _scanHeightStartBottom = ScanVolume.YBottom;
            }

            return _isScanDragging || _isScanAxisMove || _isScanRotating || _isScanScaling || _isScanHeightAdjust;
        }

        private bool HandleScanManipulationMove(Point currentPos, Point delta)
        {
            if (_isScanDragging)
            {
                float planeY = _gridPlaneY;
                if (TryProjectPointerToPlane(planeY, currentPos, out var hit))
                {
                    SetScanVolume(ScanVolume with
                    {
                        CenterX = hit.X + _scanDragOffset.X,
                        CenterZ = hit.Z + _scanDragOffset.Z
                    });
                }
                _lastMousePos = currentPos;
                return true;
            }

            if (_isScanAxisMove)
            {
                if (_scanAxisDragReady && TryProjectPointerToPlane(_scanAxisPlaneY, currentPos, out var hitAxis))
                {
                    var startCenter = new Vector3D<float>(_scanAxisStartCenterX, _scanAxisPlaneY, _scanAxisStartCenterZ);
                    float currentCoord = Silk.NET.Maths.Vector3D.Dot(hitAxis - startCenter, _scanMoveAxisWorld);
                    float deltaWorld = currentCoord - _scanAxisStartCoord;
                    var deltaVec = _scanMoveAxisWorld * deltaWorld;
                    SetScanVolume(ScanVolume with
                    {
                        CenterX = _scanAxisStartCenterX + deltaVec.X,
                        CenterZ = _scanAxisStartCenterZ + deltaVec.Z
                    });
                }
                else
                {
                    var s = ScanVolume.Sanitize();
                    float midY = (s.YTop + s.YBottom) * 0.5f;
                    var center = new Vector3D<float>(s.CenterX, midY, s.CenterZ);
                    var mouseDelta = new Vector2D<float>((float)delta.X, (float)delta.Y);
                    float axisLen = MathF.Max(50f, MathF.Max(s.SizeX, s.SizeZ) * 0.35f);
                    if (TryGetAxisScreenDirection(center, _scanMoveAxisWorld, axisLen, out var axisScreenDir, out var worldPerPixel))
                    {
                        float deltaAlong = mouseDelta.X * axisScreenDir.X + mouseDelta.Y * axisScreenDir.Y;
                        float deltaWorld = deltaAlong * worldPerPixel;
                        var deltaVec = _scanMoveAxisWorld * deltaWorld;
                        SetScanVolume(s with
                        {
                            CenterX = s.CenterX + deltaVec.X,
                            CenterZ = s.CenterZ + deltaVec.Z
                        });
                    }
                }

                _lastMousePos = currentPos;
                return true;
            }

            if (_isScanRotating)
            {
                float planeY = _gridPlaneY;
                if (TryProjectPointerToPlane(planeY, currentPos, out var hitRotate))
                {
                    float angle = MathF.Atan2(hitRotate.Z - ScanVolume.CenterZ, hitRotate.X - ScanVolume.CenterX);
                    float deltaAngle = angle - _scanRotateStartAngle;
                    SetScanVolume(ScanVolume with { YawDegrees = _scanYawStart + (deltaAngle * (180f / MathF.PI)) });
                }
                _lastMousePos = currentPos;
                return true;
            }

            if (_isScanScaling)
            {
                var s = ScanVolume.Sanitize();
                GetScanAxes(s, out var xAxis, out var zAxis);
                float midY = (s.YTop + s.YBottom) * 0.5f;
                var center = new Vector3D<float>(s.CenterX, midY, s.CenterZ);

                var mouseDelta = new Vector2D<float>((float)delta.X, (float)delta.Y);
                const float minSize = 10f;
                if (_activeScanHandle == ScanHandleKind.SizeXPos || _activeScanHandle == ScanHandleKind.SizeXNeg)
                {
                    float axisLen = MathF.Max(50f, s.SizeX * 0.5f);
                    if (TryGetAxisScreenDirection(center, xAxis, axisLen, out var axisScreenDir, out var worldPerPixel))
                    {
                        float deltaAlong = mouseDelta.X * axisScreenDir.X + mouseDelta.Y * axisScreenDir.Y;
                        float deltaWorld = deltaAlong * worldPerPixel;

                        float oldSize = s.SizeX;
                        float desiredSize = _activeScanHandle == ScanHandleKind.SizeXPos
                            ? oldSize + deltaWorld
                            : oldSize - deltaWorld;
                        float newSize = MathF.Max(minSize, desiredSize);
                        float appliedSizeDelta = newSize - oldSize;

                        float faceMove = _activeScanHandle == ScanHandleKind.SizeXPos
                            ? appliedSizeDelta
                            : -appliedSizeDelta;

                        var centerShift = xAxis * (faceMove * 0.5f);
                        SetScanVolume(s with
                        {
                            CenterX = s.CenterX + centerShift.X,
                            CenterZ = s.CenterZ + centerShift.Z,
                            SizeX = newSize
                        });
                    }
                }
                else if (_activeScanHandle == ScanHandleKind.SizeZPos || _activeScanHandle == ScanHandleKind.SizeZNeg)
                {
                    float axisLen = MathF.Max(50f, s.SizeZ * 0.5f);
                    if (TryGetAxisScreenDirection(center, zAxis, axisLen, out var axisScreenDir, out var worldPerPixel))
                    {
                        float deltaAlong = mouseDelta.X * axisScreenDir.X + mouseDelta.Y * axisScreenDir.Y;
                        float deltaWorld = deltaAlong * worldPerPixel;

                        float oldSize = s.SizeZ;
                        float desiredSize = _activeScanHandle == ScanHandleKind.SizeZPos
                            ? oldSize + deltaWorld
                            : oldSize - deltaWorld;
                        float newSize = MathF.Max(minSize, desiredSize);
                        float appliedSizeDelta = newSize - oldSize;

                        float faceMove = _activeScanHandle == ScanHandleKind.SizeZPos
                            ? appliedSizeDelta
                            : -appliedSizeDelta;

                        var centerShift = zAxis * (faceMove * 0.5f);
                        SetScanVolume(s with
                        {
                            CenterX = s.CenterX + centerShift.X,
                            CenterZ = s.CenterZ + centerShift.Z,
                            SizeZ = newSize
                        });
                    }
                }
                _lastMousePos = currentPos;
                return true;
            }

            if (_isScanHeightAdjust)
            {
                float span = MathF.Max(10f, _scanHeightStartTop - _scanHeightStartBottom);
                float sensitivity = MathF.Max(0.25f, span * 0.0035f);
                float deltaHeight = (float)(-delta.Y) * sensitivity;
                if (_activeScanHandle == ScanHandleKind.YTop)
                {
                    SetScanVolume(ScanVolume with { YTop = ScanVolume.YTop + deltaHeight });
                }
                else if (_activeScanHandle == ScanHandleKind.YBottom)
                {
                    SetScanVolume(ScanVolume with { YBottom = ScanVolume.YBottom + deltaHeight });
                }
                _lastMousePos = currentPos;
                return true;
            }

            return false;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var ptr = e.GetCurrentPoint(this);
            _lastMousePos = ptr.Position;
            _lastGlobalMousePos = _topLevel != null ? e.GetPosition(_topLevel) : ptr.Position;

            bool leftOnly = ptr.Properties.IsLeftButtonPressed && !ptr.Properties.IsRightButtonPressed;
            if (PointSelectionModeEnabled && leftOnly)
            {
                BeginPointSelection(ptr.Position);
                e.Pointer.Capture(this);
                Focus();
                e.Handled = true;
                return;
            }

            if (ScanVolumeEditEnabled && leftOnly)
            {
                if (StartScanHandleManipulation(ptr.Position))
                {
                    e.Pointer.Capture(this);
                    Focus();
                    e.Handled = true;
                    return;
                }
            }

            if (ptr.Properties.IsRightButtonPressed || (ptr.Properties.IsLeftButtonPressed && !PointSelectionModeEnabled && _activeScanHandle == ScanHandleKind.None)) _isLooking = true;
            if (_isLooking)
            {
                e.Pointer.Capture(this);
                Focus();
            }

            e.Handled = true;
            Focus(); // Request focus for keyboard if needed
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_isPointSelecting)
            {
                EndPointSelection(e.GetCurrentPoint(this).Position, e.KeyModifiers);
                if (e.Pointer.Captured == this)
                {
                    e.Pointer.Capture(null);
                }
                e.Handled = true;
                return;
            }

            _isScanDragging = false;
            _isScanAxisMove = false;
            _scanAxisDragReady = false;
            _isScanRotating = false;
            _isScanScaling = false;
            _isScanHeightAdjust = false;
            _activeScanHandle = ScanHandleKind.None;
            _isLooking = false;
            if (e.Pointer.Captured == this)
            {
                e.Pointer.Capture(null);
            }
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var currentPos = e.GetCurrentPoint(this).Position;
            var delta = currentPos - _lastMousePos;

            if (_isPointSelecting)
            {
                UpdatePointSelection(currentPos);
                e.Handled = true;
                return;
            }

            if (ScanVolumeEditEnabled && !_isScanDragging && !_isScanAxisMove && !_isScanRotating && !_isScanScaling && !_isScanHeightAdjust)
            {
                _hoverScanHandle = PickScanHandle(currentPos);
                Cursor = _hoverScanHandle switch
                {
                    ScanHandleKind.MoveCenter => new Cursor(StandardCursorType.SizeAll),
                    ScanHandleKind.MoveXAxis or ScanHandleKind.MoveZAxis => new Cursor(StandardCursorType.SizeWestEast),
                    ScanHandleKind.RotateYaw => new Cursor(StandardCursorType.Hand),
                    ScanHandleKind.YTop or ScanHandleKind.YBottom => new Cursor(StandardCursorType.SizeNorthSouth),
                    ScanHandleKind.SizeXPos or ScanHandleKind.SizeXNeg or ScanHandleKind.SizeZPos or ScanHandleKind.SizeZNeg => new Cursor(StandardCursorType.SizeWestEast),
                    _ => new Cursor(StandardCursorType.Arrow)
                };
            }

            if (HandleScanManipulationMove(currentPos, delta))
            {
                e.Handled = true;
                return;
            }

            bool pressed = e.GetCurrentPoint(this).Properties.IsRightButtonPressed ||
                           (!PointSelectionModeEnabled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (_hoverScanHandle == ScanHandleKind.None || !ScanVolumeEditEnabled));
            if (_isLooking || pressed)
            {
                Camera.Look((float)delta.X, (float)delta.Y);
                _hoverDirty = true;
            }

            _lastMousePos = currentPos;
            _hoverDirty = true;
            Invalidate(); // Always invalidate on mouse move to update hovered coordinate
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            Camera.Zoom((float)e.Delta.Y);
            OnMoveSpeedChanged?.Invoke(Camera.MoveSpeed);
            _hoverDirty = true;
            Invalidate();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (PointSelectionModeEnabled && e.Key == Key.Delete)
            {
                OnDeleteSelectionRequested?.Invoke();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                ClearPointSelection();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.G)
            {
                OnToggleSelectionModeRequested?.Invoke();
                e.Handled = true;
                return;
            }

            _pressedKeys.Add(e.Key);
            Invalidate();
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            _pressedKeys.Remove(e.Key);
            Invalidate();
            e.Handled = true;
        }

        private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
        {
            if (IsKeyboardFocusWithin)
            {
                return;
            }

            if (PointSelectionModeEnabled && e.Key == Key.Delete)
            {
                OnDeleteSelectionRequested?.Invoke();
                return;
            }

            if (e.Key == Key.Escape)
            {
                ClearPointSelection();
                return;
            }

            if (e.Key == Key.G)
            {
                OnToggleSelectionModeRequested?.Invoke();
                return;
            }

            _pressedKeys.Add(e.Key);
            Invalidate();
        }

        private void OnTopLevelKeyUp(object? sender, KeyEventArgs e)
        {
            _pressedKeys.Remove(e.Key);
            Invalidate();
        }

        private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_isPointSelecting)
            {
                return;
            }

            var local = e.GetPosition(this);
            bool inside = local.X >= 0 && local.Y >= 0 && local.X <= Bounds.Width && local.Y <= Bounds.Height;
            if (!inside)
            {
                return;
            }

            var props = e.GetCurrentPoint(this).Properties;
            if (PointSelectionModeEnabled && props.IsLeftButtonPressed && !props.IsRightButtonPressed)
            {
                BeginPointSelection(local);
                e.Pointer.Capture(this);
                Focus();
                return;
            }

            if (ScanVolumeEditEnabled && props.IsLeftButtonPressed && !props.IsRightButtonPressed && StartScanHandleManipulation(local))
            {
                _lastMousePos = local;
                e.Pointer.Capture(this);
                Focus();
                return;
            }

            if (props.IsRightButtonPressed || (!PointSelectionModeEnabled && props.IsLeftButtonPressed))
            {
                _isLooking = true;
                _lastMousePos = local;
                _lastGlobalMousePos = _topLevel != null ? e.GetPosition(_topLevel) : local;
                e.Pointer.Capture(this);
                Focus();
            }
        }

        private void OnTopLevelPointerMoved(object? sender, PointerEventArgs e)
        {
            var currentGlobal = _topLevel != null ? e.GetPosition(_topLevel) : e.GetPosition(this);

            if (_isPointSelecting)
            {
                UpdatePointSelection(e.GetPosition(this));
                _lastGlobalMousePos = currentGlobal;
                return;
            }

            if (_isLooking)
            {
                var delta = currentGlobal - _lastGlobalMousePos;
                Camera.Look((float)delta.X, (float)delta.Y);
                _hoverDirty = true;
            }

            _lastGlobalMousePos = currentGlobal;

            // Update local mouse pos for depth reading even if not looking
            var local = e.GetPosition(this);
            if (local.X >= 0 && local.Y >= 0 && local.X <= Bounds.Width && local.Y <= Bounds.Height)
            {
                var localDelta = local - _lastMousePos;
                if (HandleScanManipulationMove(local, localDelta))
                {
                    _hoverDirty = true;
                    Invalidate();
                    return;
                }

                _lastMousePos = local;
                if (ScanVolumeEditEnabled && !_isScanDragging && !_isScanAxisMove && !_isScanRotating && !_isScanScaling && !_isScanHeightAdjust)
                {
                    _hoverScanHandle = PickScanHandle(local);
                    Cursor = _hoverScanHandle switch
                    {
                        ScanHandleKind.MoveCenter => new Cursor(StandardCursorType.SizeAll),
                        ScanHandleKind.MoveXAxis or ScanHandleKind.MoveZAxis => new Cursor(StandardCursorType.SizeWestEast),
                        ScanHandleKind.RotateYaw => new Cursor(StandardCursorType.Hand),
                        ScanHandleKind.YTop or ScanHandleKind.YBottom => new Cursor(StandardCursorType.SizeNorthSouth),
                        ScanHandleKind.SizeXPos or ScanHandleKind.SizeXNeg or ScanHandleKind.SizeZPos or ScanHandleKind.SizeZNeg => new Cursor(StandardCursorType.SizeWestEast),
                        _ => new Cursor(StandardCursorType.Arrow)
                    };
                }
                else if (!ScanVolumeEditEnabled)
                {
                    _hoverScanHandle = ScanHandleKind.None;
                    Cursor = new Cursor(StandardCursorType.Arrow);
                }
                _hoverDirty = true;
            }

            Invalidate();
        }

        private void OnTopLevelPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isPointSelecting)
            {
                EndPointSelection(e.GetPosition(this), e.KeyModifiers);
                if (e.Pointer.Captured == this)
                {
                    e.Pointer.Capture(null);
                }
                Invalidate();
                return;
            }

            _isLooking = false;
            _isScanDragging = false;
            _isScanAxisMove = false;
            _scanAxisDragReady = false;
            _isScanRotating = false;
            _isScanScaling = false;
            _isScanHeightAdjust = false;
            _activeScanHandle = ScanHandleKind.None;
        }

        private bool UpdateCameraMovement(float dt)
        {
            if (dt <= 0) return false;

            float forward = 0;
            float right = 0;
            float up = 0;

            if (_pressedKeys.Contains(Key.W) || _pressedKeys.Contains(Key.Z) || _pressedKeys.Contains(Key.Up)) forward += 1;
            if (_pressedKeys.Contains(Key.S) || _pressedKeys.Contains(Key.Down)) forward -= 1;
            if (_pressedKeys.Contains(Key.D) || _pressedKeys.Contains(Key.Right)) right += 1;
            if (_pressedKeys.Contains(Key.A) || _pressedKeys.Contains(Key.Q) || _pressedKeys.Contains(Key.Left)) right -= 1;
            if (_pressedKeys.Contains(Key.Space)) up += 1;
            if (_pressedKeys.Contains(Key.LeftCtrl) || _pressedKeys.Contains(Key.RightCtrl)) up -= 1;

            bool sprint = _pressedKeys.Contains(Key.LeftShift) || _pressedKeys.Contains(Key.RightShift);
            if (forward != 0 || right != 0 || up != 0)
            {
                float len = MathF.Sqrt(forward * forward + right * right + up * up);
                if (len > 1.0f)
                {
                    forward /= len;
                    right /= len;
                    up /= len;
                }

                Camera.Move(forward, right, up, dt, sprint);
                _hoverDirty = true;
                return true;
            }
            return false;
        }
    }
}
