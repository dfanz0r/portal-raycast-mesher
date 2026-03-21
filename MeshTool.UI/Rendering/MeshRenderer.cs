using MeshTool.Core.Data;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Handles mesh buffer ownership, uploads, and opaque mesh rendering.
    /// </summary>
    public sealed class MeshRenderer : IDisposable
    {
        private const int MeshVertexStride = 6 * sizeof(float);

        private readonly GL _gl;
        private readonly ShaderProgram _meshProgram;
        private readonly VertexArray _meshBuffer;
        private bool _disposed;

        public MeshRenderer(GL gl, Action<string>? log)
        {
            _gl = gl;
            _meshProgram = new ShaderProgram(_gl, "Mesh", ShaderSource.MeshVertexSimple, ShaderSource.MeshFragmentSimple, log);
            _meshBuffer = new VertexArray(_gl, MeshVertexStride);
            _meshBuffer.SetAttribute(0, 3, VertexAttribPointerType.Float, 0);
            _meshBuffer.SetAttribute(1, 3, VertexAttribPointerType.Float, 3 * sizeof(float));
        }

        public uint VaoHandle => _meshBuffer.VaoHandle;

        public void UploadRaw(float[] data, int vertexCount)
        {
            _meshBuffer.UploadData(data, vertexCount, BufferUsageARB.StaticDraw);
        }

        public void UploadTriangles(System.Collections.Generic.List<Triangle> triangles, out int vertexCount)
        {
            vertexCount = triangles.Count * 3;
            if (vertexCount <= 0)
            {
                return;
            }

            int meshFloatCount = vertexCount * 6;
            float[] meshData = new float[meshFloatCount];
            for (int i = 0; i < triangles.Count; i++)
            {
                var tri = triangles[i];
                var edge1 = tri.B.Position - tri.A.Position;
                var edge2 = tri.C.Position - tri.A.Position;
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

                int idx = i * 18;
                WriteVertex(meshData, idx + 0, tri.A.Position.X, tri.A.Position.Y, tri.A.Position.Z, n.X, n.Y, n.Z);
                WriteVertex(meshData, idx + 6, tri.B.Position.X, tri.B.Position.Y, tri.B.Position.Z, n.X, n.Y, n.Z);
                WriteVertex(meshData, idx + 12, tri.C.Position.X, tri.C.Position.Y, tri.C.Position.Z, n.X, n.Y, n.Z);
            }

            _meshBuffer.UploadData(meshData, vertexCount, BufferUsageARB.StaticDraw);
        }

        public void Render(Matrix4X4<float> view, Matrix4X4<float> proj, int vertexCount)
        {
            if (vertexCount <= 0)
            {
                return;
            }

            _meshProgram.Use();
            _meshProgram.SetViewProjection(view, proj);
            _gl.BindVertexArray(_meshBuffer.VaoHandle);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
        }

        private static void WriteVertex(float[] meshData, int idx, double x, double y, double z, double nx, double ny, double nz)
        {
            meshData[idx + 0] = (float)x;
            meshData[idx + 1] = (float)y;
            meshData[idx + 2] = (float)z;
            meshData[idx + 3] = (float)nx;
            meshData[idx + 4] = (float)ny;
            meshData[idx + 5] = (float)nz;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _meshBuffer.Dispose();
            _meshProgram.Dispose();
            _disposed = true;
        }
    }
}
