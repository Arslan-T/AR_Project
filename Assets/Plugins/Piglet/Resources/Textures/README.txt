RuntimeDefaultNormalTexture.png is used as the default normal texture
during runtime glTF imports, in the case where the model does not
specify its own normal texture.

This texture is needed because normal textures are encoded differently
during Editor glTF imports vs runtime glTF imports, and so we
need different default textures in each case.

During Editor imports, Piglet creates a Unity texture asset with the
type set to "Normal map". This causes the texture to be encoded in
DXT5nm format, with the x coordinate in the alpha channel and the y
coordinate in the green channel. (Since the normals are unit-length,
the z coordinate can be calculated from the x and y coordinates.) In
the shader code
(e.g. Resources/Shaders/Standard/MetallicRoughness.cginc), the
`UnpackNormal` function moves the x and y coordinates back to the
red/green channels and fills in the z coordinate on the blue channel.

During runtime glTF imports, Piglet just loads the normal texture like
any other texture and leaves the x/y/z coordinates in the
red/green/blue channels. Therefore there is a separate if/else case in
the shader code for runtime glTF imports, where the `UnpackNormal`
function is not used.

In the case of glTF models that don't specify a normal texture, the
shader falls back to using the default "bump" texture. However, since
the "bump" texture is also encoded in DXT5nm, it does not produce
correct results during runtime imports. We handle this by explicitly
setting the normal texture to RuntimeDefaultNormalTexture.png instead,
in GltfImporter.LoadMaterial.
