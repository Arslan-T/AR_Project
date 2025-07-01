using System;

namespace Piglet
{
    /// <summary>
    /// Flags containing metadata needed to correctly load a texture.
    /// Mainly this is used to indicate the color space (linear or sRGB)
    /// of the underlying image data.
    /// </summary>
    [Flags]
    public enum TextureLoadingFlags
    {
        None = 0,
        /// <summary>
        /// The underlying image data is linear (i.e. not gamma-encoded).
        /// </summary>
        Linear = 1,
        /// <summary>
        /// The texture will be used as a normal map. This is important
        /// information during Editor glTF imports, because Unity uses
        /// a specialized format (DXTnm) for normal maps.
        /// </summary>
        NormalMap = 2
    }
}
