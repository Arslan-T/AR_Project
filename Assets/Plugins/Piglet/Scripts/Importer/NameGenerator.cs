using System;
using System.Collections.Generic;

namespace Piglet
{
    /// <summary>
    /// <para>
    /// Generates unique and safe values for the `.name` fields of
    /// Texture2D/Material/Mesh/AnimationClip assets.
    /// </para>
    /// <para>
    /// During Editor glTF imports, the `.name` field of each asset is used as
    /// the basename of the corresponding Unity asset file on disk (e.g.
    /// "wood.png", "walk.anim"). Thus we must ensure that the values of
    /// `.name` are: (1) unique, and (2) safe to use as filenames. (The values
    /// of `.name` fields do not matter during runtime glTF imports, since the
    /// assets are never serialized to disk.)
    /// </para>
    /// <para>
    /// Generally, we would like value of `.name` to exactly match the name
    /// of the corresponding entity in the glTF file (e.g. a texture named "wood").
    /// However, we must be careful about uniqueness since there is nothing that
    /// prevents a glTF file from using the same name for multiple assets
    /// (e.g. multiple textures named "wood"), and this would cause the
    /// Unity asset files to clobber each other during a Editor glTF import.
    /// </para>
    /// <para>
    /// Another consideration is that names are optional in glTF files,
    /// and thus we must generate a suitable default name when the glTF
    /// file does not provide one (e.g. "texture_0", "texture_1", ...).
    /// This is the purpose of the `defaultNamePrefix` parameter to the
    /// constructor.
    /// </para>
    /// </summary>
    public class NameGenerator
    {
        /// <summary>
        /// The prefix used to generate default names when no suggested name
        /// is provided, i.e. the entity from the glTF file (e.g. texture, mesh)
        /// has not been explicitly assigned a name. The default name prefix
        /// is typically something like "texture", "material", "mesh", etc.,
        /// and the generated names look like "texture_0", "texture_1", etc.
        /// </summary>
        protected string _defaultNamePrefix;

        /// <summary>
        /// Tracks the set of asset names that have already been used.
        /// </summary>
        protected HashSet<string> _usedNames;

        /// <summary>
        /// A user-specified function that is applied to the names
        /// returned by `GenerateName` (e.g. mask out illegal characters).
        /// </summary>
        protected Func<string, string> _nameFilterFunc;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="defaultNamePrefix">
        /// The prefix used to generate default names when no suggested name
        /// is provided, i.e. the entity from the glTF file (e.g. texture, mesh)
        /// has not been explicitly assigned a name. The default name prefix
        /// is typically something like "texture", "material", "mesh", etc.,
        /// and the generated names look like "texture_0", "texture_1", etc.
        /// </param>
        /// <param name="nameFilterFunc">
        /// A user-specified function that is applied to the names
        /// returned by `GenerateName` (e.g. mask out illegal characters).
        /// </param>
        public NameGenerator(string defaultNamePrefix,
            Func<string, string> nameFilterFunc)
        {
            _defaultNamePrefix = defaultNamePrefix;
            _nameFilterFunc = nameFilterFunc;
            _usedNames = new HashSet<string>();
        }

        /// <summary>
        /// Generate an asset name that is: (1) unique, and
        /// and (2) similar or identical to the suggested name (if any).
        /// </summary>
        /// <param name="suggestedName">
        /// <para>
        /// The suggested name, which usually comes from the
        /// name of the corresponding entity (e.g. texture) in the glTF file.
        /// This name will be returned unmodified provided that it is: (1) not null,
        /// (2) unique, and (3) not changed by the user-provided name filter
        /// function (see `nameFilterFunc` argument of the constructor for
        /// this class).
        /// </para>
        /// <para>
        /// This parameter is allowed to be null, in which case a default name
        /// will be generated based on the `defaultNamePrefix` provided to the
        /// constructor of this class.
        /// </para>
        /// </param>
        public string GenerateName(string suggestedName)
        {
            if (suggestedName == null)
                suggestedName = string.Format("{0}_{1}", _defaultNamePrefix, _usedNames.Count);

            // Apply user-provided name filter function
            // (e.g. mask out illegal characters in the name).

            if (_nameFilterFunc != null)
                suggestedName = _nameFilterFunc(suggestedName);

            // if necessary, append a numeric suffix to make the name unique

            var result = suggestedName;
            for (var i = 2; _usedNames.Contains(result); ++i)
                result = string.Format("{0}_{1}", suggestedName, i);

            // mark the name as used

            _usedNames.Add(result);

            return result;
        }

    }
}
