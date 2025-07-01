#ifndef METALLIC_ROUGHNESS_INCLUDED
#define METALLIC_ROUGHNESS_INCLUDED

#pragma target 3.0

sampler2D _normalTexture;

sampler2D _occlusionTexture;

float4 _emissiveFactor;
sampler2D _emissiveTexture;

sampler2D _baseColorTexture;
float4 _baseColorFactor;

half _roughnessFactor;
half _metallicFactor;
sampler2D _metallicRoughnessTexture;

bool _runtime;
bool _linear;

struct Input
{
    // Note: The `uv` prefix "magically" maps to
    // the corresponding shader property, e.g.
    // `uv_baseColorTexture` -> `_baseColorTexture`.

    float2 uv_baseColorTexture;
    float2 uv_normalTexture;
    float2 uv_occlusionTexture;
    float2 uv_emissiveTexture;
    float2 uv_metallicRoughnessTexture;

    // Note: COLOR faults to (1,1,1,1) if unset

    float4 vertexColor : COLOR;

    // Note: VFACE is used to implement double-sided
    // rendering of triangles. The value of VFACE is
    // positive when a triangle is front-facing and negative when
    // a triangle is back-facing. See VFACE section of
    // https://docs.unity3d.com/Manual/SL-ShaderSemantics.html
    // and also a related discussion at
    // https://forum.unity.com/threads/standard-shader-modified-to-be-double-sided-is-very-shiny-on-the-underside.393068/

    fixed vface : VFACE;
};

void surf (Input IN, inout SurfaceOutputStandard o)
{
    fixed4 c = tex2D (_baseColorTexture, IN.uv_baseColorTexture) * IN.vertexColor * _baseColorFactor;
    o.Albedo = c.rgb;
    o.Alpha = c.a;

    // Compute normals.
    //
    // There are a few complications here. In addition
    // to the usual problem of UnityWebRequestTexture performing
    // an unwanted sRGB -> linear conversion during runtime
    // glTF imports, the normal texture is encoded differently
    // during Editor glTF imports vs runtime glTF imports.
    //
    // During Editor imports, Piglet creates a Unity texture
    // asset with the type set to "Normal map". This causes
    // the texture to be encoded in DXT5nm format, with the
    // x coordinate in the alpha channel and the y coordinate
    // in the green channel. (Since the normals are unit-length,
    // the z coordinate can be calculated from the x and y
    // coordinates.) The `UnpackNormal` function below moves the
    // x and y coordinates back to the red/green channels
    // and fills in the z coordinate on the blue channel.
    //
    // During runtime glTF imports, Piglet just loads the normal
    // texture like any other texture and leaves the
    // x/y/z coordinates in the red/green/blue channels.
    // Therefore the `UnpackNormal` function is not
    // used during runtime glTF imports.
    //
    // In the case of glTF models that don't specify a normal
    // textures, the shader falls back to using the
    // default "bump" texture. However, since the "bump" texture
    // is also encoded in DXT5nm, it does not produce
    // correct results during runtime imports. We handle this
    // by explicitly setting the normal texture to
    // Resources/Textures/RuntimeDefaultNormalTexture.png
    // during runtime glTF imports, in cases where
    // the model does not provide its own normal
    // texture.

    float4 normal = tex2D(_normalTexture, IN.uv_normalTexture);
    if (_runtime)
    {
        o.Normal = normal;
        // undo sRGB -> linear conversion by UnityWebRequestTexture
        if (_linear)
            o.Normal = LinearToGammaSpace(o.Normal);
        o.Normal = normalize(o.Normal * 2 - 1);
    }
    else
    {
        o.Normal = UnpackNormal(normal);
    }

    if (IN.vface < 0)
        o.Normal.z *= -1.0;

    o.Occlusion = tex2D (_occlusionTexture, IN.uv_occlusionTexture).r;
    // undo sRGB -> linear conversion by UnityWebRequestTexture
    if (_runtime && _linear)
        o.Occlusion = LinearToGammaSpaceExact(o.Occlusion);

    o.Emission = tex2D (_emissiveTexture, IN.uv_emissiveTexture) * _emissiveFactor;

    float4 metallic_roughness = tex2D(_metallicRoughnessTexture, IN.uv_metallicRoughnessTexture);

    o.Metallic = metallic_roughness.b;
    // undo sRGB -> linear conversion by UnityWebRequestTexture
    if (_runtime && _linear)
        o.Metallic = LinearToGammaSpaceExact(o.Metallic);
    o.Metallic *= _metallicFactor;

    float roughness = metallic_roughness.g;
    // undo sRGB -> linear conversion by UnityWebRequestTexture
    if (_runtime && _linear)
        roughness = LinearToGammaSpaceExact(roughness);
    o.Smoothness = 1.0 - roughness * _roughnessFactor;
}

#endif