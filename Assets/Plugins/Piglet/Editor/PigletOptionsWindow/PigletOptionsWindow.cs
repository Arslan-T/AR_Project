using System;
using UnityEditor;
using UnityEngine;

namespace Piglet
{
    /// <summary>
    /// Editor window for controlling options during
    /// Editor glTF imports (e.g. dragging-and-dropping
    /// a .glb file into the Project Browser window).
    /// </summary>
    public class PigletOptionsWindow : EditorWindow
    {
        /// <summary>
        /// Struct that contains the style settings for
        /// the various IMGUI controls (e.g. GUILayout.Button)
        /// used by the Piglet Options winodw.
        /// </summary>
        private class Styles
        {
            public GUIStyle Button;
            public GUIStyle Label;
            public GUIStyle TextField;
            public GUIStyle Title;
            public GUIStyle ToggleLevel1;
            public GUIStyle ToggleLevel2;
            public GUIStyle ToggleLevel3;
        }

        /// <summary>
        /// Struct that contains the style settings for
        /// the various IMGUI controls (e.g. GUILayout.Button)
        /// used by the Piglet Options winodw.
        /// </summary>
        private Styles _styles;

        /// <summary>
        /// Initialize the style settings for the various IMGUI
        /// controls used by the Piglet Options window.
        /// </summary>
        private void InitStyles()
        {
            if (_styles != null)
                return;

#if UNITY_2019_3_OR_NEWER
            const int fontSize = 14;
            const int titleFontSize = 16;
#else
            const int fontSize = 12;
            const int titleFontSize = 16;
#endif

            _styles = new Styles();

            _styles.Button = new GUIStyle(GUI.skin.button);
            _styles.Button.fontSize = fontSize;
            _styles.Button.padding.left = 0;
            _styles.Button.margin.left = 0;

            _styles.Label = new GUIStyle(GUI.skin.label);
            _styles.Label.padding.left = 0;
            _styles.Label.fontSize = fontSize;

            _styles.TextField = new GUIStyle(GUI.skin.textField);
            _styles.TextField.fontSize = fontSize;

            _styles.Title = new GUIStyle(GUI.skin.label);
            _styles.Title.alignment = TextAnchor.MiddleLeft;
            _styles.Title.padding.left = 0;
            _styles.Title.margin = new RectOffset(0, 0, 15, 15);
            _styles.Title.fontSize = titleFontSize;
            _styles.Title.fontStyle = FontStyle.Bold;

            // Note: For toggle controls, `padding.left` sets
            // the distance from the left edge of the control
            // to start of the text. This value needs to
            // be large enough to ensure that the text does
            // not overlap the checkbox graphic on the left
            // side of the control.

            _styles.ToggleLevel1 = new GUIStyle(GUI.skin.toggle);
            _styles.ToggleLevel1.margin.left = 0;
            _styles.ToggleLevel1.padding.left = 20;
            _styles.ToggleLevel1.fontSize = fontSize;

            _styles.ToggleLevel2 = new GUIStyle(_styles.ToggleLevel1);
            _styles.ToggleLevel2.margin.left += 20;

            _styles.ToggleLevel3 = new GUIStyle(_styles.ToggleLevel2);
            _styles.ToggleLevel3.margin.left += 20;
        }

        /// <summary>
        /// Method to open the Piglet Options window. This
        /// method is triggered by the Window -> Piglet Options
        /// item in the Unity menu.
        /// </summary>
        [MenuItem("Window/Piglet Options")]
        public static void ShowWindow()
        {
            EditorWindow window = GetWindow(typeof(PigletOptionsWindow),
                false, "Piglet Options");

#if UNITY_EDITOR_LINUX && !UNITY_2019_3_OR_NEWER
            // Workaround for a strange bug in Unity 2018.4.16f1 on Linux:
            // If `window.minSize` and `window.maxSize` are set to the same value,
            // the created window is smaller than the target dimensions and can't be resized.
            // Another issue is that `window.minSize` and `window.maxSize` do not seem
            // to be enforced on Linux.
            window.minSize = new Vector2(285f, 460f);
            window.maxSize = new Vector2(286f, 461f);
#else
            window.minSize = new Vector2(285f, 460f);
            window.maxSize = window.minSize;
#endif
        }

        /// <summary>
        /// Draws the UI for the Piglet Options window by calling various
        /// IMGUI methods.
        /// </summary>
        void OnGUI()
        {
            InitStyles();

            const int MARGIN = 15;

            Rect contentRect = new Rect(
                MARGIN, MARGIN,
                position.width - 2 * MARGIN,
                position.height - 2 * MARGIN);

            EditorGUI.BeginChangeCheck();

            GUILayout.BeginArea(contentRect);

            var pigletOptions = PigletOptions.Instance;

            GUILayout.Label("Global Options", _styles.Title);

            pigletOptions.EnableEditorGltfImports
                = GUILayout.Toggle(
                    pigletOptions.EnableEditorGltfImports,
                    new GUIContent("Enable glTF imports in Editor",
                       "Enable/disable automatic glTF imports in the Editor"),
                    _styles.ToggleLevel1);

                GUI.enabled = pigletOptions.EnableEditorGltfImports;

                pigletOptions.LogProgress
                   = GUILayout.Toggle(
                       pigletOptions.LogProgress,
                       new GUIContent("Log import progress in Console",
                          "Log progress messages to Unity Console window during " +
                          "Editor glTF imports (useful for debugging)"),
                       _styles.ToggleLevel2);

            GUILayout.Label("Import Options", _styles.Title);

            pigletOptions.ImportOptions.AutoScale
                = GUILayout.Toggle(
                    pigletOptions.ImportOptions.AutoScale,
                    new GUIContent("Scale model to standard size",
                       "Automatically scale the imported glTF model so that " +
                       "its longest dimension is equal to the given size"),
                    _styles.ToggleLevel1);

                GUI.enabled = pigletOptions.EnableEditorGltfImports
                    && pigletOptions.ImportOptions.AutoScale;

                GUILayout.BeginHorizontal(GUILayout.Height(20));
                   GUILayout.Space(20);

                   GUILayout.BeginVertical();
                       GUILayout.FlexibleSpace();
                       GUILayout.Label("Size", _styles.Label);
                       GUILayout.FlexibleSpace();
                   GUILayout.EndVertical();

                   GUILayout.BeginVertical();
                       GUILayout.FlexibleSpace();
                       pigletOptions.ImportOptions.AutoScaleSize =
                           EditorGUILayout.FloatField(
                               pigletOptions.ImportOptions.AutoScaleSize,
                               _styles.TextField,  GUILayout.Width(50));
                       GUILayout.FlexibleSpace();
                   GUILayout.EndVertical();

                   GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUI.enabled = pigletOptions.EnableEditorGltfImports;

            pigletOptions.ImportOptions.ImportAnimations
                = GUILayout.Toggle(
                    pigletOptions.ImportOptions.ImportAnimations,
                     new GUIContent("Import animations",
                        "Import animations from glTF file as Unity AnimationClips"),
                     _styles.ToggleLevel1);

                    GUI.enabled = pigletOptions.EnableEditorGltfImports
                        && pigletOptions.ImportOptions.ImportAnimations;

                    GUILayout.BeginHorizontal(GUILayout.Height(20));
                        GUILayout.Space(20);

                        GUILayout.BeginVertical();
                            GUILayout.FlexibleSpace();
                            GUILayout.Label("Animation clip type", _styles.Label);
                            GUILayout.FlexibleSpace();
                        GUILayout.EndVertical();

                        GUILayout.BeginVertical();
                            GUILayout.FlexibleSpace();
                            pigletOptions.ImportOptions.AnimationClipType
                                = (AnimationClipType) EditorGUILayout.Popup(
                                    (int) pigletOptions.ImportOptions.AnimationClipType,
                                    Enum.GetNames(typeof(AnimationClipType)),
                                    GUILayout.Width(100));
                            GUILayout.FlexibleSpace();
                        GUILayout.EndVertical();

                        GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUI.enabled = pigletOptions.EnableEditorGltfImports;

            GUILayout.Label("Drag-and-Drop Options", _styles.Title);

            pigletOptions.DragAndDropOptions.PromptBeforeOverwritingFiles
               = GUILayout.Toggle(
                   pigletOptions.DragAndDropOptions.PromptBeforeOverwritingFiles,
                   new GUIContent("Prompt before overwriting files",
                       "Show confirmation prompt if glTF import directory " +
                       "already exists"),
                   _styles.ToggleLevel1);

            pigletOptions.DragAndDropOptions.CopyGltfFilesIntoProject
               = GUILayout.Toggle(
                   pigletOptions.DragAndDropOptions.CopyGltfFilesIntoProject,
                   new GUIContent("Copy source glTF files into project",
                       "Copy the dragged-and-dropped glTF file/folder into the project " +
                       "before doing the glTF import. By default only the " +
                       "results of the glTF import (i.e. Unity prefab and associated assets) " +
                       "are added to the project."),
                   _styles.ToggleLevel1);

            GUILayout.Label("Post-import Options", _styles.Title);

            pigletOptions.PostImportOptions.SelectPrefabInProject
               = GUILayout.Toggle(
                   pigletOptions.PostImportOptions.SelectPrefabInProject,
                   new GUIContent("Select prefab in Project Browser",
                       "After a glTF import has completed, select/highlight " +
                       "the generated prefab in the Project Browser window"),
                   _styles.ToggleLevel1);

            pigletOptions.PostImportOptions.OpenPrefabInSceneView
               = GUILayout.Toggle(
                   pigletOptions.PostImportOptions.OpenPrefabInSceneView,
                   new GUIContent("Open prefab in Scene View",
                       "After a glTF import has completed, open the generated " +
                       "prefab in the Scene View tab. (This is equivalent to " +
                       "double-clicking the prefab in the Project Browser.)"),
                   _styles.ToggleLevel1);

            GUI.enabled = true;

            GUILayout.Space(20);

            if (GUILayout.Button(new GUIContent("Reset to Defaults",
                "Reset all options to their default values"),
                _styles.Button, GUILayout.Width(150)))
            {
                pigletOptions.Reset();

                // To be safe, we must assume that pressing the
                // "Reset to Defaults" button caused some changes
                // to the settings, although we have no way of
                // knowing for sure.
                //
                // Calling `EditorUtility.SetDirty` tells Unity that
                // PigletOptions should be serialized to disk during
                // the next call to `AssetDatabase.SaveAssets`.

                EditorUtility.SetDirty(pigletOptions);
            }

            GUILayout.EndArea();

            // If the user changed one or more options in the Piglet Options window
            // (e.g. unchecking a checkbox), mark `pigletOptions` as dirty.
            // This lets Unity know that it needs to save the object to disk
            // during the next call to `AssetDatabase.SaveAll`.

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(pigletOptions);
        }
    }
}