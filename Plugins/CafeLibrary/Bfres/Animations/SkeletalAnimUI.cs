﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UIFramework;
using BfresLibrary;
using CafeLibrary.Rendering;
using MapStudio.UI;
using Toolbox.Core.Animations;
using System.Collections.Immutable;

namespace CafeLibrary
{
    class SkeletalAnimUI
    {
        public static TreeNode ReloadTree(TreeNode Root, BfresSkeletalAnim anim, ResFile resFile)
        {
            if (resFile == null)
                return null;

            Root.Icon = anim.UINode.Icon;
            Root.Header = anim.Name;
            Root.CanRename = true;
            Root.OnHeaderRenamed += delegate
            {
                anim.UINode.Header = Root.Header;
                anim.OnRenamed(Root.Header);
            };
            Root.Tag = anim;
            Root.IsExpanded = true;
            Root.ContextMenus.Add(new MenuItem("Add Bone", () =>
            {
                var boneGroup = new BfresSkeletalAnim.BoneAnimGroup(new BoneAnim());
                boneGroup.Name = "NewBone";
                anim.AnimGroups.Add(boneGroup);
                //Add to ui
                Root.AddChild(GetGroupNode(anim, boneGroup));
                anim.IsEdited = true;
            }));
            Root.ContextMenus.Add(new MenuItem("Rename", () => Root.ActivateRename = true));

            void SortAnimation(bool ascending)
            {
                // Sort anim.Root.Children by Header
                var sortedChildren = ascending
                    ? anim.Root.Children.OrderBy(child => child.Header).ToList()
                    : anim.Root.Children.OrderByDescending(child => child.Header).ToList();

                anim.Root.Children.Clear();
                foreach (var child in sortedChildren)
                {
                    anim.Root.Children.Add(child);
                }

                // Sort anim.AnimGroups by Name
                if (ascending)
                {
                    anim.AnimGroups.Sort((group1, group2) =>
                        string.Compare(group1.Name, group2.Name, StringComparison.Ordinal));
                }
                else
                {
                    anim.AnimGroups.Sort((group1, group2) =>
                        string.Compare(group2.Name, group1.Name, StringComparison.Ordinal));
                }
            }

            Root.ContextMenus.Add(new MenuItem("Sort (A -> Z)", () =>
            {
                SortAnimation(true);
            }));
            Root.ContextMenus.Add(new MenuItem("Sort (Z -> A)", () =>
            {
                SortAnimation(false);
            }));
            Root.ContextMenus.Add(new MenuItem("Delete Selected", () =>
            {
                // Create a copy of the Children collection to iterate over
                foreach (var boneNode in anim.Root.Children.ToList())
                {
                    if (!boneNode.IsSelected)
                    {
                        continue;
                    }

                    foreach (var child in boneNode.Children)
                    {
                        if (child is AnimationTree.GroupNode groupNode)
                        {
                            groupNode.OnGroupRemoved?.Invoke(child, EventArgs.Empty);
                        }
                    }

                    STAnimGroup? foundGroup = anim.AnimGroups.Find(group => group.Name == boneNode.Header);
                    if (foundGroup == null)
                    {
                        continue;
                    }

                    // Remove from animation
                    anim.AnimGroups.Remove(foundGroup);

                    // Remove from UI
                    anim.Root.Children.Remove(boneNode);
                }
            }));

            Root.Children.Clear();
            foreach (BfresSkeletalAnim.BoneAnimGroup group in anim.AnimGroups)
                Root.AddChild(GetGroupNode(anim, group));

            return Root;
        }

        public static TreeNode GetGroupNode(BfresSkeletalAnim anim, BfresSkeletalAnim.BoneAnimGroup group)
        {
            TreeNode boneNode = new TreeNode(group.Name);
            boneNode.IsExpanded = false;
            boneNode.Tag = group;
            boneNode.Icon = MapStudio.UI.IconManager.BONE_ICON.ToString();
            boneNode.CanRename = true;
            boneNode.OnHeaderRenamed += delegate
            {
                //not changed
                if (group.BoneAnimData.Name == boneNode.Header)
                    return;

                //Dupe name
                if (anim.AnimGroups.Any(x => x.Name == boneNode.Header))
                {
                    TinyFileDialog.MessageBoxErrorOk($"Name {boneNode.Header} already exists!");
                    //revert
                    boneNode.Header = group.BoneAnimData.Name;
                    return;
                }

                group.BoneAnimData.Name = boneNode.Header;
                group.Name = boneNode.Header;
            };
            boneNode.ContextMenus.Add(new MenuItem("Rename", () => boneNode.ActivateRename = true));
            boneNode.ContextMenus.Add(new MenuItem(""));
            boneNode.ContextMenus.Add(new MenuItem("Delete", () =>
            {
                foreach (var child in boneNode.Children)
                {
                    if (child is AnimationTree.GroupNode)
                        ((AnimationTree.GroupNode)child).OnGroupRemoved?.Invoke(child, EventArgs.Empty);
                }

                //Remove from animation
                anim.AnimGroups.Remove(group);
                //Remove from UI
                anim.Root.Children.Remove(boneNode);
            }));

            boneNode.AddChild(GetTrackNode(anim, group.Translate.X, "Translate.X"));
            boneNode.AddChild(GetTrackNode(anim, group.Translate.Y, "Translate.Y"));
            boneNode.AddChild(GetTrackNode(anim, group.Translate.Z, "Translate.Z"));

            boneNode.AddChild(GetTrackNode(anim, group.Rotate.X, "Rotate.X", true));
            boneNode.AddChild(GetTrackNode(anim, group.Rotate.Y, "Rotate.Y", true));
            boneNode.AddChild(GetTrackNode(anim, group.Rotate.Z, "Rotate.Z", true));

            boneNode.AddChild(GetTrackNode(anim, group.Scale.X, "Scale.X"));
            boneNode.AddChild(GetTrackNode(anim, group.Scale.Y, "Scale.Y"));
            boneNode.AddChild(GetTrackNode(anim, group.Scale.Z, "Scale.Z"));

            if (group.Translate.GetTracks().Any(x => x.KeyFrames.Count > 1))
            {
    
            }
            if (group.Rotate.GetTracks().Any(x => x.KeyFrames.Count > 1))
            {
            
            }
            if (group.Scale.GetTracks().Any(x => x.KeyFrames.Count > 1))
            {

            }
            return boneNode;
        }

        static AnimationTree.TrackNode GetTrackNode(BfresSkeletalAnim anim, BfresAnimationTrack track, string name, bool degrees = false)
        {
            track.Name = name;
            if (degrees)
                return new AnimationTree.TrackNodeDegreesConversion(anim, track) { Tag = track, Icon = '\uf1b2'.ToString() };
            else
                return new AnimationTree.TrackNode(anim, track) { Tag = track, Icon = '\uf1b2'.ToString() };
        }

        class DegreesTrackNode
        {

        }
    }
}
