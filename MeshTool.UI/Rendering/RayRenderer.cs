using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;
using MeshTool.Core.Data;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Handles ray buffer lifecycle, uploads, and OIT rendering passes.
    /// </summary>
    public sealed class RayRenderer : IDisposable
    {
        private const int RayVertexStride = 7 * sizeof(float);
        private const int FloatsPerRay = 14;
        private const float NormalRayLength = 300.0f;

        private readonly GL _gl;
        private readonly Action<string>? _log;
        private readonly ShaderProgram _accumProgram;
        private readonly ShaderProgram _revealProgram;
        private DynamicBuffer _buffer;
        private bool _disposed;

        public RayRenderer(GL gl, Action<string>? log)
        {
            _gl = gl;
            _log = log;
            _accumProgram = new ShaderProgram(_gl, "RayAccum", ShaderSource.RayOitVertex, ShaderSource.RayOitAccumFragment, _log);
            _revealProgram = new ShaderProgram(_gl, "RayReveal", ShaderSource.RayOitVertex, ShaderSource.RayOitRevealFragment, _log);
            _buffer = new DynamicBuffer(_gl, FloatsPerRay * sizeof(float));
        }

        public uint BufferHandle => _buffer.Handle;

        public void ConfigureVao(uint vaoHandle)
        {
            unsafe
            {
                _gl.BindVertexArray(vaoHandle);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer.Handle);
                _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, RayVertexStride, (void*)0);
                _gl.EnableVertexAttribArray(0);
                _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, RayVertexStride, (void*)(3 * sizeof(float)));
                _gl.EnableVertexAttribArray(1);
                _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, RayVertexStride, (void*)(6 * sizeof(float)));
                _gl.EnableVertexAttribArray(2);
            }
        }

        public bool EnsureCapacity(int capacity)
        {
            return _buffer.EnsureCapacity(capacity);
        }

        public void UploadFull(Vertex[] points, MeshTool.Core.Data.Ray[] rays, int pointCount, int rayCount)
        {
            if (rayCount <= 0)
            {
                return;
            }

            int rayFloatCount = rayCount * FloatsPerRay;
            float[] rayData = new float[rayFloatCount];

            for (int i = 0; i < rays.Length; i++)
            {
                int idx = i * FloatsPerRay;
                WriteMissRay(rayData, idx, rays[i]);
            }

            int offset = rays.Length * FloatsPerRay;
            for (int i = 0; i < pointCount; i++)
            {
                int idx = offset + i * FloatsPerRay;
                WriteNormalRay(rayData, idx, points[i]);
            }

            _buffer.UploadSubData(rayData, 0, rayCount);
        }

        public unsafe void ShiftExistingNormalRays(int missRayCount, int oldPointCount, int addedMisses)
        {
            if (addedMisses <= 0 || oldPointCount <= 0)
            {
                return;
            }

            int bytesPerRay = FloatsPerRay * sizeof(float);
            uint tempVbo = _gl.GenBuffer();
            int normalBytes = oldPointCount * bytesPerRay;
            try
            {
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, tempVbo);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)normalBytes, null, BufferUsageARB.DynamicDraw);

                nint oldNormalsOffset = (nint)(missRayCount * bytesPerRay);
                _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _buffer.Handle);
                _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, tempVbo);
                _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, oldNormalsOffset, 0, (nuint)normalBytes);

                nint shiftedNormalsOffset = (nint)((missRayCount + addedMisses) * bytesPerRay);
                _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, tempVbo);
                _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, _buffer.Handle);
                _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer, 0, shiftedNormalsOffset, (nuint)normalBytes);
            }
            finally
            {
                _gl.DeleteBuffer(tempVbo);
            }
        }

        public void UploadMissRays(MeshTool.Core.Data.Ray[] misses, int destinationRayIndex)
        {
            if (misses.Length == 0)
            {
                return;
            }

            float[] missData = new float[misses.Length * FloatsPerRay];
            for (int i = 0; i < misses.Length; i++)
            {
                WriteMissRay(missData, i * FloatsPerRay, misses[i]);
            }

            _buffer.UploadSubData(missData, destinationRayIndex, misses.Length);
        }

        public void UploadNormalRays(Vertex[] points, int destinationRayIndex)
        {
            if (points.Length == 0)
            {
                return;
            }

            float[] rayData = new float[points.Length * FloatsPerRay];
            for (int i = 0; i < points.Length; i++)
            {
                WriteNormalRay(rayData, i * FloatsPerRay, points[i]);
            }

            _buffer.UploadSubData(rayData, destinationRayIndex, points.Length);
        }

        public void RenderAccum(Matrix4X4<float> view, Matrix4X4<float> proj, Vector3D<float> camPos, float currentTime, uint vaoHandle, int missRayCount, int pointCount, bool hasMissRays, bool hasNormalRays)
        {
            _accumProgram.Use();
            _accumProgram.SetViewProjection(view, proj);
            _gl.Uniform1(_accumProgram.GetUniformLocation("uCurrentTime"), currentTime);
            _gl.Uniform3(_accumProgram.GetUniformLocation("uCameraPos"), camPos.X, camPos.Y, camPos.Z);
            DrawRaySegments(vaoHandle, missRayCount, pointCount, hasMissRays, hasNormalRays);
        }

        public void RenderReveal(Matrix4X4<float> view, Matrix4X4<float> proj, Vector3D<float> camPos, float currentTime, uint vaoHandle, int missRayCount, int pointCount, bool hasMissRays, bool hasNormalRays)
        {
            _revealProgram.Use();
            _revealProgram.SetViewProjection(view, proj);
            _gl.Uniform1(_revealProgram.GetUniformLocation("uCurrentTime"), currentTime);
            _gl.Uniform3(_revealProgram.GetUniformLocation("uCameraPos"), camPos.X, camPos.Y, camPos.Z);
            DrawRaySegments(vaoHandle, missRayCount, pointCount, hasMissRays, hasNormalRays);
        }

        private void DrawRaySegments(uint vaoHandle, int missRayCount, int pointCount, bool hasMissRays, bool hasNormalRays)
        {
            _gl.BindVertexArray(vaoHandle);
            if (hasMissRays)
            {
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(missRayCount * 2));
            }

            if (hasNormalRays)
            {
                _gl.DrawArrays(PrimitiveType.Lines, missRayCount * 2, (uint)(pointCount * 2));
            }
        }

        private static void WriteMissRay(float[] buffer, int idx, MeshTool.Core.Data.Ray ray)
        {
            buffer[idx + 0] = (float)ray.Start.X;
            buffer[idx + 1] = (float)ray.Start.Y;
            buffer[idx + 2] = (float)ray.Start.Z;
            buffer[idx + 3] = 1f;
            buffer[idx + 4] = 0f;
            buffer[idx + 5] = 0f;
            buffer[idx + 6] = ray.SpawnTime;
            buffer[idx + 7] = (float)ray.End.X;
            buffer[idx + 8] = (float)ray.End.Y;
            buffer[idx + 9] = (float)ray.End.Z;
            buffer[idx + 10] = 1f;
            buffer[idx + 11] = 0f;
            buffer[idx + 12] = 0f;
            buffer[idx + 13] = ray.SpawnTime;
        }

        private static void WriteNormalRay(float[] buffer, int idx, Vertex point)
        {
            float px = (float)point.Position.X;
            float py = (float)point.Position.Y;
            float pz = (float)point.Position.Z;

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

            buffer[idx + 0] = px;
            buffer[idx + 1] = py;
            buffer[idx + 2] = pz;
            buffer[idx + 3] = 1f;
            buffer[idx + 4] = 1f;
            buffer[idx + 5] = 0f;
            buffer[idx + 6] = 0f;
            buffer[idx + 7] = px + nx * NormalRayLength;
            buffer[idx + 8] = py + ny * NormalRayLength;
            buffer[idx + 9] = pz + nz * NormalRayLength;
            buffer[idx + 10] = 1f;
            buffer[idx + 11] = 1f;
            buffer[idx + 12] = 0f;
            buffer[idx + 13] = 0f;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _buffer.Dispose();
            _accumProgram.Dispose();
            _revealProgram.Dispose();
            _disposed = true;
        }
    }
}
