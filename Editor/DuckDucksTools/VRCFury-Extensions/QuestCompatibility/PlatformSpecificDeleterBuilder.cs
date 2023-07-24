using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using VF.Inspector;
using VF.Feature.Base;
using VF.Model;
using VF.Model.Feature;
using VRC.SDK3.Dynamics.Contact.Components;

using Object = UnityEngine.Object;

namespace VF.Model.Feature {
    [Serializable]
    public class PlatformSpecificDeleter : NewFeatureModel {

        public List<GameObject> deleteObjects = new List<GameObject>();

        public PlatformSelection platformSelection = PlatformSelection.AllPlatforms;

        public enum PlatformSelection {
            AllPlatforms,
            DesktopOnly,
            QuestOnly
        }
    }
}

namespace VF.Feature {
    public class ComponentDeleterBuilder : FeatureBuilder<PlatformSpecificDeleter> {
        public override string GetEditorTitle() {
            return "Duck's Tools/Platform Specific Object Deleter";
        }

        public override bool AvailableOnProps() {
            return true;
        }

        public override VisualElement CreateEditor(SerializedProperty prop) {

            var content = new VisualElement();

            content.Add(VRCFuryEditorUtils.Info("Objects are deleted at the end of the build process. " +
                "Toggles are still generated but will have no effect."));

            content.Add(VRCFuryEditorUtils.Prop(
                prop.FindPropertyRelative("platformSelection"),
                "Platform: "
            ));

            content.Add(VRCFuryEditorUtils.WrappedLabel("Delete Objects:"));

            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("deleteObjects")));

            return content;
        }

        // strip any nulls from the list
        // if nulls show up later, we throw an exception
        [FeatureBuilderAction(FeatureOrder.Default)]
        public void StripNulls() {
            model.deleteObjects.RemoveAll(o => o == null);
        }

        // run after any defaults capture
        [FeatureBuilderAction(FeatureOrder.BlendshapeOptimizer - 2)]
        public void Apply() {
            bool isQuestBuild = (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android);
            bool isDesktopBuild = !isQuestBuild;
            bool applyForQuest = (model.platformSelection != PlatformSpecificDeleter.PlatformSelection.DesktopOnly);
            bool applyForDesktop = (model.platformSelection != PlatformSpecificDeleter.PlatformSelection.QuestOnly);

            // Do not apply we are not building for the desired platform
            if (isQuestBuild && !applyForQuest || isDesktopBuild && !applyForDesktop) {
                return;
            }

            foreach (var obj in model.deleteObjects) {
                // ensure the object is not destroyed
                // This can happen when something is nuked by armature link
                if (obj == null) {
                    throw new Exception("An object was destroyed before it could be accessed by the deleter. (probably a bug or misuse of armature link)");
                }

                // delete the object
                Object.DestroyImmediate(obj);
            }
        }
    }
}