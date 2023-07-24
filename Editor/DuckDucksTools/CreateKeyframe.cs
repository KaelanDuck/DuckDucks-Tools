using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

public class CreateKeyframe : EditorWindow
{
    [MenuItem("Tools/Create Animation Keyframe")]
    static void Init()
    {
        CreateKeyframe window = (CreateKeyframe)EditorWindow.GetWindow(typeof(CreateKeyframe));
        window.Show();
    }

    public GameObject root;
    public AnimationClip clip;
    public Component target;
    public string propName;
    public Object refObject;
    public float floatValue;
    public float time;

    public void OnGUI() {
        root = (GameObject)EditorGUILayout.ObjectField("Animation Root", root, typeof(GameObject), true);
        clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", clip, typeof(AnimationClip), true);
        target = (Component)EditorGUILayout.ObjectField("Target", target, typeof(Component), true);
        propName = EditorGUILayout.TextField("Property Name", propName);
        time = EditorGUILayout.FloatField("Keyframe Time", time);
        EditorGUILayout.Space();
        refObject = EditorGUILayout.ObjectField("Reference Object", refObject, typeof(Object), true);
        floatValue = EditorGUILayout.FloatField("Float Value", floatValue);

        EditorGUILayout.Space();

        if (GUILayout.Button("Create Object Ref Keyframe")) {
            EditorCurveBinding binding = new EditorCurveBinding();
            binding.path = AnimationUtility.CalculateTransformPath(target.transform, root.transform);
            binding.type = target.GetType();
            binding.propertyName = propName;
            var existingCurve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (existingCurve == null) {
                Debug.Log("Creating new curve");
                var kf = new ObjectReferenceKeyframe();
                kf.time = time;
                kf.value = refObject;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, new ObjectReferenceKeyframe[] { kf });
            } else {
                Debug.Log("Adding to existing curve");
                var kf = new ObjectReferenceKeyframe();
                kf.time = time;
                kf.value = refObject;
                var newCurve = existingCurve.ToList().Append(kf).ToArray();
                AnimationUtility.SetObjectReferenceCurve(clip, binding, newCurve);
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Create Float Keyframe")) {
            EditorCurveBinding binding = new EditorCurveBinding();
            binding.path = AnimationUtility.CalculateTransformPath(target.transform, root.transform);
            binding.type = target.GetType();
            binding.propertyName = propName;
            var existingCurve = AnimationUtility.GetEditorCurve(clip, binding);
            if (existingCurve == null) {
                Debug.Log("Creating new curve");
                var kf = new Keyframe();
                kf.time = time;
                kf.value = floatValue;
                AnimationCurve curve = new AnimationCurve(new Keyframe[] { kf });
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            } else {
                Debug.Log("Adding to existing curve");
                var kf = new Keyframe();
                kf.time = time;
                kf.value = floatValue;
                var newCurve = new AnimationCurve(existingCurve.keys.ToList().Append(kf).ToArray());
                AnimationUtility.SetEditorCurve(clip, binding, newCurve);
            }
        }


    }
}
