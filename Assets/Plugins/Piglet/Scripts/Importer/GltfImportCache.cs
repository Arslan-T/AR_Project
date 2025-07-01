using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Piglet
{
    /// <summary>
    /// Caches Unity assets that have been created during a glTF import.
    ///
    /// Caching Unity assets facilitates their reuse in later
    /// import phases. For example, imported Unity textures
    /// need to be accessed/reused when importing materials.
    /// </summary>
    public class GltfImportCache
    {
        /// <summary>
        /// Binary data buffers loaded from GLTF file.
        /// </summary>
        public List<byte[]> Buffers;

        /// <summary>
        /// Textures loaded from GLTF file. In GLTF, textures
        /// are images with additional parameters applied
        /// (e.g. scaling, filtering).
        /// </summary>
        public List<Texture2D> Textures;

        /// <summary>
        /// Boolean indicating if the corresponding texture was
        /// loaded upside-down. UnityWebRequestTexture loads
        /// PNG/JPG images upside-down, whereas KtxUnity loads
        /// KTX2/BasisU images right-side-up. We need to keep
        /// track of this so that we can correct the texture
        /// orientation later in the glTF import process.
        /// </summary>
        public List<bool> TextureIsUpsideDown;

        /// <summary>
        /// <para>
        /// Default normal texture for runtime glTF imports.
        /// This texture is used whenever a glTF material does not
        /// specify its own normal texture.
        /// </para>
        /// <para>
        /// We need to use a different default normal texture during
        /// runtime glTF imports because normal textures are
        /// encoded differently during runtime and Editor glTF imports.
        /// See Assets/Piglet/Resources/Textures/README.txt for
        /// further explanation.
        /// </para>
        /// </summary>
        public Texture2D RuntimeDefaultNormalTexture;

        /// <summary>
        /// Materials imported from GLTF file.
        /// </summary>
        public List<Material> Materials;

        /// <summary>
        /// The index of the default material in the `Materials`
        /// array. This default material is only created when
        /// the glTF importer encounters a mesh without an
        /// explicitly assigned material.
        /// </summary>
        public int DefaultMaterialIndex;

        /// <summary>
        /// Index of the special Z-write material in the Materials
        /// array. The Z-write material is used only with
        /// semi-transparent materials, and only when URP (Universal
        /// Render Pipeline) is the active render pipeline. It
        /// performs a preliminary Z-write-only shader pass to address
        /// the problem of Order Independent Transparency (OIT).  For
        /// background on the OIT problem, see:
        /// https://forum.unity.com/threads/render-mode-transparent-doesnt-work-see-video.357853/#post-2315934
        /// </summary>
        public int ZWriteMaterialIndex;

        /// <summary>
        /// Meshes imported from GLTF file. In GLTF, meshes
        /// consist of one or more submeshes called "primitives",
        /// where each primitive can have a different material.
        /// Here the outer list are the top-level meshes and the inner
        /// lists are the primitives that make up each mesh.
        /// </summary>
        public List<List<KeyValuePair<Mesh,Material>>> Meshes;

        /// <summary>
        /// The nodes of the GLTF scene hierarchy, which have
        /// a one-to-one correspondence to Unity GameObjects.
        /// The integer keys of the dictionary correspond to
        /// indices in the GLTF nodes array.
        ///
        /// I use a dictionary to hold the nodes because they
        /// are created while traversing the scene hierarchy,
        /// and thus are not necessarily loaded in array order.
        /// </summary>
        public Dictionary<int, GameObject> Nodes;

        /// <summary>
        /// Animations imported from GLTF file.
        /// </summary>
        public List<AnimationClip> Animations;

        /// <summary>
        /// Stores the names of imported animation clips.
        /// This array is used to work around a Unity bug/quirk
        /// where the value of the AnimationClip.name field
        /// is lost when the clip is serialized to disk with
        /// `AssetDatabase.CreateAsset`. (More specifically,
        /// the original value of AnimationClip.name gets
        /// replaced with the basename of the .asset file
        /// for the clip.)
        /// </summary>
        public List<string> AnimationNames;

        /// <summary>
        /// Index of the special animation clip that resets
        /// the model to its default pose. This clip is needed
        /// because Unity has no built-in way to reset the
        /// model transforms to their original state after
        /// playing an animation.
        /// </summary>
        public int StaticPoseAnimationIndex;

        /// <summary>
        /// The GameObject corresponding to the root of the
        /// imported glTF scene.
        /// </summary>
        public GameObject Scene;

        /// <summary>
        /// Maps mesh index -> node indices. The same mesh
        /// may be attached to multiple nodes, causing it
        /// to be instantiated multiple times in a scene
        /// (e.g. blades of grass).
        /// </summary>
        public Dictionary<int, List<int>> MeshToNodes;

        /// <summary>
        /// Maps node index -> game objects for mesh primitives.
        ///
        /// A glTF mesh is composed of one or more "mesh primitives",
        /// where each primitive has its own geometry data and
        /// material. Each Unity mesh that we create during a glTF
        /// import corresponds to a single glTF mesh primitive
        /// (not to an entire glTF mesh!).
        ///
        /// Moreover, when we are importing the glTF scene hierarchy
        /// into Unity, we must create a separate game object
        /// for each mesh primitive, since Unity allows only one
        /// mesh/material combo per game object. The
        /// game objects for mesh primitives belonging to the
        /// same glTF mesh are created as siblings in the Unity
        /// scene hierarchy. Only the game object
        /// for the first mesh primitive (i.e. primitive 0)
        /// is added to the `Nodes` dictionary, whereas the
        /// full list of sibling game objects (including primitive 0)
        /// is recorded in `NodeToMeshPrimitives`.
        /// </summary>
        public Dictionary<int, List<GameObject>> NodeToMeshPrimitives;

        /// <summary>
        /// Indices of meshes that have one or more mesh primitives
        /// containing morph targets.
        /// </summary>
        public List<int> MeshesWithMorphTargets;

        /// <summary>
        /// Maps skin index -> node indices
        /// </summary>
        public Dictionary<int, List<int>> SkinToNodes;

        public GltfImportCache()
        {
            DefaultMaterialIndex = -1;
            ZWriteMaterialIndex = -1;
            StaticPoseAnimationIndex = -1;

            Buffers = new List<byte[]>();
            Textures = new List<Texture2D>();
            TextureIsUpsideDown = new List<bool>();
            Materials = new List<Material>();
            Meshes = new List<List<KeyValuePair<Mesh, Material>>>();
            Nodes = new Dictionary<int, GameObject>();
            Animations = new List<AnimationClip>();
            AnimationNames = new List<string>();
            Scene = null;

            MeshToNodes = new Dictionary<int, List<int>>();
            NodeToMeshPrimitives = new Dictionary<int, List<GameObject>>();
            MeshesWithMorphTargets = new List<int>();
            SkinToNodes = new Dictionary<int, List<int>>();
        }

        /// <summary>
        /// Remove a game object from the Unity scene and from memory.
        /// </summary>
        protected static void Destroy(GameObject gameObject)
        {
            // Note: We must use Object.DestroyImmediate instead of
            // Object.Destroy during Editor glTF imports, because
            // Object.Destroy relies only works in Play Mode.

            if (Application.isPlaying)
                Object.Destroy(gameObject);
            else
                Object.DestroyImmediate(gameObject);
        }

        /// <summary>
        /// Destroy all game objects created by the glTF import.
        /// </summary>
        public void Clear()
        {
            foreach (var gameObject in Nodes.Values)
                Destroy(gameObject);

            foreach (var gameObjects in NodeToMeshPrimitives.Values)
            {
                for (int i = 0; i < gameObjects.Count; ++i)
                {
                    // Skip destroying the game object for mesh primitive 0,
                    // since that game object also belongs to Nodes
                    // and has already been destroyed in the loop above.
                    // See comment for NodeToMeshPrimitives for
                    // further info.
                    if (i == 0)
                        continue;

                    Destroy(gameObjects[i]);
                }
            }

            if (Scene != null)
                Destroy(Scene);

            // tell Unity to unload any game objects that are not referenced
            // in the scene (e.g. the game objects we destroyed above)
            Resources.UnloadUnusedAssets();
        }
    }
}
