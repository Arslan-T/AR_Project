// This shader is written to implement a glTF material
// using the "metallic roughness" model (physically based
// rendering).
//
// See: https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#reference-pbrmetallicroughness

Shader "Piglet/MetallicRoughnessMask"
{
    Properties
    {
        // The following properties correspond to basic glTF material properties
        // described at:
        //
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#reference-material

        _normalTexture ("Normal Map", 2D) = "bump" {}

        _occlusionTexture ("Occlusion Map", 2D) = "white" {}

        _emissiveFactor ("Emissive Factor", Color) = (1, 1, 1, 1)
        _emissiveTexture ("Emission Map", 2D) = "black" {}

        _alphaCutoff ("Alpha Cutoff", Float) = 0.0

        // The following properties correspond to metallic-roughness model properties
        // described at:
        //
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#reference-pbrmetallicroughness

        _baseColorFactor ("Base Color Factor", Color) = (1, 1, 1, 1)
        _baseColorTexture ("Base Color Texture", 2D) = "white" {}

        _roughnessFactor ("Roughness Factor", Range(0,1)) = 1.0
        _metallicFactor ("Metallic Factor", Range(0,1)) = 1.0
        _metallicRoughnessTexture ("Metallic Roughness Texture", 2D) = "white" {}

        // Boolean shader properties.
        //
        // _runtime: "Is this a runtime glTF import?"
        // _linear: "Is the Unity Editor/Player in linear rendering mode?"
        //
        // In the case where both `_runtime` and `_linear`
        // are true (i.e. equal to 1.0), we need to undo the
        // Linear -> sRGB color conversions that UnityWebRequestTexture
        // incorrectly performs on linear textures (e.g. the normal texture).
        // (`UnityWebRequestTexture` assumes that all input
        // textures are sRGB-encoded and does not have a `linear` parameter like
        // `Texture2D.LoadImage`.)
        //
        // Piglet uses `UnityWebRequestTexture` to load textures during
        // runtime glTF imports because it does not stall the main Unity
        // thread during PNG/JPG decompression like `Texture2D.LoadImage`
        // does. In the case of Editor glTF imports, we do not need to
        // make any color corrections because Piglet uses `Texture2D.LoadImage`
        // to load the textures instead.

        _runtime ("Runtime", Int) = 0.0
        _linear ("Linear", Int) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" }
        Cull Off
        LOD 200

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Standard alphatest:_alphaCutoff noshadow nolightmap nofog nometa nolppv
        #include "MetallicRoughness.cginc"
        ENDCG
    }
    FallBack "Diffuse"
}
