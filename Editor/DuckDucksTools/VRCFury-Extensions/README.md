# DuckDuck's Tools
This folder contains VRCFury Extensions. All of them exist in the 'Duck's Tools' submenu in the VRCFury Add Feature dropdown.

# BIG WARNING
**VRCFury has no stable API, therefore updates CAN AND WILL break these extensions, so don't rely on them as a critical part of your avatars workflow.**

Do NOT include these builders in the same VRCFury component as all your toggles. Due to Unity BS if any builders in a list disappear or fail to compile the entire list will explode and **you will lose ALL your toggles** on that list. Create a separate VRCFury component via 'Add Component' and put them there.

For similar reasons, do NOT include these components in prefabs in your project. If the same thing happens and your shit is in a prefab **you will NOT be able to save your project EVER AGAIN.**

Extra warning: Don't blame me if the things here horribly break your project. I write these largely for myself and am prepared to deal with the consequences.

Okay warning part over.

## Mesh Deleter
Deletes parts of a mesh depending on a texture mask. The mask is sampled based on the first UV of the mesh. You can select which submesh this applies to and chose if black or white parts of the mask remove the corresponding parts of the mesh.

## Mesh Merger
Automatically merges skinned meshes in your avatar. Has the option to exclude the face mesh (the mesh named 'Body') to avoid dumping lots of blendshapes onto a big mesh. Will automatically merge towards the 'Body' mesh to preserve MMD compatibility. This will avoid merging meshes that have either: active toggles, conflicting blendshapes (or animations for them), or conflicting material property animations.

Note: this is unfinished and may make mistakes.

## Quest Compatibility
This has its own readme in the folder, go read that.