using System;

namespace Piglet
{
    /// <summary>
    /// Options for automatic Editor actions after a glTF import
    /// successfully completes.
    /// </summary>
    [Serializable]
    public class PostImportOptions
    {
        /// <summary>
        /// If true, select the imported prefab in the Project Browser
        /// after the glTF import successfully completes.
        /// </summary>
        public bool SelectPrefabInProject;

        /// <summary>
        /// If true, open the imported prefab in Scene View
        /// after the glTF import successfully completes.
        /// </summary>
        public bool OpenPrefabInSceneView;

        /// <summary>
        /// Constructor. This method sets the default option values
        /// for Editor actions that occur after a glTF import
        /// successfully completes.
        /// </summary>
        public PostImportOptions()
        {
            SelectPrefabInProject = true;
            OpenPrefabInSceneView = true;
        }
    }
}