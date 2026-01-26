using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using MapStudio.UI;

namespace TrackStudio.Tools
{
    /// <summary>
    /// Manages and executes tool actions from the Tools menu.
    /// Provides a modular system for adding tools without modifying MainWindow directly.
    /// </summary>
    public static class ToolManager
    {
        private static readonly List<IToolAction> _tools = new List<IToolAction>();

        /// <summary>
        /// Gets all registered tools.
        /// </summary>
        public static IReadOnlyList<IToolAction> Tools => _tools.AsReadOnly();

        /// <summary>
        /// Registers a tool action.
        /// </summary>
        /// <param name="tool">The tool to register.</param>
        public static void Register(IToolAction tool)
        {
            if (tool == null)
                throw new ArgumentNullException(nameof(tool));

            if (!_tools.Contains(tool))
                _tools.Add(tool);
        }

        /// <summary>
        /// Registers multiple tool actions.
        /// </summary>
        /// <param name="tools">The tools to register.</param>
        public static void RegisterRange(IEnumerable<IToolAction> tools)
        {
            foreach (var tool in tools)
                Register(tool);
        }

        /// <summary>
        /// Unregisters a tool action.
        /// </summary>
        /// <param name="tool">The tool to unregister.</param>
        public static void Unregister(IToolAction tool)
        {
            _tools.Remove(tool);
        }

        /// <summary>
        /// Clears all registered tools.
        /// </summary>
        public static void Clear()
        {
            _tools.Clear();
        }

        /// <summary>
        /// Initializes all default tools.
        /// </summary>
        public static void InitializeDefaultTools()
        {
            // Register all default tools here
            Register(new MaterialReplacerTool());
            Register(new BoneOptimizerTool());
            Register(new VertexFormatOptimizerTool());
        }

        /// <summary>
        /// Renders all tool menu items using ImGui.
        /// </summary>
        public static void RenderToolMenu()
        {
            foreach (var tool in _tools)
            {
                bool canExecute = tool.CanExecute();
                
                // Skip rendering disabled tools or render them grayed out
                if (!canExecute)
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
                }

                if (ImGui.MenuItem(TranslationSource.GetText(tool.Name)))
                {
                    if (canExecute)
                        tool.Execute();
                }

                if (!string.IsNullOrEmpty(tool.Description) && ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(tool.Description);
                }

                if (!canExecute)
                {
                    ImGui.PopStyleVar();
                }
            }
        }

        /// <summary>
        /// Gets a tool by name.
        /// </summary>
        /// <param name="name">The name of the tool.</param>
        /// <returns>The tool if found, otherwise null.</returns>
        public static IToolAction GetTool(string name)
        {
            return _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a tool by type.
        /// </summary>
        /// <typeparam name="T">The type of tool to get.</typeparam>
        /// <returns>The tool if found, otherwise null.</returns>
        public static T GetTool<T>() where T : IToolAction
        {
            return _tools.OfType<T>().FirstOrDefault();
        }
    }
}
