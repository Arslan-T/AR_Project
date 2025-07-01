using System;
using System.Collections.Generic;
using System.IO;

namespace Piglet
{
    public static class PathUtil
    {
        /// <summary>
        /// <para>
        /// Return the parent directory of the given path, or
        /// null if the given path is a root path (e.g. "C:/", "/").
        /// </para>
        /// <para>
        /// This method behaves the same as `Path.GetDirectoryName`,
        /// with two minor differences:
        /// <list type="number">
        /// <item><description>
        /// This method will return null instead of the empty string
        /// in the case where the given path is a root path (e.g.
        /// "C:/", "/").
        /// </description></item>
        /// <item><description>
        /// This return value does not depend on whether
        /// the input path has a trailing slash. (Internally,
        /// any trailing slash is removed prior to calling
        /// `Path.GetDirectoryName`.)
        /// </description></item>
        /// </list>
        /// </para>
        /// </summary>
        public static string GetParentDirectory(string path)
        {
            path = NormalizePathSeparators(path);
            return Path.GetDirectoryName(path);
        }

        /// <summary>
        /// Get the absolute file path for the given `path`.
        /// If `path` is a relative path, then it will be resolved relative
        /// to `basePath`. If `path` is an absolute path it
        /// will be returned unmodified.
        /// </summary>
        /// <param name="basePath">
        /// The `basePath` for resolving relative paths. This must
        /// be an absolute path to a file or directory. If it
        /// is a path for a file, the parent directory of the file
        /// will be used when resolving `path`.
        /// </param>
        /// <param name="path">
        /// A relative or absolute path for a file/directory.
        /// </param>
        static public string ResolvePath(string basePath, string path)
        {
            if (path == null)
                return null;

            // Note: We must avoid passing base64-encoded data URIs
            // to the `Uri` constructor below because it results in
            // "UriFormatException: Invalid URI: The Uri string is too long."

            if (UriUtil.IsDataUri(path))
                return null;

            var uri = new Uri(path, UriKind.RelativeOrAbsolute);

            if (!uri.IsAbsoluteUri)
            {
                // If `basePath` is a directory, we must ensure that it
                // has a trailing slash (`\` or `/`) in order to produce
                // the correct path.
                //
                // From `Uri(Uri, string)` constructor documentation [1]:
                //
                // "If the baseUri has relative parts (like /api), then the
                // relative part must be terminated with a slash, (like /api/),
                // if the relative part of baseUri is to be preserved in the
                // constructed Uri.
                //
                // [1]: https://docs.microsoft.com/en-us/dotnet/api/system.uri.-ctor?view=net-5.0#System_Uri__ctor_System_Uri_System_Uri_

                if (Directory.Exists(basePath) && !basePath.EndsWith("/"))
                    basePath += "/";

                var baseUri = new Uri(basePath);
                uri = new Uri(baseUri, path);
            }

            if (!uri.IsFile)
                return null;

            return uri.LocalPath;
        }

        /// <summary>
        /// Collapse the given list of absolute or relative paths to a non-redundant
        /// list of the topmost files/folders. In other words, filter out all
        /// paths that are contained within other paths. This can be thought
        /// of as the inverse operation of a recursive directory listing.
        /// </summary>
        public static IEnumerable<string> CollapsePaths(
            IEnumerable<string> paths)
        {
            var normalizedPaths = new List<string>();
            foreach (var path in paths)
                normalizedPaths.Add(NormalizePathSeparators(path));

            var pathSet = new HashSet<string>(normalizedPaths);

            var collapsedPaths = new List<string>();
            foreach (var path in normalizedPaths)
            {
                var subsumed = false;

                for (var parentPath = Path.GetDirectoryName(path);
                    parentPath.Length > 0;
                    parentPath = Path.GetDirectoryName(parentPath))
                {
                    if (pathSet.Contains(parentPath))
                    {
                        subsumed = true;
                        break;
                    }
                }

                if (!subsumed)
                    collapsedPaths.Add(path);
            }

            return collapsedPaths;
        }

        /// <summary>
        /// Split the given relative/absolute path into a
        /// array of path components, where each component is separated
        /// from the next by path separator (backslash or
        /// forward slash).
        /// </summary>
        public static string[] GetPathComponents(string path)
        {
            path = NormalizePathSeparators(path);
            return path.Split('/');
        }

        public static string NormalizePathSeparators(string path)
        {
            string result = path.Replace("\\\\", "/").Replace("\\", "/");

            // remove trailing slash if present, because this can affect
            // the results of some .NET methods (e.g. `Path.GetDirectoryName`)
            if (result.EndsWith("/"))
                result = result.Substring(0, result.Length - 1);

            return result;
        }

        /// <summary>
        /// Join `path1` and `path2` with a forward slash to form a single
        /// relative/absolute path. This method works exactly like `Path.Combine`
        /// but additional normalizes the path separators by replacing
        /// backslashes with forward slashes and removing any trailing slash.
        /// </summary>
        public static string Combine(string path1, string path2)
        {
            return NormalizePathSeparators(Path.Combine(path1, path2));
        }

        /// <summary>
        /// Return a path with any trailing backslash ('\') or a forward
        /// slash (`/') character removed.
        /// </summary>
        public static string TrimTrailingDirectorySeparatorChar(string path)
        {
            if (!path.EndsWith("\\") && !path.EndsWith("/"))
                return path;

            return path.Substring(0, path.Length - 1);
        }

        /// <summary>
        /// Return the last component of the input path, which could be either a
        /// filename or a directory.
        /// </summary>
        public static string GetLastPathComponent(string path)
        {
            var components = GetPathComponents(path);

            if (components.Length == 0)
                return null;

            return components[components.Length - 1];
        }
    }
}
