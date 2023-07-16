using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VF.Builder;
using VF.Inspector;
using VF.Feature.Base;
using VF.Model.Feature;
using UnityEngine.UIElements;

using UComponent = UnityEngine.Component;

using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Avatars.Components;
using VRC.Dynamics; // ContactBase

namespace VF.Model.Feature {
    [Serializable]
    public class QuestCompatibility : NewFeatureModel {
        // for debugging, run this feature even if the build target is not quest
        public bool debug_runAlways = false;

        // List of materials to swap for other materials, effectively a dictionary
        public List<MaterialSwap> materialOverrides = new List<MaterialSwap>();

        // List of renderers with the associated slot to delete, effectively a List<Pair<Renderer, int>>
        public List<MaterialSlot> materialSlotDeletes = new List<MaterialSlot>();

        // default replacement shaders
        public MobileShader defaultShader = MobileShader.ToonLit;
        // default should be to remove particle materials because they are broken
        // see: https://feedback.vrchat.com/bug-reports/p/1281questlongstandingavatars-additive-particle-shader-rendering-brokenincorrect
        public MobileShader defaultParticleShader = MobileShader.RemoveAllParticles;

        // remove vertex Colors from meshes
        public bool removeVertexColors = true;

        // List of components to delete (dynamics)
        public List<ComponentDelete> componentDeletes = new List<ComponentDelete>();

        [Serializable]
        public class MaterialSwap {
            public Material from;
            public Material to;
            public bool ResetMePlease;
        }

        [Serializable]
        public class MaterialSlot {
            public Renderer renderer;
            public int slot;
            public bool ResetMePlease;
        }

        [Serializable]
        public class ComponentDelete {
            public UnityEngine.Component component;
            public bool delete;
            public bool ResetMePlease;
        }

        public enum MobileShader {
            StandardLite,
            Diffuse,
            BumpedDiffuse,
            BumpedMappedSpecular,
            ToonLit,
            [InspectorName("MatCap Lit")] MatCapLit,
            [InspectorName("Particles/Additive (BROKEN)")] ParticlesAdditive,
            [InspectorName("Particles/Multiply (BROKEN)")] ParticlesMultiply,
            [InspectorName("Remove all Particles")] RemoveAllParticles,
        }
    }
}

namespace VF.Feature {
    public class QuestCompatibilityBuilder : FeatureBuilder<QuestCompatibility> {
        #region Editor
        public override string GetEditorTitle() {
            return "Duck's Tools/Quest Compatibility (Experimental)";
        }

        public override bool AvailableOnProps() {
            return false; // until I figure out a better system for nesting these
        }

        public override VisualElement CreateEditor(SerializedProperty prop) {
            var content = new VisualElement();
            content.Add(VRCFuryEditorUtils.Info(
                "This feature will swap materials and delete incompatible components " +
                "for Quest builds. This feature is not applied to PC builds.\n" +
                "It is recommended to build a test copy or view with 'Build in play mode' " +
                "enabled to ensure your avatar looks correct on Quest."));
            
            bool isProp = featureBaseObject.GetComponent<VRCAvatarDescriptor>() == null;

            // get this via reflection so we don't need to depend on the validation hijack
            bool isQuest = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
            Type validationHijack = Type.GetType("VF.ValidationHijack");
            FieldInfo validationHijackFailureField = validationHijack?.GetField("validationHijackFailed", BindingFlags.Static | BindingFlags.Public);
            bool validationHijackFailed = validationHijackFailureField == null || (bool)validationHijackFailureField.GetValue(null);

            // If the validation hijack is missing, the user has no way to upload their avatar.
            // We should warn them about this and provide a button to force the build and upload.
            if (isQuest && !isProp && validationHijackFailed) {
                content.Add(VRCFuryEditorUtils.Warn("Validation Hijack not found. Please ensure your avatar is valid for Quest before uploading."));
                // vertical space
                content.Add(new VisualElement { style = { height = 10 }});
                content.Add(new Button(() => {
                    // just do what the SDK does
                    // save fog settings
                    VRC.Editor.EnvConfig.FogSettings fogSettings = VRC.Editor.EnvConfig.GetFogSettings();
                    VRC.Editor.EnvConfig.SetFogSettings(
                        new VRC.Editor.EnvConfig.FogSettings(VRC.Editor.EnvConfig.FogSettings.FogStrippingMode.Custom, true, true, true));
                    // strip shaders
                    EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders", true);
                    // build and upload
                    VRC.SDKBase.Editor.VRC_SdkBuilder.shouldBuildUnityPackage = VRCSdkControlPanel.FutureProofPublishEnabled;
                    VRC.SDKBase.Editor.VRC_SdkBuilder.ExportAndUploadAvatarBlueprint(featureBaseObject);
                    // restore fog settings
                    VRC.Editor.EnvConfig.SetFogSettings(fogSettings);
                }) { text = "Force Build and Publish" });

                // horizontal line
                content.Add(new VisualElement { style = { height = 1, backgroundColor = Color.gray, marginBottom = 10, marginTop = 5 }});
            }

            // debug mode
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("debug_runAlways"), "Run on PC builds (DEBUG): "));

            // Vertex Colors
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("removeVertexColors"), "Remove Vertex Colors (recommended): "));

            // default replacement shaders
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("defaultShader"), "Default Replacement Shader: "));
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("defaultParticleShader"), "Default Particle Shader: "));

            // horizontal line
            content.Add(new VisualElement { style = { height = 1, backgroundColor = Color.gray, marginBottom = 10, marginTop = 5 }});

            // Material overrides
            content.Add(new Label("Material Overrides:") { style = { unityFontStyleAndWeight = FontStyle.Bold }});
            content.Add(VRCFuryEditorUtils.WrappedLabel("These materials will be swapped for the specified materials in the Quest build, " +
                                  "swapping to an empty material will delete any mesh slots with that material."));
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("materialOverrides"), RenderOverridesList));

            // horizontal line
            content.Add(new VisualElement { style = { height = 1, backgroundColor = Color.gray, marginBottom = 10, marginTop = 5 }});

            // Material slot deletions
            content.Add(new Label("Delete Material Slots:") { style = { unityFontStyleAndWeight = FontStyle.Bold }});
            content.Add(new Label("These material slots will be deleted."));
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("materialSlotDeletes"), RenderDeletionsList));

            // the dynamics section is not available on props
            if (isProp) {
                return content;
            }

            // horizontal line
            content.Add(new VisualElement { style = { height = 1, backgroundColor = Color.gray, marginBottom = 10, marginTop = 5 }});

            // Component deletions
            content.Add(new Label("Avatar Dynamics:") { style = { unityFontStyleAndWeight = FontStyle.Bold }});
            content.Add(VRCFuryEditorUtils.WrappedLabel("During build, you will be warned if exceeding avatar dynamics Quest limits."));

            // check for any physbones that aren't network id'd
            var networkIDCollection = featureBaseObject.GetComponent<VRCAvatarDescriptor>().NetworkIDCollection;
            var nonNetworkedPhysbones = featureBaseObject.GetComponentsInChildren<VRCPhysBone>(true)
                .Where(x => !networkIDCollection.Any(y => y.gameObject == x.gameObject));
            
            // warn if any physbones have no network id
            if (nonNetworkedPhysbones.Any()) {
                // vertical space
                content.Add(new VisualElement { style = { height = 10 }});

                // warning
                content.Add(VRCFuryEditorUtils.Warn(
                    "One or more VRCPhysBones do not have a network ID. " +
                    "This can cause issues where grab and stretch may " +
                    "not sync correctly if Physbones are not identical " +
                    "between platforms."));
                
                // info to fix
                content.Add(VRCFuryEditorUtils.Info(
                    "Network IDs can be assigned using the Network ID Utility.\n" +
                    "Select your avatar from the dropdown and click 'Regenerate Scene IDs'\n" +
                    "Do not forget to reupload your PC avatar after doing this!"
                ));

                content.Add(VRCFuryEditorUtils.Button("Open Network ID Utility", () => {
                    var win = EditorWindow.GetWindow<VRCNetworkIDUtility>();
                    // whooooops, settarget is not public
                    var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                    var argTypes = new Type[] { typeof(VRCAvatarDescriptor) };
                    var setTargetMethod = typeof(VRCNetworkIDUtility).GetMethod("SetTarget", bf, null, argTypes, null);
                    setTargetMethod.Invoke(win, new object[] { featureBaseObject.GetComponent<VRCAvatarDescriptor>() });
                }));


                // horizontal line
                content.Add(new VisualElement { style = { height = 1, backgroundColor = Color.gray, marginBottom = 10, marginTop = 5 }});
            }

            // vertical space
            content.Add(new VisualElement { style = { height = 10 }});

            content.Add(VRCFuryEditorUtils.WrappedLabel(
                "Checked components will be deleted from the avatar during Quest build. " +
                "This is necessary if your avatar exceeds Quest limits for dynamics."));

            // vertical space
            content.Add(new VisualElement { style = { height = 10 }});

            // button to "populate list"
            var refreshButton = new Button(() => {
                var avi = featureBaseObject.gameObject;
                var physbones = avi.GetComponentsInChildren<VRCPhysBone>(true);
                var colliders = avi.GetComponentsInChildren<VRCPhysBoneCollider>(true);
                var contacts = avi.GetComponentsInChildren<ContactBase>(true);

                var oldList = model.componentDeletes;

                var listProp = prop.FindPropertyRelative("componentDeletes");
                listProp.ClearArray();

                var allComponents = (new List<UComponent>()).Union(physbones).Union(colliders).Union(contacts).ToList();

                foreach (var component in allComponents) {
                    listProp.arraySize++;
                    var elem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
                    elem.FindPropertyRelative("component").objectReferenceValue = component;
                    elem.FindPropertyRelative("delete").boolValue = oldList.Find(x => x.component == component)?.delete ?? false;
                }

                listProp.serializedObject.ApplyModifiedProperties();
            }) {
                text = "Refresh List"
            };

            var removePhysbonesButton = new Button(() => {
                var listProp = prop.FindPropertyRelative("componentDeletes");
                for (int i = 0; i < listProp.arraySize; i++) {
                    var elem = listProp.GetArrayElementAtIndex(i);
                    var component = elem.FindPropertyRelative("component").objectReferenceValue;
                    if (component is VRCPhysBone) {
                        elem.FindPropertyRelative("delete").boolValue = true;
                    }
                }
                listProp.serializedObject.ApplyModifiedProperties();
            }) {
                text = "Remove Physbones & Colliders"
            };

            var removeContactsButton = new Button(() => {
                var listProp = prop.FindPropertyRelative("componentDeletes");
                for (int i = 0; i < listProp.arraySize; i++) {
                    var elem = listProp.GetArrayElementAtIndex(i);
                    var component = elem.FindPropertyRelative("component").objectReferenceValue;
                    if (component is ContactBase) {
                        elem.FindPropertyRelative("delete").boolValue = true;
                    }
                }
                listProp.serializedObject.ApplyModifiedProperties();
            }) {
                text = "Remove Contacts"
            };

            var uncheckAllButton = new Button(() => {
                var listProp = prop.FindPropertyRelative("componentDeletes");
                for (int i = 0; i < listProp.arraySize; i++) {
                    var elem = listProp.GetArrayElementAtIndex(i);
                    elem.FindPropertyRelative("delete").boolValue = false;
                }
                listProp.serializedObject.ApplyModifiedProperties();
            }) {
                text = "Keep All"
            };

            // run these buttons in a row
            var row = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart,
                    flexWrap = Wrap.Wrap
                }
            };

            var col1 = new VisualElement();
            var col2 = new VisualElement() {
                style = {
                    flexGrow = 1
                }
            };

            col1.Add(refreshButton);
            col1.Add(uncheckAllButton);
            col2.Add(removePhysbonesButton);
            col2.Add(removeContactsButton);
            row.Add(col1);
            row.Add(col2);
            content.Add(row);

            // vertical space
            content.Add(new VisualElement { style = { height = 10 }});
            
            //TODO: render an immutable list without add/delete/reorder buttons
            content.Add(VRCFuryEditorUtils.List(prop.FindPropertyRelative("componentDeletes"), RenderComponentDeletionListElem));
            
            return content;
        }

        // Renders the small component deletion list
        private static VisualElement RenderComponentDeletionListElem(int i, SerializedProperty listElem) {
            var componentProp = listElem.FindPropertyRelative("component");
            UComponent component = componentProp.objectReferenceValue as UComponent;

            StyleColor typeColor = 
                component is VRCPhysBone ? new Color(0f, 1f, 0f, 0.2f) :
                component is VRCPhysBoneCollider ? new Color(0f, 0f, 1f, 0.2f) :
                component is ContactBase ? new Color(1f, 1f, 0f, 0.2f) :
                Color.red;

            var row = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart
                }
            };

            if (component == null) {
                row.Add(VRCFuryEditorUtils.WrappedLabel("????????????????? (object was deleted)"));
                return row;
            }

            var select_button = new Button(() => {
                EditorGUIUtility.PingObject(component.gameObject);
            }) {
                text = "Select"
            };

            row.Add(select_button);

            var componentField = VRCFuryEditorUtils.Prop(componentProp); // typeof(UnityEngine.Component)
            componentField.style.flexGrow = 1;
            componentField.style.flexShrink = 0;
            componentField.style.flexBasis = 0;
            componentField.style.backgroundColor = typeColor;
            componentField.SetEnabled(false);

            row.Add(componentField);

            // blank space 20px wide
            var deleteField = VRCFuryEditorUtils.Prop(listElem.FindPropertyRelative("delete"), "Delete:", 30); // typeof(bool)
            row.Add(new VisualElement { style = { width = 20 }});
            row.Add(deleteField);

            return row;
        }

        // Renders the list of material overrides.
        // Renders each list element like [object field] → [object field]
        private static VisualElement RenderOverridesList(int i, SerializedProperty listElem) {
            var row = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart
                }
            };
            var matFromField = VRCFuryEditorUtils.Prop(listElem.FindPropertyRelative("from")); // typeof(Material)
            matFromField.style.flexGrow = 1;
            var matToField = VRCFuryEditorUtils.Prop(listElem.FindPropertyRelative("to")); // typeof(Material)
            matToField.style.flexGrow = 1;

            row.Add(matFromField);
            row.Add(new Label("→") { style = { flexGrow = 0, flexBasis = 40, unityTextAlign = TextAnchor.MiddleCenter } });
            row.Add(matToField);

            return row;
        }

        // Renders the list of material slot deletions.
        // Renders each list element like [object field] Slot: [int field]
        private static VisualElement RenderDeletionsList(int i, SerializedProperty listElem) {
            var row = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart
                }
            };
            var rendererField = VRCFuryEditorUtils.Prop(listElem.FindPropertyRelative("renderer")); // typeof(Renderer)
            rendererField.style.flexGrow = 1;
            var slotField = VRCFuryEditorUtils.Prop(listElem.FindPropertyRelative("slot")); // typeof(int)
            slotField.style.flexGrow = 0;
            slotField.style.flexBasis = 40;

            row.Add(rendererField);
            row.Add(new Label("Slot:") { style = { flexGrow = 0, flexBasis = 40, unityTextAlign = TextAnchor.MiddleCenter } });
            row.Add(slotField);

            return row;
        }
        #endregion

        #region Apply
        [FeatureBuilderAction(FeatureOrder.ApplyToggleRestingState + 2)]
        public void Apply() {
            // on non-pc build platform, do nothing
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android && !model.debug_runAlways) {
                Debug.Log("Not on Android build target, skipping Quest Compatibility Builder");
                return;
            }

            // we want to allow this on props, so we work from featurebase instead of avatarobject
            GameObject rootObject = this.featureBaseObject;

            ReplaceIncompatibleMaterials(rootObject);
            NullAnyMaterialSlotsInTheDeletionList(rootObject);
            // the above two steps must be done before the next step
            RemoveAllNullMats(rootObject);
            DeleteIncompatibleComponents(rootObject);

            if (model.removeVertexColors) RemoveVertexColors(rootObject);

            // delete dynamics as per the list
            // don't do this on props
            if (rootObject == avatarObject) {
                DeleteDynamics(rootObject);
                WarnOnDynamicsLimits(rootObject);
            }
        }
        #endregion

        #region Dynamics Builder
        // removes dynamics components as specified by the user
        private void DeleteDynamics(GameObject avatar) {
            foreach (var item in model.componentDeletes) {
                if (item.delete && item.component != null) {
                    UComponent.DestroyImmediate(item.component);
                }
            }
        }

        // carbon copy of some internal sdk class, but without the assembly reference
        private class PhysboneStats {
            public int componentCount;
            public int transformCount;
            public int colliderCount;
            public int collisionCheckCount;

        }

        // Calculates physbone performance stats similarly to the SDK, but taking into account
        // components that will be deleted
        private PhysboneStats CalculatePhysbonePerformanceStats(GameObject avatar) {
            var physbones = avatar.GetComponentsInChildren<VRCPhysBone>(true);
            var colliders = avatar.GetComponentsInChildren<VRCPhysBoneCollider>(true);

            int numTransforms = 0;
            int numCollisionChecks = 0;

            foreach (var physbone in physbones) {
                //TODO: does VRCPhysBone.InitTransforms(...) have side effects?
                // we need to call it so that physbone.bones is populated
                // this could be calculated manually, but this is easier
                physbone.InitTransforms(force: true);
                int numBonesInThisComponent = physbone.bones.Count();
                numTransforms += numBonesInThisComponent;
                int usedColliders = physbone.colliders.Where(c => c != null).Count();
                numCollisionChecks += usedColliders * numBonesInThisComponent;
            }
            // dump the stats into a convenient struct in the SDK
            return new PhysboneStats() {
                componentCount = physbones.Count(), 
                transformCount = numTransforms, 
                colliderCount = colliders.Count(), 
                collisionCheckCount = numCollisionChecks
            };
        }

        private void WarnOnDynamicsLimits(GameObject avatar) {
            int numContacts = avatar.GetComponentsInChildren<ContactBase>(true).Count();
            //int numReceivers = avatar.GetComponentsInChildren<VRCContactReceiver>(true).Count();
            //int numSenders = avatar.GetComponentsInChildren<VRCContactSender>(true).Count();
            //numContacts = numReceivers + numSenders;

            var perfStats = CalculatePhysbonePerformanceStats(avatar);

            bool doWarning = false;
            string warningText = "Avatar exceeds the following Avatar Dynamics limits:\n";

            Action<string> warn = (string s) => {
                doWarning = true;
                warningText += $"- {s}\n";
            };

            

            if (numContacts > 16) warn($"{numContacts} contacts (max 16)\n");
            if (perfStats.componentCount > 8) warn($"{perfStats.componentCount} physbone components (max 8)");
            if (perfStats.transformCount > 64) warn($"{perfStats.transformCount} physbone transforms (max 64)");
            if (perfStats.colliderCount > 16) warn($"{perfStats.colliderCount} physbone colliders (max 16)");
            if (perfStats.collisionCheckCount > 64) warn($"{perfStats.collisionCheckCount} physbone collision checks (max 64)");

            warningText += "These components will be removed.";

            if (!doWarning) return; // no warnings, we're done

            var ask = EditorUtility.DisplayDialog("VRCFury", warningText, "Remove Dynamics", "Cancel Build");

            if (ask == false /* Cancel Build */) throw new Exception("User cancelled build (Dynamics Limits)");
            else RemoveOverLimitsDynamics(avatar, perfStats, numContacts);
        }

        private void RemoveOverLimitsDynamics(GameObject avatar, PhysboneStats stats, int numContacts) {
            if (numContacts > 16) {
                avatar.GetComponentsInChildren<ContactBase>(true).ToList().ForEach(c => UComponent.DestroyImmediate(c));
            }

            if (stats.componentCount > 8 || stats.transformCount > 64) {
                avatar.GetComponentsInChildren<VRCPhysBone>(true).ToList().ForEach(c => UComponent.DestroyImmediate(c));
            }

            if (stats.colliderCount > 16 || stats.collisionCheckCount > 64) {
                avatar.GetComponentsInChildren<VRCPhysBoneCollider>(true).ToList().ForEach(c => UComponent.DestroyImmediate(c));
            }
        }
        #endregion

        #region Materials Builder
        // Keep around the materials we generate so we don't generate them multiple times from the same source material
        private Dictionary<Material, Material> materialCache;
        private Dictionary<Material, Material> materialCacheParticle;

        // Replaces all materials in the avatar with quest-compatible versions
        // Also replaces materials in any material swap animations
        // Will use the material override if one is specified
        private void ReplaceIncompatibleMaterials(GameObject root) {
            // initialize the material cache
            materialCache = new Dictionary<Material, Material>();
            materialCacheParticle = new Dictionary<Material, Material>();

            // find all renderers and change their materials, this is the easy part
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers) {
                Func<Material,Material> materialSelector = (Material m) => QuestMaterialFromMaterial(m, renderer is ParticleSystemRenderer);
                var mats = renderer.sharedMaterials.Select(materialSelector).ToArray();
                renderer.sharedMaterials = mats;
            }

            // find all material swap animations and change them too
            var layers = manager.GetAllUsedControllers()
                .SelectMany(c => c.GetLayers());

            // Do this to iterate over all clips in all layers
            foreach (var layer in layers) {
                //AnimatorIterator.ForEachClip(layer, (clip) => {
                foreach (var clip in new AnimatorIterator.Clips().From(layer)) {
                    // If the clip has no material references, we can skip it
                    var numMaterialRefs = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                        .ToList()
                        .SelectMany(b => AnimationUtility.GetObjectReferenceCurve(clip, b))
                        .Where(k => k.value is Material)
                        .Count();
                    if (numMaterialRefs == 0) return;

                    // ensure we can modify the clip
                    //CopyClipIfRequired(ref clip, setClip);
                    // should no longer be required after update
                    
                    // Iterate over each binding and change the material
                    AnimationUtility.GetObjectReferenceCurveBindings(clip)
                        .ToList()
                        .ForEach(b => {
                            // get the keyframes
                            var curve = AnimationUtility.GetObjectReferenceCurve(clip, b);
                            // Get a reference to the gameobject so we can check if it has a particle system renderer
                            // if so, replace the material with a particle material
                            GameObject go = root.transform.Find(b.path).gameObject;
                            bool hasParticleRenderer = go.GetComponent<ParticleSystemRenderer>() != null;

                            // for every keyframe, swap the material if it's a material
                            curve = curve.Select(k => {
                                var mat = k.value as Material;
                                if (mat) {
                                    //note: if this swaps to a null material
                                    // it will be result in strange behavior
                                    k.value = QuestMaterialFromMaterial(mat, hasParticleRenderer);
                                }
                                return k;
                            }).ToArray();
                            // set the new curve back
                            AnimationUtility.SetObjectReferenceCurve(clip, b, curve);
                        });

                    // call the setter to save the changes
                    //setClip(clip); // no longer required after update
                };
            }
        }

        private void SanitizeMaterial(Material mat) {
            // strip unused textures from the material
            // I don't think they are included in the build
            // but this is a good idea anyway
            foreach (string texname in mat.GetTexturePropertyNames()) {
                if (mat.shader.FindPropertyIndex(texname) == -1) {
                    mat.SetTexture(texname, null);
                }
            }

            // Some shaders can set the always pass to disabled, which hides the material entirely
            mat.SetShaderPassEnabled("Always", true);

            //TODO: strip unused properties from the material
        }

        // Maps the selectable enum to the actual shader string
        public static Dictionary<QuestCompatibility.MobileShader, string> mobileShaderEnumToShaderString = new Dictionary<QuestCompatibility.MobileShader, string>() {
            { QuestCompatibility.MobileShader.StandardLite,         "VRChat/Mobile/Standard Lite" },
            { QuestCompatibility.MobileShader.Diffuse,              "VRChat/Mobile/Diffuse" },
            { QuestCompatibility.MobileShader.BumpedDiffuse,        "VRChat/Mobile/Bumped Diffuse" },
            { QuestCompatibility.MobileShader.BumpedMappedSpecular, "VRChat/Mobile/Bumped Mapped Specular" },
            { QuestCompatibility.MobileShader.ToonLit,              "VRChat/Mobile/Toon Lit" },
            { QuestCompatibility.MobileShader.MatCapLit,            "VRChat/Mobile/MatCap Lit" },
            { QuestCompatibility.MobileShader.ParticlesAdditive,    "VRChat/Mobile/Particles/Additive" },
            { QuestCompatibility.MobileShader.ParticlesMultiply,    "VRChat/Mobile/Particles/Multiply" },
            { QuestCompatibility.MobileShader.RemoveAllParticles,   null },
        };

        // Creates a new quest-compatible material given a source material
        private Material QuestMaterialFromMaterial(Material mat, bool particleRenderer) {
            // check for an explicit override
            var overrideMat = model.materialOverrides.FirstOrDefault(m => m.from == mat);
            if (overrideMat != null) {
                return overrideMat.to;
            }

            if (mat == null) return null;

            // starts with "VRChat/Mobile/" then we leave it alone
            if (mat.shader.name.StartsWith("VRChat/Mobile/")) {
                return mat;
            }

            // different materials for particle systems
            var matCache = particleRenderer ? materialCacheParticle : materialCache;

            // if we've already made a quest version of this material, use that
            if (matCache.ContainsKey(mat)) {
                return matCache[mat];
            }

            string shaderName = mobileShaderEnumToShaderString[particleRenderer ? model.defaultParticleShader : model.defaultShader];
            // shader name will be null for particle systems when the remove all option is selected
            if (shaderName == null) return null;
            Shader replacementShader = Shader.Find(shaderName);

            var newMat = new Material(replacementShader);
            newMat.name = mat.name + "_QuestCompat";
            newMat.renderQueue = mat.renderQueue;
            newMat.enableInstancing = mat.enableInstancing;
            newMat.CopyPropertiesFromMaterial(mat);

            SanitizeMaterial(newMat);

            // save it to the asset database
            VRCFuryAssetDatabase.SaveAsset(newMat, tmpDir, newMat.name);

            // add it to the cache
            matCache[mat] = newMat;

            return newMat;
        }

        // Sets any material slots in the slot deletion list to null
        // The actual slots are deleted in a subsequent step
        private void NullAnyMaterialSlotsInTheDeletionList(GameObject root) {
            foreach (var deletion in model.materialSlotDeletes) {
                var renderer = deletion.renderer;

                // check the user isn't doing something silly
                if (deletion.slot >= renderer.sharedMaterials.Length) {
                    throw new Exception("Material slot " + deletion.slot + " doesn't exist on " + renderer.gameObject.name);
                }

                // must read modify write
                var mats = renderer.sharedMaterials;
                mats[deletion.slot] = null;
                renderer.sharedMaterials = mats;
            }
        }

        // Finds all material slots on renderers with a null material and deletes them
        private void RemoveAllNullMats(GameObject root) {
            // delete all null material slots and update any animations that reference
            // any deleted or reordered material slots

            var renderers = root.GetComponentsInChildren<Renderer>(true);

            // list of states containing relevant material animations
            List<AnimatorState> states = new List<AnimatorState>();

            // find all states that reference a material slot, and add them to the list
            // We do this so we don't need to iterate over all states for every deleted slot
            foreach (var layer in manager.GetAllUsedControllers().SelectMany(c => c.GetLayers())) {
                //AnimatorIterator.ForEachState(layer, state => {
                foreach (var state in new AnimatorIterator.States().From(layer)) {
                    var clip = state.motion as AnimationClip;

                    //TODO: root out all other animation clips from inside blend trees
                    if (clip == null) continue;

                    // Add object reference curve bindings that reference a renderer
                    AnimationUtility.GetObjectReferenceCurveBindings(clip)
                        .Where(b => AnimationUtility.GetAnimatedObject(avatarObject, b) is Renderer)
                        .Where(b => b.propertyName.StartsWith("m_Materials.Array.data["))
                        .ToList()
                        .ForEach(b => { states.Add(state); });
                };
            }           

            foreach (var renderer in renderers) {
                // if there's only one material and it's null, delete the renderer
                // side effect: the user may intent to have a renderer with a null material (ie. material swapping something onto it)
                // but I don't think that's a common use case
                // other use case: antawa's optimizer tool creates renderers with null materials and null meshes
                if (
                    renderer.sharedMaterials.Length == 1 
                    && renderer.sharedMaterials[0] == null
                    && !(
                        renderer is SkinnedMeshRenderer smr 
                        && smr.sharedMesh == null
                    )
                ) {

                    UComponent.DestroyImmediate(renderer);
                    continue;
                }

                var x = renderer.sharedMaterials;

                // repeatedly delete one null material slot at a time until there are no more
                if (renderer.sharedMaterials.Contains(null)) {
                    DeleteNullMatsAndFixAnims(renderer, states);
                }
            }
        }

        // Deletes a given material slot from a renderer
        // Also updates any material swap animations to point to the correct slot
        // And deletes any animations that reference the deleted slot
        private void DeleteNullMatsAndFixAnims(Renderer renderer, List<AnimatorState> states) {
            var mats = renderer.sharedMaterials.ToList();

            Mesh inputMesh = null;

            if (renderer is SkinnedMeshRenderer smr) inputMesh = smr.sharedMesh;
            else if (renderer is MeshRenderer mr) inputMesh = mr.GetComponent<MeshFilter>().sharedMesh;
            else throw new Exception("Incompatible renderer type: " + renderer.GetType());

            // antawa's optimizer tool creates renderers with null materials and null meshes
            if (inputMesh == null) return;

            if (mats.Count == 0) return; // ???
            if (!mats.Contains(null)) return; // ??? shouldn't have called this function

            // keeps track of which material slots need to be remapped to which
            Dictionary<int, int> remapSlots = new Dictionary<int, int>();
            
            // list of all the slots that are null
            var deletions = mats.Select((m, i) => new { m, i })
                .Where(x => x.m == null)
                .Select(x => x.i)
                .ToList();
            
            // make a copy of the mesh
            Mesh newMesh = mutableManager.MakeMutable(inputMesh); //Object.Instantiate(inputMesh);

            // copy through indices for all non-null slots
            // and add entries to the remapSlots dictionary
            int output_submesh_num = 0;
            for (int input_submesh_num = 0; input_submesh_num < inputMesh.subMeshCount; input_submesh_num++) {
                if (deletions.Contains(input_submesh_num)) continue; // skip submesh

                // copy the submesh
                int[] indices = inputMesh.GetIndices(input_submesh_num);
                newMesh.SetIndices(indices, inputMesh.GetTopology(input_submesh_num), output_submesh_num);
                remapSlots.Add(input_submesh_num, output_submesh_num);
                output_submesh_num++;
            }

            // delete all other slots
            for (int i = output_submesh_num; i < newMesh.subMeshCount; i++) {
                newMesh.SetIndices(new int[0], MeshTopology.Triangles, i);
            }
            
            newMesh.subMeshCount = output_submesh_num;

            //VRCFuryAssetDatabase.SaveAsset(newMesh, tmpDir, newMesh.name);

            // fixup any animations
            // ie. if we delete the 2nd material slot, we need to update any animations pointing to
            // the 3rd material slot to point to the 2nd instead and etc..
            foreach (var state in states) {
                var clip = state.motion as AnimationClip;
                // ignore any states that aren't animation clips (ie. blend trees)
                if (clip == null) continue;

                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                foreach (var binding in bindings) {
                    // ignore any bindings that aren't for this renderer
                    if (AnimationUtility.GetAnimatedObject(avatarObject, binding) != renderer) continue;

                    string bindingName = binding.propertyName;
                    // ignore any bindings that aren't for material slots
                    if (!bindingName.StartsWith("m_Materials.Array.data[")) continue;

                    // get the slot index
                    int animatedSlot = int.Parse(bindingName.Split('[', ']')[1]);

                    // no need to fix up if the slot is before the deleted slot
                    int remappedSlot;
                    // for the edge case where the animation animates a slot that never existed
                    if (!remapSlots.TryGetValue(animatedSlot, out remappedSlot)) remappedSlot = animatedSlot;
                    if (remappedSlot == animatedSlot) continue;

                    // we need to fix this up, so make sure we can modify the clip
                    //CopyClipIfRequired(ref clip, s => state.motion = s);

                    if (animatedSlot == -1) {
                        // delete the animation
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                    } else {
                        var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                        EditorCurveBinding newBinding = binding;
                        newBinding.propertyName = newBinding.propertyName.Replace("[" + animatedSlot + "]", "[" + remappedSlot + "]");
                        AnimationUtility.SetObjectReferenceCurve(clip, newBinding, curve);
                    }
                }
            }

            EditorUtility.SetDirty(newMesh);

            // update the renderer
            if (renderer is SkinnedMeshRenderer smr2) smr2.sharedMesh = newMesh;
            else if (renderer is MeshRenderer mr2) mr2.GetComponent<MeshFilter>().sharedMesh = newMesh;

            // delete the material slots, this is easy
            mats.RemoveAll(m => m == null);
            renderer.sharedMaterials = mats.ToArray();
        }
        #endregion

        #region Component Remover
        // These components are allowed on PC avatars but not Quest avatars
        public static readonly string[] disallowedComponents = {
            "VRC.SDK3.Avatars.Components.VRCSpatialAudioSource",
            "DynamicBone",
            "DynamicBoneCollider",
            "RootMotion.FinalIK.IKExecutionOrder",
            "RootMotion.FinalIK.VRIK",
            "RootMotion.FinalIK.FullBodyBipedIK",
            "RootMotion.FinalIK.LimbIK",
            "RootMotion.FinalIK.AimIK",
            "RootMotion.FinalIK.BipedIK",
            "RootMotion.FinalIK.GrounderIK",
            "RootMotion.FinalIK.GrounderFBBIK",
            "RootMotion.FinalIK.GrounderVRIK",
            "RootMotion.FinalIK.GrounderQuadruped",
            "RootMotion.FinalIK.TwistRelaxer",
            "RootMotion.FinalIK.ShoulderRotator",
            "RootMotion.FinalIK.FBBIKArmBending",
            "RootMotion.FinalIK.FBBIKHeadEffector",
            "RootMotion.FinalIK.FABRIK",
            "RootMotion.FinalIK.FABRIKChain",
            "RootMotion.FinalIK.FABRIKRoot",
            "RootMotion.FinalIK.CCDIK",
            "RootMotion.FinalIK.RotationLimit",
            "RootMotion.FinalIK.RotationLimitHinge",
            "RootMotion.FinalIK.RotationLimitPolygonal",
            "RootMotion.FinalIK.RotationLimitSpline",
            "UnityEngine.Cloth",
            "UnityEngine.Light",
            "UnityEngine.BoxCollider",
            "UnityEngine.SphereCollider",
            "UnityEngine.CapsuleCollider",
            "UnityEngine.Rigidbody",
            "UnityEngine.Joint",
            "UnityEngine.Animations.AimConstraint", // no constraints -> pain
            "UnityEngine.Animations.LookAtConstraint",
            "UnityEngine.Animations.ParentConstraint",
            "UnityEngine.Animations.PositionConstraint",
            "UnityEngine.Animations.RotationConstraint",
            "UnityEngine.Animations.ScaleConstraint",
            "UnityEngine.Camera",
            "UnityEngine.AudioSource",
            "ONSPAudioSource"
        };
        // Finds any components that are not allowed on Quest and deletes them
        private void DeleteIncompatibleComponents(GameObject root) {
            var components = root.GetComponentsInChildren<UComponent>(true);

            // delete all components that are not allowed on quest
            List<string> disallowed = disallowedComponents.ToList();

            components.Where(c => c.GetType() != typeof(Transform)) // minor optimization?
                .Where(c => disallowed.Contains(c.GetType().FullName))
                .ToList()
                .ForEach(c => UComponent.DestroyImmediate(c));
        }
        #endregion

        #region Vertex Color Remover
        // avoid duplicating meshes when multiple renderers share the same mesh
        private Dictionary<Mesh, Mesh> meshCache = new Dictionary<Mesh, Mesh>();

        private Mesh RemoveVertexColorsFromMesh(Mesh input) {
            if (input == null) return null;
            // check we haven't already processed this mesh
            if (meshCache.ContainsKey(input)) return meshCache[input];
            // if the mesh doesn't have vertex Colors, we don't need to do anything
            if (input.colors.Length == 0) return input;
            // make a copy of the mesh
            Mesh output = mutableManager.MakeMutable(input);
            // remove the vertex colours
            output.colors = new Color[] {};
            VRCFuryEditorUtils.MarkDirty(output);
            // add the mesh to the cache
            meshCache.Add(input, output);

            return output;
        }

        // Removes vertex Colors from any meshes that have them
        private void RemoveVertexColors(GameObject root) {
            var renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers) {
                // process the mesh through RemoveVertexColors 
                if (renderer is SkinnedMeshRenderer smr) smr.sharedMesh = RemoveVertexColorsFromMesh(smr.sharedMesh);
                else if (renderer is MeshRenderer mr) mr.GetComponent<MeshFilter>().sharedMesh = RemoveVertexColorsFromMesh(mr.GetComponent<MeshFilter>().sharedMesh);
            }
        }
        #endregion
    }
}