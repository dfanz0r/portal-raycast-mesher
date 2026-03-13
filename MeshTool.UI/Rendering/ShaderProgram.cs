using Silk.NET.OpenGL;
using System;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Encapsulates an OpenGL shader program, handling compilation and lifecycle.
    /// </summary>
    public sealed class ShaderProgram : IDisposable
    {
        private readonly GL _gl;
        private readonly uint _programHandle;
        private readonly string _name;
        private bool _disposed;

        /// <summary>
        /// Gets the OpenGL program handle.
        /// </summary>
        public uint Handle => _programHandle;

        /// <summary>
        /// Gets the shader name for debugging/logging.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Creates a new shader program from vertex and fragment shader source code.
        /// </summary>
        /// <param name="gl">The OpenGL context.</param>
        /// <param name="name">A name for debugging/logging purposes.</param>
        /// <param name="vertexSource">The vertex shader source code.</param>
        /// <param name="fragmentSource">The fragment shader source code.</param>
        /// <param name="onError">Optional callback for compilation/link errors.</param>
        public ShaderProgram(GL gl, string name, string vertexSource, string fragmentSource, Action<string>? onError = null)
        {
            _gl = gl ?? throw new ArgumentNullException(nameof(gl));
            _name = name ?? throw new ArgumentNullException(nameof(name));

            uint vertexShader = CompileShader(ShaderType.VertexShader, vertexSource, "VS", onError);
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource, "FS", onError);

            _programHandle = LinkProgram(vertexShader, fragmentShader, onError);

            // Shaders are no longer needed after linking
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
        }

        private uint CompileShader(ShaderType type, string source, string stage, Action<string>? onError)
        {
            uint shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);

            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0)
            {
                string log = _gl.GetShaderInfoLog(shader);
                string message = $"[{stage} ERROR] [{_name}] {log}";
                onError?.Invoke(message);
            }

            return shader;
        }

        private uint LinkProgram(uint vertexShader, uint fragmentShader, Action<string>? onError)
        {
            uint program = _gl.CreateProgram();
            _gl.AttachShader(program, vertexShader);
            _gl.AttachShader(program, fragmentShader);
            _gl.LinkProgram(program);

            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0)
            {
                string log = _gl.GetProgramInfoLog(program);
                string message = $"[LINK ERROR] [{_name}] {log}";
                onError?.Invoke(message);
            }

            return program;
        }

        /// <summary>
        /// Activates this shader program for rendering.
        /// </summary>
        public void Use()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ShaderProgram));

            _gl.UseProgram(_programHandle);
        }

        /// <summary>
        /// Gets the location of a uniform variable.
        /// </summary>
        /// <param name="name">The uniform variable name.</param>
        /// <returns>The uniform location, or -1 if not found.</returns>
        public int GetUniformLocation(string name)
        {
            return _gl.GetUniformLocation(_programHandle, name);
        }

        /// <summary>
        /// Sets a 4x4 matrix uniform.
        /// </summary>
        public unsafe void SetMatrix4(int location, Silk.NET.Maths.Matrix4X4<float> matrix)
        {
            _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
        }

        /// <summary>
        /// Sets a 3-component vector uniform.
        /// </summary>
        public void SetVector3(int location, float x, float y, float z)
        {
            _gl.Uniform3(location, x, y, z);
        }

        /// <summary>
        /// Sets a 2-component vector uniform.
        /// </summary>
        public void SetVector2(int location, float x, float y)
        {
            _gl.Uniform2(location, x, y);
        }

        /// <summary>
        /// Sets a float uniform.
        /// </summary>
        public void SetFloat(int location, float value)
        {
            _gl.Uniform1(location, value);
        }

        /// <summary>
        /// Sets an integer uniform.
        /// </summary>
        public void SetInt(int location, int value)
        {
            _gl.Uniform1(location, value);
        }

        /// <summary>
        /// Sets a 4-component vector uniform.
        /// </summary>
        public void SetVector4(int location, float x, float y, float z, float w)
        {
            _gl.Uniform4(location, x, y, z, w);
        }

        /// <summary>
        /// Sets view and projection matrix uniforms (common to most shaders).
        /// </summary>
        public unsafe void SetViewProjection(Silk.NET.Maths.Matrix4X4<float> view, Silk.NET.Maths.Matrix4X4<float> projection)
        {
            int viewLoc = _gl.GetUniformLocation(_programHandle, "uView");
            int projLoc = _gl.GetUniformLocation(_programHandle, "uProjection");

            if (viewLoc >= 0)
                _gl.UniformMatrix4(viewLoc, 1, false, (float*)&view);
            if (projLoc >= 0)
                _gl.UniformMatrix4(projLoc, 1, false, (float*)&projection);
        }

        /// <summary>
        /// Disposes the shader program and releases OpenGL resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _gl.DeleteProgram(_programHandle);
                _disposed = true;
            }
        }
    }
}
