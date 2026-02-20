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

namespace MeshTool.UI.Controls
{
    public class OpenGlViewport : OpenGlControlBase
    {
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
        public bool ShowRays { get; set; } = true;
        public bool ShowMesh { get; set; } = false;
        public float SurfelScale { get; set; } = 1.0f;
        public Action<string>? OnLog { get; set; }
        public Action<Vector3D<float>?>? OnHoveredCoordinateChanged { get; set; }

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

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            long now = _frameTimer.ElapsedTicks;
            float dt = _lastFrameTicks == 0 ? 0.016f : (float)(now - _lastFrameTicks) / Stopwatch.Frequency;
            if (dt > 0.1f) dt = 0.1f;
            _lastFrameTicks = now;

            bool cameraMoved = UpdateCameraMovement(dt);
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

            float maxAngleCos = MathF.Cos(0.01f); // ~0.57 degrees cone
            float bestDepth = float.MaxValue;
            Silk.NET.Maths.Vector3D<float>? bestPoint = null;
            object lockObj = new object();

            System.Threading.Tasks.Parallel.ForEach(_points, p =>
            {
                var toPoint = new Silk.NET.Maths.Vector3D<float>((float)p.Position.X, (float)p.Position.Y, (float)p.Position.Z) - ray.Origin;
                float depth = Silk.NET.Maths.Vector3D.Dot(toPoint, ray.Direction);

                if (depth > 0 && depth < bestDepth)
                {
                    float distSq = toPoint.LengthSquared;
                    float cosTheta = depth / MathF.Sqrt(distSq);

                    if (cosTheta > maxAngleCos)
                    {
                        lock (lockObj)
                        {
                            if (depth < bestDepth)
                            {
                                bestDepth = depth;
                                bestPoint = new Silk.NET.Maths.Vector3D<float>((float)p.Position.X, (float)p.Position.Y, (float)p.Position.Z);
                            }
                        }
                    }
                }
            });

            UpdateHoveredCoordinate(bestPoint);
        }

        public void Invalidate()
        {
            RequestNextFrameRendering();
        }

        private Vector3D<float>? _hoveredCoordinate;
        private List<TerrainTool.Data.Vertex> _points = new List<TerrainTool.Data.Vertex>();

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

        public void LoadMesh(List<TerrainTool.Data.Triangle>? triangles)
        {
            _renderer?.UpdateMesh(triangles);
            Invalidate();
        }

        public void LoadData(TerrainTool.Data.Vertex[] points, TerrainTool.Data.Ray[] rays, float avgDistance, bool resetCamera = true)
        {
            _points.Clear();
            _points.AddRange(points);
            _renderer?.UpdateData(points, rays, avgDistance);

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

        public void AppendData(TerrainTool.Data.Vertex[]? newPoints, TerrainTool.Data.Ray[]? newMisses, float avgDistance)
        {
            if (newPoints != null) _points.AddRange(newPoints);
            _renderer?.AppendData(newPoints, newMisses, avgDistance);
            _hoverDirty = true;
            Invalidate();
        }

        // Camera Controls
        private Point _lastMousePos;
        private bool _isLooking = false;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var ptr = e.GetCurrentPoint(this);
            _lastMousePos = ptr.Position;
            _lastGlobalMousePos = _topLevel != null ? e.GetPosition(_topLevel) : ptr.Position;

            if (ptr.Properties.IsRightButtonPressed || ptr.Properties.IsLeftButtonPressed) _isLooking = true;
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

            bool pressed = e.GetCurrentPoint(this).Properties.IsRightButtonPressed || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
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
                _lastMousePos = local;
                _hoverDirty = true;
            }

            Invalidate();
        }

        private void OnTopLevelPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isLooking = false;
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
