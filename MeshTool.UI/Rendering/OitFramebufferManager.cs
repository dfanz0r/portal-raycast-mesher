using Silk.NET.OpenGL;
using System;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Manages framebuffers for Order-Independent Transparency (OIT) rendering.
    /// Handles MSAA framebuffers, resolve framebuffers, and OIT accumulation/reveal buffers.
    /// </summary>
    public sealed class OitFramebufferManager : IDisposable
    {
        private readonly GL _gl;
        private readonly Action<string>? _logger;

        // MSAA FBO
        private uint _msaaFbo;
        private uint _msaaColor;
        private uint _msaaAccum;
        private uint _msaaReveal;
        private uint _msaaDepth;

        // Resolve FBO
        private uint _resolveFbo;
        private uint _resolveColor;
        private uint _resolveDepth;

        // OIT Resolve FBOs
        private uint _oitAccumResolveFbo;
        private uint _oitRevealResolveFbo;
        private uint _oitAccumColor;
        private uint _oitRevealColor;

        // MSAA Accum/Reveal FBOs (share depth with main MSAA FBO)
        private uint _msaaAccumFbo;
        private uint _msaaRevealFbo;

        private int _width;
        private int _height;
        private int _msaaSamples;
        private bool _disposed;

        /// <summary>
        /// Gets the MSAA framebuffer handle.
        /// </summary>
        public uint MsaaFbo => _msaaFbo;

        /// <summary>
        /// Gets the MSAA accumulation framebuffer handle.
        /// </summary>
        public uint MsaaAccumFbo => _msaaAccumFbo;

        /// <summary>
        /// Gets the MSAA reveal framebuffer handle.
        /// </summary>
        public uint MsaaRevealFbo => _msaaRevealFbo;

        /// <summary>
        /// Gets the resolve framebuffer handle.
        /// </summary>
        public uint ResolveFbo => _resolveFbo;

        /// <summary>
        /// Gets the resolved color texture.
        /// </summary>
        public uint ResolveColorTexture => _resolveColor;

        /// <summary>
        /// Gets the OIT accumulation texture.
        /// </summary>
        public uint OitAccumTexture => _oitAccumColor;

        /// <summary>
        /// Gets the OIT reveal texture.
        /// </summary>
        public uint OitRevealTexture => _oitRevealColor;

        /// <summary>
        /// Gets the current width.
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// Gets the current height.
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// Gets the number of MSAA samples.
        /// </summary>
        public int MsaaSamples => _msaaSamples;

        /// <summary>
        /// Creates a new OitFramebufferManager.
        /// </summary>
        /// <param name="gl">The OpenGL context.</param>
        /// <param name="msaaSamples">Number of MSAA samples (will be clamped to max supported).</param>
        /// <param name="logger">Optional logger for error messages.</param>
        public OitFramebufferManager(GL gl, int msaaSamples = 4, Action<string>? logger = null)
        {
            _gl = gl ?? throw new ArgumentNullException(nameof(gl));
            _logger = logger;

            // Clamp MSAA samples to max supported
            try
            {
                int maxSamples = _gl.GetInteger(GLEnum.MaxSamples);
                _msaaSamples = Math.Min(msaaSamples, maxSamples);
                if (_msaaSamples > 1)
                {
                    _gl.Enable(EnableCap.Multisample);
                    _logger?.Invoke($"[GL] MSAA Enabled with {_msaaSamples} samples.");
                }
                else
                {
                    _logger?.Invoke($"[GL] MSAA not supported (MaxSamples = {maxSamples}).");
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[GL] MSAA check failed: {ex.Message}");
                _msaaSamples = 1;
            }
        }

        /// <summary>
        /// Ensures framebuffers are sized correctly for the given dimensions.
        /// </summary>
        /// <param name="width">The required width.</param>
        /// <param name="height">The required height.</param>
        /// <returns>True if framebuffers were recreated.</returns>
        public bool EnsureSize(int width, int height)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OitFramebufferManager));

            if (width == _width && height == _height && _msaaFbo != 0)
                return false;

            CleanupFramebuffers();
            CreateFramebuffers(width, height);
            return true;
        }

        private unsafe void CreateFramebuffers(int width, int height)
        {
            _width = width;
            _height = height;

            // 1. MSAA FBO (Renderbuffers)
            _msaaFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);

            _msaaColor = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColor);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.Rgba8, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaColor);

            _msaaAccum = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaAccum);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.Rgba16f, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, RenderbufferTarget.Renderbuffer, _msaaAccum);

            _msaaReveal = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaReveal);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.Rgba8, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2, RenderbufferTarget.Renderbuffer, _msaaReveal);

            _msaaDepth = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepth);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)_msaaSamples, InternalFormat.DepthComponent32f, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);

            var status1 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status1 != GLEnum.FramebufferComplete)
                _logger?.Invoke($"[GL ERROR] MSAA FBO incomplete: {status1}");

            // 2. Resolve FBO (Textures)
            _resolveFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFbo);

            _resolveColor = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _resolveColor);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _resolveColor, 0);

            _resolveDepth = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _resolveDepth);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32f, (uint)width, (uint)height, 0, PixelFormat.DepthComponent, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _resolveDepth, 0);

            var status2 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status2 != GLEnum.FramebufferComplete)
                _logger?.Invoke($"[GL ERROR] Resolve FBO incomplete: {status2}");

            // 3. OIT resolve textures/FBOs (single sample)
            _oitAccumResolveFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _oitAccumResolveFbo);

            _oitAccumColor = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _oitAccumColor);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.HalfFloat, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _oitAccumColor, 0);

            var status3 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status3 != GLEnum.FramebufferComplete)
                _logger?.Invoke($"[GL ERROR] OIT Accum Resolve FBO incomplete: {status3}");

            _oitRevealResolveFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _oitRevealResolveFbo);

            _oitRevealColor = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _oitRevealColor);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _oitRevealColor, 0);

            var status4 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status4 != GLEnum.FramebufferComplete)
                _logger?.Invoke($"[GL ERROR] OIT Reveal Resolve FBO incomplete: {status4}");

            // 4. MSAA OIT FBOs (single color attachment each, shared depth)
            _msaaAccumFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaAccumFbo);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaAccum);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);
            var status5 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status5 != GLEnum.FramebufferComplete)
                _logger?.Invoke($"[GL ERROR] MSAA Accum FBO incomplete: {status5}");

            _msaaRevealFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaRevealFbo);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaReveal);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);
            var status6 = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status6 != GLEnum.FramebufferComplete)
                _logger?.Invoke($"[GL ERROR] MSAA Reveal FBO incomplete: {status6}");
        }

        private void CleanupFramebuffers()
        {
            if (_msaaFbo != 0) _gl.DeleteFramebuffer(_msaaFbo);
            if (_msaaAccumFbo != 0) _gl.DeleteFramebuffer(_msaaAccumFbo);
            if (_msaaRevealFbo != 0) _gl.DeleteFramebuffer(_msaaRevealFbo);
            if (_msaaColor != 0) _gl.DeleteRenderbuffer(_msaaColor);
            if (_msaaAccum != 0) _gl.DeleteRenderbuffer(_msaaAccum);
            if (_msaaReveal != 0) _gl.DeleteRenderbuffer(_msaaReveal);
            if (_msaaDepth != 0) _gl.DeleteRenderbuffer(_msaaDepth);

            if (_resolveFbo != 0) _gl.DeleteFramebuffer(_resolveFbo);
            if (_resolveColor != 0) _gl.DeleteTexture(_resolveColor);
            if (_resolveDepth != 0) _gl.DeleteTexture(_resolveDepth);
            if (_oitAccumResolveFbo != 0) _gl.DeleteFramebuffer(_oitAccumResolveFbo);
            if (_oitRevealResolveFbo != 0) _gl.DeleteFramebuffer(_oitRevealResolveFbo);
            if (_oitAccumColor != 0) _gl.DeleteTexture(_oitAccumColor);
            if (_oitRevealColor != 0) _gl.DeleteTexture(_oitRevealColor);

            _msaaFbo = _msaaAccumFbo = _msaaRevealFbo = 0;
            _msaaColor = _msaaAccum = _msaaReveal = _msaaDepth = 0;
            _resolveFbo = 0;
            _resolveColor = _resolveDepth = 0;
            _oitAccumResolveFbo = _oitRevealResolveFbo = 0;
            _oitAccumColor = _oitRevealColor = 0;
        }

        /// <summary>
        /// Binds the main MSAA framebuffer for opaque rendering.
        /// </summary>
        public void BindMsaaFramebuffer()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OitFramebufferManager));

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
            _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        }

        /// <summary>
        /// Binds the MSAA accumulation framebuffer for OIT pass.
        /// </summary>
        public void BindMsaaAccumFramebuffer()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OitFramebufferManager));

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaAccumFbo);
        }

        /// <summary>
        /// Binds the MSAA reveal framebuffer for OIT pass.
        /// </summary>
        public void BindMsaaRevealFramebuffer()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OitFramebufferManager));

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaRevealFbo);
        }

        /// <summary>
        /// Clears the MSAA accumulation buffer.
        /// </summary>
        public void ClearAccumBuffer()
        {
            BindMsaaAccumFramebuffer();
            _gl.ClearColor(0f, 0f, 0f, 0f);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        }

        /// <summary>
        /// Clears the MSAA reveal buffer.
        /// </summary>
        public void ClearRevealBuffer()
        {
            BindMsaaRevealFramebuffer();
            _gl.ClearColor(1f, 1f, 1f, 1f);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        }

        /// <summary>
        /// Resolves MSAA buffers to single-sample textures.
        /// </summary>
        public void ResolveBuffers()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OitFramebufferManager));

            // Resolve MSAA OIT attachments to single-sample OIT textures.
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaAccumFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _oitAccumResolveFbo);
            _gl.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaRevealFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _oitRevealResolveFbo);
            _gl.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

            // Resolve MSAA opaque color/depth to resolve textures
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFbo);
            _gl.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
        }

        /// <summary>
        /// Blits the resolved color buffer to the specified framebuffer.
        /// </summary>
        /// <param name="targetFbo">The target framebuffer.</param>
        public void BlitToFramebuffer(uint targetFbo)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OitFramebufferManager));

            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _resolveFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, targetFbo);
            _gl.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        }

        /// <summary>
        /// Disposes all framebuffer resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                CleanupFramebuffers();
                _disposed = true;
            }
        }
    }
}
