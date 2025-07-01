using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Networking;

#if KTX_UNITY_0_9_1_OR_NEWER
using KtxUnity;
#endif

namespace Piglet
{
	/// <summary>
	/// Utility methods for reading/loading textures.
	/// </summary>
	public static class TextureUtil
	{
#if KTX_UNITY_0_9_1_OR_NEWER
		/// <summary>
		/// Load a Texture2D from the given KTX2 image (byte array), using the
		/// KtxUnity package: https://github.com/atteneder/KtxUnity.
		/// </summary>
		public static IEnumerable<(YieldType, Texture2D)> LoadKtx2Data(byte[] data, bool linear)
		{
			var ktxTexture = new KtxTexture();

#if KTX_UNITY_1_0_0_OR_NEWER
			// In KtxUnity 1.0.0, KtxUnity switched from a coroutine-based API
			// to an async/await-based API.
			//
			// For a helpful overview of the differences between coroutine
			// (IEnumerator) methods and async/await methods, including
			// examples of how to translate between the two types of
			// methods, see the following blog post:
			//
			// http://www.stevevermeulen.com/index.php/2017/09/using-async-await-in-unity3d-2017/

			using (var na = new NativeArray<byte>(data, KtxNativeInstance.defaultAllocator))
			{
				var task = ktxTexture.LoadFromBytes(na, linear);

				while (!task.IsCompleted)
					yield return (YieldType.Blocked, null);

				if (task.IsFaulted)
					throw task.Exception;

				yield return (YieldType.Continue, task.Result.texture);
			}
#else
			// In version 0.9.1 and older, KtxUnity used a coroutine
			// (IEnumerator) based API, rather than an async/await-based
			// API.

			Texture2D result = null;

			ktxTexture.onTextureLoaded += (texture, _) => { result = texture; };

			using (var na = new NativeArray<byte>(data, KtxNativeInstance.defaultAllocator))
			{
				// We use a stack here because KtxUnity's `LoadBytesRoutine` returns
				// nested IEnumerators, and we need to iterate/execute
				// through them in depth-first order.
				//
				// `LoadBytesRoutine` works as-is when run with Unity's
				// `MonoBehaviour.StartCoroutine` because the Unity game loop
				// implements nested execution of IEnumerators as a special behaviour.
				// Piglet does not have the option of using `StartCoroutine`
				// because it needs to run `LoadBytesRoutine` outside of Play Mode
				// during Editor glTF imports.

				var task = new Stack<IEnumerator>();
				task.Push(ktxTexture.LoadBytesRoutine(na, linear));
				while (task.Count > 0)
				{
					if (!task.Peek().MoveNext())
						task.Pop();
					else if (task.Peek().Current is IEnumerator)
						task.Push((IEnumerator)task.Peek().Current);
				}
			}

			yield return (YieldType.Continue, result);
#endif
		}
#endif

		/// <summary>
		/// <para>
		/// Load a Unity Texture2D from in-memory image data in
		/// KTX2 format.
		/// </para>
		/// <para>
		/// This method requires KtxUnity >= 0.9.1 to decode the
		/// KTX2 data. If KtxUnity is not installed (or the version
		/// is too old), log a warning and return null.
		/// </para>
		/// </summary>
		static public IEnumerable<(YieldType, Texture2D)> LoadTextureKtx2(
			byte[] data, TextureLoadingFlags textureLoadingFlags)
		{
#if KTX_UNITY_0_9_1_OR_NEWER
			// Note:
			//
			// For runtime glTF imports (i.e. Application.isPlaying == true),
			// we set `linear` to false here to match the behaviour
			// of PNG/JPG texture loading with `UnityWebRequestTexture`, which
			// does not have a `linear` parameter and always assumes that
			// all PNG/JPG data is gamma-encoded (i.e. sRGB data) [1].
			//
			// We therefore need to correct the color values for all linear textures
			// (e.g. normal maps) downstream in the shaders, by reversing
			// the erroneous sRGB -> linear translation that was originally
			// applied by UnityWebRequestTexture/KtxUnity. This shader correction
			// only needs to be made during runtime glTF imports, since
			// Editor glTF imports use `Texture2D.LoadImage` instead of
			// `UnityWebRequestTexture` to load PNG/JPG data.
			// (Runtime imports need to use `UnityWebRequestTexture` because
			// `Texture2D.LoadImage` stalls the main Unity thread during PNG/JPG
			// decompression.)
			//
			// [1]: https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequestTexture.GetTexture.html

			var linear = Application.isPlaying
				? false : textureLoadingFlags.HasFlag(TextureLoadingFlags.Linear);

			foreach (var result in TextureUtil.LoadKtx2Data(data, linear))
				yield return result;
#elif KTX_UNITY
			Debug.LogWarning("Failed to load texture in KTX2 format, "+
				 "because KtxUnity package is older than 0.9.1.");
			yield return (YieldType.Continue, null);
#else
			Debug.LogWarningFormat("Failed to load texture in KTX2 format "+
				"because KtxUnity package is not installed. Please see "+
				"\"Installing KtxUnity\" in the Piglet manual.");
			yield return (YieldType.Continue, null);
#endif
			yield break;
		}

		/// <summary>
		/// Load Texture2D from a URI for a KTX2 file.
		/// </summary>
		static public IEnumerable<(YieldType, Texture2D)> LoadTextureKtx2(
			Uri uri, TextureLoadingFlags textureLoadingFlags)
		{
			byte[] data = null;
			foreach (var (yieldType, result) in UriUtil.ReadAllBytesEnum(uri))
			{
				data = result;
				yield return (yieldType, null);
			}

			foreach (var (yieldType, texture) in LoadTextureKtx2(data, textureLoadingFlags))
				yield return (yieldType, texture);
		}

		/// <summary>
		/// Return a "readable" version of a Texture2D. In Unity,
		/// a "readable" Texture2D is a texture whose uncompressed
		/// color data is available in RAM, in addition to existing on
		/// the GPU. A Texture2D must be readable before
		/// certain methods can be called (e.g. `GetPixels`, `SetPixels`,
		/// `encodeToPNG`). The code for this method was copied from
		/// the following web page, with minor modifications:
		/// https://support.unity.com/hc/en-us/articles/206486626-How-can-I-get-pixels-from-unreadable-textures-
		/// </summary>
		public static Texture2D GetReadableTexture(Texture2D texture)
		{
			if (texture.isReadable)
				return texture;

			// Create a temporary RenderTexture of the same size as the texture.
			//
			// Note: `RenderTextureReadWrite.Linear` means that RGB
			// color values will copied from source textures/materials without
			// modification, i.e. without color space conversions. For further
			// details, see:
			// https://docs.unity3d.com/ScriptReference/RenderTextureReadWrite.html
			var tmp = RenderTexture.GetTemporary(
				texture.width,
				texture.height,
				0,
				RenderTextureFormat.Default,
				RenderTextureReadWrite.Linear);

			// Blit the pixels on texture to the RenderTexture
			Graphics.Blit(texture, tmp);

			// Backup the currently set RenderTexture
			var previous = RenderTexture.active;

			// Set the current RenderTexture to the temporary one we created
			RenderTexture.active = tmp;

			// Create a new readable Texture2D to copy the pixels to it
			//
			// Note 1: EncodeToPNG only works for textures that use
			// TextureFormat.ARGB32 or TextureFormat.RGB24 [1].
			//
			// Note 2: We pass true for the last parameter ("linear")
			// to ensure that no color space transformations take place.
			// (We want the image data in the returned Texture2D to be
			// an exact copy from the input Texture2D.)

			var readableTexture = new Texture2D(
				texture.width, texture.height, TextureFormat.ARGB32,
				texture.mipmapCount > 1, true);

			readableTexture.name = texture.name;

			// Copy the pixels from the RenderTexture to the new Texture
			readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
			readableTexture.Apply();

			// Reset the active RenderTexture
			RenderTexture.active = previous;

			// Release the temporary RenderTexture
			RenderTexture.ReleaseTemporary(tmp);

			// "readableTexture" now has the same pixels from "texture"
			// and it's readable.
			return readableTexture;
		}

		/// <summary>
		/// Create a Unity Texture2D from in-memory image data (PNG/JPG/KTX2).
		/// </summary>
		/// <returns>
		/// A two-item tuple consisting of: (1) a Texture2D,
		/// and (2) a bool that is true if the texture
		/// was loaded upside-down. The bool is needed because
		/// `UnityWebRequestTexture` loads PNG/JPG images into textures
		/// upside-down, whereas KtxUnity loads KTX2/BasisU images
		/// right-side-up.
		/// </returns>
		static public IEnumerable<(YieldType, Texture2D, bool)> LoadTexture(
			byte[] data, TextureLoadingFlags textureLoadingFlags)
		{
			// Case 1: Load KTX2/BasisU texture using KtxUnity.
			//
			// Unlike PNG/JPG images, KTX2/BasisU images are optimized
			// for use with GPUs.

			if (ImageFormatUtil.GetImageFormat(data) == ImageFormat.KTX2)
			{
				foreach (var (yieldType, texture) in LoadTextureKtx2(data, textureLoadingFlags))
					yield return (yieldType, texture, false);
				yield break;
			}

			// Case 2: Load PNG/JPG during a runtime glTF import.
			//
			// `UnityWebRequestTexture` is a better option than `Texture2D.LoadImage`
			// during runtime glTF imports because it performs the PNG/JPG decompression
			// on a background thread. Unfortunately, `UnityWebRequestTexture`
			// still uploads the uncompressed PNG/JPG data to the GPU
			// in a synchronous manner, and so stalling of the main Unity
			// thread is not completely eliminated. For further details/discussion,
			// see: https://forum.unity.com/threads/asynchronous-texture-upload-and-downloadhandlertexture.562303/
			//
			// One obstacle to using `UnityWebRequestTexture`
			// here is that it requires a URI (i.e. an URL or file path)
			// to read the data from. On Windows and Android, `UriUtil.CreateUri`
			// writes the PNG/JPG data to a temporary file under
			// `Application.temporaryCachePath` and returns the file path.
			// On WebGL, `UriUtil.CreateUri` creates a temporary localhost
			// URL on the Javascript side using `URL.createObjectURL`.

			string uri = null;
			foreach (var result in UriUtil.CreateUri(data))
			{
				uri = result;
				yield return (YieldType.Continue, null, false);
			}

			foreach (var (yieldType, texture) in LoadTexturePngOrJpg(uri))
				yield return (yieldType, texture, true);
		}

		/// <summary>
		/// Coroutine to load a Texture2D from a URI.
		/// </summary>
		/// <returns>
		/// A two-item tuple consisting of: (1) a Texture2D,
		/// and (2) a bool that is true if the texture
		/// was loaded upside-down. The bool is needed because
		/// `UnityWebRequestTexture` loads PNG/JPG images into textures
		/// upside-down, whereas KtxUnity loads KTX2/BasisU images
		/// right-side-up.
		/// </returns>
		static public IEnumerable<(YieldType, Texture2D, bool)> LoadTexture(
			string uri, TextureLoadingFlags textureLoadingFlags)
		{
			foreach (var result in
				LoadTexture(new Uri(uri), textureLoadingFlags))
			{
				yield return result;
			}
		}

		/// <summary>
		/// <para>
		/// Coroutine to load a Texture2D from a URI.
		/// </para>
		///
		/// <para>
		/// Note!: This method reads/downloads the entire file *twice* when
		/// the input URI is in PNG/JPG format. If you know the image format of
		/// the URI, you will get better performance by calling either
		/// LoadTextureKtx2(uri) or LoadTexturePngOrJpg(uri) directly.
		/// </para>
		/// </summary>
		/// <returns>
		/// A two-item tuple consisting of: (1) a Texture2D,
		/// and (2) a bool that is true if the texture
		/// was loaded upside-down. The bool is needed because
		/// `UnityWebRequestTexture` loads PNG/JPG images into textures
		/// upside-down, whereas KtxUnity loads KTX2/BasisU images
		/// right-side-up.
		/// </returns>
		static public IEnumerable<(YieldType, Texture2D, bool)> LoadTexture(
			Uri uri, TextureLoadingFlags textureLoadingFlags)
		{
			// Optimization:
			//
			// If the URI points to a local file, read file header
			// to determine the image format and then use the
			// appropriate specialized LoadTexture* method.

			if (uri.IsFile)
			{
				if (ImageFormatUtil.GetImageFormat(uri.LocalPath) == ImageFormat.KTX2)
				{
					foreach (var (yieldType, texture) in LoadTextureKtx2(uri, textureLoadingFlags))
						yield return (yieldType, texture, false);
				}
				else
				{
					foreach (var (yieldType, texture) in LoadTexturePngOrJpg(uri))
						yield return (yieldType, texture, true);
				}

				yield break;
			}

			// Read entire byte content of URI into memory.

			byte[] data = null;
			foreach (var (yieldType, result) in UriUtil.ReadAllBytesEnum(uri))
			{
				data = result;
				yield return (yieldType, null, false);
			}

			// Case 1: Texture has KTX2 format, so load it with KtxUnity (if installed).

			if (ImageFormatUtil.GetImageFormat(data) == ImageFormat.KTX2)
			{
				foreach (var (yieldType, _texture) in LoadTextureKtx2(data, textureLoadingFlags))
					yield return (yieldType, _texture, false);
				yield break;
			}

			// Case 2: Texture is not KTX2. Assume file is a PNG/JPG and
			// re-download it with a UnityWebRequestTexture.

			foreach (var (yieldType, _texture) in LoadTexturePngOrJpg(uri))
				yield return (yieldType, _texture, true);
		}

		/// <summary>
		/// Coroutine to load a Texture2D from a URI in PNG/JPG format.
		/// </summary>
		static public IEnumerable<(YieldType, Texture2D)> LoadTexturePngOrJpg(string uri)
		{
			foreach (var result in LoadTexturePngOrJpg(new Uri(uri)))
				yield return result;
		}

		/// <summary>
		/// Coroutine to load a Texture2D from a URI in PNG/JPG format.
		/// </summary>
		static public IEnumerable<(YieldType, Texture2D)> LoadTexturePngOrJpg(Uri uri)
		{
			var request = UnityWebRequestTexture.GetTexture(uri, true);
			request.SendWebRequest();

			while (!request.isDone)
				yield return (YieldType.Blocked, null);

			if( request.HasError())
				throw new Exception( string.Format(
					"failed to load image URI {0}: {1}",
					uri, request.error ) );

			var texture = DownloadHandlerTexture.GetContent(request);

			yield return (YieldType.Continue, texture);
		}
	}
}
