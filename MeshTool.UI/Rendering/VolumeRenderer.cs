using MeshTool.UI.Controls;
using MeshTool.UI.Models;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Handles scan volume lines, handles, density preview, and selection overlays.
    /// </summary>
    public sealed class VolumeRenderer : IDisposable
    {
        private const float ScanDensityRebuildMoveThreshold = 24f;
        private const int ScanLineVertexStride = 6 * sizeof(float);
        private const int ScanHandleVertexStride = 10 * sizeof(float);
        private const int SelectionFillVertexStride = 3 * sizeof(float);

        private readonly GL _gl;
        private readonly OpenGlViewport _viewport;
        private readonly ShaderProgram _gizmoSolidProgram;
        private readonly ShaderProgram _densityPointsProgram;
        private readonly ShaderProgram _gizmoAccumProgram;
        private readonly ShaderProgram _gizmoRevealProgram;
        private readonly ShaderProgram _flatAccumProgram;
        private readonly ShaderProgram _flatRevealProgram;
        private readonly ShaderProgram _lineProgram;
        private readonly VertexArray _scanVolumeBuffer;
        private readonly VertexArray _scanHandleBuffer;
        private readonly VertexArray _scanDensityBuffer;
        private readonly VertexArray _selectionFillBuffer;
        private bool _scanDensityBufferValid;
        private ScanVolumeSettings _lastDensityScanVolume;
        private float _lastDensityFineTargetStep = -1f;
        private float _lastDensityGridPlaneY = float.NaN;
        private Vector3D<float> _lastDensityCameraPos;
        private bool _disposed;

        public VolumeRenderer(GL gl, OpenGlViewport viewport, Action<string>? log)
        {
            _gl = gl;
            _viewport = viewport;

            _gizmoSolidProgram = new ShaderProgram(_gl, "GizmoSolid", ShaderSource.GizmoSolidVertex, ShaderSource.GizmoSolidFragment, log);
            _densityPointsProgram = new ShaderProgram(_gl, "DensityPoints", ShaderSource.DensityPointsVertex, ShaderSource.DensityPointsFragment, log);
            _gizmoAccumProgram = new ShaderProgram(_gl, "GizmoAccum", ShaderSource.GizmoSolidVertex, ShaderSource.GizmoAccumFragment, log);
            _gizmoRevealProgram = new ShaderProgram(_gl, "GizmoReveal", ShaderSource.GizmoSolidVertex, ShaderSource.GizmoRevealFragment, log);
            _flatAccumProgram = new ShaderProgram(_gl, "FlatAccum", ShaderSource.FlatColorVertex, ShaderSource.FlatOitAccumFragment, log);
            _flatRevealProgram = new ShaderProgram(_gl, "FlatReveal", ShaderSource.FlatColorVertex, ShaderSource.FlatOitRevealFragment, log);
            _lineProgram = new ShaderProgram(_gl, "Axes", ShaderSource.AxesVertex, ShaderSource.AxesFragment, log);

            _scanHandleBuffer = new VertexArray(_gl, ScanHandleVertexStride, 4096);
            _scanHandleBuffer.SetAttribute(0, 3, VertexAttribPointerType.Float, 0);
            _scanHandleBuffer.SetAttribute(1, 3, VertexAttribPointerType.Float, 3 * sizeof(float));
            _scanHandleBuffer.SetAttribute(2, 3, VertexAttribPointerType.Float, 6 * sizeof(float));
            _scanHandleBuffer.SetAttribute(3, 1, VertexAttribPointerType.Float, 9 * sizeof(float));

            _scanDensityBuffer = new VertexArray(_gl, ScanLineVertexStride, 400000);
            _scanDensityBuffer.SetAttribute(0, 3, VertexAttribPointerType.Float, 0);
            _scanDensityBuffer.SetAttribute(1, 3, VertexAttribPointerType.Float, 3 * sizeof(float));

            _scanVolumeBuffer = new VertexArray(_gl, ScanLineVertexStride, 256);
            _scanVolumeBuffer.SetAttribute(0, 3, VertexAttribPointerType.Float, 0);
            _scanVolumeBuffer.SetAttribute(1, 3, VertexAttribPointerType.Float, 3 * sizeof(float));

            _selectionFillBuffer = new VertexArray(_gl, SelectionFillVertexStride, 6);
            _selectionFillBuffer.SetAttribute(0, 3, VertexAttribPointerType.Float, 0);
        }

        public int ScanVolumeVertexCount { get; private set; }
        public int ScanHandleVertexCount { get; private set; }
        public int ScanDensityVertexCount { get; private set; }
        public int ScanDensityBroadCount { get; private set; }
        public int SelectionFillVertexCount { get; private set; }

        public void UpdateScanVolumeBuffer(ScanVolumeSettings scanVolume, int hoverHandle, int activeHandle, bool showScanHandles, bool showSelectionBox, Vector3D<float> selectionStartWorld, Vector3D<float> selectionEndWorld, float selectionYBottom, float selectionYTop)
        {
            var s = scanVolume.Sanitize();
            float[] data = ScanVolumeGeometryBuilder.BuildScanVolumeLineVertices(s, hoverHandle, activeHandle, showScanHandles, showSelectionBox, selectionStartWorld, selectionEndWorld, selectionYBottom, selectionYTop);
            ScanVolumeVertexCount = data.Length / 6;
            _scanVolumeBuffer.UploadData(data, ScanVolumeVertexCount, BufferUsageARB.DynamicDraw);
        }

        public void UpdateScanHandleBuffer(ScanVolumeSettings scanVolume, int hoverHandle, int activeHandle)
        {
            var s = scanVolume.Sanitize();
            float[] data = ScanVolumeGeometryBuilder.BuildScanHandleSolidVertices(s, hoverHandle, activeHandle, _viewport.Camera.Position);
            ScanHandleVertexCount = data.Length / 10;
            _scanHandleBuffer.UploadData(data, ScanHandleVertexCount, BufferUsageARB.DynamicDraw);
        }

        public void UpdateSelectionFillBuffer(bool showSelectionBox, Vector4D<float>[] selectionAreas, float selectionAreasPlaneY, Vector3D<float> selectionStartWorld, Vector3D<float> selectionEndWorld)
        {
            SelectionFillVertexCount = 0;
            if (!showSelectionBox && selectionAreas.Length == 0)
            {
                return;
            }

            var verts = new List<float>((selectionAreas.Length + (showSelectionBox ? 1 : 0)) * 18);
            float y = selectionAreasPlaneY + 0.05f;

            static void AddArea(List<float> target, float areaY, float minX, float maxX, float minZ, float maxZ)
            {
                if ((maxX - minX) < 0.001f || (maxZ - minZ) < 0.001f)
                {
                    return;
                }

                target.Add(minX); target.Add(areaY); target.Add(minZ);
                target.Add(maxX); target.Add(areaY); target.Add(minZ);
                target.Add(maxX); target.Add(areaY); target.Add(maxZ);
                target.Add(minX); target.Add(areaY); target.Add(minZ);
                target.Add(maxX); target.Add(areaY); target.Add(maxZ);
                target.Add(minX); target.Add(areaY); target.Add(maxZ);
            }

            for (int i = 0; i < selectionAreas.Length; i++)
            {
                var a = selectionAreas[i];
                AddArea(verts, y, a.X, a.Y, a.Z, a.W);
            }

            if (showSelectionBox)
            {
                float minX = MathF.Min(selectionStartWorld.X, selectionEndWorld.X);
                float maxX = MathF.Max(selectionStartWorld.X, selectionEndWorld.X);
                float minZ = MathF.Min(selectionStartWorld.Z, selectionEndWorld.Z);
                float maxZ = MathF.Max(selectionStartWorld.Z, selectionEndWorld.Z);
                AddArea(verts, y, minX, maxX, minZ, maxZ);
            }

            if (verts.Count == 0)
            {
                return;
            }

            SelectionFillVertexCount = verts.Count / 3;
            _selectionFillBuffer.UploadData(verts.ToArray(), SelectionFillVertexCount, BufferUsageARB.DynamicDraw);
        }

        public void UpdateScanDensityBuffer(ScanVolumeSettings scanVolume, float gridPlaneY, float scanFineTargetStep, ref float fineDensityPreviewRadius)
        {
            var s = scanVolume.Sanitize();
            if (!ShouldRebuildScanDensity(s, gridPlaneY, scanFineTargetStep))
            {
                return;
            }

            var density = ScanVolumeGeometryBuilder.BuildScanDensityVertices(s, gridPlaneY, scanFineTargetStep, _viewport.Camera.Position, ref fineDensityPreviewRadius);
            float[] data = density.Vertices;
            ScanDensityVertexCount = data.Length / 6;
            ScanDensityBroadCount = density.BroadCount;
            _scanDensityBuffer.UploadData(data, ScanDensityVertexCount, BufferUsageARB.DynamicDraw);

            _scanDensityBufferValid = true;
            _lastDensityScanVolume = s;
            _lastDensityFineTargetStep = scanFineTargetStep;
            _lastDensityGridPlaneY = gridPlaneY;
            _lastDensityCameraPos = _viewport.Camera.Position;
        }

        public void RenderOpaque(Matrix4X4<float> view, Matrix4X4<float> proj, bool showScanDensityPreview, bool showScanVolume, bool showScanHandles, float fineDensityPreviewRadius)
        {
            if (showScanDensityPreview && ScanDensityVertexCount > 0)
            {
                var camPosPreview = _viewport.Camera.Position;
                _gl.DepthMask(false);
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                _densityPointsProgram.Use();
                _densityPointsProgram.SetViewProjection(view, proj);
                _gl.BindVertexArray(_scanDensityBuffer.VaoHandle);

                int camLoc = _densityPointsProgram.GetUniformLocation("uCameraXZ");
                int radiusLoc = _densityPointsProgram.GetUniformLocation("uFadeRadius");
                int bandLoc = _densityPointsProgram.GetUniformLocation("uFadeBand");
                int fadeEnableLoc = _densityPointsProgram.GetUniformLocation("uEnableFade");
                int psLoc = _densityPointsProgram.GetUniformLocation("uPointSize");

                _gl.Uniform2(camLoc, camPosPreview.X, camPosPreview.Z);
                float fadeBand = Math.Clamp(fineDensityPreviewRadius * 0.22f, 260f, 1100f);
                _gl.Uniform1(radiusLoc, fineDensityPreviewRadius);
                _gl.Uniform1(bandLoc, fadeBand);

                if (ScanDensityBroadCount > 0)
                {
                    _gl.Uniform1(fadeEnableLoc, 0.0f);
                    _gl.Uniform1(psLoc, 3.5f);
                    _gl.DrawArrays(PrimitiveType.Points, 0, (uint)ScanDensityBroadCount);
                }

                int fineCount = ScanDensityVertexCount - ScanDensityBroadCount;
                if (fineCount > 0)
                {
                    _gl.Uniform1(fadeEnableLoc, 1.0f);
                    _gl.Uniform1(psLoc, 2.0f);
                    _gl.DrawArrays(PrimitiveType.Points, ScanDensityBroadCount, (uint)fineCount);
                }

                _gl.Disable(EnableCap.Blend);
                _gl.DepthMask(true);
            }

            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Greater);

            if (showScanVolume && showScanHandles && ScanHandleVertexCount > 0)
            {
                _gl.DepthMask(true);
                _gl.Disable(EnableCap.Blend);
                _gl.Disable(EnableCap.CullFace);
                _gizmoSolidProgram.Use();
                _gizmoSolidProgram.SetViewProjection(view, proj);
                _gl.BindVertexArray(_scanHandleBuffer.VaoHandle);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)ScanHandleVertexCount);
            }

            if (showScanVolume && ScanVolumeVertexCount > 0)
            {
                _gl.DepthMask(false);
                _lineProgram.Use();
                _lineProgram.SetViewProjection(view, proj);
                _gl.BindVertexArray(_scanVolumeBuffer.VaoHandle);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)ScanVolumeVertexCount);
                _gl.DepthMask(true);
            }
        }

        public void RenderAccum(Matrix4X4<float> view, Matrix4X4<float> proj, bool hasSelectionFill, bool hasScanHandlePlanes)
        {
            if (hasSelectionFill)
            {
                _flatAccumProgram.Use();
                _flatAccumProgram.SetViewProjection(view, proj);
                _gl.Uniform4(_flatAccumProgram.GetUniformLocation("uColor"), 0.88f, 0.42f, 1.0f, 0.22f);
                _gl.Disable(EnableCap.CullFace);
                _gl.BindVertexArray(_selectionFillBuffer.VaoHandle);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)SelectionFillVertexCount);
            }

            if (hasScanHandlePlanes)
            {
                _gizmoAccumProgram.Use();
                _gizmoAccumProgram.SetViewProjection(view, proj);
                _gl.Disable(EnableCap.CullFace);
                _gl.BindVertexArray(_scanHandleBuffer.VaoHandle);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)ScanHandleVertexCount);
            }
        }

        public void RenderReveal(Matrix4X4<float> view, Matrix4X4<float> proj, bool hasSelectionFill, bool hasScanHandlePlanes)
        {
            if (hasSelectionFill)
            {
                _flatRevealProgram.Use();
                _flatRevealProgram.SetViewProjection(view, proj);
                _gl.Uniform4(_flatRevealProgram.GetUniformLocation("uColor"), 0.88f, 0.42f, 1.0f, 0.22f);
                _gl.Disable(EnableCap.CullFace);
                _gl.BindVertexArray(_selectionFillBuffer.VaoHandle);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)SelectionFillVertexCount);
            }

            if (hasScanHandlePlanes)
            {
                _gizmoRevealProgram.Use();
                _gizmoRevealProgram.SetViewProjection(view, proj);
                _gl.Disable(EnableCap.CullFace);
                _gl.BindVertexArray(_scanHandleBuffer.VaoHandle);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)ScanHandleVertexCount);
            }
        }

        private bool ShouldRebuildScanDensity(ScanVolumeSettings s, float gridPlaneY, float scanFineTargetStep)
        {
            if (!_scanDensityBufferValid)
            {
                return true;
            }

            if (!_lastDensityScanVolume.Equals(s))
            {
                return true;
            }

            if (MathF.Abs(_lastDensityFineTargetStep - scanFineTargetStep) > 0.01f)
            {
                return true;
            }

            if (MathF.Abs(_lastDensityGridPlaneY - gridPlaneY) > 0.01f)
            {
                return true;
            }

            var cam = _viewport.Camera.Position;
            float dx = cam.X - _lastDensityCameraPos.X;
            float dz = cam.Z - _lastDensityCameraPos.Z;
            return (dx * dx) + (dz * dz) >= (ScanDensityRebuildMoveThreshold * ScanDensityRebuildMoveThreshold);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _scanVolumeBuffer.Dispose();
            _scanHandleBuffer.Dispose();
            _scanDensityBuffer.Dispose();
            _selectionFillBuffer.Dispose();
            _gizmoSolidProgram.Dispose();
            _densityPointsProgram.Dispose();
            _gizmoAccumProgram.Dispose();
            _gizmoRevealProgram.Dispose();
            _flatAccumProgram.Dispose();
            _flatRevealProgram.Dispose();
            _lineProgram.Dispose();
            _disposed = true;
        }
    }
}
