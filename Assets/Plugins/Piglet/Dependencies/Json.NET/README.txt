Piglet.Newtonsoft.Json.dll is built from commit fe1c295, from
the `piglet` branch of
https://github.com/AwesomesauceLabs/Newtonsoft.Json-for-Unity.

The above repo is a fork of
https://github.com/jilleJr/Newtonsoft.Json-for-Unity with the
following changes:

(1) Reduce the AOT DLL size from 657k -> 436k by undefining many
compile-time constants for features that Piglet doesn't need. The
compile-time defines for the AOT DLL build are specified as a
semicolon-separated list in Src/Newtonsoft.Json/Newtonsoft.Json.csproj
(`i<DefineConstants>` tag).

(2) Move all C# classes from namespace `Newtonsoft.Json` ->
`Piglet.Newtonsoft.Json`, to prevent conflicts with Unity's own
"Newtonsoft Json" package, which is installed by default in new Unity
projects since Unity 2020.3.10f1.

(3) Rename the output DLL file from `Newtonsoft.Json.dll` ->
`Piglet.Newtonsoft.Json.dll` (again to avoid conflicts with Unity's
"Newtonsoft Json" package). I changed the output DLL filename by
changing the value of the `<AssemblyName>` tag in
Src/Newtonsoft.Json/Newtonsoft.Json.csproj.
