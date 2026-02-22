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
        public ScanVolumeSettings ScanVolume { get; private set; } = ScanVolumeSettings.Default;
        public int HoverScanHandleId => (int)_hoverScanHandle;
        public int ActiveScanHandleId => (int)_activeScanHandle;
        public Action<string>? OnLog { get; set; }
        public Action<Vector3D<float>?>? OnHoveredCoordinateChanged { get; set; }
        public Action<ScanVolumeSettings>? OnScanVolumeChanged { get; set; }

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
            if (_renderer != null)
            {
                _renderer.ShowScanVolume = ShowScanVolume;
                _renderer.ShowScanHandles = ShowScanHandles;
                _renderer.ShowScanDensityPreview = ShowScanDensityPreview;
                _renderer.ScanFineTargetStep = ScanFineTargetStep;
                _renderer.UpdateScanVolume(ScanVolume);
                _renderer.UpdateScanHandleState(HoverScanHandleId, ActiveScanHandleId);
            }
            _renderer?.Render(fb, Bounds.Size);

            if (_hoverDirty)
            {
                UpdateHoveredPoint();
                _hoverDirty = false;
            }

            bool hasAnimations = _renderer != null && _renderer.HasActiveAnimations();

            if (cameraMoved || hasAnimations || _pressedKeys.Count > 0)
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
            _hoverCellSize = MathF.Max(avgDistance * 2.0f, 0.25f);
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

            float t = (planeY - ray.Origin.Y) / ray.Direction.Y;
            if (t <= 0)
            {
                return false;
            }

            point = ray.Origin + ray.Direction * t;
            return true;
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

            var ray = Camera.GetRay((float)pointer.X, (float)pointer.Y, (float)Bounds.Width, (float)Bounds.Height);
            var s = ScanVolume.Sanitize();
            float sceneScale = MathF.Max(20f, MathF.Max(s.SizeX, s.SizeZ));
            float planeTol = MathF.Max(40f, sceneScale * 0.05f);
            float centerTol = MathF.Max(30f, sceneScale * 0.03f);

            float hx = s.SizeX * 0.5f;
            float hz = s.SizeZ * 0.5f;
            float midY = (s.YTop + s.YBottom) * 0.5f;

            if (TryProjectPointerToPlane(midY, pointer, out var onPlane))
            {
                var local = WorldToScanLocal(s, onPlane);
                float lx = local.X;
                float lz = local.Y;

                // Allow direct click on the dedicated rotate handle (not only the ring).
                if (TryGetScanHandlePosition(s, ScanHandleKind.RotateYaw, out var rotateHandleWorld))
                {
                    var toHandle = rotateHandleWorld - onPlane;
                    float handlePlanarDist = MathF.Sqrt(toHandle.X * toHandle.X + toHandle.Z * toHandle.Z);
                    if (handlePlanarDist <= planeTol * 1.25f)
                    {
                        return ScanHandleKind.RotateYaw;
                    }
                }

                // Dedicated axis move handles near center.
                if (TryGetScanHandlePosition(s, ScanHandleKind.MoveXAxis, out var moveXWorld))
                {
                    var d = moveXWorld - onPlane;
                    float dist = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
                    if (dist <= planeTol * 1.15f)
                    {
                        return ScanHandleKind.MoveXAxis;
                    }
                }
                if (TryGetScanHandlePosition(s, ScanHandleKind.MoveZAxis, out var moveZWorld))
                {
                    var d = moveZWorld - onPlane;
                    float dist = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
                    if (dist <= planeTol * 1.15f)
                    {
                        return ScanHandleKind.MoveZAxis;
                    }
                }

                if (MathF.Abs(lx) <= centerTol && MathF.Abs(lz) <= centerTol)
                {
                    return ScanHandleKind.MoveCenter;
                }

                bool nearXEdge = MathF.Abs(MathF.Abs(lx) - hx) <= planeTol && MathF.Abs(lz) <= hz + planeTol;
                if (nearXEdge)
                {
                    return lx >= 0 ? ScanHandleKind.SizeXPos : ScanHandleKind.SizeXNeg;
                }

                bool nearZEdge = MathF.Abs(MathF.Abs(lz) - hz) <= planeTol && MathF.Abs(lx) <= hx + planeTol;
                if (nearZEdge)
                {
                    return lz >= 0 ? ScanHandleKind.SizeZPos : ScanHandleKind.SizeZNeg;
                }
            }

            if (TryGetScanHandlePosition(s, ScanHandleKind.YTop, out var hTop) && TryGetScanHandlePosition(s, ScanHandleKind.YBottom, out var hBottom))
            {
                float pointTol = MathF.Max(34f, sceneScale * 0.04f);

                float DistanceToRay(Vector3D<float> p)
                {
                    var op = p - ray.Origin;
                    float t = Silk.NET.Maths.Vector3D.Dot(op, ray.Direction);
                    if (t < 0f) return float.MaxValue;
                    var q = ray.Origin + ray.Direction * t;
                    return (p - q).Length;
                }

                float dTop = DistanceToRay(hTop);
                float dBottom = DistanceToRay(hBottom);
                if (dTop <= pointTol || dBottom <= pointTol)
                {
                    return dTop <= dBottom ? ScanHandleKind.YTop : ScanHandleKind.YBottom;
                }
            }

            return ScanHandleKind.None;
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
                GetScanAxes(ScanVolume, out var xAxis, out var zAxis);
                _scanMoveAxisWorld = _activeScanHandle == ScanHandleKind.MoveXAxis ? xAxis : zAxis;
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

            if (ptr.Properties.IsRightButtonPressed || (ptr.Properties.IsLeftButtonPressed && _activeScanHandle == ScanHandleKind.None)) _isLooking = true;
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
            _isScanDragging = false;
            _isScanAxisMove = false;
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

            bool pressed = e.GetCurrentPoint(this).Properties.IsRightButtonPressed || (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (_hoverScanHandle == ScanHandleKind.None || !ScanVolumeEditEnabled));
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
            var local = e.GetPosition(this);
            bool inside = local.X >= 0 && local.Y >= 0 && local.X <= Bounds.Width && local.Y <= Bounds.Height;
            if (!inside)
            {
                return;
            }

            var props = e.GetCurrentPoint(this).Properties;
            if (ScanVolumeEditEnabled && props.IsLeftButtonPressed && !props.IsRightButtonPressed && StartScanHandleManipulation(local))
            {
                _lastMousePos = local;
                e.Pointer.Capture(this);
                Focus();
                return;
            }

            if (props.IsRightButtonPressed || props.IsLeftButtonPressed)
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
            _isLooking = false;
            _isScanDragging = false;
            _isScanAxisMove = false;
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
