using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Piglet.GLTF;
using UnityEditor;
using UnityEngine;

namespace Piglet
{
    /// <summary>
    /// This class uses Unity's `AssetPostprocessor` framework to
    /// automatically detect when new glTF files have been added to the current
    /// Unity project, and then imports them using `EditorGltfImporter`.
    /// Users can add glTF files to the project by either dragging-and-dropping
    /// them into the Unity Project Browser window or by saving them
    /// directly under the Assets folder.
    /// </summary>
    public class ProjectBrowserGltfImporter : AssetPostprocessor
    {
        /// <summary>
        /// A queue of glTF import coroutines (IEnumerators), where each
        /// coroutine corresponds to a single .gltf/.glb/.zip file
        /// that needs to be imported by Piglet. New import coroutines are
        /// added to the queue when Unity's AssetPostprocessor framework
        /// detects a new/changed glTF file located under Assets.
        /// </summary>
        private static List<IEnumerator> _importTasks;

        /// <summary>
        /// Absolute source paths of files that were dragged-and-dropped
        /// into the Project Browser window, and have not yet been
        /// imported. This list may contain non-glTF files, but such
        /// files will be silently ignored by Piglet and processed
        /// using Unity's default drag-and-drop handling.
        /// </summary>
        private static HashSet<string> _dragAndDropSourcePaths;

        /// <summary>
        /// This flag is used to temporarily disable automatic
        /// drag-and-drop glTF imports if the user is holding down the
        /// Control and/or Command keys. This feature is useful when the
        /// user just wants to copy glTF files into the project without
        /// any automatic conversions taking place.
        /// </summary>
        private static bool _disableDragAndDropImports;

        /// <summary>
        /// This HashSet is used for two purposes:
        /// (1) Skipping over nested files/folders after the
        /// user drags-and-drops a folder into the Project Browser, and
        /// (2) Preventing re-import of a glTF file when the
        /// user renames the file.
        /// </summary>
        private static HashSet<string> _visitedAssets;

        /// <summary>
        /// Performs one-time setup when the Unity Editor first loads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Setup()
        {
            _importTasks = new List<IEnumerator>();
            _dragAndDropSourcePaths = new HashSet<string>();
            _disableDragAndDropImports = false;
            _visitedAssets = new HashSet<string>();

            // Callback that is invoked at regular intervals by the
            // Editor (similar to the `Update` of a `MonoBehaviour`).
            // This callback is used to incrementally execute glTF import
            // coroutines in `_importTasks`.

            EditorApplication.update += OnEditorUpdate;

            // Note: EditorApplication.projectWindowItemOnGUI is invoked after
            // drawing each file/folder item that is currently visible
            // in the Project Browser, in both the left pane (folder tree) and
            // right pane (file list). We use this callback to catch
            // drop-and-drop events that happen in the Project Browser window.

            EditorApplication.projectWindowItemOnGUI += ProjectItemOnGUI;
        }

        /// <summary>
        /// Update handler that gets invoked by the Editor at regular intervals
        /// (multiple times per second). This method incrementally executes
        /// any pending glTF import tasks that were triggered by drag-and-drop
        /// or by saving glTF files directly into the project.
        /// </summary>
        static private void OnEditorUpdate()
        {
            while (_importTasks.Count > 0 && !_importTasks[0].MoveNext())
                _importTasks.RemoveAt(0);
        }

        /// <summary>
        /// Callback method that is invoked after drawing each file/folder
        /// item that is currently visible in the Project Browser
        /// window. We use this callback to catch drag-and-drop events
        /// in the Project Browser and to get the source paths of the
        /// dragged-and-dropped files.
        /// </summary>
        private static void ProjectItemOnGUI(string guid, Rect selectionrect)
        {
            if (Event.current.type != EventType.DragPerform
                && Event.current.rawType != EventType.DragPerform)
                return;

            // Note:
            //
            // `DragAndDrop.paths` contains the source paths for
            // the files/folders that were dragged-and-dropped
            // into the Unity Project Browser.
            //
            // The `!AssetPathUtil.IsAssetPath` check below excludes
            // files/folders that were dragged from one folder to
            // another within the current Unity project. We do this so
            // that glTF imports are only triggered by dragging-and-dropping
            // *external* glTF files/folders into the project.

            foreach (var path in DragAndDrop.paths)
            {
                if (!AssetPathUtil.IsAssetPath(path))
                    _dragAndDropSourcePaths.Add(path);
            }

            // Temporarily disable drag-and-drop-triggered glTF imports
            // if the user is holding down the Control and/or Command keys.
            // This feature is useful when the user just wants to copy glTF
            // files into the project without any automatic conversions
            // taking place.

            _disableDragAndDropImports = Event.current.control || Event.current.command;
        }

        /// <summary>
        /// AssetPostprocessor callback method that is invoked when files
        /// under Assets are created, modified, deleted, or moved. We use
        /// this callback to trigger automatic glTF imports when the
        /// user adds new glTF file(s) to the Unity project, either
        /// via drag-and-drop into the Project Browser or by directly
        /// saving glTF file(s) under Assets.
        /// </summary>
        /// <param name="importedAssets">
        /// Files under Assets that were created or modified since the
        /// last call to this method.</param>
        /// <param name="deletedAssets">
        /// Files under Assets that were deleted since the last call to
        /// this method.
        /// </param>
        /// <param name="movedAssets">
        /// Paths to files under Assets that were moved since the last
        /// call to this method. This array contains the *new* paths
        /// to the files *after* being moved.
        /// </param>
        /// <param name="movedFromAssetPaths">
        /// Paths to files under Assets that were moved since the last
        /// call to this method. This parameter contains the *old* paths
        /// to the files *before* being moved.
        /// </param>
        private static void OnPostprocessAllAssets(string[] importedAssets,
            string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Note: `PigletOptions.Instance` returns null if it is called too soon after
            // the Unity Editor starts up. I think this is due to a quirk of `Resource.Load`
            // method.

            if (PigletOptions.Instance == null || !PigletOptions.Instance.EnableEditorGltfImports)
                return;

            // Filter out asset paths that we don't care about:
            //
            // (1) Ignore paths not located under "Assets". Unity
            // sometimes calls `OnPostprocessAllAssets` with other
            // project-relative paths, e.g.
            // "ProjectSettings/EditorSettings.asset".
            //
            // (2) Ignore paths located under "Assets/StreamingAssets".
            // The AssetDatabase API does not seem to work correctly
            // under this folder and attempting to do a Editor glTF
            // import there produces strange/confusing errors.
            //
            // (3) When an asset is moved/renamed, Unity sometimes
            // adds the same asset path to both `importedAssets` and
            // `movedAssets` (and sometimes just to `movedAssets`). I
            // have no idea what the rules are for that decision.
            // To make things more consistent, I unconditionally
            // remove any paths from `importedAssets` that also appear
            // in `movedAssets`.

            var movedAssetsSet = new HashSet<string>(movedAssets);

            importedAssets = importedAssets
                .Where(AssetPathUtil.IsAssetPath)
                .Where(x => !AssetPathUtil.IsStreamingAssetPath(x))
                .Where(x => !movedAssetsSet.Contains(x))
                .ToArray();

            // Postprocess asset files/folders that were created by drag-and-drop.
            //
            // Assets created by drag-and-drop require special handling
            // in order to override Unity's default behaviour with respect
            // to overwriting existing files. See comments for
            // `OnPostprocessDragAndDropAsset` method for further details.
            //
            // Note: We use `CollapseAssetPaths` below so that when the user
            // drags-and-drops a folder, we only process the top-level folder and
            // not each file/subfolder recursively contained within.
            // This simplifies the logic of the drag-and-drop code.

            var topLevelAssets = PathUtil.CollapsePaths(importedAssets).ToArray();
            foreach (var assetPath in topLevelAssets)
            {
                // Get external path that was dragged into Unity to create this asset.
                var dragAndDropSourcePath = GetDragAndDropSourcePath(assetPath);

                // If asset was created by drag-and-drop.
                if (dragAndDropSourcePath != null)
                    OnPostprocessDragAndDropAsset(dragAndDropSourcePath, assetPath);
            }

            // Import glTF files that were saved directly under Assets from an
            // external program (e.g. Blender, Windows File Explorer).

            foreach (var assetPath in importedAssets)
            {
                // `_visitedAssets` is used for two purposes:
                //
                // (1) Skipping files/folders that have already been
                // processed by drag-and-drop imports above.
                // (2) Preventing re-import of a glTF file when the
                // user renames it in the Project Browser (see comments above).

                if (_visitedAssets.Contains(assetPath))
                {
                    _visitedAssets.Remove(assetPath);
                    continue;
                }

                if (!AssetPathUtil.IsGltfAsset(assetPath))
                    continue;

                var gltfPath = AssetPathUtil.GetAbsolutePath(assetPath);
                var importFolderName = Path.GetFileNameWithoutExtension(assetPath);
                var parentFolder = AssetPathUtil.GetParentFolder(assetPath);
                var importFolder = PathUtil.Combine(parentFolder, importFolderName);

                _importTasks.Add(GltfImportTask(gltfPath, importFolder));
            }
        }

        /// <summary>
        /// <para>
        /// Postprocess an asset file/folder that was created by
        /// dragging-and-dropping an external file into the Project Browser.
        /// In comparison to assets that are created by saving files
        /// under Assets from an external program (e.g. Blender), assets created
        /// by drag-and-drop require special handling because we override some
        /// of Unity's default drag-and-drop behaviours:
        /// <list type="number">
        /// <item><description>
        /// When dragging-and-dropping a file/folder that already exists in
        /// the target directory, Unity's default behaviour is to append/increment
        /// a numeric suffix on the file/folder name to avoid overwriting
        /// the existing file/folder (see [1] below). However, to match Piglet's legacy
        /// drag-and-drop behaviour, we instead replace the existing
        /// file/folder after showing a confirmation prompt to the user.
        /// </description></item>
        /// <item><description>
        /// Unity's default behaviour it to create a copy of the dragged-and-dropped
        /// file/folder in the project. However, to match Piglet's legacy drag-and-drop
        /// behaviour, the only thing we add to the project is the Piglet-generated
        /// import folder containing the output Unity prefab and texture/material/mesh/animation
        /// assets. Since Unity has already copied the source glTF file/folder into
        /// the project before calling this method (`OnPostprocessAllAssets`),
        /// we delete the project's copy before starting the glTF import.
        /// Note that setting `PigletOptions.Instance.DragAndDropOptions.CopyGltfFilesIntoProject`
        /// to true overrides this behaviour and keeps the project-side copy of
        /// the source glTF file/folder.
        /// </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// [1]: https://docs.unity3d.com/ScriptReference/AssetDatabase.GenerateUniqueAssetPath.html
        /// </para>
        /// </summary>
        /// <param name="dragAndDropSourcePath">
        /// The absolute path of the external file/folder that was dragged
        /// into the Project Browser to create `assetPath`.
        /// </param>
        /// <param name="assetPath">
        /// The asset path for the project file/folder that was created
        /// by drag-and-drop. This path must be relative to the root of the
        /// Unity project and begin with "Assets".
        /// </param>
        private static void OnPostprocessDragAndDropAsset(
            string dragAndDropSourcePath, string assetPath)
        {
            // Remove path from `_dragAndDropSourcePaths` to indicate that
            // it has been processed.

            _dragAndDropSourcePaths.Remove(dragAndDropSourcePath);

            // Skip non-glTF files/folders.

            if (!AssetPathUtil.FindGltfAssets(assetPath).Any())
                return;

            // If user was holding Ctrl and/or Command keys during drag-and-drop,
            // temporarily disable glTF imports and use Unity's default
            // drag-and-drop behaviour instead, which just copies the
            // file/folder into the project.

            if (_disableDragAndDropImports)
            {
                foreach (var path in AssetPathUtil.GetRecursiveAssetsList(assetPath))
                    _visitedAssets.Add(path);
                return;
            }

            // Determine the target path for the drag-and-drop operation.

            var dragAndDropSourceName =
                PathUtil.GetLastPathComponent(dragAndDropSourcePath);

            var dragAndDropTargetFolder =
                AssetPathUtil.GetParentFolder(assetPath);

            var dragAndDropTargetPath =
                PathUtil.Combine(dragAndDropTargetFolder, dragAndDropSourceName);

            // If `assetPath` does not match `dragAndDropTargetPath`,
            // it means that Unity has automatically renamed `assetPath`
            // to avoid overwriting an existing file/folder, by
            // appending/incrementing a numeric suffix (see [1]).
            //
            // [1]: https://docs.unity3d.com/ScriptReference/AssetDatabase.GenerateUniqueAssetPath.html

            var targetPathExists = assetPath != dragAndDropTargetPath;

            // Confirm overwrite of any existing files/folders.

            var dragAndDropOptions = PigletOptions.Instance.DragAndDropOptions;

            if (dragAndDropOptions.PromptBeforeOverwritingFiles
                && !ConfirmOverwriteDuringDragAndDrop(
                    dragAndDropSourcePath, dragAndDropTargetPath,
                    targetPathExists))
            {
                // User chose "Cancel" from the overwrite prompt.
                //
                // Delete the project copy of the dragged-and-dropped
                // glTF file/folder and mark `assetPath` as visited
                // to prevent further processing.
                //
                // Note: We can't immediately delete `assetPath`
                // here because Unity hasn't written the file/folder
                // to disk yet, so we queue a task instead.

                foreach (var path in AssetPathUtil.GetRecursiveAssetsList(assetPath))
                    _visitedAssets.Add(path);

                _importTasks.Add(CreateImportTask(
                    () => AssetDatabase.DeleteAsset(assetPath)));

                return;
            }

            if (dragAndDropOptions.CopyGltfFilesIntoProject)
            {
                // Case 1: The `CopyGltfFilesIntoProject` option is true.
                //
                // We let the downstream code in `OnPostprocessAllAssets`
                // handle the glTF import, just as if the glTF file had been
                // saved under Assets from an external program (e.g. Blender).
                //
                // If the user dragged-and-dropped a glTF file (not a folder),
                // copy any external .png/.jpg/.bin files referenced by the glTF file
                // into the project. (In the case that the user drags-and-drops
                // a folder, we don't need to do anything special -- we
                // assume that any required .png/.jpg/.bin files are already
                // contained within the folder.)

                if (!AssetDatabase.IsValidFolder(assetPath))
                {
                    var assetFolder = AssetPathUtil.GetParentFolder(assetPath);
                    _importTasks.Add(CreateImportTask(
                        () => CopyGltfDependenciesIntoProject(
                            dragAndDropSourcePath, assetFolder)));
                }

                // If Unity appended/incremented a numeric suffix
                // on `assetPath` to avoid overwriting an existing file/folder,
                // undo that renaming and force the existing file/folder to be
                // overwritten instead. (This is done to match Piglet's
                // legacy drag-and-drop behaviour.)

                if (assetPath != dragAndDropTargetPath)
                {
                    foreach (var path in AssetPathUtil.GetRecursiveAssetsList(assetPath))
                        _visitedAssets.Add(path);

                    _importTasks.Add(CreateImportTask(
                        () => AssetPathUtil.MoveAsset(assetPath, dragAndDropTargetPath)));

                    _importTasks.Add(CreateImportTask(
                        () => ImportGltfAssets(dragAndDropTargetPath)));
                }

            }
            else
            {
                // Case 2: The `CopyGltfFilesIntoProject` option is false.
                //
                // Delete the project copy of the glTF file/folder that was
                // created by Unity during the drag-and-drop. Then import
                // the glTF files directly from the drag-and-drop source
                // file/folder.
                //
                // Importing from the drag-and-drop source file/folder ensures
                // that we resolve any relative paths to .jpg/.png/.bin files
                // inside .gltf/.glb files.
                //
                // Note: We need to queue a task to delete `assetPath` because it
                // hasn't been written to disk yet.

                foreach (var path in AssetPathUtil.GetRecursiveAssetsList(assetPath))
                    _visitedAssets.Add(path);

                _importTasks.Add(CreateImportTask(
                    () => AssetDatabase.DeleteAsset(assetPath)));

                foreach (var gltfPath in FileUtil.FindGltfFiles(dragAndDropSourcePath))
                {
                    var importFolderName = Path.GetFileNameWithoutExtension(gltfPath);
                    var importFolder = PathUtil.Combine(
                        dragAndDropTargetFolder, importFolderName);

                    _importTasks.Add(GltfImportTask(gltfPath, importFolder));
                }
            }
        }

        /// <summary>
        /// Create an import task (IEnumerator method) from a C# Action.
        /// This method is used to defer actions that can't be performed
        /// immediately (e.g. deleting a file that Unity hasn't written
        /// to disk yet).
        /// </summary>
        private static IEnumerator CreateImportTask(Action action)
        {
            action.Invoke();
            yield return null;
        }

        /// <summary>
        /// <para>
        /// Call `AssetDatabase.ImportAsset` on the
        /// glTF file(s) at the given asset path, which triggers
        /// a new invocation of `OnPostprocessAllAssets` with the
        /// glTF file(s) passed in via `importedAssets`.
        /// </para>
        /// <para>
        /// If the given asset path is a glTF file, then just that file will
        /// be imported. If the asset path is a folder, then all glTF file(s)
        /// contained within the folder will be imported recursively.
        /// </para>
        /// </summary>
        private static void ImportGltfAssets(string assetPath)
        {
            foreach (var gltfAsset in AssetPathUtil.FindGltfAssets(assetPath))
                AssetDatabase.ImportAsset(gltfAsset);
        }

        /// <summary>
        /// <para>
        /// Get the absolute path of the external file/folder that was
        /// dragged-and-dropped into Unity to create `assetPath`. We determine
        /// this by comparing `assetPath` against the list of source paths from
        /// the last IMGUI drag-and-drop event (`_dragAndDropPaths`).
        /// </para>
        /// <para>
        /// Note that this method is not as straighforward as one might
        /// expect, because Unity appends a numeric suffix to `assetPath` when
        /// a file/folder with the same name already exists in the target project
        /// directory (to avoid overwriting the existing file). This method
        /// is able to determine the correct source path for the drag-and-drop
        /// even when Unity has appended a numeric suffix to `assetPath`.
        /// </para>
        /// </summary>
        protected static string GetDragAndDropSourcePath(string assetPath)
        {
            var filename = PathUtil.GetLastPathComponent(assetPath);

            string dragAndDropPath = null;
            var numMatches = 0;

            foreach (var path in _dragAndDropSourcePaths)
            {
                if (PathUtil.GetLastPathComponent(path) == filename)
                {
                    dragAndDropPath = path;
                    numMatches++;
                }
            }

            if (numMatches > 1)
            {
                throw new Exception(String.Format(
                    "Cannot drag-and-drop multiple glTF files with the same filename: \"{0}\". " +
                    "Please drag-and-drop the files one at a time instead.", filename));
            }

            if (numMatches == 1)
                return dragAndDropPath;

            // We were not able to find `filename` in `_dragAndDropPaths` above.
            // It may be because Unity automatically appended a numeric suffix,
            // in order to prevent overwriting an existing file.
            //
            // Try decrementing/removing the numeric suffix of `assetPath`
            // (if any) and try again.

            var decrementedAssetPath = AssetPathUtil.DecrementFilename(assetPath);
            if (decrementedAssetPath == null)
                return null;

            return GetDragAndDropSourcePath(decrementedAssetPath);
        }

        /// <summary>
        /// Given the path of a glTF file that was dragged-and-dropped into
        /// the Unity project, determine which existing project files (if any)
        /// would be overwritten by performing a glTF import of that file.
        /// Show a dialog to confirm overwrite of the affected files
        /// and return true if the user chooses to proceed, or false if
        /// the user chooses to cancel. If no files would be overwritten by
        /// the glTF import, then return true without showing the confirmation
        /// dialog.
        /// </summary>
        /// <param name="dragAndDropSourcePath">
        /// The path of a glTF file/folder that was dragged-and-dropped into the
        /// Unity Project Browser window. This path must be an absolute path.
        /// </param>
        /// <param name="dragAndDropTargetPath">
        /// The project path where the copy of the dragged-and-dropped
        /// file/folder will be created. This must be path relative to the
        /// root of the Unity project (e.g. "Assets/Imports/model.gltf").
        /// </param>
        /// <returns>
        /// True if the user confirmed overwrite of the affected
        /// project files/folders, or false if the user chose to cancel
        /// the glTF import.
        /// </returns>
        private static bool ConfirmOverwriteDuringDragAndDrop(
            string dragAndDropSourcePath, string dragAndDropTargetPath,
            bool targetPathExists)
        {
            var assetPaths = GetFilesOverwrittenByDragAndDrop(
                dragAndDropSourcePath, dragAndDropTargetPath, targetPathExists);
            if (assetPaths.Count == 0)
                return true;

            return EditorUtility.DisplayDialog(
                "Warning!",
                String.Format(
                    "Overwrite the following files and folders?\n\n{0}",
                    String.Join("\n", assetPaths)),
                "OK", "Cancel");
        }

        /// <summary>
        /// Given the path of a glTF file that was dragged-and-dropped into the
        /// Unity project, return the list of project files/folders that would
        /// be overwritten if a glTF import was performed on that file.
        /// </summary>
        /// <param name="dragAndDropSourcePath">
        /// The path of a glTF file/folder that was dragged-and-dropped into the
        /// Unity Project Browser window. This path must be an absolute path.
        /// </param>
        /// <param name="dragAndDropTargetPath">
        /// The project path where the copy of the dragged-and-dropped
        /// file/folder will be created. This must be path relative to the
        /// root of the Unity project (e.g. "Assets/Imports/model.gltf").
        /// </param>
        /// <param name="targetPathExists">
        /// If this is true, it means that `dragAndDropTargetPath` existed
        /// before the user performed the drag-and-drop operation.
        /// Note that `dragAndDropTargetPath` is always created by Unity
        /// if it doesn't already exist, so directly testing for the presence
        /// of that file/folder does not provide any useful information.
        /// </param>
        /// <returns>
        /// A list of Unity project files/folders that would be overwritten
        /// during a glTF import of given glTF file (`assetPath`). The
        /// return paths are relative to the Unity project root (e.g.
        /// "Assets/Imports/model.gltf").
        /// </returns>
        private static List<string> GetFilesOverwrittenByDragAndDrop(
            string dragAndDropSourcePath, string dragAndDropTargetPath,
            bool targetPathExists)
        {
            var overwrittenPaths = new List<string>();

            var dragAndDropTargetFolder =
                AssetPathUtil.GetParentFolder(dragAndDropTargetPath);

            var dragAndDropOptions = PigletOptions.Instance.DragAndDropOptions;

            if (dragAndDropOptions.CopyGltfFilesIntoProject)
            {
                // Case 1: `CopyGltfFilesIntoProject` option is true.
                //
                // In addition to creating the import folder with the
                // prefab and associated texture/material/mesh/animation assets,
                // the source glTF file/folder will also be copied into the project.

                if (targetPathExists)
                    overwrittenPaths.Add(dragAndDropTargetPath);

                // If the user dragged-and-dropped a glTF file (not a folder),
                // it means that Piglet will:
                //
                // (1) Create/overwrite an import folder in the same folder
                // as the glTF file.
                // (2) Copy any .png/.jpg/.bin files referenced by the
                // glTF file alongside the glTF file itself.
                //
                // The overwrite logic is simpler when dragging-and-dropping a folder,
                // because in that case we only create/overwrite the project-side copy of
                // the folder (and everything underneath it). Piglet creates the import
                // folder(s) inside the target folder alongside the corresponding glTF
                // file(s), and any required .png/.jpg/.bin files are assumed to provided
                // inside the folder. (In other words, Piglet does not do "smart copying"
                // of dependencies in the case of dragging-and-dropping folders.)

                if (File.Exists(dragAndDropSourcePath))
                {
                    // Check if import folder already exists.

                    var importPath = GetImportFolderPath(dragAndDropTargetPath);

                    if (AssetPathUtil.Exists(importPath))
                        overwrittenPaths.Add(importPath);

                    // Check if glTF dependencies (e.g. .png files) already exist.

                    foreach (var relativePath in
                        GetGltfDependencies(dragAndDropSourcePath))
                    {
                        var destAssetPath = AssetPathUtil.ResolveAssetPath(
                            dragAndDropTargetFolder, relativePath);

                        if (AssetPathUtil.Exists(destAssetPath))
                            overwrittenPaths.Add(destAssetPath);
                    }

                }
            }
            else
            {
                // Case 2: `CopyGltfFilesIntoProject` option is false.
                //
                // The source glTF file/folder will not be copied into the project.
                // Only the import folder will be added to the project,
                // i.e. the folder containing the Unity prefab and associated
                // texture/material/mesh/animation assets.

                foreach (var gltfPath in
                    FileUtil.FindGltfFiles(path: dragAndDropSourcePath))
                {
                    var importFolderName =
                        Path.GetFileNameWithoutExtension(gltfPath);

                    var importFolderPath = PathUtil.Combine(
                        dragAndDropTargetFolder, importFolderName);

                    // Special case. (See documentation for `targetPathExists`
                    // parameter above.)
                    if (importFolderPath == dragAndDropTargetPath)
                    {
                        if (targetPathExists)
                            overwrittenPaths.Add(importFolderPath);
                        continue;
                    }

                    if (AssetPathUtil.Exists(importFolderPath))
                        overwrittenPaths.Add(importFolderPath);
                }
            }

            return overwrittenPaths;
        }

        /// <summary>
        /// Given the path of a glTF file inside the current Unity project,
        /// return the path of the import folder that would be created by Piglet.
        /// The import folder contains the Unity prefab generated during the
        /// glTF import, and any texture/material/mesh/animation assets that
        /// it depends on.
        /// </summary>
        /// <param name="assetPath">
        /// The path of a .gltf/.glb/.zip file in the current Unity
        /// project. This path must be relative to the Unity project
        /// root (e.g. "Assets/Imported/model.gltf").
        /// </param>
        private static string GetImportFolderPath(string assetPath)
        {
            var importFolderName = Path.GetFileNameWithoutExtension(assetPath);
            var parentFolder = AssetPathUtil.GetParentFolder(assetPath);

            return PathUtil.Combine(parentFolder, importFolderName);
        }

        /// <summary>
        /// Get the list of local files that are referenced by the given
        /// .gltf or .glb file.
        /// </summary>
        private static IEnumerable<string> GetGltfDependencies(string absolutePath)
        {
            var referencedFiles = new HashSet<string>();

            var data = File.ReadAllBytes(absolutePath);

            // If the input file is a .zip, all needed files are assumed
            // to be contained within the archive.

            if (ZipUtil.IsZipData(data))
                return referencedFiles;

            // Parse JSON content of glTF file into a hierarchy of C# objects.

            var gltf = GLTFParser.ParseJson(data);

            // Check glTF buffer definitions for references to local files
            // (e.g. .bin files).

            if (gltf.Buffers != null)
            {
                foreach (var buffer in gltf.Buffers)
                {
                    if (UriUtil.IsRelativeFilePath(buffer.Uri))
                    {
                        // Collapse each (relative) path down to its first component.
                        // For example, transform "textures/wood.png" -> "textures".
                        // We do this because we want to completely replace
                        // existing folders when copying glTF dependencies into
                        // the Unity project, rather than creating a mix of new
                        // and old files in the existing folder.

                        referencedFiles.Add(
                            PathUtil.GetPathComponents(buffer.Uri)[0]);
                    }
                }
            }

            // Check glTF image buffer definitions for references to local files
            // (e.g. .png files, .jpg files).

            if (gltf.Images != null)
            {
                foreach (var image in gltf.Images)
                {
                    if (UriUtil.IsRelativeFilePath(image.Uri))
                    {
                        // Collapse each (relative) path down to its first component.
                        // For example, transform "textures/wood.png" -> "textures".
                        // We do this because we want to completely replace
                        // existing folders when copying glTF dependencies into
                        // the Unity project, rather than creating a mix of new
                        // and old files in the existing folder.

                        referencedFiles.Add(
                            PathUtil.GetPathComponents(image.Uri)[0]);
                    }
                }
            }

            return referencedFiles;
        }

        /// <summary>
        /// Copy any external files (.e.g. .png, .jpg, .bin) referenced
        /// by the given glTF file to the target project folder. Existing
        /// files/folders will be silently overwritten.
        /// </summary>
        /// <param name="gltfPath">
        /// The absolute path of the source .gltf/.glb/.zip file.
        /// </param>
        /// <param name="assetFolder">
        /// The target asset folder relative to the Unity project root
        /// (e.g. "Assets/Imports").
        /// </param>
        private static void CopyGltfDependenciesIntoProject(string gltfPath, string assetFolder)
        {
            var sourceDir = Path.GetDirectoryName(gltfPath);
            var targetDir = AssetPathUtil.GetAbsolutePath(assetFolder);

            foreach (var relativePath in GetGltfDependencies(gltfPath))
            {
                var sourcePath = PathUtil.ResolvePath(sourceDir, relativePath);
                var destPath = PathUtil.ResolvePath(targetDir, relativePath);

                // When copying directories, completely replace
                // the destination folder if it already exists.
                // (As opposed to creating a mix of new and old files in
                // the destination directory.)

                if (Directory.Exists(sourcePath) && Directory.Exists(destPath))
                    Directory.Delete(destPath, true);

                // Copy file/directory recursively, like Linux `cp -Rf` command.

                FileUtil.CopyRecursively(sourcePath, destPath);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Coroutine to import a glTF file with Piglet's EditorGltfImporter.
        /// </summary>
        /// <param name="gltfPath">
        /// Absolute path of glTF file that will be imported.
        /// </param>
        /// <param name="importFolder">
        /// Path to the asset folder that will contain the Piglet-generated prefab
        /// and texture/material/mesh/animation assets. This path must
        /// be specified relative to the root of the Unity project and have
        /// "Assets" as its first component (i.e. an "asset path"). The target
        /// folder will be created if it does not exist or overwritten if
        /// it does exist.
        /// </param>
        private static IEnumerator GltfImportTask(string gltfPath, string importFolder)
        {
            string gltfBasename = Path.GetFileName(gltfPath);

            bool abortImport = false;

            // configure logging during glTF import

            ProgressLog.Instance.AddLineCallback = null;
            ProgressLog.Instance.UpdateLineCallback = null;

            var pigletOptions = PigletOptions.Instance;

            if (pigletOptions.LogProgress)
            {
                ProgressLog.Instance.AddLineCallback = Debug.Log;
                ProgressLog.Instance.UpdateLineCallback = Debug.Log;
            }

            // callback for updating progress during glTF import

            void OnProgress(GltfImportStep type, int count, int total)
            {
                ProgressLog.Instance.OnImportProgress(type, count, total);

                abortImport = EditorUtility.DisplayCancelableProgressBar(
                    $"Importing {gltfBasename}...",
                    ProgressLog.Instance.GetProgressMessage(),
                    (float) count / total);
            }

            // remove existing import folder (if present)

            var importPath = AssetPathUtil.GetAbsolutePath(importFolder);

            if (Directory.Exists(importPath) || File.Exists(importPath))
            {
                FileUtil.DeleteFileOrDirectory(importPath);
                AssetDatabase.Refresh();
            }

            GltfImportTask importTask =
                EditorGltfImporter.GetImportTask(gltfPath, importPath,
                    pigletOptions.ImportOptions);

            importTask.OnProgress = OnProgress;

            GameObject importedPrefab = null;
            importTask.OnCompleted = (prefab) => importedPrefab = prefab;

            // restart import timer at zero
            ProgressLog.Instance.StartImport();

            while (true)
            {
                if (abortImport)
                {
                    importTask.Abort();
                    EditorUtility.ClearProgressBar();
                    yield break;
                }

                try
                {
                    if (!importTask.MoveNext())
                        break;
                }
                catch (ExternalFileNotFoundException e)
                {
                    Debug.LogException(e);

                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Import Failed",
                        string.Format("Import of \"{0}\" failed because it references " +
                              "external files (e.g. PNG files for textures). Try copying " +
                              "the folder containing \"{0}\" into the project instead.",
                            gltfBasename),
                        "OK");

                    yield break;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);

                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Import Failed",
                        String.Format("Import of {0} failed. "
                            + "See Unity console log for details.", gltfBasename),
                        "OK");

                    yield break;
                }

                yield return null;
            }

            // Not sure why this is needed, but it seems to make the
            // post-import actions below work more reliably.

            AssetDatabase.Refresh();

            // Open the import folder in the Project Browser and
            // highlight the prefab file.
            //
            // So far, I can't figure out how to select the prefab asset in the
            // right Project Browser pane so that it gets the blue selection
            // bar behind it, as if the user had clicked on it.
            // But `EditorGUIUtility.PingObject` highlights the prefab file
            // temporarily, which is still very helpful.

            if (pigletOptions.PostImportOptions.SelectPrefabInProject)
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = importedPrefab;
                EditorGUIUtility.PingObject(importedPrefab);

                yield return null;
            }

            // Open the prefab in the Scene View and center the camera on it.

            if (pigletOptions.PostImportOptions.OpenPrefabInSceneView)
            {
                AssetDatabase.OpenAsset(importedPrefab);

                SceneView.lastActiveSceneView.ShowTab();

                // Note: This is the best method I could find
                // for automatically centering the prefab in
                // the scene view. For further info, see
                // https://answers.unity.com/questions/813814/framing-objects-via-script-in-the-unity-editor.html
                SceneView.FrameLastActiveSceneView();
            }

            EditorUtility.ClearProgressBar();
        }

    }
}
