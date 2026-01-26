using System;
using System.Collections.Generic;

namespace TrackStudio.Tools
{
    /// <summary>
    /// Interface for tool actions that can be executed from the Tools menu.
    /// </summary>
    public interface IToolAction
    {
        /// <summary>
        /// The display name of the tool shown in the menu.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Optional description of what the tool does.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Executes the tool action.
        /// </summary>
        void Execute();

        /// <summary>
        /// Returns true if the tool can be executed in the current state.
        /// </summary>
        bool CanExecute();
    }
}
