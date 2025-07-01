using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Assertions;

namespace Piglet
{
    public static class FileUtil
    {
        /// <summary>
        /// Recursively copy a file/directory from `sourcePath` to `destPath`,
        /// using the same behaviour as `cp -Rf` on Linux. Both arguments to
        /// this method must be absolute paths.
        /// </summary>
        static public void CopyRecursively(string sourcePath, string destPath)
        {
            if (!Exists(sourcePath))
            {
                throw new FileNotFoundException(string.Format(
                    "source path does not exist: {0}", sourcePath));
            }

            // Case 1: Source path is regular file.

            if (File.Exists(sourcePath))
            {
                if (Directory.Exists(destPath))
                    destPath = Path.Combine(destPath, Path.GetFileName(sourcePath));

                File.Copy(sourcePath, destPath, true);
                return;
            }

            // Case 2: Source path is directory. Recursively copy the directory
            // to `destPath`.

            var dir = new DirectoryInfo(sourcePath);
            Assert.IsTrue(dir.Exists);

            // If `destPath` is a regular file, delete it so that we can replace
            // it with a directory of the same name.

            if (File.Exists(destPath))
                File.Delete(destPath);

            // The last component of `destPath` may be used to specify a basename
            // for the copied file/directory, but the second-last component
            // of `destPath` must be an existing directory. (This matches
            // the behaviour of the Linux `cp` command.)
            //
            // Note: `parentDir.Length` will be zero when `destPath` is a
            // root path (e.g. "C:/").

            var parentDir = PathUtil.GetParentDirectory(destPath);
            if (parentDir != null && !Directory.Exists(parentDir))
                throw new DirectoryNotFoundException(string.Format(
                    "destination directory does not exist: {0}", parentDir));

            Directory.CreateDirectory(destPath);

            foreach (var file in dir.GetFiles())
            {
                var destFilePath = PathUtil.Combine(destPath, file.Name);
                file.CopyTo(destFilePath, true);
            }

            foreach (var subdir in dir.GetDirectories())
            {
                var destDirPath = PathUtil.Combine(destPath, subdir.Name);
                CopyRecursively(subdir.FullName, destDirPath);
            }
        }

        /// <summary>
        /// Return true if the given path points to an existing
        /// file or directory, or false otherwise.
        /// </summary>
        public static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>
        /// <para>
        /// Return a recursive list of file/directory paths for the
        /// given input path.
        /// </para>
        /// <para>
        /// If the input path is a directory, return
        /// the path of every file and subdirectory contained within
        /// that directory (at every depth). If the given path
        /// is a regular file, return the input path unmodified.
        /// If the given path does not correspond to an existing
        /// file or directory on disk, return the empty collection.
        /// </para>
        /// <para>
        /// The caller should not make any assumptions about the order in
        /// which paths are returned from this method.
        /// </para>
        /// </summary>
        /// <param name="path">
        /// Relative or absolute path for a file or directory.
        /// </param>
        static public IEnumerable<string> GetRecursiveFilesList(string path)
        {
            // Return an empty list if the given path does not correspond
            // to an existing file or directory.

            if (!File.Exists(path) && !Directory.Exists(path))
                yield break;

            // Return the input path (i.e. the root file/directory).

            yield return path;

            // If the input path is a regular file.

            if (File.Exists(path))
                yield break;

            foreach (var filePath in Directory.GetFiles(path))
                yield return filePath;

            // Note: Contrary to what one might expect, the call to
            // Directory.GetDirectories(path) here returns the subdirectories at
            // every depth of nesting, not just the top level of
            // subdirectories.

            foreach (var innerFolderPath in Directory.GetDirectories(path))
            {
                yield return innerFolderPath;

                foreach (var filePath in Directory.GetFiles(innerFolderPath))
                    yield return filePath;
            }
        }

        /// <summary>
        /// Delete the file or directory at the given path,
        /// if any such file/directory exists. Directories
        /// are deleted recursively.
        /// </summary>
        static public void DeleteFileOrDirectory(string path)
        {
            if (File.Exists(path))
                File.Delete(path);

            if (Directory.Exists(path))
                Directory.Delete(path, true);

            // Delete associated .meta file. Unity will automatically
            // clean these up for us, but may print warnings in the
            // Unity Console about having to delete dangling .meta files.
            // We delete the .meta files ourselves to avoid this
            // unwanted noise.

            var meta = path + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }

        /// <summary>
        /// Return true if the given path is a .gltf/.glb/.zip file that can be imported
        /// by Piglet, or false otherwise.
        /// </summary>
        public static bool IsGltfFile(string path)
        {
            if (Directory.Exists(path))
                return false;

            var extension = Path.GetExtension(path).ToLower();

            if (extension != ".gltf" && extension != ".glb" && extension != ".zip")
                return false;

            if (extension == "zip" && !ZipUtil.ContainsGltfFile(path))
                return false;

            return true;
        }

        /// <summary>
        /// <para>
        /// Return a recursive list of glTF files (i.e. .gltf/.glb/.zip
        /// files) for the given path.
        /// </para>
        /// <para>
        /// If the given path is a directory, return the
        /// path of every .gltf/.glb/.zip file contained within that
        /// directory, recursively. If the given path is a regular
        /// file, return the input path if it is a .gltf/.glb/.zip
        /// file or the empty collection otherwise. If the given
        /// path does not correspond to an existing file or directory,
        /// return the empty collection.
        /// </para>
        /// <para>
        /// The caller should not make any assumptions about the order in
        /// which paths are returned from this method.
        /// </para>
        /// </summary>
        public static IEnumerable<string> FindGltfFiles(string path)
        {
            foreach (var filePath in GetRecursiveFilesList(path: path))
            {
                if (IsGltfFile(filePath))
                    yield return filePath;
            }
        }

        /// <summary>
        /// Write a byte array to a file.
        /// </summary>
        static public IEnumerable WriteAllBytes(string path, byte[] data)
        {
            using (var inputStream = new MemoryStream(data))
            {
                using (var outputStream = new FileStream(
                    path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var copyTask = StreamUtil.CopyStreamEnum(inputStream, outputStream);
                    while (copyTask.MoveNext())
                        yield return null;
                }
            }
        }
    }
}
