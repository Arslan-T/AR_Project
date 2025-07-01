using System;

namespace Piglet
{
    /// <summary>
    /// Options that control the behaviour of drag-and-drop glTF imports in the Editor.
    /// </summary>
    [Serializable]
    public class DragAndDropOptions
    {
        /// <summary>
        /// If true, show a confirmation prompt whenever a drag-and-drop
        /// import would overwrite existing files.
        /// </summary>
        public bool PromptBeforeOverwritingFiles;

        /// <summary>
        /// If true, copy the source glTF file and any referenced
        /// external files (e.g. PNG files for textures) into the
        /// project. Otherwise, only the results of the glTF import
        /// are added to the project, i.e. the folder containing
        /// the Unity prefab and the texture/material/mesh/animation
        /// assets it depends on.
        /// </summary>
        public bool CopyGltfFilesIntoProject;

        /// <summary>
        /// Constructor. This method sets the default values for
        /// options related to drag-and-drop glTF imports.
        /// </summary>
        public DragAndDropOptions()
        {
            PromptBeforeOverwritingFiles = true;
            CopyGltfFilesIntoProject = false;
        }
    }
}
