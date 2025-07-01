using UnityEngine;

namespace Piglet
{
    /// <summary>
    /// User-configurable options for drag-and-drop import of
    /// glTF models in the Project Browser.  These options are
    /// set in the Piglet Options window, located under
    /// Window -> Piglet Options in the Unity menu.
    /// </summary>
    [CreateAssetMenu(fileName="PigletOptions", menuName="Piglet Options", order=51)]
    public class PigletOptions : ScriptableObject
    {
        /// <summary>
        /// Singleton instance of PigletOptions.
        /// </summary>
        private static PigletOptions _instance;

        /// <summary>
        /// Get a reference to the singleton instance of PigletOptions, creating
        /// it first if necessary.
        /// </summary>
        public static PigletOptions Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<PigletOptions>("PigletOptions");
                return _instance;
            }
        }

        /// <summary>
        /// Globally enables/disables glTF imports in the Editor.
        /// </summary>
        [SerializeField] public bool EnableEditorGltfImports;

        /// <summary>
        /// If true, print progress messages to the Unity
        /// Console during a glTF import.
        /// </summary>
        [SerializeField] public bool LogProgress;

        /// <summary>
        /// Options that are common to both Editor and runtime glTF imports.
        /// </summary>
        [SerializeField] public GltfImportOptions ImportOptions;

        /// <summary>
        /// Options that control the behaviour of drag-and-drop glTF imports in the Editor.
        /// </summary>
        [SerializeField] public DragAndDropOptions DragAndDropOptions;

        /// <summary>
        /// Options that control Editor actions after a glTF import
        /// successfully completes.
        /// </summary>
        [SerializeField] public PostImportOptions PostImportOptions;

        /// <summary>
        /// Reset all Piglet glTF import options to default values.
        /// </summary>
        public void Reset()
        {
            EnableEditorGltfImports = true;
            LogProgress = false;
            ImportOptions = new GltfImportOptions();
            DragAndDropOptions = new DragAndDropOptions();
            PostImportOptions = new PostImportOptions();
        }
    }
}
