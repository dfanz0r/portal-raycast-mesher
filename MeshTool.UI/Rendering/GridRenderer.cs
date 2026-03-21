using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Handles grid rendering, including OIT passes and composite quad usage.
    /// </summary>
    public sealed class GridRenderer : IDisposable
    {
        private const int GridVertexStride = 3 * sizeof(float);

        private readonly GL _gl;
        private readonly Action<string>? _log;
        private readonly VertexArray _gridBuffer;
        private readonly ShaderProgram _gridAccumProgram;
        private readonly ShaderProgram _gridRevealProgram;
        private readonly ShaderProgram _compositeProgram;
        private bool _disposed;

        public GridRenderer(GL gl, Action<string>? log)
        {
            _gl = gl;
            _log = log;

            _gridAccumProgram = new ShaderProgram(_gl, "GridAccum", ShaderSource.GridClipVertex, ShaderSource.GridOitAccumFragment, _log);
            _gridRevealProgram = new ShaderProgram(_gl, "GridReveal", ShaderSource.GridClipVertex, ShaderSource.GridOitRevealFragment, _log);
            _compositeProgram = new ShaderProgram(_gl, "Composite", ShaderSource.GridRaycastVertex, ShaderSource.CompositeFragmentOit, _log);

            _gridBuffer = new VertexArray(_gl, GridVertexStride);
            float[] gridVerts =
            {
                -1f,  1f, 0f,
                -1f, -1f, 0f,
                 1f, -1f, 0f,
                -1f,  1f, 0f,
                 1f, -1f, 0f,
                 1f,  1f, 0f
            };
            _gridBuffer.UploadData(gridVerts, 6, BufferUsageARB.StaticDraw);
            _gridBuffer.SetAttribute(0, 3, VertexAttribPointerType.Float, 0);
        }

        public uint VaoHandle => _gridBuffer.VaoHandle;

        public void RenderAccum(Matrix4X4<float> view, Matrix4X4<float> proj, Vector3D<float> camPos, float gridPlaneY)
        {
            _gridAccumProgram.Use();
            SetGridUniforms(_gridAccumProgram, view, proj, camPos, gridPlaneY);
            _gl.BindVertexArray(_gridBuffer.VaoHandle);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        public void RenderReveal(Matrix4X4<float> view, Matrix4X4<float> proj, Vector3D<float> camPos, float gridPlaneY)
        {
            _gridRevealProgram.Use();
            SetGridUniforms(_gridRevealProgram, view, proj, camPos, gridPlaneY);
            _gl.BindVertexArray(_gridBuffer.VaoHandle);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        public void RenderComposite(uint opaqueTexture, uint accumTexture, uint revealTexture)
        {
            _compositeProgram.Use();

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, opaqueTexture);
            _gl.Uniform1(_compositeProgram.GetUniformLocation("uOpaqueColor"), 0);

            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, accumTexture);
            _gl.Uniform1(_compositeProgram.GetUniformLocation("uAccumColor"), 1);

            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.BindTexture(TextureTarget.Texture2D, revealTexture);
            _gl.Uniform1(_compositeProgram.GetUniformLocation("uRevealColor"), 2);

            _gl.BindVertexArray(_gridBuffer.VaoHandle);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void SetGridUniforms(ShaderProgram program, Matrix4X4<float> view, Matrix4X4<float> proj, Vector3D<float> camPos, float gridPlaneY)
        {
            program.SetViewProjection(view, proj);
            _gl.Uniform3(program.GetUniformLocation("uCameraPos"), camPos.X, camPos.Y, camPos.Z);
            _gl.Uniform1(program.GetUniformLocation("uGridPlaneY"), gridPlaneY);

            double camHeight = Math.Max(Math.Abs((double)(camPos.Y - gridPlaneY)), 0.0001);
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

            _gl.Uniform1(program.GetUniformLocation("uGridSpacingMinor"), spacingMinor);
            _gl.Uniform1(program.GetUniformLocation("uGridSpacingMajor0"), spacingMajor0);
            _gl.Uniform1(program.GetUniformLocation("uGridSpacingMajor1"), spacingMajor1);
            _gl.Uniform2(program.GetUniformLocation("uGridPhaseMinor"), phaseMinorX, phaseMinorZ);
            _gl.Uniform2(program.GetUniformLocation("uGridPhaseMajor0"), phaseMajor0X, phaseMajor0Z);
            _gl.Uniform2(program.GetUniformLocation("uGridPhaseMajor1"), phaseMajor1X, phaseMajor1Z);
            _gl.Uniform1(program.GetUniformLocation("uGridFade"), fade);

            float baseFadeStart = 85000.0f;
            float camHeightFadeStart = Math.Abs(camPos.Y - gridPlaneY) * 12.0f;
            float fadeStart = MathF.Max(baseFadeStart, camHeightFadeStart);
            _gl.Uniform1(program.GetUniformLocation("uGridFadeStart"), fadeStart);
            _gl.Uniform1(program.GetUniformLocation("uGridFadeEnd"), fadeStart * 1.15f);
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _gridBuffer.Dispose();
            _gridAccumProgram.Dispose();
            _gridRevealProgram.Dispose();
            _compositeProgram.Dispose();
            _disposed = true;
        }
    }
}
