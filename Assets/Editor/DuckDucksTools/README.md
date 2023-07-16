# DuckDuck's Tools

CS files in this folder can be dumped into the project and used as-is. Make sure to put them into the editor folder or some other editor-only assembly.

## CreateKeyframe.cs
This can create arbitrary keyframes in animation clips. Select the root of the animation (usually your avatar), the clip, the target component (SkinnedMeshRenderer for material property and blendshape stuff), the property name, the keyframe time, and then either the float value or the object reference.

You can then create the animation keyframe for either float curves or object reference curves with the appropriate button.

This lives in Tools/Create Animation Keyframe

## FixBones.cs
Reverts humanoid poses to their prefab state. Useful if your avi gets stuck in a claw pose in the editor.

This lives in Tools/Fix Bones

## Mesh Deleter.cs
Deletes polys from a mesh based on an image mask. Generally only supports single-material meshes. Removes any parts of the mesh where the mask for that UV is black.

A finished version of this is in the VRCFury-Extensions folder.

Lives in Tools/MeshDeleter

## MinLightingAnimationGenerator.cs
Generates material property animations for everything on your avatar into a specific animation clip. This creates the property for every renderer that is the child of an avatar in case your animation is used in multiple avatars in your project.

It creates two keyframes in case you want to do a radial puppet or something like I do. It creates a lot of animation curves, so be careful.

It lives in Tools/MinLightingAnimationGenerator