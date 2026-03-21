using MeshTool.Core.Data;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Handles point/surfel buffers, uploads, selection updates, and rendering.
    /// </summary>
    public sealed class PointRenderer : IDisposable
    {
        private const int PointInstanceStride = 8 * sizeof(float);

        private readonly GL _gl;
        private readonly Action<string>? _log;
        private readonly ShaderProgram _pointsProgram;
        private readonly ShaderProgram _surfelsProgram;
        private readonly DynamicBuffer _instanceBuffer;
        private readonly uint _vaoPoints;
        private readonly uint _vaoSurfels;
        private readonly uint _vboSurfelVerts;
        private readonly int _surfelVertexCount;
        private bool _disposed;

        public PointRenderer(GL gl, Action<string>? log)
        {
            _gl = gl;
            _log = log;
            _pointsProgram = new ShaderProgram(_gl, "Point", ShaderSource.PointVertex, ShaderSource.PointFragment, _log);

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
            _surfelsProgram = new ShaderProgram(_gl, "Surfel", ShaderSource.SurfelVertex, fsSurfel, _log);
            _instanceBuffer = new DynamicBuffer(_gl, PointInstanceStride);

            _vaoPoints = _gl.GenVertexArray();
            _vaoSurfels = _gl.GenVertexArray();
            _vboSurfelVerts = _gl.GenBuffer();

            int segments = 16;
            _surfelVertexCount = segments * 3;
            float[] surfelVerts = new float[_surfelVertexCount * 3];
            for (int i = 0; i < segments; i++)
            {
                float a1 = (float)i / segments * 2.0f * MathF.PI;
                float a2 = (float)(i + 1) / segments * 2.0f * MathF.PI;
                int idx = i * 9;
                surfelVerts[idx + 0] = 0f; surfelVerts[idx + 1] = 0f; surfelVerts[idx + 2] = 0f;
                surfelVerts[idx + 3] = MathF.Cos(a1); surfelVerts[idx + 4] = 0f; surfelVerts[idx + 5] = MathF.Sin(a1);
                surfelVerts[idx + 6] = MathF.Cos(a2); surfelVerts[idx + 7] = 0f; surfelVerts[idx + 8] = MathF.Sin(a2);
            }

            unsafe
            {
                _gl.BindVertexArray(_vaoPoints);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer.Handle);
                _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)0);
                _gl.EnableVertexAttribArray(0);
                _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(3 * sizeof(float)));
                _gl.EnableVertexAttribArray(1);
                _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(7 * sizeof(float)));
                _gl.EnableVertexAttribArray(2);

                _gl.BindVertexArray(_vaoSurfels);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboSurfelVerts);
                fixed (float* v = surfelVerts)
                {
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(surfelVerts.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
                }
                _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
                _gl.EnableVertexAttribArray(0);

                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer.Handle);
                _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)0);
                _gl.EnableVertexAttribArray(1);
                _gl.VertexAttribDivisor(1, 1);
                _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(3 * sizeof(float)));
                _gl.EnableVertexAttribArray(2);
                _gl.VertexAttribDivisor(2, 1);
                _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(6 * sizeof(float)));
                _gl.EnableVertexAttribArray(3);
                _gl.VertexAttribDivisor(3, 1);
                _gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(7 * sizeof(float)));
                _gl.EnableVertexAttribArray(4);
                _gl.VertexAttribDivisor(4, 1);

                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
                _gl.BindVertexArray(0);
            }
        }

        public uint PointsVaoHandle => _vaoPoints;
        public uint SurfelsVaoHandle => _vaoSurfels;
        public int SurfelVertexCount => _surfelVertexCount;

        public bool EnsureCapacity(int capacity)
        {
            bool changed = _instanceBuffer.EnsureCapacity(capacity);
            if (changed)
            {
                RebindInstanceVaos();
            }

            return changed;
        }

        public void UploadPoints(Vertex[] points, int pointCount, HashSet<int> selectedPointIndices, ref float minPointY, ref float maxPointY)
        {
            if (pointCount <= 0)
            {
                return;
            }

            int pointFloatCount = pointCount * 8;
            float[] vertices = ArrayPool<float>.Shared.Rent(pointFloatCount);
            try
            {
                for (int i = 0; i < pointCount; i++)
                {
                    WritePoint(vertices, i * 8, points[i], selectedPointIndices.Contains(i));
                    if (points[i].Position.Y < minPointY) minPointY = (float)points[i].Position.Y;
                    if (points[i].Position.Y > maxPointY) maxPointY = (float)points[i].Position.Y;
                }

                _instanceBuffer.UploadSubData(vertices, 0, pointCount);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(vertices);
            }
        }

        public void AppendPoints(Vertex[] points, int destinationIndex, HashSet<int> selectedPointIndices, ref float minPointY, ref float maxPointY)
        {
            if (points.Length == 0)
            {
                return;
            }

            int pointFloatCount = points.Length * 8;
            float[] vertices = ArrayPool<float>.Shared.Rent(pointFloatCount);
            try
            {
                for (int i = 0; i < points.Length; i++)
                {
                    int pointIndex = destinationIndex + i;
                    WritePoint(vertices, i * 8, points[i], selectedPointIndices.Contains(pointIndex));
                    if (points[i].Position.Y < minPointY) minPointY = (float)points[i].Position.Y;
                    if (points[i].Position.Y > maxPointY) maxPointY = (float)points[i].Position.Y;
                }

                _instanceBuffer.UploadSubData(vertices, destinationIndex, points.Length);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(vertices);
            }
        }

        public void UploadSelectionState(IReadOnlyList<Vertex> points, int pointCount, HashSet<int> selectedPointIndices)
        {
            if (pointCount <= 0)
            {
                return;
            }

            int pointFloatCount = pointCount * 8;
            float[] vertices = ArrayPool<float>.Shared.Rent(pointFloatCount);
            try
            {
                for (int i = 0; i < pointCount; i++)
                {
                    WritePoint(vertices, i * 8, points[i], selectedPointIndices.Contains(i));
                }

                _instanceBuffer.UploadSubData(vertices, 0, pointCount);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(vertices);
            }
        }

        public void RenderPoints(Matrix4X4<float> view, Matrix4X4<float> proj, int pointCount, bool useDynamicColorMapping, float minPointY, float maxPointY)
        {
            if (pointCount <= 0)
            {
                return;
            }

            _pointsProgram.Use();
            _pointsProgram.SetViewProjection(view, proj);
            _gl.BindVertexArray(_vaoPoints);
            _gl.PointSize(4.0f);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
            _gl.Uniform1(_pointsProgram.GetUniformLocation("uUseDynamicColor"), useDynamicColorMapping ? 1.0f : 0.0f);
            _gl.Uniform1(_pointsProgram.GetUniformLocation("uWorldMinY"), minPointY);
            _gl.Uniform1(_pointsProgram.GetUniformLocation("uWorldMaxY"), maxPointY);
            _gl.DrawArrays(PrimitiveType.Points, 0, (uint)pointCount);
        }

        public void RenderSurfels(Matrix4X4<float> view, Matrix4X4<float> proj, int pointCount, float avgDistance, float surfelScale, float currentTime, Vector3D<float>? hoveredCoordinate, bool useDynamicColorMapping, float minPointY, float maxPointY)
        {
            if (pointCount <= 0)
            {
                return;
            }

            _surfelsProgram.Use();
            _surfelsProgram.SetViewProjection(view, proj);
            _gl.Uniform1(_surfelsProgram.GetUniformLocation("uScale"), avgDistance * 0.5f * surfelScale);
            _gl.Uniform1(_surfelsProgram.GetUniformLocation("uCurrentTime"), currentTime);

            int hasHoveredLoc = _surfelsProgram.GetUniformLocation("uHasHovered");
            int hoveredPosLoc = _surfelsProgram.GetUniformLocation("uHoveredPos");
            if (hoveredCoordinate.HasValue)
            {
                _gl.Uniform1(hasHoveredLoc, 1.0f);
                _gl.Uniform3(hoveredPosLoc, (float)hoveredCoordinate.Value.X, (float)hoveredCoordinate.Value.Y, (float)hoveredCoordinate.Value.Z);
            }
            else
            {
                _gl.Uniform1(hasHoveredLoc, 0.0f);
            }

            _gl.Uniform1(_surfelsProgram.GetUniformLocation("uUseDynamicColor"), useDynamicColorMapping ? 1.0f : 0.0f);
            _gl.Uniform1(_surfelsProgram.GetUniformLocation("uWorldMinY"), minPointY);
            _gl.Uniform1(_surfelsProgram.GetUniformLocation("uWorldMaxY"), maxPointY);
            _gl.BindVertexArray(_vaoSurfels);
            _gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, (uint)_surfelVertexCount, (uint)pointCount);
        }

        private unsafe void RebindInstanceVaos()
        {
            _gl.BindVertexArray(_vaoPoints);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer.Handle);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(3 * sizeof(float)));
            _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(7 * sizeof(float)));

            _gl.BindVertexArray(_vaoSurfels);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer.Handle);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)0);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(3 * sizeof(float)));
            _gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(6 * sizeof(float)));
            _gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, PointInstanceStride, (void*)(7 * sizeof(float)));
        }

        private static void WritePoint(float[] target, int index, Vertex point, bool selected)
        {
            float nx = (float)point.Normal.X;
            float ny = (float)point.Normal.Y;
            float nz = (float)point.Normal.Z;
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

            target[index + 0] = (float)point.Position.X;
            target[index + 1] = (float)point.Position.Y;
            target[index + 2] = (float)point.Position.Z;
            target[index + 3] = nx;
            target[index + 4] = ny;
            target[index + 5] = nz;
            target[index + 6] = point.SpawnTime;
            target[index + 7] = selected ? 1f : 0f;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _instanceBuffer.Dispose();
            _gl.DeleteVertexArray(_vaoPoints);
            _gl.DeleteVertexArray(_vaoSurfels);
            _gl.DeleteBuffer(_vboSurfelVerts);
            _pointsProgram.Dispose();
            _surfelsProgram.Dispose();
            _disposed = true;
        }
    }
}
