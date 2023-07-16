using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using System.Linq;

// Generates an animation with keyframes animating _LightingMinLightBrightness between 0 and 0.1
// for all mesh renderers in the scene. that are children of an avatar
public class MinLightingAnimationGenerator : EditorWindow
{
    [MenuItem("Tools/MinLightingAnimationGenerator")]
    static void Open() {
        var win = GetWindow<MinLightingAnimationGenerator>();
        win.animatedPaths.Clear();
    }

    public AnimationClip clip;
    public string animatedProperty = "_LightingMinLightBrightness";

    public List<string> animatedPaths = new List<string>();

    public float leftKeyframeValue = 0;
    public float rightKeyframeValue = 0.1f;

    private Vector2 scrollPosition;

    public void OnGUI() {

        GUILayout.Label("Generate global min-lighting (or other material property) animations for all avatars in the scene", new GUIStyle(EditorStyles.boldLabel) { wordWrap = true });

        clip = (AnimationClip) EditorGUILayout.ObjectField("Animation Clip:", clip, typeof(AnimationClip), false);

        animatedProperty = EditorGUILayout.TextField("Animated Property:", animatedProperty);

        leftKeyframeValue = EditorGUILayout.FloatField("OFF Value:", leftKeyframeValue);
        rightKeyframeValue = EditorGUILayout.FloatField("ON Value:", rightKeyframeValue);

        if (clip == null || animatedProperty == "") GUI.enabled = false;

        if (GUILayout.Button("Generate")) {
            // clear the list of animated paths
            animatedPaths.Clear();

            // first, find all avatars in the scene
            //var avatars = FindObjectsOfTypeAll<VRCAvatarDescriptor>();
            var avatars = Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>();

            // then find all renderers in the scene that are children of an avatar
            var renderers = avatars.SelectMany(a => a.GetComponentsInChildren<Renderer>(true));

            // for each renderer, add a keyframe to the animation
            foreach (var renderer in renderers) {
                var parent = renderer.GetComponentsInParent<VRCAvatarDescriptor>(true)[0];
                var path = AnimationUtility.CalculateTransformPath(renderer.transform, parent.transform);
                if (animatedPaths.Contains(path)) continue;
                animatedPaths.Add(path);

                var binding = EditorCurveBinding.FloatCurve(path, renderer.GetType(), "material." + animatedProperty);
                var curve = new AnimationCurve();
                curve.AddKey(0, leftKeyframeValue);
                curve.AddKey(1, rightKeyframeValue);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            
        }

        GUI.enabled = true;

        if (animatedPaths.Count > 0) {
            GUILayout.Label("Animated Paths:", EditorStyles.boldLabel);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            foreach (var path in animatedPaths) {
                GUILayout.Label(path);
            }
            GUILayout.EndScrollView();
        }

    }
}
