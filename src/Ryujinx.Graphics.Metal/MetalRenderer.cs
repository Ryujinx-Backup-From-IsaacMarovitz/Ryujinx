using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Shader.Translation;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;
using System;
using System.Runtime.Versioning;

namespace Ryujinx.Graphics.Metal
{
    [SupportedOSPlatform("macos")]
    public sealed class MetalRenderer : IRenderer
    {
        private readonly MTLDevice _device;
        private readonly MTLCommandQueue _queue;
        private readonly Func<CAMetalLayer> _getMetalLayer;

        private Pipeline _pipeline;
        private Window _window;

        internal BufferManager BufferManager { get; private set; }
        internal HelperShader HelperShader { get; private set; }

        public event EventHandler<ScreenCaptureImageInfo> ScreenCaptured;
        public bool PreferThreading => true;
        public IPipeline Pipeline => _pipeline;
        public IWindow Window => _window;

        public MetalRenderer(Func<CAMetalLayer> metalLayer)
        {
            _device = MTLDevice.CreateSystemDefaultDevice();

            if (_device.ArgumentBuffersSupport != MTLArgumentBuffersTier.Tier2)
            {
                throw new NotSupportedException("Metal backend requires Tier 2 Argument Buffer support.");
            }

            _queue = _device.NewCommandQueue();
            _getMetalLayer = metalLayer;
        }

        public void Initialize(GraphicsDebugLevel logLevel)
        {
            var layer = _getMetalLayer();
            layer.Device = _device;
            layer.FramebufferOnly = false;

            _window = new Window(this, layer);
            _pipeline = new Pipeline(this, _device, _queue);

            BufferManager = new BufferManager(this, _device);
            HelperShader = new HelperShader(this, _device);
        }

        public void BackgroundContextAction(Action action, bool alwaysBackground = false)
        {
            Logger.Warning?.Print(LogClass.Gpu, "Not Implemented!");
        }

        public BufferHandle CreateBufferSparse(ReadOnlySpan<BufferRange> storageBuffers)
        {
            throw new NotImplementedException();
        }

        public IImageArray CreateImageArray(int size, bool isBuffer)
        {
            throw new NotImplementedException();
        }

        public BufferHandle CreateBuffer(int size, BufferAccess access)
        {
            return BufferManager.CreateWithHandle(size);
        }

        public BufferHandle CreateBuffer(nint pointer, int size)
        {
            return BufferManager.CreateHostImported(pointer, size);
        }


        public IProgram CreateProgram(ShaderSource[] shaders, ShaderInfo info)
        {
            return new Program(shaders, _device);
        }

        public ISampler CreateSampler(SamplerCreateInfo info)
        {
            return new Sampler(_device, info);
        }

        public ITexture CreateTexture(TextureCreateInfo info)
        {
            if (info.Target == Target.TextureBuffer)
            {
                return new TextureBuffer(_device, this, _pipeline, info);
            }

            return new Texture(_device, this, _pipeline, info);
        }

        public ITextureArray CreateTextureArray(int size, bool isBuffer)
        {
            throw new NotImplementedException();
        }

        public bool PrepareHostMapping(IntPtr address, ulong size)
        {
            // TODO: Metal Host Mapping
            return false;
        }

        public void CreateSync(ulong id, bool strict)
        {
            Logger.Warning?.Print(LogClass.Gpu, "Not Implemented!");
        }

        public void DeleteBuffer(BufferHandle buffer)
        {
            BufferManager.Delete(buffer);
        }

        public PinnedSpan<byte> GetBufferData(BufferHandle buffer, int offset, int size)
        {
            return BufferManager.GetData(buffer, offset, size);
        }

        public Capabilities GetCapabilities()
        {
            // TODO: Finalize these values
            return new Capabilities(
                api: TargetApi.Metal,
                vendorName: HardwareInfoTools.GetVendor(),
                SystemMemoryType.UnifiedMemory,
                hasFrontFacingBug: false,
                hasVectorIndexingBug: false,
                needsFragmentOutputSpecialization: true,
                reduceShaderPrecision: true,
                supportsAstcCompression: true,
                supportsBc123Compression: true,
                supportsBc45Compression: true,
                supportsBc67Compression: true,
                supportsEtc2Compression: true,
                supports3DTextureCompression: true,
                supportsBgraFormat: true,
                supportsR4G4Format: false,
                supportsR4G4B4A4Format: true,
                supportsScaledVertexFormats: false,
                supportsSnormBufferTextureFormat: true,
                supportsSparseBuffer: false,
                supports5BitComponentFormat: true,
                supportsBlendEquationAdvanced: false,
                supportsFragmentShaderInterlock: true,
                supportsFragmentShaderOrderingIntel: false,
                supportsGeometryShader: false,
                supportsGeometryShaderPassthrough: false,
                supportsTransformFeedback: false,
                supportsImageLoadFormatted: false,
                supportsLayerVertexTessellation: false,
                supportsMismatchingViewFormat: true,
                supportsCubemapView: true,
                supportsNonConstantTextureOffset: false,
                supportsQuads: false,
                // TODO: Metal Bindless Support
                supportsSeparateSampler: false,
                supportsShaderBallot: false,
                supportsShaderBarrierDivergence: false,
                supportsShaderFloat64: false,
                supportsTextureGatherOffsets: false,
                supportsTextureShadowLod: false,
                supportsVertexStoreAndAtomics: false,
                supportsViewportIndexVertexTessellation: false,
                supportsViewportMask: false,
                supportsViewportSwizzle: false,
                supportsIndirectParameters: true,
                supportsDepthClipControl: false,
                uniformBufferSetIndex: 0,
                storageBufferSetIndex: 1,
                textureSetIndex: 2,
                imageSetIndex: 3,
                extraSetBaseIndex: 0,
                maximumExtraSets: 0,
                maximumUniformBuffersPerStage: Constants.MaxUniformBuffersPerStage,
                maximumStorageBuffersPerStage: Constants.MaxStorageBuffersPerStage,
                maximumTexturesPerStage: Constants.MaxTexturesPerStage,
                maximumImagesPerStage: Constants.MaxTextureBindings,
                maximumComputeSharedMemorySize: (int)_device.MaxThreadgroupMemoryLength,
                maximumSupportedAnisotropy: 0,
                shaderSubgroupSize: 256,
                storageBufferOffsetAlignment: 16,
                textureBufferOffsetAlignment: 16,
                gatherBiasPrecision: 0
            );
        }

        public ulong GetCurrentSync()
        {
            Logger.Warning?.Print(LogClass.Gpu, "Not Implemented!");
            return 0;
        }

        public HardwareInfo GetHardwareInfo()
        {
            return new HardwareInfo(HardwareInfoTools.GetVendor(), HardwareInfoTools.GetModel(), "Apple");
        }

        public IProgram LoadProgramBinary(byte[] programBinary, bool hasFragmentShader, ShaderInfo info)
        {
            throw new NotImplementedException();
        }

        public void SetBufferData(BufferHandle buffer, int offset, ReadOnlySpan<byte> data)
        {
            BufferManager.SetData(buffer, offset, data, _pipeline.EndRenderPassDelegate);
        }

        public void UpdateCounters()
        {
            // https://developer.apple.com/documentation/metal/gpu_counters_and_counter_sample_buffers/creating_a_counter_sample_buffer_to_store_a_gpu_s_counter_data_during_a_pass?language=objc
        }

        public void PreFrame()
        {

        }

        public ICounterEvent ReportCounter(CounterType type, EventHandler<ulong> resultHandler, float divisor, bool hostReserved)
        {
            // https://developer.apple.com/documentation/metal/gpu_counters_and_counter_sample_buffers/creating_a_counter_sample_buffer_to_store_a_gpu_s_counter_data_during_a_pass?language=objc
            var counterEvent = new CounterEvent();
            resultHandler?.Invoke(counterEvent, type == CounterType.SamplesPassed ? (ulong)1 : 0);
            return counterEvent;
        }

        public void ResetCounter(CounterType type)
        {
            // https://developer.apple.com/documentation/metal/gpu_counters_and_counter_sample_buffers/creating_a_counter_sample_buffer_to_store_a_gpu_s_counter_data_during_a_pass?language=objc
        }

        public void WaitSync(ulong id)
        {
            throw new NotImplementedException();
        }

        public void SetInterruptAction(Action<Action> interruptAction)
        {
            // Not needed for now
        }

        public void Screenshot()
        {
            // TODO: Screenshots
        }

        public void Dispose()
        {
            _pipeline.Dispose();
            _window.Dispose();
        }
    }
}
