using BfresLibrary;
using GLFrameworkEngine;
using MapStudio.UI;
using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Toolbox.Core;
using Toolbox.Core.Animations;
using Toolbox.Core.ViewModels;
using UIFramework;

namespace CafeLibrary.Rendering
{
    public class BfresShapeAnim : STAnimation, IContextMenu, IPropertyUI, IEditableAnimation
    {
        private string ModelName = null;

        private int Hash;

        public TreeNode Root { get; set; }

        public ShapeAnim ShapeAnim;

        public ResFile ResFile { get; set; }

        public NodeBase UINode { get; set; }

        public Type GetTypeUI() => typeof(MaterialParamAnimationEditor);

        public void OnLoadUI(object uiInstance) { }

        public void OnRenderUI(object uiInstance)
        {
        }

        private ResDict<ShapeAnim> AnimDict;

        public BfresShapeAnim() { }

        public BfresShapeAnim(ResFile resFile, ResDict<ShapeAnim> dict, ShapeAnim anim, string name)
        {
            Root = new AnimationTree.AnimNode(this);
            ResFile = resFile;
            AnimDict = dict;
            ModelName = name;
            ShapeAnim = anim;
            UINode = new NodeBase(anim.Name) { Tag = this };
            UINode.CanRename = true;
            UINode.OnHeaderRenamed += delegate
            {
                //not changed
                if (anim.Name == UINode.Header)
                    return;

                //Dupe name
                if (AnimDict.ContainsKey(UINode.Header))
                {
                    TinyFileDialog.MessageBoxErrorOk($"Name {UINode.Header} already exists!");
                    //revert
                    UINode.Header = anim.Name;
                    return;
                }
                OnNameChanged(UINode.Header);
            };
            UINode.Icon = '\uf0e7'.ToString();

            CanPlay = false; //Default all animations to not play unless toggled in list
            Reload(anim);
        }

        public BfresShapeAnim(ShapeAnim anim, string name)
        {
            ModelName = name;
            CanPlay = false; //Default all animations to not play unless toggled in list
            Reload(anim);
        }

        public void OnSave()
        {
            ShapeAnim.FrameCount = (int)this.FrameCount;
            if (this.Loop)
            {
                ShapeAnim.Flags |= ShapeAnim.ShapeAnimFlags.Looping;
            }
            else
            {
                ShapeAnim.Flags &= ~ShapeAnim.ShapeAnimFlags.Looping;
            }

            int hash = BfresAnimations.CalculateGroupHashes(this);
            if (IsEdited || hash != Hash) //Generate anim data
            {
                //MaterialAnimConverter.ConvertAnimation(this, ShapeAnim);
            }
            Hash = hash;
        }

        public void OnNameChanged(string newName)
        {
            string previousName = ShapeAnim.Name;
            ShapeAnim.Name = newName;

            if (AnimDict.ContainsKey(previousName))
            {
                AnimDict.RemoveKey(previousName);
                AnimDict.Add(ShapeAnim.Name, ShapeAnim);
            }
        }

        public MenuItemModel[] GetContextMenuItems()
        {
            return new MenuItemModel[]
            {
                new MenuItemModel("Export", ExportAction),
                new MenuItemModel("Replace", ReplaceAction),
                new MenuItemModel(""),
                new MenuItemModel("Rename", () => UINode.ActivateRename = true),
                new MenuItemModel(""),
                new MenuItemModel("Delete", DeleteAction)
            };
        }

        private void ExportAction()
        {
            var dlg = new ImguiFileDialog();
            dlg.SaveDialog = true;
            dlg.FileName = $"{ShapeAnim.Name}.bfmaa";
            dlg.AddFilter(".bfmaa", ".bfmaa");
            dlg.AddFilter(".json", ".json");

            if (dlg.ShowDialog())
            {
                OnSave();
                ShapeAnim.Export(dlg.FilePath, ResFile);
            }
        }

        private void ReplaceAction()
        {
            var dlg = new ImguiFileDialog();
            dlg.FileName = $"{ShapeAnim.Name}.bfmaa";
            dlg.AddFilter(".bfmaa", ".bfmaa");
            dlg.AddFilter(".json", ".json");

            if (dlg.ShowDialog())
            {
                ShapeAnim.Import(dlg.FilePath, ResFile);
                ShapeAnim.Name = this.Name;

                Reload(ShapeAnim);
            }
        }

        private void DeleteAction()
        {
            int result = TinyFileDialog.MessageBoxInfoYesNo("Are you sure you want to remove these animations? Operation cannot be undone.");
            if (result != 1)
                return;

            UINode.Parent.Children.Remove(UINode);

            if (ResFile.ShapeAnims.ContainsValue(ShapeAnim))
                ResFile.ShapeAnims.Remove(ShapeAnim);
        }

        public BfresShapeAnim Clone()
        {
            BfresShapeAnim anim = new BfresShapeAnim();
            anim.Name = this.Name;
            anim.ModelName = this.ModelName;
            anim.FrameCount = this.FrameCount;
            anim.Frame = this.Frame;
            anim.Loop = this.Loop;
            anim.AnimGroups = this.AnimGroups;
            return anim;
        }

        /// <summary>
        /// Gets the parent render that this animation belongs to.
        /// </summary>
        public BfresRender GetParentRender()
        {
            if (DataCache.ModelCache.ContainsKey(ModelName))
                return (BfresRender)DataCache.ModelCache[ModelName];

            return null;
        }

        public override void NextFrame()
        {
            foreach (ShapeAnimGroup group in AnimGroups)
            {
                var meshes = GetMeshes(group.Name);
                foreach (var mesh in meshes)
                    ParseKeyTrack(mesh, group.BaseTarget, group.Tracks);
            }
        }

        private void ParseKeyTrack(STGenericMesh mesh, STAnimationTrack baseTarget, List<STAnimationTrack> tracks)
        {
            float[] weights = new float[tracks.Count];
            for (int i = 0; i < tracks.Count; i++)
                weights[i] = tracks[i].GetFrameValue(this.Frame);

            float totalWeight = 0;
            foreach (float f in weights)
                totalWeight += f;

            float baseWeight = 1.0f - totalWeight;
            float total = totalWeight + baseWeight;

            Vector3[] morphPositions = new Vector3[mesh.Vertices.Count];

            //Trasform the existing track key data
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var vertex = mesh.Vertices[i];

                //Determine what vertex to morph from.
                if (baseTarget != null && mesh.KeyGroups.ContainsKey(baseTarget.Name))
                {
                    var keyShape = mesh.KeyGroups[baseTarget.Name];
                    //Add the keyed position target
                    morphPositions[i] = keyShape.Vertices[i].Position;
                }
                else
                    morphPositions[i] = vertex.Position;

                Vector3 position = morphPositions[i];
                position *= baseWeight;

                foreach (var track in tracks)
                {
                    if (!mesh.KeyGroups.ContainsKey(track.Name))
                        continue;

                    //The total weight used for a single vertex point
                    var weight = track.GetFrameValue(this.Frame);

                    //Get the track's key shape
                    var keyShape = mesh.KeyGroups[track.Name];
                    //Add the keyed position and weigh it
                    position += keyShape.Vertices[i].Position * weight;
                }

                //Set the output vertex
                position /= total;
                morphPositions[i] = position;
            }

            // mesh.MorphPositions = morphPositions;
            // mesh.UpdateVertexData = true;
        }

        public void Reload(ShapeAnim anim)
        {
            Name = anim.Name;
            FrameCount = anim.FrameCount;
            FrameRate = 60.0f;
            Loop = anim.Flags.HasFlag(ShapeAnim.ShapeAnimFlags.Looping);

            AnimGroups.Clear();
            foreach (var shapeAnim in anim.VertexShapeAnims)
            {
                var group = new ShapeAnimGroup();
                AnimGroups.Add(group);
                group.Name = shapeAnim.Name;

                //Get the shape keys used for animating
                int baseIndex = 0;
                for (int i = 0; i < shapeAnim.KeyShapeAnimInfos.Count; i++)
                {
                    int startBaseIndex = shapeAnim.KeyShapeAnimInfos.Count/* - shapeAnim.BaseDataList.Length*/;

                    var keyShapeInfo = shapeAnim.KeyShapeAnimInfos[i];

                    //Get the curve index for animated indices
                    int curveIndex = keyShapeInfo.CurveIndex;

                    //Make a new sampler track using step interpolation
                    var track = new BfresAnimationTrack();
                    track.InterpolationType = STInterpoaltionType.Step;
                    track.Name = keyShapeInfo.Name;

                    if (group.BaseTarget == null && curveIndex == -1)
                    {
                        group.BaseTarget = track;
                    }

                    if (curveIndex != -1 && shapeAnim.Curves != null)
                        BfresAnimations.GenerateKeys(track, shapeAnim.Curves[curveIndex]);
                    else if (i >= startBaseIndex)
                    {
                        float baseWeight = shapeAnim.BaseDataList[baseIndex];
                        track.KeyFrames.Add(new STKeyFrame(0, baseWeight));
                        baseIndex++;
                    }

                    group.Tracks.Add(track);
                }
            }
        }

        public class ShapeAnimGroup : STAnimGroup
        {
            public List<STAnimationTrack> Tracks = new List<STAnimationTrack>();
            public STAnimationTrack BaseTarget = null;

            public override List<STAnimationTrack> GetTracks() { return Tracks; }
        }

        private List<STGenericMesh> GetMeshes(string name)
        {
            List<STGenericMesh> meshes = new List<STGenericMesh>();
            foreach (GenericRenderer render in DataCache.ModelCache.Values)
            {
                foreach (ModelAsset model in render.Models)
                {
                    foreach (var mesh in model.ModelData.Meshes)
                    {
                        if (mesh.Name == name)
                            meshes.Add(mesh);
                    }
                }
            }
            return meshes;
        }
    }
}