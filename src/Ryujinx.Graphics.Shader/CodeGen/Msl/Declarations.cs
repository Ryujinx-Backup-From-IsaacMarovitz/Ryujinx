using Ryujinx.Common;
using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using Ryujinx.Graphics.Shader.StructuredIr;
using Ryujinx.Graphics.Shader.Translation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ryujinx.Graphics.Shader.CodeGen.Msl
{
    static class Declarations
    {
        /*
         * Description of MSL Binding Model
         *
         * There are a few fundamental differences between how GLSL and MSL handle I/O.
         * This comment will set out to describe the reasons why things are done certain ways
         * and to describe the overall binding model that we're striving for here.
         *
         * Main I/O Structs
         *
         * Each stage has a main input and output struct (if applicable) labeled as [Stage][In/Out], i.e VertexIn.
         * Every field within these structs is labeled with an [[attribute(n)]] property,
         * and the overall struct is labeled with [[stage_in]] for input structs, and defined as the
         * output type of the main shader function for the output struct. This struct also contains special
         * attribute-based properties like [[position]] that would be "built-ins" in a GLSL context.
         *
         * These structs are passed as inputs to all inline functions due to containing "built-ins"
         * that inline functions assume access to.
         *
         * Vertex & Zero Buffers
         *
         * Binding indices 0-16 are reserved for vertex buffers, and binding 18 is reserved for the zero buffer.
         *
         * Uniforms & Storage Buffers
         *
         * Uniforms and storage buffers are tightly packed into their respective argument buffers
         * (effectively ignoring binding indices at shader level), with each pointer to the corresponding
         * struct that defines the layout and fields of these buffers (usually just a single data array), laid
         * out one after the other in ascending order of their binding index.
         *
         * The uniforms argument buffer is always bound at a fixed index of 20.
         * The storage buffers argument buffer is always bound at a fixed index of 21.
         *
         * These structs are passed as inputs to all inline functions as in GLSL or SPIRV,
         * uniforms and storage buffers would be globals, and inline functions assume access to these buffers.
         *
         * Samplers & Textures
         *
         * Metal does not have a combined image sampler like sampler2D in GLSL, as a result we need to bind
         * an individual texture and a sampler object for each instance of a combined image sampler.
         * Samplers and textures are bound in a shared argument buffer. This argument buffer is tightly packed
         * (effectively ignoring binding indices at shader level), with texture and their samplers (if present)
         * laid out one after the other in ascending order of their binding index.
         *
         * The samplers and textures argument buffer is always bound at a fixed index of 22.
         *
         */

        public static void Declare(CodeGenContext context, StructuredProgramInfo info)
        {
            // TODO: Re-enable this warning
            context.AppendLine("#pragma clang diagnostic ignored \"-Wunused-variable\"");
            context.AppendLine();
            context.AppendLine("#include <metal_stdlib>");
            context.AppendLine("#include <simd/simd.h>");
            context.AppendLine();
            context.AppendLine("using namespace metal;");
            context.AppendLine();

            var fsi = (info.HelperFunctionsMask & HelperFunctionsMask.FSI) != 0;

            DeclareInputAttributes(context, info.IoDefinitions.Where(x => IsUserDefined(x, StorageKind.Input)));
            context.AppendLine();
            DeclareOutputAttributes(context, info.IoDefinitions.Where(x => x.StorageKind == StorageKind.Output));
            context.AppendLine();
            DeclareBufferStructures(context, context.Properties.ConstantBuffers.Values, true, fsi);
            DeclareBufferStructures(context, context.Properties.StorageBuffers.Values, false, fsi);
            DeclareTextures(context, context.Properties.Textures.Values);
            DeclareImages(context, context.Properties.Images.Values, fsi);

            if ((info.HelperFunctionsMask & HelperFunctionsMask.FindLSB) != 0)
            {
                AppendHelperFunction(context, "Ryujinx.Graphics.Shader/CodeGen/Msl/HelperFunctions/FindLSB.metal");
            }

            if ((info.HelperFunctionsMask & HelperFunctionsMask.FindMSBS32) != 0)
            {
                AppendHelperFunction(context, "Ryujinx.Graphics.Shader/CodeGen/Msl/HelperFunctions/FindMSBS32.metal");
            }

            if ((info.HelperFunctionsMask & HelperFunctionsMask.FindMSBU32) != 0)
            {
                AppendHelperFunction(context, "Ryujinx.Graphics.Shader/CodeGen/Msl/HelperFunctions/FindMSBU32.metal");
            }

            if ((info.HelperFunctionsMask & HelperFunctionsMask.SwizzleAdd) != 0)
            {
                AppendHelperFunction(context, "Ryujinx.Graphics.Shader/CodeGen/Msl/HelperFunctions/SwizzleAdd.metal");
            }
        }

        static bool IsUserDefined(IoDefinition ioDefinition, StorageKind storageKind)
        {
            return ioDefinition.StorageKind == storageKind && ioDefinition.IoVariable == IoVariable.UserDefined;
        }

        public static void DeclareLocals(CodeGenContext context, StructuredFunction function, ShaderStage stage, bool isMainFunc = false)
        {
            if (isMainFunc)
            {
                DeclareMemories(context, context.Properties.LocalMemories.Values, isShared: false);
                DeclareMemories(context, context.Properties.SharedMemories.Values, isShared: true);

                switch (stage)
                {
                    case ShaderStage.Vertex:
                        context.AppendLine("VertexOut out = {};");
                        // TODO: Only add if necessary
                        context.AppendLine("uint instance_index = instance_id + base_instance;");
                        break;
                    case ShaderStage.Fragment:
                        context.AppendLine("FragmentOut out = {};");
                        break;
                }

                // TODO: Only add if necessary
                if (stage != ShaderStage.Compute)
                {
                    // MSL does not give us access to [[thread_index_in_simdgroup]]
                    // outside compute. But we may still need to provide this value in frag/vert.
                    context.AppendLine("uint thread_index_in_simdgroup = simd_prefix_exclusive_sum(1);");
                }
            }

            foreach (AstOperand decl in function.Locals)
            {
                string name = context.OperandManager.DeclareLocal(decl);

                context.AppendLine(GetVarTypeName(decl.VarType) + " " + name + ";");
            }
        }

        public static string GetVarTypeName(AggregateType type, bool atomic = false)
        {
            var s32 = atomic ? "atomic_int" : "int";
            var u32 = atomic ? "atomic_uint" : "uint";

            return type switch
            {
                AggregateType.Void => "void",
                AggregateType.Bool => "bool",
                AggregateType.FP32 => "float",
                AggregateType.S32 => s32,
                AggregateType.U32 => u32,
                AggregateType.Vector2 | AggregateType.Bool => "bool2",
                AggregateType.Vector2 | AggregateType.FP32 => "float2",
                AggregateType.Vector2 | AggregateType.S32 => "int2",
                AggregateType.Vector2 | AggregateType.U32 => "uint2",
                AggregateType.Vector3 | AggregateType.Bool => "bool3",
                AggregateType.Vector3 | AggregateType.FP32 => "float3",
                AggregateType.Vector3 | AggregateType.S32 => "int3",
                AggregateType.Vector3 | AggregateType.U32 => "uint3",
                AggregateType.Vector4 | AggregateType.Bool => "bool4",
                AggregateType.Vector4 | AggregateType.FP32 => "float4",
                AggregateType.Vector4 | AggregateType.S32 => "int4",
                AggregateType.Vector4 | AggregateType.U32 => "uint4",
                _ => throw new ArgumentException($"Invalid variable type \"{type}\"."),
            };
        }

        private static void DeclareMemories(CodeGenContext context, IEnumerable<MemoryDefinition> memories, bool isShared)
        {
            string prefix = isShared ? "threadgroup " : string.Empty;

            foreach (var memory in memories)
            {
                string arraySize = "";
                if ((memory.Type & AggregateType.Array) != 0)
                {
                    arraySize = $"[{memory.ArrayLength}]";
                }
                var typeName = GetVarTypeName(memory.Type & ~AggregateType.Array);
                context.AppendLine($"{prefix}{typeName} {memory.Name}{arraySize};");
            }
        }

        private static void DeclareBufferStructures(CodeGenContext context, IEnumerable<BufferDefinition> buffers, bool constant, bool fsi)
        {
            var name = constant ? "ConstantBuffers" : "StorageBuffers";
            var addressSpace = constant ? "constant" : "device";

            List<string> argBufferPointers = [];

            // TODO: Avoid Linq if we can
            var sortedBuffers = buffers.OrderBy(x => x.Binding).ToArray();

            foreach (BufferDefinition buffer in sortedBuffers)
            {
                var needsPadding = buffer.Layout == BufferLayout.Std140;
                string fsiSuffix = constant && fsi ? " [[raster_order_group(0)]]" : "";

                argBufferPointers.Add($"{addressSpace} {Defaults.StructPrefix}_{buffer.Name}* {buffer.Name}{fsiSuffix};");

                context.AppendLine($"struct {Defaults.StructPrefix}_{buffer.Name}");
                context.EnterScope();

                foreach (StructureField field in buffer.Type.Fields)
                {
                    var type = field.Type;
                    type |= (needsPadding && (field.Type & AggregateType.Array) != 0) ? AggregateType.Vector4 : AggregateType.Invalid;

                    type &= ~AggregateType.Array;

                    string typeName = GetVarTypeName(type);
                    string arraySuffix = "";

                    if (field.Type.HasFlag(AggregateType.Array))
                    {
                        if (field.ArrayLength > 0)
                        {
                            arraySuffix = $"[{field.ArrayLength}]";
                        }
                        else
                        {
                            // Probably UB, but this is the approach that MVK takes
                            arraySuffix = "[1]";
                        }
                    }

                    context.AppendLine($"{typeName} {field.Name}{arraySuffix};");
                }

                context.LeaveScope(";");
                context.AppendLine();
            }

            context.AppendLine($"struct {name}");
            context.EnterScope();

            foreach (var pointer in argBufferPointers)
            {
                context.AppendLine(pointer);
            }

            context.LeaveScope(";");
            context.AppendLine();
        }

        private static void DeclareTextures(CodeGenContext context, IEnumerable<TextureDefinition> textures)
        {
            context.AppendLine("struct Textures");
            context.EnterScope();

            List<string> argBufferPointers = [];

            // TODO: Avoid Linq if we can
            var sortedTextures = textures.OrderBy(x => x.Binding).ToArray();

            foreach (TextureDefinition texture in sortedTextures)
            {
                var textureTypeName = texture.Type.ToMslTextureType();
                argBufferPointers.Add($"{textureTypeName} tex_{texture.Name};");

                if (!texture.Separate && texture.Type != SamplerType.TextureBuffer)
                {
                    argBufferPointers.Add($"sampler samp_{texture.Name};");
                }
            }

            foreach (var pointer in argBufferPointers)
            {
                context.AppendLine(pointer);
            }

            context.LeaveScope(";");
            context.AppendLine();
        }

        private static void DeclareImages(CodeGenContext context, IEnumerable<TextureDefinition> images, bool fsi)
        {
            context.AppendLine("struct Images");
            context.EnterScope();

            List<string> argBufferPointers = [];

            // TODO: Avoid Linq if we can
            var sortedImages = images.OrderBy(x => x.Binding).ToArray();

            foreach (TextureDefinition image in sortedImages)
            {
                var imageTypeName = image.Type.ToMslTextureType(true);
                string fsiSuffix = fsi ? " [[raster_order_group(0)]]" : "";

                argBufferPointers.Add($"{imageTypeName} {image.Name}{fsiSuffix};");
            }

            foreach (var pointer in argBufferPointers)
            {
                context.AppendLine(pointer);
            }

            context.LeaveScope(";");
            context.AppendLine();
        }

        private static void DeclareInputAttributes(CodeGenContext context, IEnumerable<IoDefinition> inputs)
        {
            if (context.Definitions.IaIndexing)
            {
                // Not handled
            }
            else
            {
                if (inputs.Any() || context.Definitions.Stage != ShaderStage.Compute)
                {
                    string prefix = "";

                    switch (context.Definitions.Stage)
                    {
                        case ShaderStage.Vertex:
                            context.AppendLine($"struct VertexIn");
                            break;
                        case ShaderStage.Fragment:
                            context.AppendLine($"struct FragmentIn");
                            break;
                    }

                    context.EnterScope();

                    if (context.Definitions.Stage == ShaderStage.Fragment)
                    {
                        // TODO: check if it's needed
                        context.AppendLine("float4 position [[position, invariant]];");
                        context.AppendLine("bool front_facing [[front_facing]];");
                        context.AppendLine("float2 point_coord [[point_coord]];");
                    }

                    foreach (var ioDefinition in inputs.OrderBy(x => x.Location))
                    {
                        string iq = string.Empty;

                        if (context.Definitions.Stage == ShaderStage.Fragment)
                        {
                            iq = context.Definitions.ImapTypes[ioDefinition.Location].GetFirstUsedType() switch
                            {
                                PixelImap.Constant => "[[flat]] ",
                                PixelImap.ScreenLinear => "[[center_no_perspective]] ",
                                _ => string.Empty,
                            };
                        }

                        string type = ioDefinition.IoVariable switch
                        {
                            // IoVariable.Position => "float4",
                            IoVariable.GlobalId => "uint3",
                            IoVariable.VertexId => "uint",
                            IoVariable.VertexIndex => "uint",
                            // IoVariable.PointCoord => "float2",
                            _ => GetVarTypeName(context.Definitions.GetUserDefinedType(ioDefinition.Location, isOutput: false))
                        };
                        string name = ioDefinition.IoVariable switch
                        {
                            // IoVariable.Position => "position",
                            IoVariable.GlobalId => "global_id",
                            IoVariable.VertexId => "vertex_id",
                            IoVariable.VertexIndex => "vertex_index",
                            // IoVariable.PointCoord => "point_coord",
                            _ => $"{Defaults.IAttributePrefix}{ioDefinition.Location}"
                        };
                        string suffix = ioDefinition.IoVariable switch
                        {
                            // IoVariable.Position => "[[position, invariant]]",
                            IoVariable.GlobalId => "[[thread_position_in_grid]]",
                            IoVariable.VertexId => "[[vertex_id]]",
                            // TODO: Avoid potential redeclaration
                            IoVariable.VertexIndex => "[[vertex_id]]",
                            // IoVariable.PointCoord => "[[point_coord]]",
                            IoVariable.UserDefined => context.Definitions.Stage == ShaderStage.Fragment ? $"[[user(loc{ioDefinition.Location})]]" : $"[[attribute({ioDefinition.Location})]]",
                            _ => ""
                        };

                        context.AppendLine($"{type} {name} {iq}{suffix};");
                    }

                    context.LeaveScope(";");
                }
            }
        }

        private static void DeclareOutputAttributes(CodeGenContext context, IEnumerable<IoDefinition> outputs)
        {
            if (context.Definitions.IaIndexing)
            {
                // Not handled
            }
            else
            {
                if (outputs.Any() || context.Definitions.Stage == ShaderStage.Fragment)
                {
                    string prefix = "";

                    switch (context.Definitions.Stage)
                    {
                        case ShaderStage.Vertex:
                            context.AppendLine($"struct VertexOut");
                            break;
                        case ShaderStage.Fragment:
                            context.AppendLine($"struct FragmentOut");
                            break;
                        case ShaderStage.Compute:
                            context.AppendLine($"struct KernelOut");
                            break;
                    }

                    context.EnterScope();

                    foreach (var ioDefinition in outputs.OrderBy(x => x.Location))
                    {
                        string type = ioDefinition.IoVariable switch
                        {
                            IoVariable.Position => "float4",
                            IoVariable.PointSize => "float",
                            IoVariable.FragmentOutputColor => GetVarTypeName(context.Definitions.GetFragmentOutputColorType(ioDefinition.Location)),
                            IoVariable.FragmentOutputDepth => "float",
                            IoVariable.ClipDistance => "float",
                            _ => GetVarTypeName(context.Definitions.GetUserDefinedType(ioDefinition.Location, isOutput: true))
                        };
                        string name = ioDefinition.IoVariable switch
                        {
                            IoVariable.Position => "position",
                            IoVariable.PointSize => "point_size",
                            IoVariable.FragmentOutputColor => $"color{ioDefinition.Location}",
                            IoVariable.FragmentOutputDepth => "depth",
                            IoVariable.ClipDistance => "clip_distance",
                            _ => $"{Defaults.OAttributePrefix}{ioDefinition.Location}"
                        };
                        string suffix = ioDefinition.IoVariable switch
                        {
                            IoVariable.Position => "[[position, invariant]]",
                            IoVariable.PointSize => "[[point_size]]",
                            IoVariable.UserDefined => $"[[user(loc{ioDefinition.Location})]]",
                            IoVariable.FragmentOutputColor => $"[[color({ioDefinition.Location})]]",
                            IoVariable.FragmentOutputDepth => "[[depth(any)]]",
                            IoVariable.ClipDistance => $"[[clip_distance]][{Defaults.TotalClipDistances}]",
                            _ => ""
                        };

                        context.AppendLine($"{type} {name} {suffix};");
                    }

                    context.LeaveScope(";");
                }
            }
        }

        private static void AppendHelperFunction(CodeGenContext context, string filename)
        {
            string code = EmbeddedResources.ReadAllText(filename);

            code = code.Replace("\t", CodeGenContext.Tab);

            context.AppendLine(code);
            context.AppendLine();
        }
    }
}
