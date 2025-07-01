#if UNITY_EDITOR
using Material = UnityEngine.Material;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Piglet.GLTF;
using Piglet.GLTF.Schema;
using UnityEngine;
using Piglet.UnityGLTF.Extensions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Profiling;
using Mesh = UnityEngine.Mesh;

namespace Piglet
{
	/// <summary>
	/// A glTF importer for use inside the Unity Editor.
	/// EditorGltfImporter differs from RuntimeGltfImporter
	/// in the following ways: (1) EditorGltfImporter serializes
	/// the imported assets (e.g. textures, materials, meshes)
	/// to disk as Unity assets during import, whereas
	/// RuntimeGltfImporter only creates assets in memory.
	/// (2) EditorGltfImporter creates a prefab as its
	/// final output, whereas RuntimeGltfImporter creates
	/// an ordinary hierarchy of GameObjects (and returns the
	/// root).
	/// </summary>
	public class EditorGltfImporter : GltfImporter
	{
		// Import paths and options
		/// <summary>
		/// Parent directory of directory where importer will
		/// create Unity prefab and associated files
		/// (e.g. meshes, materials). Must be located inside Unity
		/// project folder.
		/// </summary>
		private string _importPath;

		/// <summary>
		/// Constructor
		/// </summary>
		public EditorGltfImporter(string gltfPath, string importPath,
			GltfImportOptions importOptions,
			ProgressCallback progressCallback = null)
			: base(new Uri(gltfPath), null, importOptions, progressCallback)
		{
			_importPath = importPath;
		}

		/// <summary>
		/// Coroutine-style implementation of GLTF import.
		/// </summary>
		/// <param name="gltfPath">
		/// Absolute path to .gltf/.glb file.
		/// </param>
		/// <param name="importPath">
		/// Absolute path of folder where prefab and
		/// associated assets will be created. Must be located under
		/// the "Assets" folder for the current Unity project.
		/// </param>
		/// <param name="importOptions">
		/// Options controlling glTF importer behaviour (e.g. should
		/// the imported model be automatically scaled to a certain size?).
		/// </param>
		public static GltfImportTask GetImportTask(string gltfPath,
			string importPath, GltfImportOptions importOptions = null)
		{
			GltfImportTask importTask = new GltfImportTask();

			if (importOptions == null)
				importOptions = new GltfImportOptions();

			EditorGltfImporter importer = new EditorGltfImporter(
				gltfPath, importPath, importOptions,
				(step, completed, total) =>
					 importTask.OnProgress?.Invoke(step, completed, total));

			importTask.AddTask(() => Directory.CreateDirectory(importPath));
			importTask.AddTask(importer.ReadUri());
			importTask.AddTask(importer.ParseFile());
			importTask.AddTask(importer.CheckRequiredGltfExtensions());
			importTask.AddTask(importer.LoadBuffers());
			importTask.AddTask(importer.LoadTextures());
			importTask.AddTask(importer.LoadMaterials());
			importTask.AddTask(importer.LoadMeshes());
			importTask.AddTask(importer.LoadScene());
			importTask.AddTask(importer.LoadMorphTargets());
			importTask.AddTask(importer.LoadSkins());
            importTask.AddTask(importer.ScaleModel());
			importTask.AddTask(importer.LoadAnimations());
			importTask.AddTask(importer.AddAnimationComponentsToSceneObject);

			// note: the final subtask must return the
			// root GameObject for the imported model.
			importTask.AddTask(importer.CreatePrefabEnum());

			// callbacks to clean up any imported game objects / files
			// when the user aborts the import or an exception
			// occurs
			importTask.OnAborted += importer.Clear;
			importTask.OnException += _ => importer.Clear();

			return importTask;
		}

		/// <summary>
		/// Load the PNG/JPG/KTX2 image data for a glTF texture
		/// into a byte array.
		/// </summary>
		/// <param name="textureIndex">
		/// The index of the target texture in the glTF file.
		/// </param>
		/// <returns>
		/// A byte array containing the PNG/JPG/KTX2 data for
		/// the texture.
		/// </returns>
		protected IEnumerable<(YieldType, byte[])> LoadTextureData(int textureIndex)
		{
			var imageId = GetImageIndex(textureIndex);
			if (imageId < 0)
			{
				yield return (YieldType.Continue, null);
				yield break;
			}

			var image = _root.Images[imageId];

			byte[] data = null;
			foreach (var (yieldType, result) in LoadImageData(image))
			{
				data = result;
				yield return (yieldType, null);
			}

			yield return (YieldType.Continue, data);
		}

		/// <summary>
		/// Convert the given KTX2 image to an equivalent PNG image.
		/// This conversion is necessary because (as of Unity 2020.1), Unity
		/// does not natively support .ktx2 files for texture assets.
		/// </summary>
		/// <param name="data">
		/// Byte array containing image data in KTX2/ETC1S or KTX2/UASTC format.
		/// </param>
		/// <returns>
		/// Byte array containing PNG version of input image.
		/// </returns>
		public IEnumerable<(YieldType, byte[])> ConvertKtx2ToPng(byte[] data)
		{
			Texture2D texture = null;

#if KTX_UNITY_0_9_1_OR_NEWER
			foreach (var (yieldType, result) in TextureUtil.LoadKtx2Data(data, true))
			{
				texture = result;
				yield return (yieldType, null);
			}
#elif KTX_UNITY
			Debug.LogWarning("Failed to load texture in KTX2 format, "+
				 "because KtxUnity package is older than 0.9.1.");
#else
			Debug.LogWarning("Failed to load texture in KTX2 format "+
				"because KtxUnity package is not installed. Please install KtxUnity "+
				"by following the directions at https://github.com/atteneder/KtxUnity "+
				"(requires Unity 2019.3+).");
#endif

	        // Abort if Ktx2Unity isn't installed or Ktx2Unity failed
	        // to load the texture for any reason.

	        if (texture == null)
	        {
		        yield return (YieldType.Continue, null);
		        yield break;
	        }

	        // We must convert the texture to a "readable" texture
            // before we can call EncodeToPNG() on it.
            //
            // In Unity, a "readable" texture is a texture whose
            // uncompressed color data is cached in RAM, in
            // addition to existing on the GPU. For further info, see:
            // https://docs.unity3d.com/ScriptReference/TextureImporter-isReadable.html
            //
            // We need to perform this extra step because KtxUnity
            // currently has no option to import the texture as
            // readable when it is first loaded.

            texture = TextureUtil.GetReadableTexture(texture);

            yield return (YieldType.Continue, null);

            // Re-encode the data in PNG format and write to disk.

	        yield return (YieldType.Continue, texture.EncodeToPNG());
		}

		/// <summary>
		/// Write the given PNG/JPG/KTX2 data to an image file on disk.
		/// </summary>
		/// <param name="data">
		/// Byte array containing the PNG/JPG/KTX2 data.
		/// </param>
		/// <param name="textureName">
		/// The basename for the output image file (e.g. "wood", "texture_1").
		/// For example, a textureName of "wood" could result in creating
		/// an image file called "Assets/MyModel/Textures/wood.png".
		/// </param>
		/// <returns>
		/// The path to the output image file under Assets
		/// (e.g. "Assets/MyModel/Textures/wood.png").
		/// </returns>
		protected IEnumerable<(YieldType, string)> SerializeTexture(byte[] data, string textureName)
		{
			// Create the `Textures` subfolder under main import folder
			// (if it doesn't already exist).

			var importDir = PathUtil.Combine(
				AssetPathUtil.GetAssetPath(_importPath), "Textures");

			Directory.CreateDirectory(AssetPathUtil.GetAbsolutePath(importDir));

			// Identify format of image data. Currently supported formats
			// are PNG/JPG/KTX2, and KTX2 is only supported when
			// KtxUnity 0.9.1 or newer is installed.
			//
			// Note: As of Unity 2020.1, Unity does not have any native
			// support for KTX2 format. Thus for Editor glTF imports, we
			// convert KTX2 images to PNG before serializing to disk.

			var imageFormat = ImageFormatUtil.GetImageFormat(data);
			if (imageFormat == ImageFormat.KTX2)
			{
				byte[] pngData = null;

				foreach (var (yieldType, result) in ConvertKtx2ToPng(data))
				{
					pngData = result;
					yield return (yieldType, null);
				}

				// if KtxUnity is not installed
				if (pngData == null)
				{
					yield return (YieldType.Continue, null);
					yield break;
				}

				data = pngData;
				imageFormat = ImageFormatUtil.GetImageFormat(data);
			}

			// write raw PNG/JPG/KTX2 bytes to .png/.jpg file.

			string basename;

			switch (imageFormat)
			{
				case ImageFormat.PNG:
					basename = String.Format("{0}.png", textureName);
					break;

				case ImageFormat.JPEG:
					basename = String.Format("{0}.jpg", textureName);
					break;

				default:
					throw new Exception("Unrecognized image format. " +
							"This method only supports PNG/JPEG/KTX2 format.");
			}

			var assetPath = PathUtil.Combine(importDir, basename);

			File.WriteAllBytes(AssetPathUtil.GetAbsolutePath(assetPath), data);

			yield return (YieldType.Continue, assetPath);
		}

		/// <summary>
		/// Create Unity Texture2D assets from glTF texture descriptions.
		/// </summary>
		override protected IEnumerator<YieldType> LoadTextures()
		{
			if (_root.Textures == null || _root.Textures.Count == 0)
				yield break;

			_progressCallback?.Invoke(GltfImportStep.Texture, 0, _root.Textures.Count);

			// Paths to serialized .png/.jpg files for each texture.

			var assetPaths = new List<string>();

			// Determine color space and other metadata about textures, based
			// on the material slots where they are used.

			var textureLoadingFlags = ComputeTextureFlagsFromMaterials();

			// Generates asset names that are: (1) unique, (2) safe to use as filenames,
			// and (3) similar to the original entity name from the glTF file (if any).

			var assetNameGenerator = new NameGenerator(
				"texture", AssetPathUtil.GetLegalAssetName);

			// Step 1: Write raw PNG/JPG data to image files on disk.

			for (var i = 0; i < _root.Textures.Count; ++i)
			{
				var textureName = assetNameGenerator.GenerateName(_root.Textures[i].Name);

				// read raw PNG/JPG/KTX2 data into byte array

				byte[] data = null;
				foreach (var (yieldType, result) in LoadTextureData(i))
				{
					data = result;
					yield return yieldType;
				}

				if (data == null)
				{
					assetPaths.Add(null);
					_imported.TextureIsUpsideDown.Add(true);
					continue;
				}

				// We use the TextureIsUpsideDown flags to remember if the
				// image was originally loaded into the texture upside-down,
				// so that we can correct the orientation downstream, by changing
				// the TextureScale parameter on the target Material.
				//
				// This adjustment is necessary because Unity has a quirk
				// where it always loads PNG/JPG images into textures upside-down.
				// In contrast, KtxUnity loads KTX2 images into textures
				// right-side-up.

				var imageFormat = ImageFormatUtil.GetImageFormat(data);
				switch (imageFormat)
				{
					case ImageFormat.PNG:
					case ImageFormat.JPEG:

						_imported.TextureIsUpsideDown.Add(true);
						break;

					case ImageFormat.KTX2:

						_imported.TextureIsUpsideDown.Add(false);
						break;

					default:

						throw new Exception("Unrecognized image format. " +
								"Pigelt only supports PNG/JPEG/KTX2 formats.");
				}

				// write PNG/JPG/KTX2 data to an image file on disk

				string assetPath = null;
				foreach (var (yieldType, result) in SerializeTexture(data, textureName))
				{
					assetPath = result;
					yield return yieldType;
				}
				assetPaths.Add(assetPath);

				_progressCallback(GltfImportStep.Texture, i + 1, _root.Textures.Count);
			}

			// Step 2: Import image files from disk with default texture settings, using
			// AssetDatabase.ImportAsset. Texture import settings (e.g. sRGB flag) are
			// corrected as necessary in Step 2 below, by re-importing them with
			// TextureImporter.SaveAndReimport.
			//
			// Note: Using `AssetDatabase.StartAssetEditing` / `AssetDatabase.StopAssetEditing`
			// dramatically improves the performance of Editor glTF imports, by allowing
			// us to create multiple assets without triggering an expensive
			// `AssetDatabase.Refresh` call. However, we must ensure that
			// `AssetDatabase.StopAssetEditing` is always called by putting it in a
			// `catch` block. Otherwise, the Editor could be left in an unusable state [1].
			//
			// For a good technical overview of Unity's `AssetDatabase` (including
			// `AssetDatabase.Refresh`) see [2] and [3].
			//
			// [1]: https://docs.unity3d.com/ScriptReference/AssetDatabase.StartAssetEditing.html
			// [2]: https://www.youtube.com/watch?v=S2P9n5U9xVw
			// [3]: https://blog.unity.com/technology/tips-for-working-more-effectively-with-the-asset-database

			AssetDatabase.StartAssetEditing();

			for (var i = 0; i < _root.Textures.Count; ++i)
			{
				try
				{
					if (assetPaths[i] != null)
						AssetDatabase.ImportAsset(assetPaths[i]);
				}
				catch (Exception)
				{
					AssetDatabase.StopAssetEditing();
					throw;
				}
			}

			AssetDatabase.StopAssetEditing();

			yield return YieldType.Continue;

			// Step 3: Re-import textures using TextureImport.SaveAndReimport,
			// so that we can set the correct values for the texture import settings
			// (e.g. sRGB flag).
			//
			// It is unfortunate that Unity does not provide
			// a straightforward way to import texture assets with the correct
			// settings in a single step. A potential workaround to avoid importing
			// every texture twice to use AssetPostprocessor.PreprocessTexture,
			// as discussed at [1]. However, since that approach is awkward and
			// error-prone, I've decided to leave it for later work.
			//
			// My profiling indicates that the loop below (i.e. "Step 2") lengthens
			// the overall texture import time by about 30% (e.g. 9 seconds ->
			// 12 seconds).

			AssetDatabase.StartAssetEditing();

			for (var i = 0; i < _root.Textures.Count; ++i)
			{
				try
				{
					if (assetPaths[i] == null)
						continue;

					// Optimization: If the texture uses default import settings
					// (i.e. sRGB = true and textureType = Default), there is no
					// need to re-import with TextureImporter below.

					if (!textureLoadingFlags[i].HasFlag(TextureLoadingFlags.Linear)
					    && !textureLoadingFlags[i].HasFlag(TextureLoadingFlags.NormalMap))
						continue;

					// Configure texture import settings as per `textureLoadingFlags`.

					var importer = AssetImporter.GetAtPath(assetPaths[i]) as TextureImporter;
					if (importer == null)
						continue;

					if (textureLoadingFlags[i].HasFlag(TextureLoadingFlags.NormalMap))
						importer.textureType = TextureImporterType.NormalMap;

					importer.sRGBTexture = !textureLoadingFlags[i].HasFlag(
						TextureLoadingFlags.Linear);

					importer.SaveAndReimport();
				}
				catch (Exception)
				{
					AssetDatabase.StopAssetEditing();
					throw;
				}
			}

			AssetDatabase.StopAssetEditing();

			// Step 4: Reload the textures from the .png/.jpg files on disk.
			// Once reloaded, the Texture objects are now "backed" by the
			// underlying .png/.jpg files, meaning that changes to the textures
			// in memory will be automatically synced to disk.

			for (var i = 0; i < _root.Textures.Count; ++i)
			{
				// reload texture asset

				Texture2D texture = null;

				if (assetPaths[i] != null)
				{
					texture = (Texture2D) AssetDatabase.LoadAssetAtPath(
						assetPaths[i], typeof(Texture2D));
				}

				_imported.Textures.Add(texture);

				yield return YieldType.Continue;
			}

		}

		/// <summary>
		/// Create Unity materials from glTF material definitions and
		/// serialize them to asset files under the "Materials" subfolder.
		/// </summary>
		override protected IEnumerable LoadMaterials()
		{
			// First create the materials in memory, as in a runtime glTF import.

			foreach (var _ in base.LoadMaterials())
				yield return null;

			if (_imported.Materials == null || _imported.Materials.Count == 0)
				yield break;

			// Create the `Materials` subfolder under the main import folder.

			var importDir = PathUtil.Combine(
				AssetPathUtil.GetAssetPath(_importPath), "Materials");

			Directory.CreateDirectory(AssetPathUtil.GetAbsolutePath(importDir));

			// Stores the paths to the `.mat` files for each material.

			var assetPaths = new List<string>();

			// Serialize materials to disk as `.mat` files.
			//
			// Note: Using `AssetDatabase.StartAssetEditing` / `AssetDatabase.StopAssetEditing`
			// dramatically improves the performance of Editor glTF imports, by allowing
			// us to create multiple assets without triggering an expensive
			// `AssetDatabase.Refresh` call. However, we must guarantee that
			// `AssetDatabase.StopAssetEditing` is always called by putting it in a
			// `catch` block. Otherwise, the Editor could be left in an unusable state [1].
			//
			// For a good technical overview of Unity's `AssetDatabase` (including
			// `AssetDatabase.Refresh`) see [2] and [3].
			//
			// [1]: https://docs.unity3d.com/ScriptReference/AssetDatabase.StartAssetEditing.html
			// [2]: https://www.youtube.com/watch?v=S2P9n5U9xVw
			// [3]: https://blog.unity.com/technology/tips-for-working-more-effectively-with-the-asset-database

			AssetDatabase.StartAssetEditing();

			for(var i = 0; i < _imported.Materials.Count; ++i)
			{
				try
				{
					var basename = String.Format("{0}.mat", _imported.Materials[i].name);
					var assetPath = PathUtil.Combine(importDir, basename);

					assetPaths.Add(assetPath);

					AssetDatabase.CreateAsset(_imported.Materials[i], assetPath);
				}
				catch (Exception)
				{
					AssetDatabase.StopAssetEditing();
					throw;
				}
			}

			AssetDatabase.StopAssetEditing();

			yield return null;

			// Reload the materials from the `.mat` files on disk. Once reloaded,
			// the Material objects are now "backed" by the underlying .mat files,
			// meaning that changes to the materials in memory will be
			// automatically synced to the files on disk.

			for(var i = 0; i < _imported.Materials.Count; ++i)
			{
				_imported.Materials[i] = (Material) AssetDatabase.LoadAssetAtPath(
					assetPaths[i], typeof(Material));

				yield return null;
			}
		}

		/// <summary>
		/// Create Unity meshes from glTF mesh definitions.
		/// </summary>
		override protected IEnumerable LoadMeshes()
		{
			if (_root.Meshes == null || _root.Meshes.Count == 0)
				yield break;

			// Create meshes in memory, like in a runtime glTF import.

			foreach (var _ in base.LoadMeshes())
				yield return null;

			// Create `Meshes` subfolder under main import folder.

			var importDir = PathUtil.Combine(
				AssetPathUtil.GetAssetPath(_importPath), "Meshes");

			Directory.CreateDirectory(AssetPathUtil.GetAbsolutePath(importDir));

			// Stores path to output `.asset` file for each mesh.

			var assetPaths = new List<string>();

			// Serialize meshes to disk as `.asset` files.
			//
			// Note: Using `AssetDatabase.StartAssetEditing` / `AssetDatabase.StopAssetEditing`
			// dramatically improves the performance of Editor glTF imports, by allowing
			// us to create multiple assets without triggering an expensive
			// `AssetDatabase.Refresh` call. However, we must guarantee that
			// `AssetDatabase.StopAssetEditing` is always called by putting it in a
			// `catch` block. Otherwise, the Editor could be left in an unusable state [1].
			//
			// For a good technical overview of Unity's `AssetDatabase` (including
			// `AssetDatabase.Refresh`) see [2] and [3].
			//
			// [1]: https://docs.unity3d.com/ScriptReference/AssetDatabase.StartAssetEditing.html
			// [2]: https://www.youtube.com/watch?v=S2P9n5U9xVw
			// [3]: https://blog.unity.com/technology/tips-for-working-more-effectively-with-the-asset-database

			AssetDatabase.StartAssetEditing();

			// for each glTF mesh
			for (var i = 0; i < _imported.Meshes.Count; ++i)
			{
				// for each glTF mesh primitive
				for (var j = 0; j < _imported.Meshes[i].Count; ++j)
				{
					try
					{
						var mesh = _imported.Meshes[i][j].Key;

						var basename = String.Format("{0}.asset", mesh.name);
						var assetPath = PathUtil.Combine(importDir, basename);

						assetPaths.Add(assetPath);

						AssetDatabase.CreateAsset(mesh, assetPath);
					}
					catch (Exception)
					{
						AssetDatabase.StopAssetEditing();
						throw;
					}
				}
			}

			AssetDatabase.StopAssetEditing();

			yield return null;

			// Reload the meshes from the `.asset` files on disk. Once reloaded,
			// the Mesh objects are now "backed" by the underlying .asset files,
			// meaning that changes to the meshes in memory will be
			// automatically synced to the files on disk.

			var assetPathIndex = 0;

			// for each glTF mesh
			for (var i = 0; i < _imported.Meshes.Count; ++i)
			{
				// for each glTF mesh primitive
				for (var j = 0; j < _imported.Meshes[i].Count; ++j)
				{
					var mesh = (Mesh) AssetDatabase.LoadAssetAtPath(
						assetPaths[assetPathIndex++], typeof(Mesh));
					var material = _imported.Meshes[i][j].Value;

					_imported.Meshes[i][j] = new KeyValuePair<Mesh, Material>(mesh, material);

					yield return null;
				}
			}
		}

		/// <summary>
		/// Create an empty AnimationClip. This method
		/// overrides the base class implementation to create
		/// either a Legacy or Mecanim animation clip based on
		/// the value of _importOptions.AnimationClipType.
		/// </summary>
		override protected AnimationClip CreateAnimationClip()
		{
			AnimationClip clip = null;

			switch (_importOptions.AnimationClipType)
			{
				case AnimationClipType.Legacy:

					clip = base.CreateAnimationClip();
					break;

				case AnimationClipType.Mecanim:

					clip = new AnimationClip { legacy = false };

					// Make the animation loop indefinitely.
					// Note: The clip.wrapMode field only applies to Legacy clips.

					var settings = AnimationUtility.GetAnimationClipSettings(clip);
					settings.loopTime = true;
					AnimationUtility.SetAnimationClipSettings(clip, settings);

					break;
			}

			return clip;
		}

		/// <summary>
		/// Add Animation-related components to the root scene object,
		/// for playing back animation clips at runtime.
		/// </summary>
		override protected void AddAnimationComponentsToSceneObject()
		{
			if (_root.Animations == null || _root.Animations.Count == 0)
				return;

			// If we are importing Legacy-type animation clips,
			// use the base class implementation.

			if (_importOptions.AnimationClipType == AnimationClipType.Legacy)
			{
				base.AddAnimationComponentsToSceneObject();
				return;
			}

			// Add Animation components for playing Mecanim animation
			// clips at runtime.

			AddAnimatorComponentToSceneObject();
			AddAnimationListToSceneObject();
		}

		/// <summary>
		/// Set up an `Animator` component on the root scene object,
		/// for playing back Mecanim animation clips at runtime.
		/// </summary>
		protected void AddAnimatorComponentToSceneObject()
		{
			// Attach an `Animator` component for playing Mecanim animation clips.

			var anim = _imported.Scene.AddComponent<Animator>();

			// Create an `AnimatorController`, which is a
			// state machine used by the `Animator` component to control
			// transitions/blending between animation clips.
			//
			// In our case, we create the simplest possible state machine,
			// with a separate state for each animation clip and no
			// transitions between them.

			var importDir = PathUtil.Combine(
				AssetPathUtil.GetAssetPath(_importPath), "Animations");

			Directory.CreateDirectory(AssetPathUtil.GetAbsolutePath(importDir));

			var path = PathUtil.Combine(importDir, "controller.controller");
			var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
			var stateMachine = controller.layers[0].stateMachine;

			foreach (var clip in _imported.Animations)
			{
				// if we failed to import this clip
				if (clip == null)
					continue;

				var state = stateMachine.AddState(clip.name);
				state.motion = clip;

				// make the first valid animation clip the default state
				if (stateMachine.entryTransitions.Length == 0)
					stateMachine.AddEntryTransition(state);
			}

			// assign the AnimatorController to the Animator

			anim.runtimeAnimatorController = controller;
		}

		/// <summary>
		/// Load glTF animations into Unity AnimationClips.
		/// </summary>
		override protected IEnumerable LoadAnimations()
		{
			if (_root.Animations == null || _root.Animations.Count == 0)
				yield break;

			// Create animation clips in memory, like in a runtime glTF import.

			foreach (var _ in base.LoadAnimations())
				yield return null;

			// Create `Animations` subfolder under main import folder.

			var importDir = PathUtil.Combine(
				AssetPathUtil.GetAssetPath(_importPath), "Animations");

			Directory.CreateDirectory(AssetPathUtil.GetAbsolutePath(importDir));

			// Stores path to output `.anim` asset for each animation clip.

			var assetPaths = new List<string>();

			// Serialize animation clips to disk as `.anim` files.
			//
			// Note: Using `AssetDatabase.StartAssetEditing` / `AssetDatabase.StopAssetEditing`
			// dramatically improves the performance of Editor glTF imports, by allowing
			// us to create multiple assets without triggering an expensive
			// `AssetDatabase.Refresh` call. However, we must guarantee that
			// `AssetDatabase.StopAssetEditing` is always called by putting it in a
			// `catch` block. Otherwise, the Editor could be left in an unusable state [1].
			//
			// For a good technical overview of Unity's `AssetDatabase` (including
			// `AssetDatabase.Refresh`) see [2] and [3].
			//
			// [1]: https://docs.unity3d.com/ScriptReference/AssetDatabase.StartAssetEditing.html
			// [2]: https://www.youtube.com/watch?v=S2P9n5U9xVw
			// [3]: https://blog.unity.com/technology/tips-for-working-more-effectively-with-the-asset-database

			AssetDatabase.StartAssetEditing();

			for(var i = 0; i < _imported.Animations.Count; ++i)
			{
				try
				{
					var basename = String.Format("{0}.anim", _imported.Animations[i].name);
					var assetPath = PathUtil.Combine(importDir, basename);

					assetPaths.Add(assetPath);

					AssetDatabase.CreateAsset(_imported.Animations[i], assetPath);
				}
				catch (Exception)
				{
					AssetDatabase.StopAssetEditing();
					throw;
				}
			}

			AssetDatabase.StopAssetEditing();

			yield return null;

			// Reload the animation clip from the `.anim` files on disk. Once reloaded,
			// the AnimationClip objects are now "backed" by the underlying .anim files,
			// meaning that changes to the animation clips in memory will be
			// automatically synced to the files on disk.

			for(var i = 0; i < _imported.Animations.Count; ++i)
			{
				_imported.Animations[i] = (AnimationClip) AssetDatabase.LoadAssetAtPath(
					assetPaths[i], typeof(AnimationClip));

				yield return null;
			}
		}

		/// <summary>
		/// Create a prefab from the imported hierarchy of game objects.
		/// This is the final output of an Editor glTF import.
		/// </summary>
		protected IEnumerator<GameObject> CreatePrefabEnum()
		{
			string basename = "scene.prefab";
			if (!String.IsNullOrEmpty(_imported.Scene.name))
			{
				basename = String.Format("{0}.prefab",
					AssetPathUtil.GetLegalAssetName(_imported.Scene.name));
			}

			string dir = AssetPathUtil.GetAssetPath(_importPath);
			string path = PathUtil.Combine(dir, basename);

			// Temporarily make the root GameObject of the model visible,
			// so that the prefab created from the model is also visible.
			// (The model hierarchy is kept hidden during the glTF import
			// so that the user never sees the partially reconstructed
			// model.)
			//
			// Note: Wrapping the `SaveAsPrefabAsset` call below with
			// `SetActive(true)` / `SetActive(false)` is a hacky workaround.
			// In theory I should be able to create the prefab
			// from the (invisible) model and then call `SetActive(true)`
			// on the prefab itself. But that approach intermittently fails
			// to set the active flag on the prefab, for unknown reasons.
			// (I observed this issue in Unity 2019.4.29f1 on MacOS.)

			_imported.Scene.SetActive(true);

			GameObject prefab =
				PrefabUtility.SaveAsPrefabAsset(_imported.Scene, path);

			_imported.Scene.SetActive(false);

			// Note: base.Clear() removes imported game objects from
			// the scene and from memory, but does not remove imported
			// asset files from disk.

			base.Clear();

			yield return prefab;
		}

		/// <summary>
		/// Remove any imported game objects from scene and from memory,
		/// and remove any asset files that were generated.
		/// </summary>
		protected override void Clear()
		{
			// remove imported game objects from scene and from memory
			base.Clear();

			// remove Unity asset files that were created during import
			AssetPathUtil.RemoveProjectDir(_importPath);
		}
	}
}
#endif
