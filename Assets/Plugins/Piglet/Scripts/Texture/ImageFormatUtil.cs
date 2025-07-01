using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Piglet
{
	/// <summary>
	/// Utility methods for detecting the format of binary image data
	/// (e.g. PNG, JPEG, KTX2).
	/// </summary>
    public static class ImageFormatUtil
    {
		/// <summary>
		/// The initial bytes ("magic numbers") of a file/stream that are used
		/// to identify different image formats (e.g. PNG, JPG, KTX2).
	    /// I got the magic numbers for the PNG, JPEG, and JPEG2 formats from
	    /// https://stackoverflow.com/a/9446045/12989671.
		/// </summary>
		private struct Magic
		{
			public static readonly byte[] PNG = { 137, 80, 78, 71 };
			/// <summary>
			/// Common magic number prefix for JPEG/JFIF/EXIF formats.
			/// </summary>
			public static readonly byte[] JPEG = { 255, 216, 255 };
			/// <summary>
			/// KTX2 is a container format for supercompressed and GPU-ready textures.
			/// For further info, see: https://github.khronos.org/KTX-Specification/.
			/// I got the values for the KTX2 magic bytes by examining an example
			/// KTX2 files with the Linux `od` tool, e.g.
			/// `od -A n -N 12 -t u1 myimage.ktx2`. The magic byte values are also
			/// given in Section 3.1 of https://github.khronos.org/KTX-Specification/.
			/// </summary>
			public static readonly byte[] KTX2 = { 171, 75, 84, 88, 32, 50, 48, 187, 13, 10, 26, 10 };

		}

		/// <summary>
		/// Return the image format of a local file.
		/// </summary>
		public static ImageFormat GetImageFormat(string path)
		{
			using (var stream = File.OpenRead(path))
			{
				return GetImageFormat(stream);
			}
		}

		/// <summary>
		/// Return the image format of a stream.
		/// </summary>
		public static ImageFormat GetImageFormat(Stream stream)
		{
			return GetImageFormat(StreamUtil.Read(stream, Magic.KTX2.Length)
				.ToArray());
		}

		/// <summary>
		/// Detect the image format of a raw byte array.
		/// See: https://stackoverflow.com/a/9446045/12989671.
		/// </summary>
		public static ImageFormat GetImageFormat(byte[] data)
		{
			if (Magic.PNG.SequenceEqual(data.Take(Magic.PNG.Length)))
				return ImageFormat.PNG;

			if (Magic.JPEG.SequenceEqual(data.Take(Magic.JPEG.Length)))
				return ImageFormat.JPEG;

			if (Magic.KTX2.SequenceEqual(data.Take(Magic.KTX2.Length)))
				return ImageFormat.KTX2;

			return ImageFormat.Unknown;
		}
    }
}
