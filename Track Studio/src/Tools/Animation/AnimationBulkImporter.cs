using CafeLibrary;
using MapStudio.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrackStudio.Tools;

namespace TrackStudio.src.Tools.Animation
{
    public class AnimationBulkImporter : IToolAction
    {
        public string Name => "Animation Bulk Importer";
        public string Description => "Bulk imports animation data from JSON files.";

        /// <summary>
        /// Gets the list of workspaces. Set this from MainWindow.
        /// </summary>
        public static Func<IEnumerable<Workspace>> GetWorkspaces { get; set; }

        public bool CanExecute()
        {
            return true;
        }

        public void Execute()
        {
            var dlg = new ImguiFolderDialog { Title = "Pick Folder" };
            if (!dlg.ShowDialog())
                return;

            var workspaces = GetWorkspaces?.Invoke();
            if (workspaces == null)
                return;

            foreach (var ws in workspaces)
            {
                if (ws.ActiveEditor is not BFRES bfres)
                {
                    continue;
                }

                bfres.ResFile.SkeletalAnims.Values.ToList().ForEach(anim =>
                {
                    var retargetPath = Path.Combine(dlg.SelectedPath, bfres.ResFile.Name, $"{anim.Name}_retarget.json");
                    var importPath = File.Exists(retargetPath)
                        ? retargetPath
                        : Path.Combine(dlg.SelectedPath, bfres.ResFile.Name, $"{anim.Name}.json");

                    bool wasLooping = anim.Loop;
                    string originalName = anim.Name;
                    anim.Import(importPath, bfres.ResFile);
                    anim.Name = originalName;
                    anim.Loop = wasLooping;
                });
            }
        }
    }
}
