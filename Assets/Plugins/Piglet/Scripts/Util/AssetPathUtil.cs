using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Piglet
{
    /// <summary>
    /// Utility methods for working with Unity asset paths.
    /// Unity asset paths are expressed relative to the root of the
    /// current Unity project and always begin with "Assets/".
    /// Asset paths are used by many Unity API methods (e.g.
    /// `AssetDatabase.LoadAssetAtPath`).
    /// </summary>
    public class AssetPathUtil
    {
        /// <summary>
        /// Characters that are illegal to use in filenames.
        /// I generated this list by printing the output of
        /// Path.GetInvalidFileNameChars() on Windows 10.
        /// I used to query the list of illegal chars directly
        /// from Path.GetInvalidFileNameChars(), but
        /// I discovered (via a user's bug report) that
        /// Path.GetInvalidFileNameChars() does not work
        /// correctly on MacOS.
        /// </summary>
        public static readonly char[] INVALID_FILENAME_CHARS = {
            '\u0000', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005',
            '\u0006', '\u0007', '\u0008', '\u0009', '\u000a', '\u000b',
            '\u000c', '\u000d', '\u000e', '\u000f', '\u0010', '\u0011',
            '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017',
            '\u0018', '\u0019', '\u001a', '\u001b', '\u001c', '\u001d',
            '\u001e', '\u001f', '"', '<', '>', '|', ':', '*', '?', '\\', '/' };

        /// <summary>
        /// Translate the given name to a name that is safe
        /// to use as the basename of a Unity asset file,
        /// by masking illegal characters with '_'.
        /// </summary>
        public static string GetLegalAssetName(string name)
        {
            // replace illegal filename chars with '_'
            var result = new string(name
                .Select(c => INVALID_FILENAME_CHARS.Contains(c) ? '_' : c)
                .ToArray());

            // replace '.' because we use asset names as AnimatorController state names
            result = result.Replace(".", "_");

            return result;
        }

        /// <summary>
        /// Given an absolute path for a file inside the "Assets" folder
        /// of the current Unity project, return a path relative to
        /// the Unity project root (i.e. a path with "Assets" as the
        /// first component). Many Unity API functions expect
        /// paths of this form as input.
        /// </summary>
        public static string GetAssetPath(string absolutePath)
        {
            absolutePath = PathUtil.NormalizePathSeparators(absolutePath);

            if (!Path.IsPathRooted(absolutePath))
            {
                throw new Exception(String.Format(
                    "not an absolute path: {0}", absolutePath));
            }

            // Note: `Application.path` is the absolute path to the "Assets" folder
            // of the current Unity project.
            var dataPath = PathUtil.NormalizePathSeparators(Application.dataPath);

            if (!absolutePath.StartsWith(dataPath))
            {
                throw new Exception(String.Format(
                    "path is not inside \"Assets\" folder " +
                    "for current Unity project: {0}", absolutePath));
            }

            return PathUtil.NormalizePathSeparators(absolutePath.Replace(dataPath, "Assets"));
        }

        /// <summary>
        /// Resolve a relative path relative to a base asset path.
        /// </summary>
        public static string ResolveAssetPath(string baseAssetPath, string relativePath)
        {
            var basePath = GetAbsolutePath(baseAssetPath);
            var combinedPath = PathUtil.ResolvePath(basePath, relativePath);

            return GetAssetPath(combinedPath);
        }

        /// <summary>
        /// Return true if the given path is relative to the Unity project
        /// root, or false otherwise. All such paths have "Assets" as their
        /// first component (e.g. "Assets/Models/model.gltf").
        /// </summary>
        public static bool IsAssetPath(string path)
        {
            path = PathUtil.NormalizePathSeparators(path);
            return path == "Assets" || path.StartsWith("Assets/");
        }

        /// <summary>
        /// Return true if the given path is relative to the Unity project
        /// root and is located under "Assets/StreamingAssets"
        /// (e.g. "Assets/StreamingAssets/model.gltf").
        /// </summary>
        public static bool IsStreamingAssetPath(string path)
        {
            path = PathUtil.NormalizePathSeparators(path);
            return path == "Assets/StreamingAssets" ||
                   path.StartsWith("Assets/StreamingAssets/");
        }

        // <summary>
        // Return the parent folder for the given file/folder path.
        // The input path may be either relative or absolute.
        // </summary>
        public static string GetParentFolder(string folderPath)
        {
            folderPath = PathUtil.NormalizePathSeparators(folderPath);
            return PathUtil.NormalizePathSeparators(
                Path.GetDirectoryName(folderPath));
        }

        /// <summary>
        /// Test whether a file or folder at the given asset path exists
        /// (e.g. "Assets/MyFolder/MyAsset.asset").
        /// </summary>
        public static bool Exists(string assetPath)
        {
            return FileUtil.Exists(GetAbsolutePath(assetPath));
        }

        /// <summary>
        /// Given a path relative to the root of the current Unity project
        /// (i.e. a path starting with "Assets"), return the equivalent
        /// absolute path.
        /// </summary>
        public static string GetAbsolutePath(string projectPath)
        {
            projectPath = PathUtil.NormalizePathSeparators(projectPath);
            if (!projectPath.StartsWith("Assets"))
            {
                throw new Exception(String.Format(
                    "expected path relative to Unity project root: {0}", projectPath));
            }

            var tail = string.Join("/", PathUtil.GetPathComponents(projectPath).Skip(1));
            return PathUtil.Combine(Application.dataPath, tail);
        }

        /// <summary>
        /// <para>
        /// If the basename of a file path ends with a space followed
        /// by one or more decimal digits (e.g. "Assets/Models/file 2.gltf"),
        /// decrement the numeric suffix and return the new file path
        /// (e.g. "Assets/Models/file 2.gltf" -> "Assets/Models/file 1.gltf").
        /// In the case that the numeric suffix of input path is " 1",
        /// the suffix is simply removed (e.g. "Assets/Models/file 1.gltf"
        /// -> "Assets/Models/file.gltf"). If the input file path does
        /// not end in a numeric suffix, return null.
        /// </para>
        /// <para>
        /// This method was written to override a quirky behaviour of
        /// the Unity Editor when dragging-and-dropping files into the Project
        /// Browser. If the filename being dragged into the
        /// Project Browser already exists in the target directory, Unity
        /// will create a new file with a numeric suffix rather than
        /// overwriting the existing file.
        /// </para>
        /// </summary>
        public static string DecrementFilename(string path)
        {
            var basename = Path.GetFileNameWithoutExtension(path);
            var directory = Path.GetDirectoryName(path);
            var extension = Path.GetExtension(path);

            var numericSuffixRegex = new Regex(@"(^.+) (\d+)$");

            var match = numericSuffixRegex.Match(basename);
            if (!match.Success)
                return null;

            var prefix = match.Groups[1].Value;
            var suffix = Int32.Parse(match.Groups[2].Value);

            string decrementedFilename;
            if (suffix == 1)
                decrementedFilename = PathUtil.Combine(directory,
                    String.Format("{0}{1}", prefix, extension));
            else
                decrementedFilename = PathUtil.Combine(directory,
                    String.Format("{0} {1}{2}", prefix, suffix - 1, extension));

            return decrementedFilename;
        }

        /// <summary>
        /// Return true if the asset path is a .gltf/.glb/.zip file that can be imported
        /// by Piglet, or false otherwise.
        /// </summary>
        public static bool IsGltfAsset(string assetPath)
        {
            var absolutePath = GetAbsolutePath(assetPath);
            return FileUtil.IsGltfFile(absolutePath);
        }

        /// <summary>
        /// <para>
        /// Return a recursive list of glTF assets (i.e. .gltf/.glb/.zip
        /// files) for the given asset path.
        /// </para>
        /// <para>
        /// If the given asset path is a folder, return the asset
        /// path of every .gltf/.glb/.zip file contained within that
        /// folder, recursively. If the given asset path is a regular
        /// file, return the input asset path if it is a .gltf/.glb/.zip
        /// file or the empty collection otherwise. If the given
        /// asset path does not correspond to an existing file or
        /// folder in the project, return the empty collection.
        /// </para>
        /// <para>
        /// The caller should not make any assumptions about the order in
        /// which paths are returned from this method.
        /// </para>
        /// </summary>
        public static IEnumerable<string> FindGltfAssets(string assetPath)
        {
            var absolutePath = GetAbsolutePath(assetPath);
            foreach (var path in FileUtil.GetRecursiveFilesList(absolutePath))
            {
                if (FileUtil.IsGltfFile(path))
                    yield return GetAssetPath(path);
            }
        }

#if UNITY_EDITOR

        /// <summary>
        /// Move an asset file/folder from one asset path to another.
        /// This method behaves exactly like `AssetDatabase.MoveAsset`
        /// except that it will forcefully overwrite any asset
        /// file/folder that already exists at the target path.
        /// </summary>
        public static void MoveAsset(string assetPathOld, string assetPathNew)
        {
            if (Exists(assetPathNew))
                AssetDatabase.DeleteAsset(assetPathNew);

            AssetDatabase.MoveAsset(assetPathOld, assetPathNew);
        }

        /// <summary>
        /// <para>
        /// Return a recursive list of asset paths for the given
        /// asset path.
        /// </para>
        /// <para>
        /// If the input asset path is a folder, return
        /// the path of every file and subfolder contained within
        /// that folder, recursively. If the given asset path
        /// is a regular file, return the input asset path unmodified.
        /// </para>
        /// </summary>
        public static IEnumerable<string> GetRecursiveAssetsList(string assetPath)
        {
            yield return assetPath;

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                foreach (var guid in
                    AssetDatabase.FindAssets(null, new[] {assetPath}))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    yield return path;
                }
            }
        }

        public static void RemoveProjectDir(string path)
        {
            if (!Directory.Exists(path))
                return;

            Directory.Delete(path, true);
            AssetDatabase.Refresh();
        }
#endif

    }
}
