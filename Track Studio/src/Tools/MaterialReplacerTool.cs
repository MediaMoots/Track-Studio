using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapStudio.UI;
using CafeLibrary;

namespace TrackStudio.Tools
{
    /// <summary>
    /// Tool for replacing materials in BFRES files using JSON material definitions.
    /// </summary>
    public class MaterialReplacerTool : IToolAction
    {
        public string Name => "Material Replacer";
        public string Description => "Replaces materials in open BFRES files using JSON material definitions from a selected folder.";

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

            string[] materialNames = Array.Empty<string>();

            var root = dlg.SelectedPath;

            // Cache materials list from text file
            var materialsListPath = Path.Combine(root, "Materials.txt");
            if (!File.Exists(materialsListPath))
                materialsListPath = Path.Combine(root, "Materials");

            if (File.Exists(materialsListPath))
            {
                materialNames = File.ReadAllLines(materialsListPath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToArray();
            }
            else
            {
                materialNames = Array.Empty<string>();
            }

            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name))
                    continue;

                var idx = name.IndexOfAny(new[] { ' ', '(' });
                var key = idx > 0 ? name[..idx] : name;

                var materials = Path.Combine(dir, "Materials");
                if (Directory.Exists(materials))
                    lookup[key] = materials;
            }

            var workspaces = GetWorkspaces?.Invoke();
            if (workspaces == null)
                return;

            foreach (var ws in workspaces)
            {
                if (ws.ActiveEditor is not BFRES bfres)
                {
                    continue;
                }

                var wsName = ws.Name;
                if (string.IsNullOrEmpty(wsName))
                    continue;

                var dot = wsName.IndexOf('.');
                var key = dot > 0 ? wsName[..dot] : wsName;

                if (lookup.TryGetValue(key, out var path))
                {
                    foreach (var materialName in materialNames)
                    {
                        if (!bfres.ResFile.Models[0].Materials.ContainsKey(materialName))
                            continue;

                        var materialJsonFile = Path.Combine(path, materialName + ".json");
                        if (!File.Exists(materialJsonFile))
                            continue;

                        bfres.ResFile.Models[0].Materials[materialName].Import(materialJsonFile, bfres.ResFile);
                    }
                }
            }
        }
    }
}
