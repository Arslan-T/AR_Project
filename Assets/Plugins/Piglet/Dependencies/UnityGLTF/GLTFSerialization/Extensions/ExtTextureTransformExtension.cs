using System;
using Piglet.GLTF.Schema;
using Piglet.Newtonsoft.Json;
using Piglet.Newtonsoft.Json.Linq;
using Piglet.GLTF.Math;

namespace Piglet.GLTF.Schema
{
	public class ExtTextureTransformExtension : Extension
	{
		/// <summary>
		/// The offset of the UV coordinate origin as a percentage of the texture dimensions.
		/// </summary>
		public Vector2 Offset = new Vector2(0, 0);
		public static readonly Vector2 OFFSET_DEFAULT = new Vector2(0, 0);

		/// <summary>
		/// The scale factor applied to the components of the UV coordinates.
		/// </summary>
		public Vector2 Scale = new Vector2(1, 1);
		public static readonly Vector2 SCALE_DEFAULT = new Vector2(1, 1);

		/// <summary>
		/// Overrides the textureInfo texCoord value if this extension is supported.
		/// </summary>
		public int TexCoord = 0;
		public static readonly int TEXCOORD_DEFAULT = 0;

		public ExtTextureTransformExtension(Vector2 offset, Vector2 scale, int texCoord)
		{
			Offset = offset;
			Scale = scale;
			TexCoord = texCoord;
		}

		public JProperty Serialize()
		{
			JObject ext = new JObject();

			if (Offset != OFFSET_DEFAULT)
			{
				ext.Add(new JProperty(
					ExtTextureTransformExtensionFactory.OFFSET,
					new JArray(Offset.X, Offset.Y)
				));
			}
				
			if (Scale != SCALE_DEFAULT)
			{
				ext.Add(new JProperty(
					ExtTextureTransformExtensionFactory.SCALE,
					new JArray(Scale.X, Scale.Y)
				));
			}

			if (TexCoord != TEXCOORD_DEFAULT)
			{
				ext.Add(new JProperty(
					ExtTextureTransformExtensionFactory.TEXCOORD,
					TexCoord
				));
			}

			return new JProperty(ExtTextureTransformExtensionFactory.EXTENSION_NAME, ext);
		}
	}
}
