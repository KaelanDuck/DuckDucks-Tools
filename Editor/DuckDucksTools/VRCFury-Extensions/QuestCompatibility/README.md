# DuckDuck's Tools
This is a colletion of Quest compatibility tools that almost fully automate quest compatibility.

## A warning on the Validation Hijack
This part of my tools includes a validation hijack, which is necessary to enable the 'Build & Publish' button while the avatar is still in its unmodified state.

The validation hijack consists of a lot of reflection to clear errors out of the SDK window. It is very fragile, therefore if it does not seem to work or spams errors on the console, delete `ValidationHijack.cs` and a new build button will appear in the quest compatibility VRCFury component.

Please note that since this modifies the SDK via reflection it probably violates TOS, but since VRCFury itself did this too I don't really see a problem with it. If this bothers you just delete the file.

## Platform Specific Deleter
This feature can delete objects based on which platform is being built for. This is useful for deleting PC only objects from a quest build.

This only deletes objects at the end of the build process, so toggles should still be created the same as if the object was still present.

## Quest Compatibility Builder
This is a very large builder that does a lot of things.

### Remove Vertex Colors
This is selected by default, as Quest shaders will tint using them. This will remove all vertex colors from the avatar.

### Replace Incompatible Materials
Materials with incompatible shaders are replaced with quest-friendly shaders. By default this is `Mobile/Toon Lit`, but you can change this to any other compatible shader. Particle systems are replaced separately, but since the particle shaders are broken on quest they are just removed by default if they aren't already using a compatible shader.

### Material Overrides
If the default replacement system is not sufficient, you can use this to override materials with a custom material. This is useful for things that could use the Standard Lite shader with all its features.

### Delete Material Slots
This deletes material slots from meshes. This is useful for deleting slots that are not used or won't display correctly on quest, such as transparent expressions.

### Avatar Dynamics
Avatar Dynamics are heavily limited on Quest. This feature allows you to select which Physbone components and Colliders to keep and which to remove to keep your avatar under the limit. You will be warned during build if you are over the limit.

Since haptic plugs and socket dynamics cannot be selected prior to build, use the platform specific deleter to delete them from the quest build. It is currently TODO to keep meshes and delete the dynamics from them, but for now you will have to delete them manually.