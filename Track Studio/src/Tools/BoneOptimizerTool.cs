using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapStudio.UI;
using CafeLibrary;
using BfresLibrary;
using BfresLibrary.Helpers;
using Syroot.Maths;
using Syroot.BinaryData;
using Toolbox.Core;

namespace TrackStudio.Tools
{
    /// <summary>
    /// Tool for optimizing BFRES files by removing unused bones from skeletons.
    /// Processes all .bfres and .bfres.mc files in a selected folder.
    /// </summary>
    public class BoneOptimizerTool : IToolAction
    {
        public string Name => "Bone Optimizer";
        public string Description => "Optimizes BFRES files by removing unused bones. Processes all .bfres and .bfres.mc files in a selected folder.";

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
            int totalBonesRemoved = 0;

            foreach (var filePath in bfresFiles)
            {
                try
                {
                    int bonesRemoved = ProcessBfresFile(filePath);
                    processedCount++;
                    
                    if (bonesRemoved > 0)
                    {
                        optimizedCount++;
                        totalBonesRemoved += bonesRemoved;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {filePath}: {ex.Message}");
                }
            }

            TinyFileDialog.MessageBoxInfoOk(
                $"Bone Optimization Complete!\n\n" +
                $"Files processed: {processedCount}\n" +
                $"Files optimized: {optimizedCount}\n" +
                $"Total bones removed: {totalBonesRemoved}");
        }

        /// <summary>
        /// Processes a single BFRES file and removes unused bones.
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

            int totalBonesRemoved = 0;

            foreach (var model in resFile.Models.Values)
            {
                var optimizer = new BoneOptimizer(model, resFile.ByteOrder);
                int bonesRemoved = optimizer.Optimize();
                totalBonesRemoved += bonesRemoved;
            }

            if (totalBonesRemoved > 0)
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

            return totalBonesRemoved;
        }
    }

    /// <summary>
    /// Optimizes a single model's skeleton by removing unused bones.
    /// Follows the same matrix structure as BfresModelImporter:
    /// - MatrixToBoneList = [smooth indices...] + [rigid indices...]
    /// - SmoothMatrixIndex = index within smooth portion
    /// - RigidMatrixIndex = smoothCount + index within rigid portion
    /// - InverseModelMatrices is indexed by SmoothMatrixIndex
    /// </summary>
    public class BoneOptimizer
    {
        private readonly Model _model;
        private readonly Skeleton _skeleton;
        private readonly ByteOrder _byteOrder;
        private readonly HashSet<int> _usedBoneIndices;

        public BoneOptimizer(Model model, ByteOrder byteOrder)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _skeleton = model.Skeleton;
            _byteOrder = byteOrder;
            _usedBoneIndices = new HashSet<int>();
        }

        /// <summary>
        /// Optimizes the skeleton by removing unused bones.
        /// </summary>
        public int Optimize()
        {
            if (_skeleton == null || _skeleton.Bones.Count == 0)
                return 0;

            int originalBoneCount = _skeleton.Bones.Count;

            // Step 1: Collect all used bone indices
            CollectUsedBones();

            // Step 2: Mark parent chains as used
            MarkParentChains();

            // Step 3: Get bones to keep
            var bonesToKeep = GetBonesToKeep();

            if (bonesToKeep.Count == originalBoneCount)
                return 0;

            // Step 4: Build bone index mapping (old -> new)
            var oldToNewBoneIndex = BuildBoneIndexMapping(bonesToKeep);

            // Step 5: Build matrix index mapping and rebuild MatrixToBoneList
            // This also updates SmoothMatrixIndex and RigidMatrixIndex on bones
            var oldToNewMatrixIndex = RebuildMatrixStructure(oldToNewBoneIndex);

            // Step 6: Remap vertex buffer bone indices
            RemapVertexBuffers(oldToNewMatrixIndex);

            // Step 7: Rebuild the skeleton bones dictionary
            RebuildSkeleton(bonesToKeep, oldToNewBoneIndex);

            // Step 8: Update shapes
            UpdateShapes(oldToNewBoneIndex);

            return originalBoneCount - _skeleton.Bones.Count;
        }

        /// <summary>
        /// Collects all bone indices that are used by shapes.
        /// </summary>
        private void CollectUsedBones()
        {
            _usedBoneIndices.Clear();

            foreach (var shape in _model.Shapes.Values)
            {
                // Rigid skinning - only BoneIndex matters
                if (shape.VertexSkinCount == 0)
                {
                    if (shape.BoneIndex >= 0 && shape.BoneIndex < _skeleton.Bones.Count)
                    {
                        _usedBoneIndices.Add(shape.BoneIndex);
                    }
                    continue;
                }

                // Skinned meshes - use SkinBoneIndices (these are actual bone indices)
                if (shape.SkinBoneIndices != null)
                {
                    foreach (var boneIdx in shape.SkinBoneIndices)
                    {
                        if (boneIdx < _skeleton.Bones.Count)
                        {
                            _usedBoneIndices.Add(boneIdx);
                        }
                    }
                }

                // Also check vertex buffers via MatrixToBoneList
                if (_skeleton.MatrixToBoneList != null && _skeleton.MatrixToBoneList.Count > 0)
                {
                    var vertexBuffer = _model.VertexBuffers[shape.VertexBufferIndex];
                    var helper = new VertexBufferHelper(vertexBuffer, _byteOrder);
                    
                    var indexAttr = helper.Attributes.FirstOrDefault(a => a.Name == "_i0");
                    if (indexAttr != null)
                    {
                        for (int v = 0; v < indexAttr.Data.Length; v++)
                        {
                            var data = indexAttr.Data[v];
                            int skinCount = Math.Min((int)shape.VertexSkinCount, 4);
                            
                            for (int i = 0; i < skinCount; i++)
                            {
                                int matrixIndex = (int)data[i];
                                if (matrixIndex >= 0 && matrixIndex < _skeleton.MatrixToBoneList.Count)
                                {
                                    int boneIndex = _skeleton.MatrixToBoneList[matrixIndex];
                                    if (boneIndex < _skeleton.Bones.Count)
                                    {
                                        _usedBoneIndices.Add(boneIndex);
                                    }
                                }
                            }
                        }
                    }
                }

                // Add shape's BoneIndex
                if (shape.BoneIndex >= 0 && shape.BoneIndex < _skeleton.Bones.Count)
                {
                    _usedBoneIndices.Add(shape.BoneIndex);
                }
            }
        }

        /// <summary>
        /// Marks all parent bones of used bones as required.
        /// </summary>
        private void MarkParentChains()
        {
            var bonesArray = _skeleton.Bones.Values.ToArray();
            var initialUsed = _usedBoneIndices.ToList();
            
            foreach (var boneIndex in initialUsed)
            {
                int current = boneIndex;
                while (current >= 0 && current < bonesArray.Length)
                {
                    int parentIndex = bonesArray[current].ParentIndex;
                    if (parentIndex < 0 || parentIndex >= bonesArray.Length)
                        break;
                    
                    if (_usedBoneIndices.Contains(parentIndex))
                        break;

                    _usedBoneIndices.Add(parentIndex);
                    current = parentIndex;
                }
            }
        }

        /// <summary>
        /// Gets the ordered list of bone indices to keep.
        /// </summary>
        private List<int> GetBonesToKeep()
        {
            var result = new List<int>();
            for (int i = 0; i < _skeleton.Bones.Count; i++)
            {
                if (_usedBoneIndices.Contains(i))
                {
                    result.Add(i);
                }
            }
            return result;
        }

        /// <summary>
        /// Builds mapping from old bone index to new bone index.
        /// </summary>
        private Dictionary<int, int> BuildBoneIndexMapping(List<int> bonesToKeep)
        {
            var mapping = new Dictionary<int, int>();
            for (int newIndex = 0; newIndex < bonesToKeep.Count; newIndex++)
            {
                mapping[bonesToKeep[newIndex]] = newIndex;
            }
            return mapping;
        }

        /// <summary>
        /// Rebuilds MatrixToBoneList maintaining the smooth/rigid structure.
        /// Returns mapping from old matrix index to new matrix index.
        /// Also updates SmoothMatrixIndex and RigidMatrixIndex on bones.
        /// </summary>
        private Dictionary<int, int> RebuildMatrixStructure(Dictionary<int, int> oldToNewBoneIndex)
        {
            var oldMatrixToBone = _skeleton.MatrixToBoneList;
            if (oldMatrixToBone == null || oldMatrixToBone.Count == 0)
                return new Dictionary<int, int>();

            var bonesArray = _skeleton.Bones.Values.ToArray();
            var oldToNewMatrix = new Dictionary<int, int>();

            // Find the boundary between smooth and rigid indices
            // Smooth bones have SmoothMatrixIndex >= 0, rigid bones have RigidMatrixIndex >= 0
            int smoothCount = 0;
            foreach (var bone in bonesArray)
            {
                if (bone.SmoothMatrixIndex >= 0)
                    smoothCount = Math.Max(smoothCount, bone.SmoothMatrixIndex + 1);
            }

            // Build new smooth and rigid index lists
            var newSmoothIndices = new List<ushort>();  // bone indices for smooth skinning
            var newRigidIndices = new List<ushort>();   // bone indices for rigid skinning

            // Maps old bone index to new smooth/rigid index within their respective lists
            var boneToNewSmoothIdx = new Dictionary<int, int>();
            var boneToNewRigidIdx = new Dictionary<int, int>();

            // Process smooth portion (first smoothCount entries in MatrixToBoneList)
            for (int oldMatrixIdx = 0; oldMatrixIdx < smoothCount && oldMatrixIdx < oldMatrixToBone.Count; oldMatrixIdx++)
            {
                int oldBoneIdx = oldMatrixToBone[oldMatrixIdx];
                if (oldToNewBoneIndex.TryGetValue(oldBoneIdx, out int newBoneIdx))
                {
                    int newMatrixIdx = newSmoothIndices.Count;
                    oldToNewMatrix[oldMatrixIdx] = newMatrixIdx;
                    newSmoothIndices.Add((ushort)newBoneIdx);
                    boneToNewSmoothIdx[oldBoneIdx] = newMatrixIdx;
                }
            }

            // Process rigid portion (entries after smoothCount)
            for (int oldMatrixIdx = smoothCount; oldMatrixIdx < oldMatrixToBone.Count; oldMatrixIdx++)
            {
                int oldBoneIdx = oldMatrixToBone[oldMatrixIdx];
                if (oldToNewBoneIndex.TryGetValue(oldBoneIdx, out int newBoneIdx))
                {
                    // New matrix index = new smooth count + position in rigid list
                    int newMatrixIdx = newSmoothIndices.Count + newRigidIndices.Count;
                    oldToNewMatrix[oldMatrixIdx] = newMatrixIdx;
                    newRigidIndices.Add((ushort)newBoneIdx);
                    boneToNewRigidIdx[oldBoneIdx] = newRigidIndices.Count - 1; // index within rigid list
                }
            }

            // Rebuild MatrixToBoneList = smooth + rigid
            var newMatrixToBone = new List<ushort>();
            newMatrixToBone.AddRange(newSmoothIndices);
            newMatrixToBone.AddRange(newRigidIndices);
            _skeleton.MatrixToBoneList = newMatrixToBone;

            // Update bones' SmoothMatrixIndex and RigidMatrixIndex
            for (int oldBoneIdx = 0; oldBoneIdx < bonesArray.Length; oldBoneIdx++)
            {
                var bone = bonesArray[oldBoneIdx];
                
                // Reset first
                short newSmoothIdx = -1;
                short newRigidIdx = -1;

                if (boneToNewSmoothIdx.TryGetValue(oldBoneIdx, out int smoothIdx))
                {
                    newSmoothIdx = (short)smoothIdx;
                }
                if (boneToNewRigidIdx.TryGetValue(oldBoneIdx, out int rigidIdx))
                {
                    // RigidMatrixIndex = smoothCount + index within rigid portion
                    newRigidIdx = (short)(newSmoothIndices.Count + rigidIdx);
                }

                bone.SmoothMatrixIndex = newSmoothIdx;
                bone.RigidMatrixIndex = newRigidIdx;
            }

            // Rebuild InverseModelMatrices - indexed by SmoothMatrixIndex
            if (_skeleton.InverseModelMatrices != null && _skeleton.InverseModelMatrices.Count > 0)
            {
                var oldInverseMatrices = _skeleton.InverseModelMatrices;
                var newInverseMatrices = new List<Matrix3x4>();

                // The old inverse matrices are ordered by old SmoothMatrixIndex
                // We need to reorder them according to the new SmoothMatrixIndex mapping
                for (int newSmoothIdx = 0; newSmoothIdx < newSmoothIndices.Count; newSmoothIdx++)
                {
                    // Find the old SmoothMatrixIndex for this bone
                    int newBoneIdx = newSmoothIndices[newSmoothIdx];
                    
                    // Find which old bone this new bone corresponds to
                    int oldBoneIdx = -1;
                    foreach (var kvp in oldToNewBoneIndex)
                    {
                        if (kvp.Value == newBoneIdx)
                        {
                            oldBoneIdx = kvp.Key;
                            break;
                        }
                    }

                    if (oldBoneIdx >= 0 && oldBoneIdx < bonesArray.Length)
                    {
                        // Get the old bone's old SmoothMatrixIndex
                        // We need to look this up from the original data, but we've already modified it
                        // So we search in the old smooth portion of MatrixToBoneList
                        int oldSmoothIdx = -1;
                        for (int i = 0; i < smoothCount && i < oldMatrixToBone.Count; i++)
                        {
                            if (oldMatrixToBone[i] == oldBoneIdx)
                            {
                                oldSmoothIdx = i;
                                break;
                            }
                        }

                        if (oldSmoothIdx >= 0 && oldSmoothIdx < oldInverseMatrices.Count)
                        {
                            newInverseMatrices.Add(oldInverseMatrices[oldSmoothIdx]);
                        }
                        else
                        {
                            // Fallback - add identity
                            newInverseMatrices.Add(new Matrix3x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0));
                        }
                    }
                    else
                    {
                        newInverseMatrices.Add(new Matrix3x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0));
                    }
                }

                _skeleton.InverseModelMatrices = newInverseMatrices;
            }

            return oldToNewMatrix;
        }

        /// <summary>
        /// Remaps vertex buffer bone indices (_i0, _i1, etc.) to new matrix indices.
        /// </summary>
        private void RemapVertexBuffers(Dictionary<int, int> oldToNewMatrixIndex)
        {
            if (oldToNewMatrixIndex.Count == 0)
                return;

            for (int bufIdx = 0; bufIdx < _model.VertexBuffers.Count; bufIdx++)
            {
                var vertexBuffer = _model.VertexBuffers[bufIdx];
                var helper = new VertexBufferHelper(vertexBuffer, _byteOrder);

                bool modified = false;

                foreach (var attr in helper.Attributes)
                {
                    if (!attr.Name.StartsWith("_i"))
                        continue;

                    for (int v = 0; v < attr.Data.Length; v++)
                    {
                        var data = attr.Data[v];
                        bool changed = false;

                        float x = data[0], y = data[1], z = data[2], w = data[3];

                        if (oldToNewMatrixIndex.TryGetValue((int)data[0], out int new0))
                        {
                            x = new0;
                            changed = true;
                        }
                        if (oldToNewMatrixIndex.TryGetValue((int)data[1], out int new1))
                        {
                            y = new1;
                            changed = true;
                        }
                        if (oldToNewMatrixIndex.TryGetValue((int)data[2], out int new2))
                        {
                            z = new2;
                            changed = true;
                        }
                        if (oldToNewMatrixIndex.TryGetValue((int)data[3], out int new3))
                        {
                            w = new3;
                            changed = true;
                        }

                        if (changed)
                        {
                            attr.Data[v] = new Vector4F(x, y, z, w);
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    _model.VertexBuffers[bufIdx] = helper.ToVertexBuffer();
                }
            }
        }

        /// <summary>
        /// Rebuilds the skeleton with only the kept bones.
        /// </summary>
        private void RebuildSkeleton(List<int> bonesToKeep, Dictionary<int, int> oldToNewBoneIndex)
        {
            var oldBones = _skeleton.Bones.Values.ToArray();
            var newBones = new ResDict<Bone>();

            foreach (int oldIndex in bonesToKeep)
            {
                var bone = oldBones[oldIndex];
                
                // Update parent index - find nearest kept ancestor
                if (bone.ParentIndex >= 0)
                {
                    int newParentIndex = FindNearestKeptAncestor(oldBones, bone.ParentIndex, oldToNewBoneIndex);
                    bone.ParentIndex = (short)newParentIndex;
                }

                newBones.Add(bone.Name, bone);
            }

            _skeleton.Bones.Clear();
            foreach (var kvp in newBones)
            {
                _skeleton.Bones.Add(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Finds the nearest ancestor bone that is kept, returning its new index.
        /// Returns -1 only if no ancestor is kept (true root).
        /// </summary>
        private int FindNearestKeptAncestor(Bone[] oldBones, int oldParentIndex, Dictionary<int, int> oldToNewBoneIndex)
        {
            int current = oldParentIndex;
            while (current >= 0 && current < oldBones.Length)
            {
                // If this ancestor is kept, return its new index
                if (oldToNewBoneIndex.TryGetValue(current, out int newIndex))
                {
                    return newIndex;
                }
                // Otherwise, move to its parent
                current = oldBones[current].ParentIndex;
            }
            // No ancestor found - this is a root bone
            return -1;
        }

        /// <summary>
        /// Updates shape bone indices and SkinBoneIndices.
        /// </summary>
        private void UpdateShapes(Dictionary<int, int> oldToNewBoneIndex)
        {
            foreach (var shape in _model.Shapes.Values)
            {
                // Update BoneIndex
                if (shape.BoneIndex >= 0 && oldToNewBoneIndex.TryGetValue(shape.BoneIndex, out int newBoneIndex))
                {
                    shape.BoneIndex = (ushort)newBoneIndex;
                }

                // Update SkinBoneIndices
                if (shape.SkinBoneIndices != null && shape.SkinBoneIndices.Count > 0)
                {
                    var newSkinBoneIndices = new List<ushort>();
                    foreach (var oldBoneIdx in shape.SkinBoneIndices)
                    {
                        if (oldToNewBoneIndex.TryGetValue(oldBoneIdx, out int newIdx))
                        {
                            newSkinBoneIndices.Add((ushort)newIdx);
                        }
                    }
                    shape.SkinBoneIndices = newSkinBoneIndices;
                }
            }
        }
    }
}
