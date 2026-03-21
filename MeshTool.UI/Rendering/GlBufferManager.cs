using Silk.NET.OpenGL;
using System;
using System.Runtime.InteropServices;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Encapsulates a Vertex Array Object (VAO) and its associated Vertex Buffer Object (VBO).
    /// </summary>
    public sealed class VertexArray : IDisposable
    {
        private readonly GL _gl;
        private uint _vao;
        private uint _vbo;
        private int _vertexCount;
        private int _vertexStride;
        private bool _disposed;

        /// <summary>
        /// Gets the VAO handle.
        /// </summary>
        public uint VaoHandle => _vao;

        /// <summary>
        /// Gets the VBO handle.
        /// </summary>
        public uint VboHandle => _vbo;

        /// <summary>
        /// Gets the current vertex count.
        /// </summary>
        public int VertexCount => _vertexCount;

        /// <summary>
        /// Creates a new VertexArray with the specified vertex stride.
        /// </summary>
        /// <param name="gl">The OpenGL context.</param>
        /// <param name="vertexStride">The size of each vertex in bytes.</param>
        /// <param name="initialCapacity">Initial buffer capacity in vertices.</param>
        public unsafe VertexArray(GL gl, int vertexStride, int initialCapacity = 0)
        {
            _gl = gl ?? throw new ArgumentNullException(nameof(gl));
            _vertexStride = vertexStride;

            _vao = gl.GenVertexArray();
            _vbo = gl.GenBuffer();

            if (initialCapacity > 0)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(initialCapacity * vertexStride), null, BufferUsageARB.DynamicDraw);
            }
        }

        /// <summary>
        /// Binds this VAO for rendering or configuration.
        /// </summary>
        public void Bind()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VertexArray));

            _gl.BindVertexArray(_vao);
        }

        /// <summary>
        /// Sets a vertex attribute pointer.
        /// </summary>
        /// <param name="location">The attribute location.</param>
        /// <param name="size">Number of components (1-4).</param>
        /// <param name="type">The data type.</param>
        /// <param name="offset">Byte offset from the start of the vertex.</param>
        /// <param name="normalized">Whether to normalize integer data.</param>
        public unsafe void SetAttribute(uint location, int size, VertexAttribPointerType type, int offset, bool normalized = false)
        {
            Bind();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.VertexAttribPointer(location, size, type, normalized, (uint)_vertexStride, (void*)offset);
            _gl.EnableVertexAttribArray(location);
        }

        public unsafe void SetAttribute(uint bufferHandle, uint location, int size, VertexAttribPointerType type, int stride, int offset, bool normalized = false, uint divisor = 0)
        {
            Bind();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, bufferHandle);
            _gl.VertexAttribPointer(location, size, type, normalized, (uint)stride, (void*)offset);
            _gl.EnableVertexAttribArray(location);
            _gl.VertexAttribDivisor(location, divisor);
        }

        /// <summary>
        /// Sets a vertex attribute pointer with instancing.
        /// </summary>
        public unsafe void SetAttributeInstanced(uint location, int size, VertexAttribPointerType type, int offset, bool normalized = false)
        {
            Bind();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.VertexAttribPointer(location, size, type, normalized, (uint)_vertexStride, (void*)offset);
            _gl.EnableVertexAttribArray(location);
            _gl.VertexAttribDivisor(location, 1);
        }

        /// <summary>
        /// Uploads vertex data to the buffer.
        /// </summary>
        /// <param name="data">The vertex data.</param>
        /// <param name="vertexCount">Number of vertices.</param>
        /// <param name="usage">Buffer usage hint.</param>
        public unsafe void UploadData(float[] data, int vertexCount, BufferUsageARB usage = BufferUsageARB.DynamicDraw)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VertexArray));

            _vertexCount = vertexCount;
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            fixed (float* ptr = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertexCount * _vertexStride), ptr, usage);
            }
        }

        /// <summary>
        /// Uploads vertex data to the buffer using a span.
        /// </summary>
        public unsafe void UploadData(ReadOnlySpan<float> data, int vertexCount, BufferUsageARB usage = BufferUsageARB.DynamicDraw)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VertexArray));

            _vertexCount = vertexCount;
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            fixed (float* ptr = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertexCount * _vertexStride), ptr, usage);
            }
        }

        /// <summary>
        /// Updates a portion of the buffer with new data.
        /// </summary>
        public unsafe void UpdateData(float[] data, int offsetVertices, int vertexCount)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(VertexArray));

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            fixed (float* ptr = data)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(offsetVertices * _vertexStride),
                    (nuint)(vertexCount * _vertexStride), ptr);
            }
        }

        /// <summary>
         /// Sets the vertex count without uploading data.
         /// </summary>
        public void SetVertexCount(int count)
        {
            _vertexCount = count;
        }

        /// <summary>
        /// Disposes the VAO and VBO.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_vao != 0)
                {
                    _gl.DeleteVertexArray(_vao);
                    _vao = 0;
                }
                if (_vbo != 0)
                {
                    _gl.DeleteBuffer(_vbo);
                    _vbo = 0;
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Manages a dynamic buffer that can grow as needed.
    /// </summary>
    public sealed class DynamicBuffer : IDisposable
    {
        private readonly GL _gl;
        private uint _vbo;
        private int _capacity;
        private int _stride;
        private bool _disposed;

        /// <summary>
        /// Gets the VBO handle.
        /// </summary>
        public uint Handle => _vbo;

        /// <summary>
        /// Gets the current capacity in elements.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Creates a new dynamic buffer.
        /// </summary>
        /// <param name="gl">The OpenGL context.</param>
        /// <param name="stride">Size of each element in bytes.</param>
        /// <param name="initialCapacity">Initial capacity in elements.</param>
        public unsafe DynamicBuffer(GL gl, int stride, int initialCapacity = 0)
        {
            _gl = gl ?? throw new ArgumentNullException(nameof(gl));
            _stride = stride;
            _capacity = initialCapacity;

            _vbo = gl.GenBuffer();

            if (initialCapacity > 0)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(initialCapacity * stride), null, BufferUsageARB.DynamicDraw);
            }
        }

        /// <summary>
        /// Binds this buffer.
        /// </summary>
        public void Bind()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DynamicBuffer));

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        }

        /// <summary>
        /// Ensures the buffer has at least the specified capacity, reallocating if necessary.
        /// </summary>
        /// <param name="requiredCapacity">Required capacity in elements.</param>
        /// <returns>True if the buffer was reallocated.</returns>
        public bool EnsureCapacity(int requiredCapacity)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DynamicBuffer));

            if (requiredCapacity <= _capacity)
                return false;

            int newCapacity = Math.Max(_capacity * 2, requiredCapacity);
            Reallocate(newCapacity);
            return true;
        }

        private unsafe void Reallocate(int newCapacity)
        {
            uint newVbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, newVbo);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(newCapacity * _stride), null, BufferUsageARB.DynamicDraw);

            if (_capacity > 0)
            {
                // Copy old data
                _gl.BindBuffer(BufferTargetARB.CopyReadBuffer, _vbo);
                _gl.BindBuffer(BufferTargetARB.CopyWriteBuffer, newVbo);
                _gl.CopyBufferSubData(CopyBufferSubDataTarget.CopyReadBuffer, CopyBufferSubDataTarget.CopyWriteBuffer,
                    0, 0, (nuint)(_capacity * _stride));
            }

            _gl.DeleteBuffer(_vbo);
            _vbo = newVbo;
            _capacity = newCapacity;
        }

        /// <summary>
        /// Uploads data to the buffer.
        /// </summary>
        public unsafe void UploadData(float[] data, int elementCount)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DynamicBuffer));

            EnsureCapacity(elementCount);
            Bind();

            fixed (float* ptr = data)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(elementCount * _stride), ptr);
            }
        }

        /// <summary>
        /// Uploads data to a portion of the buffer.
        /// </summary>
        public unsafe void UploadSubData(float[] data, int offsetElements, int elementCount)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DynamicBuffer));

            Bind();

            fixed (float* ptr = data)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(offsetElements * _stride),
                    (nuint)(elementCount * _stride), ptr);
            }
        }

        /// <summary>
        /// Disposes the buffer.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_vbo != 0)
                {
                    _gl.DeleteBuffer(_vbo);
                    _vbo = 0;
                }
                _disposed = true;
            }
        }
    }
}
