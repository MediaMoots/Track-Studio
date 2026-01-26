using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapStudio.UI;
using CafeLibrary;
using BfresLibrary;
using BfresLibrary.Helpers;
using BfresLibrary.GX2;
using Syroot.Maths;
using Syroot.BinaryData;
using Toolbox.Core;

namespace TrackStudio.Tools
{
    /// <summary>
    /// Tool for optimizing BFRES vertex buffer formats.
    /// Reduces weight and index attribute sizes based on VertexSkinCount.
    /// </summary>
    public class VertexFormatOptimizerTool : IToolAction
    {
        public string Name => "Vertex Format Optimizer";
        public string Description => "Optimizes BFRES vertex buffer formats by reducing weight/index attribute sizes based on skin count. Processes all .bfres and .bfres.mc files in a selected folder.";

        public bool CanExecute()
        {
            return true;
        }

        public void Execute()
        {
            var dlg = new ImguiFolderDialog { Title = "Select Folder with BFRES Files" };
            if (!dlg.ShowDialog())
                return;

            var folder = dlg.SelectedPath;
            ProcessFolder(folder);
        }

        /// <summary>
        /// Processes all BFRES files in the given folder.
        /// </summary>
        public void ProcessFolder(string folder)
        {
            var bfresFiles = Directory.GetFiles(folder, "*.bfres", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(folder, "*.bfres.mc", SearchOption.AllDirectories))
                .ToList();

            if (bfresFiles.Count == 0)
            {
                TinyFileDialog.MessageBoxInfoOk("No BFRES files found in the selected folder.");
                return;
            }

            int processedCount = 0;
            int optimizedCount = 0;
            int totalAttributesOptimized = 0;

            foreach (var filePath in bfresFiles)
            {
                try
                {
                    int attributesOptimized = ProcessBfresFile(filePath);
                    processedCount++;
                    
                    if (attributesOptimized > 0)
                    {
                        optimizedCount++;
                        totalAttributesOptimized += attributesOptimized;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {filePath}: {ex.Message}");
                }
            }

            TinyFileDialog.MessageBoxInfoOk(
                $"Vertex Format Optimization Complete!\n\n" +
                $"Files processed: {processedCount}\n" +
                $"Files optimized: {optimizedCount}\n" +
                $"Total attributes optimized: {totalAttributesOptimized}");
        }

        /// <summary>
        /// Processes a single BFRES file and optimizes vertex formats.
        /// </summary>
        public int ProcessBfresFile(string filePath)
        {
            bool isMeshCodec = filePath.EndsWith(".mc", StringComparison.OrdinalIgnoreCase);
            
            ResFile resFile;
            
            if (isMeshCodec)
            {
                var meshCodecFormat = new MeshCodecFormat();
                using (var compressedStream = File.OpenRead(filePath))
                {
                    var decompressedStream = meshCodecFormat.Decompress(compressedStream);
                    resFile = new ResFile(decompressedStream);
                }
            }
            else
            {
                using (var stream = File.OpenRead(filePath))
                {
                    resFile = new ResFile(stream);
                }
            }

            int totalAttributesOptimized = 0;

            foreach (var model in resFile.Models.Values)
            {
                var optimizer = new VertexFormatOptimizer(model, resFile.ByteOrder);
                int attributesOptimized = optimizer.Optimize();
                totalAttributesOptimized += attributesOptimized;
            }

            if (totalAttributesOptimized > 0)
            {
                if (isMeshCodec)
                {
                    var memStream = new MemoryStream();
                    resFile.Save(memStream);
                    
                    var dataToCompress = new MemoryStream(memStream.ToArray());
                    var meshCodecFormat = new MeshCodecFormat();
                    var compressedStream = meshCodecFormat.Compress(dataToCompress);
                    
                    File.WriteAllBytes(filePath, ((MemoryStream)compressedStream).ToArray());
                }
                else
                {
                    resFile.Save(filePath);
                }
            }

            return totalAttributesOptimized;
        }
    }

    /// <summary>
    /// Optimizes vertex buffer formats for a single model.
    /// Reduces weight (_w) and index (_i) attribute sizes based on VertexSkinCount.
    /// </summary>
    public class VertexFormatOptimizer
    {
        private readonly Model _model;
        private readonly ByteOrder _byteOrder;

        public VertexFormatOptimizer(Model model, ByteOrder byteOrder)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _byteOrder = byteOrder;
        }

        /// <summary>
        /// Optimizes all vertex buffers in the model.
        /// </summary>
        /// <returns>Number of attributes optimized.</returns>
        public int Optimize()
        {
            int totalOptimized = 0;

            foreach (var shape in _model.Shapes.Values)
            {
                var vertexBuffer = _model.VertexBuffers[shape.VertexBufferIndex];
                int skinCount = vertexBuffer.VertexSkinCount;

                if (skinCount == 0)
                    continue; // No skinning, nothing to optimize

                var helper = new VertexBufferHelper(vertexBuffer, _byteOrder);
                bool modified = false;

                foreach (var attr in helper.Attributes)
                {
                    // Check if this is a weight or index attribute
                    if (attr.Name.StartsWith("_w") || attr.Name.StartsWith("_i"))
                    {
                        var newFormat = GetOptimizedFormat(attr.Format, skinCount);
                        if (newFormat != attr.Format)
                        {
                            attr.Format = newFormat;
                            modified = true;
                            totalOptimized++;
                        }
                    }
                }

                if (modified)
                {
                    _model.VertexBuffers[shape.VertexBufferIndex] = helper.ToVertexBuffer();
                }
            }

            return totalOptimized;
        }

        /// <summary>
        /// Gets the optimized format for a weight/index attribute based on skin count.
        /// </summary>
        private GX2AttribFormat GetOptimizedFormat(GX2AttribFormat currentFormat, int skinCount)
        {
            if (skinCount == 1)
            {
                return OptimizeForSkinCount1(currentFormat);
            }
            else if (skinCount == 2)
            {
                return OptimizeForSkinCount2(currentFormat);
            }
            else if (skinCount == 3)
            {
                return OptimizeForSkinCount3(currentFormat);
            }
            else // skinCount >= 4
            {
                return OptimizeForSkinCount4Plus(currentFormat);
            }
        }

        private GX2AttribFormat OptimizeForSkinCount1(GX2AttribFormat format)
        {
            switch (format)
            {
                case GX2AttribFormat.Format_32_32_32_32_Single:
                case GX2AttribFormat.Format_32_32_32_Single:
                case GX2AttribFormat.Format_32_32_Single:
                    return GX2AttribFormat.Format_32_Single;

                case GX2AttribFormat.Format_32_32_32_32_SInt:
                case GX2AttribFormat.Format_32_32_32_SInt:
                case GX2AttribFormat.Format_32_32_SInt:
                    return GX2AttribFormat.Format_32_SInt;

                case GX2AttribFormat.Format_32_32_32_32_UInt:
                case GX2AttribFormat.Format_32_32_32_UInt:
                case GX2AttribFormat.Format_32_32_UInt:
                    return GX2AttribFormat.Format_32_UInt;

                case GX2AttribFormat.Format_16_16_16_16_Single:
                case GX2AttribFormat.Format_16_16_Single:
                    return GX2AttribFormat.Format_16_Single;

                case GX2AttribFormat.Format_16_16_16_16_SInt:
                case GX2AttribFormat.Format_16_16_SInt:
                    return GX2AttribFormat.Format_16_SInt;

                case GX2AttribFormat.Format_16_16_16_16_UInt:
                case GX2AttribFormat.Format_16_16_UInt:
                    return GX2AttribFormat.Format_16_UInt;

                case GX2AttribFormat.Format_8_8_8_8_UInt:
                case GX2AttribFormat.Format_8_8_UInt:
                    return GX2AttribFormat.Format_8_UInt;

                case GX2AttribFormat.Format_8_8_8_8_SInt:
                case GX2AttribFormat.Format_8_8_SInt:
                    return GX2AttribFormat.Format_8_SInt;

                case GX2AttribFormat.Format_8_8_8_8_SNorm:
                case GX2AttribFormat.Format_8_8_SNorm:
                    return GX2AttribFormat.Format_8_SNorm;

                case GX2AttribFormat.Format_8_8_8_8_UNorm:
                case GX2AttribFormat.Format_8_8_UNorm:
                    return GX2AttribFormat.Format_8_UNorm;

                case GX2AttribFormat.Format_8_8_8_8_SIntToSingle:
                case GX2AttribFormat.Format_8_8_SIntToSingle:
                    return GX2AttribFormat.Format_8_SIntToSingle;

                case GX2AttribFormat.Format_8_8_8_8_UIntToSingle:
                case GX2AttribFormat.Format_8_8_UIntToSingle:
                    return GX2AttribFormat.Format_8_UIntToSingle;

                default:
                    return format;
            }
        }

        private GX2AttribFormat OptimizeForSkinCount2(GX2AttribFormat format)
        {
            switch (format)
            {
                case GX2AttribFormat.Format_32_32_32_32_Single:
                case GX2AttribFormat.Format_32_32_32_Single:
                case GX2AttribFormat.Format_32_Single:
                    return GX2AttribFormat.Format_32_32_Single;

                case GX2AttribFormat.Format_32_32_32_32_SInt:
                case GX2AttribFormat.Format_32_32_32_SInt:
                case GX2AttribFormat.Format_32_SInt:
                    return GX2AttribFormat.Format_32_32_SInt;

                case GX2AttribFormat.Format_32_32_32_32_UInt:
                case GX2AttribFormat.Format_32_32_32_UInt:
                case GX2AttribFormat.Format_32_UInt:
                    return GX2AttribFormat.Format_32_32_UInt;

                case GX2AttribFormat.Format_16_16_16_16_Single:
                case GX2AttribFormat.Format_16_Single:
                    return GX2AttribFormat.Format_16_16_Single;

                case GX2AttribFormat.Format_16_16_16_16_SInt:
                case GX2AttribFormat.Format_16_SInt:
                    return GX2AttribFormat.Format_16_16_SInt;

                case GX2AttribFormat.Format_16_16_16_16_UInt:
                case GX2AttribFormat.Format_16_UInt:
                    return GX2AttribFormat.Format_16_16_UInt;

                case GX2AttribFormat.Format_8_8_8_8_UInt:
                case GX2AttribFormat.Format_8_UInt:
                    return GX2AttribFormat.Format_8_8_UInt;

                case GX2AttribFormat.Format_8_8_8_8_SInt:
                case GX2AttribFormat.Format_8_SInt:
                    return GX2AttribFormat.Format_8_8_SInt;

                case GX2AttribFormat.Format_8_8_8_8_SNorm:
                case GX2AttribFormat.Format_8_SNorm:
                    return GX2AttribFormat.Format_8_8_SNorm;

                case GX2AttribFormat.Format_8_8_8_8_UNorm:
                case GX2AttribFormat.Format_8_UNorm:
                    return GX2AttribFormat.Format_8_8_UNorm;

                case GX2AttribFormat.Format_8_8_8_8_SIntToSingle:
                case GX2AttribFormat.Format_8_SIntToSingle:
                    return GX2AttribFormat.Format_8_8_SIntToSingle;

                case GX2AttribFormat.Format_8_8_8_8_UIntToSingle:
                case GX2AttribFormat.Format_8_UIntToSingle:
                    return GX2AttribFormat.Format_8_8_UIntToSingle;

                default:
                    return format;
            }
        }

        private GX2AttribFormat OptimizeForSkinCount3(GX2AttribFormat format)
        {
            switch (format)
            {
                case GX2AttribFormat.Format_32_32_32_32_Single:
                case GX2AttribFormat.Format_32_32_Single:
                case GX2AttribFormat.Format_32_Single:
                    return GX2AttribFormat.Format_32_32_32_Single;

                case GX2AttribFormat.Format_32_32_32_32_SInt:
                case GX2AttribFormat.Format_32_32_SInt:
                case GX2AttribFormat.Format_32_SInt:
                    return GX2AttribFormat.Format_32_32_32_SInt;

                case GX2AttribFormat.Format_32_32_32_32_UInt:
                case GX2AttribFormat.Format_32_32_UInt:
                case GX2AttribFormat.Format_32_UInt:
                    return GX2AttribFormat.Format_32_32_32_UInt;

                default:
                    return format;
            }
        }

        private GX2AttribFormat OptimizeForSkinCount4Plus(GX2AttribFormat format)
        {
            switch (format)
            {
                case GX2AttribFormat.Format_32_32_32_Single:
                case GX2AttribFormat.Format_32_32_Single:
                case GX2AttribFormat.Format_32_Single:
                    return GX2AttribFormat.Format_32_32_32_32_Single;

                case GX2AttribFormat.Format_32_32_32_SInt:
                case GX2AttribFormat.Format_32_32_SInt:
                case GX2AttribFormat.Format_32_SInt:
                    return GX2AttribFormat.Format_32_32_32_32_SInt;

                case GX2AttribFormat.Format_32_32_32_UInt:
                case GX2AttribFormat.Format_32_32_UInt:
                case GX2AttribFormat.Format_32_UInt:
                    return GX2AttribFormat.Format_32_32_32_32_UInt;

                case GX2AttribFormat.Format_16_16_Single:
                case GX2AttribFormat.Format_16_Single:
                    return GX2AttribFormat.Format_16_16_16_16_Single;

                case GX2AttribFormat.Format_16_16_SInt:
                case GX2AttribFormat.Format_16_SInt:
                    return GX2AttribFormat.Format_16_16_16_16_SInt;

                case GX2AttribFormat.Format_16_16_UInt:
                case GX2AttribFormat.Format_16_UInt:
                    return GX2AttribFormat.Format_16_16_16_16_UInt;

                case GX2AttribFormat.Format_8_8_UInt:
                case GX2AttribFormat.Format_8_UInt:
                    return GX2AttribFormat.Format_8_8_8_8_UInt;

                case GX2AttribFormat.Format_8_8_SInt:
                case GX2AttribFormat.Format_8_SInt:
                    return GX2AttribFormat.Format_8_8_8_8_SInt;

                case GX2AttribFormat.Format_8_8_SNorm:
                case GX2AttribFormat.Format_8_SNorm:
                    return GX2AttribFormat.Format_8_8_8_8_SNorm;

                case GX2AttribFormat.Format_8_8_UNorm:
                case GX2AttribFormat.Format_8_UNorm:
                    return GX2AttribFormat.Format_8_8_8_8_UNorm;

                case GX2AttribFormat.Format_8_8_SIntToSingle:
                case GX2AttribFormat.Format_8_SIntToSingle:
                    return GX2AttribFormat.Format_8_8_8_8_SIntToSingle;

                case GX2AttribFormat.Format_8_8_UIntToSingle:
                case GX2AttribFormat.Format_8_UIntToSingle:
                    return GX2AttribFormat.Format_8_8_8_8_UIntToSingle;

                default:
                    return format;
            }
        }
    }
}
