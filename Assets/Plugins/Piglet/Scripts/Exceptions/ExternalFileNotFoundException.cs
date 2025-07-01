using System;

namespace Piglet
{
    /// <summary>
    /// This exception is thrown in cases where a .gltf/.glb file
    /// contains a URI reference to a local file (e.g. a PNG file for
    /// a texture), but that local file could not be found.
    /// </summary>
    public class ExternalFileNotFoundException : Exception
    {
        public ExternalFileNotFoundException(string message)
            : base(message) {}
    }
}
