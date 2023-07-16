using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class FixBones : Editor
{
    [MenuItem("Tools/Fix Bones")]
    public static void FixAvatarBones() {
        GameObject avatar = Selection.activeGameObject;
        if (avatar == null || !avatar.GetComponent<Animator>() || !avatar.GetComponent<Animator>().isHuman) {
            Debug.Log("No avatar selected");
            return;
        }

        Animator animator = avatar.GetComponent<Animator>();

        Undo.RecordObject(avatar, "Fix Bones");
        
        for (var i = HumanBodyBones.Hips; i < HumanBodyBones.LastBone; i++) {
            Transform bone = animator.GetBoneTransform(i);
            if (bone == null) {
                Debug.Log("Bone not found: " + i);
                continue;
            }
            // reset to prefab pose
            PrefabUtility.RevertObjectOverride(bone, InteractionMode.AutomatedAction);
        }

    }
}
