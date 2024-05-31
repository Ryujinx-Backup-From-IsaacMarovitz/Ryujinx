using Ryujinx.Graphics.GAL;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using System;
using System.Buffers;
using System.Runtime.Versioning;

namespace Ryujinx.Graphics.Metal
{
    [SupportedOSPlatform("macos")]
    class Texture : TextureBase, ITexture
    {
        public Texture(MTLDevice device, MetalRenderer renderer, Pipeline pipeline, TextureCreateInfo info) : base(device, renderer, pipeline, info)
        {
            var descriptor = new MTLTextureDescriptor
            {
                PixelFormat = FormatTable.GetFormat(Info.Format),
                Usage = MTLTextureUsage.Unknown,
                SampleCount = (ulong)Info.Samples,
                TextureType = Info.Target.Convert(),
                Width = (ulong)Info.Width,
                Height = (ulong)Info.Height,
                MipmapLevelCount = (ulong)Info.Levels
            };

            if (info.Target == Target.Texture3D)
            {
                descriptor.Depth = (ulong)Info.Depth;
            }
            else if (info.Target != Target.Cubemap)
            {
                descriptor.ArrayLength = (ulong)Info.Depth;
            }

            descriptor.Swizzle = GetSwizzle(info, descriptor.PixelFormat);

            _mtlTexture = _device.NewTexture(descriptor);
        }

        public Texture(MTLDevice device, MetalRenderer renderer, Pipeline pipeline, TextureCreateInfo info, MTLTexture sourceTexture, int firstLayer, int firstLevel) : base(device, renderer, pipeline, info)
        {
            var pixelFormat = FormatTable.GetFormat(Info.Format);
            var textureType = Info.Target.Convert();
            NSRange levels;
            levels.location = (ulong)firstLevel;
            levels.length = (ulong)Info.Levels;
            NSRange slices;
            slices.location = (ulong)firstLayer;
            slices.length = 1;

            if (info.Target != Target.Texture3D && info.Target != Target.Cubemap)
            {
                slices.length = (ulong)Info.Depth;
            }

            var swizzle = GetSwizzle(info, pixelFormat);

            _mtlTexture = sourceTexture.NewTextureView(pixelFormat, textureType, levels, slices, swizzle);
        }

        private MTLTextureSwizzleChannels GetSwizzle(TextureCreateInfo info, MTLPixelFormat pixelFormat)
        {
            var swizzleR = Info.SwizzleR.Convert();
            var swizzleG = Info.SwizzleG.Convert();
            var swizzleB = Info.SwizzleB.Convert();
            var swizzleA = Info.SwizzleA.Convert();

            if (info.Format == Format.R5G5B5A1Unorm ||
                info.Format == Format.R5G5B5X1Unorm ||
                info.Format == Format.R5G6B5Unorm)
            {
                (swizzleB, swizzleR) = (swizzleR, swizzleB);
            }
            else if (pixelFormat == MTLPixelFormat.ABGR4Unorm || info.Format == Format.A1B5G5R5Unorm)
            {
                var tempB = swizzleB;
                var tempA = swizzleA;

                swizzleB = swizzleG;
                swizzleA = swizzleR;
                swizzleR = tempA;
                swizzleG = tempB;
            }

            return new MTLTextureSwizzleChannels
            {
                red = swizzleR,
                green = swizzleG,
                blue = swizzleB,
                alpha = swizzleA
            };
        }

        public void CopyTo(ITexture destination, int firstLayer, int firstLevel)
        {
            var blitCommandEncoder = _pipeline.GetOrCreateBlitEncoder();

            if (destination is Texture destinationTexture)
            {
                if (destinationTexture.Info.Target == Target.Texture3D)
                {
                    blitCommandEncoder.CopyFromTexture(
                        _mtlTexture,
                        0,
                        (ulong)firstLevel,
                        new MTLOrigin { x = 0, y = 0, z = (ulong)firstLayer },
                        new MTLSize { width = (ulong)Math.Min(Info.Width, destinationTexture.Info.Width), height = (ulong)Math.Min(Info.Height, destinationTexture.Info.Height), depth = 1},
                        destinationTexture._mtlTexture,
                        0,
                        (ulong)firstLevel,
                        new MTLOrigin { x = 0, y = 0, z = (ulong)firstLayer });
                }
                else
                {
                    blitCommandEncoder.CopyFromTexture(
                        _mtlTexture,
                        (ulong)firstLayer,
                        (ulong)firstLevel,
                        destinationTexture._mtlTexture,
                        (ulong)firstLayer,
                        (ulong)firstLevel,
                        _mtlTexture.ArrayLength,
                        _mtlTexture.MipmapLevelCount);
                }
            }
        }

        public void CopyTo(ITexture destination, int srcLayer, int dstLayer, int srcLevel, int dstLevel)
        {
            var blitCommandEncoder = _pipeline.GetOrCreateBlitEncoder();

            if (destination is Texture destinationTexture)
            {
                if (destinationTexture.Info.Target == Target.Texture3D)
                {
                    blitCommandEncoder.CopyFromTexture(
                        _mtlTexture,
                        0,
                        (ulong)srcLevel,
                        new MTLOrigin { x = 0, y = 0, z = (ulong)srcLayer },
                        new MTLSize { width = (ulong)Math.Min(Info.Width, destinationTexture.Info.Width), height = (ulong)Math.Min(Info.Height, destinationTexture.Info.Height), depth = 1},
                        destinationTexture._mtlTexture,
                        0,
                        (ulong)dstLevel,
                        new MTLOrigin { x = 0, y = 0, z = (ulong)dstLayer });
                }
                else
                {
                    blitCommandEncoder.CopyFromTexture(
                        _mtlTexture,
                        (ulong)srcLayer,
                        (ulong)srcLevel,
                        destinationTexture._mtlTexture,
                        (ulong)dstLayer,
                        (ulong)dstLevel,
                        _mtlTexture.ArrayLength,
                        _mtlTexture.MipmapLevelCount);
                }
            }
        }

        public void CopyTo(ITexture destination, Extents2D srcRegion, Extents2D dstRegion, bool linearFilter)
        {
            _pipeline.BlitColor(this, destination, srcRegion, dstRegion, linearFilter);
        }

        public void CopyTo(BufferRange range, int layer, int level, int stride)
        {
            int outSize = Info.GetMipSize(level);
            int hostSize = GetBufferDataLength(outSize);
            int offset = range.Offset;

            var buffer = _renderer.BufferManager.GetBuffer(range.Handle, true);

            if (PrepareOutputBuffer(hostSize, buffer, out MTLBuffer copyToBuffer, out BufferHolder tempCopyHolder))
            {
                offset = 0;
            }

            CopyFromOrToBuffer(copyToBuffer, _mtlTexture, hostSize, true, layer, level, 1, 1, singleSlice: true, offset, stride);

            if (tempCopyHolder != null)
            {
                CopyDataToOutputBuffer(tempCopyHolder, buffer, hostSize, range.Offset);
                tempCopyHolder.Dispose();
            }
        }

        public ITexture CreateView(TextureCreateInfo info, int firstLayer, int firstLevel)
        {
            return new Texture(_device, _renderer, _pipeline, info, _mtlTexture, firstLayer, firstLevel);
        }

        public PinnedSpan<byte> GetData()
        {
            throw new NotImplementedException();
        }

        public PinnedSpan<byte> GetData(int layer, int level)
        {
            var blitCommandEncoder = _pipeline.GetOrCreateBlitEncoder();

            ulong bytesPerRow = (ulong)Info.GetMipStride(level);
            ulong length = bytesPerRow * (ulong)Info.Height;
            ulong bytesPerImage = 0;
            if (_mtlTexture.TextureType == MTLTextureType.Type3D)
            {
                bytesPerImage = length;
            }

            unsafe
            {
                var mtlBuffer = _device.NewBuffer(length, MTLResourceOptions.ResourceStorageModeShared);

                blitCommandEncoder.CopyFromTexture(
                    _mtlTexture,
                    (ulong)layer,
                    (ulong)level,
                    new MTLOrigin(),
                    new MTLSize { width = _mtlTexture.Width, height = _mtlTexture.Height, depth = _mtlTexture.Depth },
                    mtlBuffer,
                    0,
                    bytesPerRow,
                    bytesPerImage
                );

                return new PinnedSpan<byte>(mtlBuffer.Contents.ToPointer(), (int)length, () => mtlBuffer.Dispose());
            }
        }

        public void SetData(IMemoryOwner<byte> data)
        {
            SetData(data.Memory.Span, 0, 0, Info.GetLayers(), Info.Levels, singleSlice: false);
            data.Dispose();
        }

        public void SetData(IMemoryOwner<byte> data, int layer, int level)
        {
            SetData(data.Memory.Span, layer, level, 1, 1, singleSlice: true);
            data.Dispose();
        }

        public void SetData(IMemoryOwner<byte> data, int layer, int level, Rectangle<int> region)
        {
            SetData(data.Memory.Span, layer, level, 1, 1, singleSlice: true, region);
            data.Dispose();
        }

        private void SetData(ReadOnlySpan<byte> data, int layer, int level, int layers, int levels, bool singleSlice, Rectangle<int>? region = null)
        {
            int bufferDataLegnth = data.Length;

            using var bufferHolder = _renderer.BufferManager.Create(bufferDataLegnth);

            CopyDataToBuffer(bufferHolder.GetDataStorage(0, bufferDataLegnth), data);

            var buffer = bufferHolder.GetBuffer();

            if (region.HasValue)
            {

            }
            else
            {

            }
        }

        private int GetBufferDataLength(int length)
        {
            if (NeedsD24S8Conversion())
            {
                return length * 2;
            }

            return length;
        }

        private Format GetCompatibleGalFormat(Format format)
        {
            if (NeedsD24S8Conversion())
            {
                return Format.D32FloatS8Uint;
            }

            return format;
        }

        private void CopyDataToBuffer(Span<byte> storage, ReadOnlySpan<byte> input)
        {
            if (NeedsD24S8Conversion())
            {
                FormatConverter.ConvertD24S8ToD32FS8(storage, input);
                return;
            }

            input.CopyTo(storage);
        }

        private ReadOnlySpan<byte> GetDataFromBuffer(ReadOnlySpan<byte> storage, int size, Span<byte> output)
        {
            if (NeedsD24S8Conversion())
            {
                if (output.IsEmpty)
                {
                    output = new byte[GetBufferDataLength(size)];
                }

                FormatConverter.ConvertD32FS8ToD24S8(output, storage);
                return output;
            }

            return storage;
        }

        private bool PrepareOutputBuffer(int hostSize, MTLBuffer target, out MTLBuffer copyTarget, out BufferHolder copyTargetHolder)
        {
            if (NeedsD24S8Conversion())
            {
                copyTargetHolder = _renderer.BufferManager.Create(hostSize);
                copyTarget = copyTargetHolder.GetBuffer();

                return true;
            }

            copyTarget = target;
            copyTargetHolder = null;

            return false;
        }

        private void CopyDataToOutputBuffer(BufferHolder hostData, MTLBuffer copyTarget, int hostSize, int dstOffset)
        {
            if (NeedsD24S8Conversion())
            {
                _renderer.HelperShader.ConvertD32S8ToD24S8(_renderer, hostData, copyTarget, hostSize / (2 * sizeof(int)), dstOffset);
            }
        }

        private bool NeedsD24S8Conversion()
        {
            return FormatTable.IsD24S8(Info.Format) && MTLFormat == MTLPixelFormat.Depth32FloatStencil8;
        }

        public void CopyFromOrToBuffer(
            MTLBuffer buffer,
            MTLTexture texture,
            int size,
            bool to,
            int dstLayer,
            int dstLevel,
            int dstLayers,
            int dstLevels,
            bool singleSlice,
            int offset = 0,
            int stride = 0)
        {
            bool is3D = Info.Target == Target.Texture3D;
            int width = Math.Max(1, Info.Width >> dstLevel);
            int height = Math.Max(1, Info.Height >> dstLevel);
            int depth = is3D && !singleSlice ? Math.Max(1, Info.Depth >> dstLevel) : 1;
            int layer = is3D ? 0 : dstLayer;
            int layers = dstLayers;
            int levels = dstLevels;

            for (int level = 0; level < levels; level++)
            {
                int mipSize = GetBufferDataLength(is3D && !singleSlice
                    ? Info.GetMipSize(dstLevel + level)
                    : Info.GetMipSize2D(dstLevel + level) * dstLayers);

                int endOffset = offset + mipSize;

                if ((uint)endOffset > (uint)size)
                {
                    break;
                }

                int rowLength = ((stride == 0 ? Info.GetMipStride(dstLevel + level) : stride) / Info.BytesPerPixel) * Info.BlockWidth;

                int z = is3D ? dstLayer : 0;

                if (to)
                {
                    _pipeline.GetOrCreateBlitEncoder().CopyFromTexture(
                        texture,
                        0,
                        (ulong)level,
                        new MTLOrigin { x = 0, y = 0, z = (ulong)z },
                        new MTLSize { width = (ulong)width, height = (ulong)height, depth = (ulong)depth },
                        buffer,
                        (ulong)offset,
                        (ulong)rowLength,
                        0);
                }
                else
                {
                    _pipeline.GetOrCreateBlitEncoder().CopyFromBuffer(
                        buffer,
                        (ulong)offset,
                        (ulong)rowLength,
                        0,
                        new MTLSize { width = (ulong)width, height = (ulong)height, depth = (ulong)depth },
                        texture,
                        0,
                        (ulong)level,
                        new MTLOrigin { x = 0, y = 0, z = (ulong)z });
                }

                offset += mipSize;

                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);

                if (Info.Target == Target.Texture3D)
                {
                    depth = Math.Max(1, depth >> 1);
                }
            }
        }

        public void CopyFromOrToBuffer(
            MTLBuffer buffer,
            MTLTexture texture,
            int size,
            bool to,
            int dstLayer,
            int dstLevel,
            int x,
            int y,
            int width,
            int height)
        {
            if (to)
            {

            }
            else
            {

            }
        }

        public void SetStorage(BufferRange buffer)
        {
            throw new NotImplementedException();
        }
    }
}
