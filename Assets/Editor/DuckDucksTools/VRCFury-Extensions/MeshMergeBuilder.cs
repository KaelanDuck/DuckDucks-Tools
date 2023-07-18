using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

using VF.Inspector;
using VF.Feature.Base;
using VF.Model.Feature;
using VF.Builder;
using UnityEngine.UIElements;
using UnityEditor.Animations;

using Object = UnityEngine.Object;

namespace VF.Model.Feature {
    [Serializable]
    public class MeshMerge : NewFeatureModel {
        public bool mergeBodyMesh = true;
    }
}

namespace VF.Feature {
    public class MeshMergeBuilder : FeatureBuilder<MeshMerge> {

        public override string GetEditorTitle() {
            return "Duck's Tools/Mesh Merger";
        }

        public override bool AvailableOnProps() {
            return false;
        }

        public override VisualElement CreateEditor(SerializedProperty prop) {
            var content = new VisualElement();
            content.Add(VRCFuryEditorUtils.Info("Merges skinned meshes automatically when they are compatible."));
            content.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("mergeBodyMesh"), "Also merge face mesh: "));
            return content;
        }

        // return a map between blendshape name to value for a given skinned mesh renderer
        private Dictionary<string, float> GetBlendshapeValues(SkinnedMeshRenderer smr) {
            var blendshapeValues = new Dictionary<string, float>();
            for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) {
                var name = smr.sharedMesh.GetBlendShapeName(i);
                var value = smr.GetBlendShapeWeight(i);
                blendshapeValues[name] = value;
            }
            return blendshapeValues;
        }

        private bool AreBlendshapeValuesCompatible(SkinnedMeshRenderer smr1, SkinnedMeshRenderer smr2) {
            var bs1 = GetBlendshapeValues(smr1);
            var bs2 = GetBlendshapeValues(smr2);

            return bs1.Keys.Intersect(bs2.Keys).All(k => bs1[k] == bs2[k]);
        }

        private Dictionary<Tuple<SkinnedMeshRenderer,SkinnedMeshRenderer>,bool> smrBaseValuesCompatibleCache = new Dictionary<Tuple<SkinnedMeshRenderer,SkinnedMeshRenderer>,bool>();
        private bool AreSMRBaseValuesCompatible(SkinnedMeshRenderer smr1, SkinnedMeshRenderer smr2) {
            var key = new Tuple<SkinnedMeshRenderer,SkinnedMeshRenderer>(smr1, smr2);
            if (smrBaseValuesCompatibleCache.ContainsKey(key))
                return smrBaseValuesCompatibleCache[key];

            if ((smr1.gameObject.activeSelf != smr2.gameObject.activeSelf)
                || (smr1.gameObject.activeInHierarchy != smr2.gameObject.activeInHierarchy)
                || (smr1.probeAnchor != smr2.probeAnchor)
                || (smr1.rootBone != smr2.rootBone)
                || (smr1.quality != smr2.quality)
                || (smr1.updateWhenOffscreen != smr2.updateWhenOffscreen)
                || (smr1.shadowCastingMode != smr2.shadowCastingMode)
                || (smr1.receiveShadows != smr2.receiveShadows)
                || (smr1.lightProbeUsage != smr2.lightProbeUsage)
                || (smr1.reflectionProbeUsage != smr2.reflectionProbeUsage)
                || (smr1.skinnedMotionVectors != smr2.skinnedMotionVectors)
                || (smr1.allowOcclusionWhenDynamic != smr2.allowOcclusionWhenDynamic)
                || (!AreBlendshapeValuesCompatible(smr1, smr2))
                //TODO: check animated material properties have the same base values
                || (smr1.gameObject.name == "Body" && smr1.transform.parent == avatarObject.transform && !model.mergeBodyMesh)
                || (smr2.gameObject.name == "Body" && smr2.transform.parent == avatarObject.transform && !model.mergeBodyMesh))
            {
                smrBaseValuesCompatibleCache[key] = false;
                return false;
            } else {
                smrBaseValuesCompatibleCache[key] = true;
                return true;
            }
        }

        private static List<AnimationClip> ClipsFromBlendTree(BlendTree bt) {
            var clips = new List<AnimationClip>();
            foreach (var child in bt.children) {
                if (child.motion is BlendTree childBT) {
                    clips.AddRange(ClipsFromBlendTree(childBT));
                } else if (child.motion is AnimationClip clip) {
                    clips.Add(clip);
                }
            }
            return clips;
        }

        private HashSet<AnimationClip> relevantAnimationClips= null;

        private Dictionary<AnimationClip,List<EditorCurveBinding>> GetAnimationsReferencingSMR(SkinnedMeshRenderer smr) {
            var anims = new Dictionary<AnimationClip,List<EditorCurveBinding>>();

            // get all animation clips that reference any skinned mesh renderer
            // or enable any game object
            if (relevantAnimationClips == null) {
                List<AnimationClip> allClips = new List<AnimationClip>();
                foreach (var layer in manager.GetAllUsedControllers().SelectMany(c => c.GetLayers())) {
                    foreach (var state in new AnimatorIterator.States().From(layer)) {
                        if (state.motion is BlendTree bt) {
                            allClips.AddRange(ClipsFromBlendTree(bt));
                        } else if (state.motion is AnimationClip clip) {
                            allClips.Add(clip);
                        }
                    }
                }

                relevantAnimationClips = new HashSet<AnimationClip>(allClips.Distinct().Where(c => {
                    foreach (var binding in AnimationUtility.GetCurveBindings(c).Concat(AnimationUtility.GetObjectReferenceCurveBindings(c))) {
                        if (binding.type == typeof(SkinnedMeshRenderer)) return true;
                        if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") return true;
                    }
                    return false;
                }));
            }

            List<GameObject> SMRAndParents = avatarObject.GetComponentsInSelfAndChildren<Transform>()
                .Intersect(smr.GetComponentsInParent<Transform>(true))
                .Select(t => t.gameObject)
                .Append(smr.gameObject)
                .ToList();

            // sort the clips into those containing curves referencing the smr
            foreach (var clip in relevantAnimationClips) {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip))) {
                    if (binding.type == typeof(SkinnedMeshRenderer) && AnimationUtility.GetAnimatedObject(avatarObject, binding) == smr) {
                        if (!anims.ContainsKey(clip)) anims[clip] = new List<EditorCurveBinding>();
                        anims[clip].Add(binding);
                    }

                    if (binding.type == typeof(GameObject) 
                        && SMRAndParents.Contains(AnimationUtility.GetAnimatedObject(avatarObject, binding) as GameObject)
                        && binding.propertyName == "m_IsActive")
                    {
                        if (!anims.ContainsKey(clip)) anims[clip] = new List<EditorCurveBinding>();
                        anims[clip].Add(binding);
                    }
                }
            }

            return anims;
        }

        private static bool AnimationCurvesEquivalent(AnimationCurve curve1, AnimationCurve curve2) {
            if (curve1.length != curve2.length) return false;
            if (curve1.preWrapMode != curve2.preWrapMode) return false;
            if (curve1.postWrapMode != curve2.postWrapMode) return false;
            for (int i = 0; i < curve1.length; i++) {
                if (!curve1.keys[i].Equals(curve2.keys[i])) return false;
            }
            return true;
        }


        private bool MaterialPropertyExistsAmongMaterials(string property, IEnumerable<Material> materials) {
            return materials.Any(m => m != null && m.HasProperty(property));
        }

        private bool AreSMRAnimationsCompatibleOneWay(SkinnedMeshRenderer smr1, SkinnedMeshRenderer smr2) {
            var SMR1Anims = GetAnimationsReferencingSMR(smr1);
            var SMR2Anims = GetAnimationsReferencingSMR(smr2);

            var activeAnims1 = SMR1Anims.Where(pair => pair.Value.Any(b => b.propertyName == "m_IsActive")).ToList();
            var activeAnims2 = SMR2Anims.Where(pair => pair.Value.Any(b => b.propertyName == "m_IsActive")).ToList();

            // check that both active (m_IsActive) anims things are identical
            if (activeAnims1.Count != activeAnims2.Count) return false;
            foreach (var pair in activeAnims1) {
                if (!SMR2Anims.ContainsKey(pair.Key)) return false;
                if (SMR2Anims[pair.Key].Count != pair.Value.Count) return false;
                foreach (var binding in pair.Value) {
                    if (!SMR2Anims[pair.Key].Contains(binding)) return false;
                }
            }

            // for blendshape checking, since this is just the oneway check, check that animations on smr1 will not falsely animate smr2
            foreach (var pair in SMR1Anims) {
                var clip = pair.Key;
                var bindings = pair.Value;
                foreach (var binding in bindings) {
                    if (!binding.propertyName.StartsWith("blendShape.")) continue;
                    // if a corresponding animation exists in smr2anims, check that the keyframes are identical
                    // otherwise, check the blendshape doesn't exist in smr2
                    // add an empty list to smr2anims for ease of coding
                    if (!SMR2Anims.ContainsKey(clip)) SMR2Anims[clip] = new List<EditorCurveBinding>();

                    // check the blendshape actually exists in both meshes, if not there is no issue
                    var blendshapeName = binding.propertyName.Substring("blendShape.".Length);
                    if (
                        smr1.sharedMesh.GetBlendShapeIndex(blendshapeName) == -1
                        || smr2.sharedMesh.GetBlendShapeIndex(blendshapeName) == -1
                    ) continue;


                    if (SMR2Anims[clip].Any(b => b.propertyName == binding.propertyName)) {
                        EditorCurveBinding smr1Binding = binding;
                        EditorCurveBinding smr2Binding = SMR2Anims[clip].First(b => b.propertyName == binding.propertyName);
                        var smr1Curve = AnimationUtility.GetEditorCurve(clip, smr1Binding);
                        var smr2Curve = AnimationUtility.GetEditorCurve(clip, smr2Binding);

                        if (!AnimationCurvesEquivalent(smr1Curve, smr2Curve)) {
                            return false;
                        }
                    } else {
                        // the blendshape exists in both meshes, but is only animated in smr1
                        // therefore these meshes are not compatible
                        return false;
                    }

                }
            }

            // now check material property animations
            // do a similar thing to checking blendshapes
            foreach (var pair in SMR1Anims) {
                var clip = pair.Key;
                var bindings = pair.Value;
                foreach (var binding in bindings) {
                    if (!binding.propertyName.StartsWith("material.")) continue;
                    // if a corresponding animation exists in smr2anims, check that the keyframes are identical
                    // otherwise, for now, just reject the merge
                    // add an empty list to smr2anims for ease of coding
                    if (!SMR2Anims.ContainsKey(clip))  SMR2Anims[clip] = new List<EditorCurveBinding>();

                    // if the material property doesn't even exist in any of the materials on the second smr, it won't be incompatible
                    string propertyName = binding.propertyName.Substring("material.".Length);
                    if (
                        MaterialPropertyExistsAmongMaterials(propertyName, smr2.sharedMaterials)
                        && MaterialPropertyExistsAmongMaterials(propertyName, smr1.sharedMaterials)
                    ) {
                        // check if any animations for smr2 animate the same property
                        if (SMR2Anims[clip].Any(b => b.propertyName == binding.propertyName)) {
                            EditorCurveBinding smr1Binding = binding;
                            EditorCurveBinding smr2Binding = SMR2Anims[clip].First(b => b.propertyName == binding.propertyName);
                            var smr1Curve = AnimationUtility.GetEditorCurve(clip, smr1Binding);
                            var smr2Curve = AnimationUtility.GetEditorCurve(clip, smr2Binding);

                            if (!AnimationCurvesEquivalent(smr1Curve, smr2Curve)) {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        private bool AreSMRAnimationsCompatible(SkinnedMeshRenderer smr1, SkinnedMeshRenderer smr2) {
            return AreSMRAnimationsCompatibleOneWay(smr1, smr2) && AreSMRAnimationsCompatibleOneWay(smr2, smr1);
        }

        private bool AreSMRsCompatible(SkinnedMeshRenderer smr1, SkinnedMeshRenderer smr2) {
            return AreSMRBaseValuesCompatible(smr1, smr2) && AreSMRAnimationsCompatible(smr1, smr2);
        }

        // checks if a blendshape or material property animation affects the given skinned mesh renderer
        private bool CurveBindingReallyAffectsSMR(SkinnedMeshRenderer target, EditorCurveBinding binding) {
            if (binding.isPPtrCurve) return true;
            if (binding.propertyName.StartsWith("blendShape.")) {
                var blendshapeName = binding.propertyName.Substring("blendShape.".Length);
                return target.sharedMesh.GetBlendShapeIndex(blendshapeName) != -1;
            }
            if (binding.propertyName.StartsWith("material.")) {
                var propertyName = binding.propertyName.Substring("material.".Length);
                return MaterialPropertyExistsAmongMaterials(propertyName, target.sharedMaterials);
            }
            return true;
        }

        private void FixUpAnimations(SkinnedMeshRenderer target, SkinnedMeshRenderer source) {
            var targetAnims = GetAnimationsReferencingSMR(target);
            var sourceAnims = GetAnimationsReferencingSMR(source);

            var materialSwapAnims = sourceAnims.Where(pair => pair.Value.Any(b => b.isPPtrCurve && b.propertyName.StartsWith("m_Materials.Array.data["))).ToList();

            // we will overwrite the curve binding in the target animation with the source animation
            // as the target curve binding is irrelevant
            var animsToOverwrite = new Dictionary<AnimationClip, List<EditorCurveBinding>>();
            // we will rewrite the curve binding in the target animation to point to the same object as in the source animation
            // as there is no source curve binding, we will just use the target curve binding
            var animsToRewrite = new Dictionary<AnimationClip, List<EditorCurveBinding>>();

            // sort each source animation into one of the above categories
            foreach (var kvp in sourceAnims) {
                var animClip = kvp.Key;
                var bindings = kvp.Value;

                if (!animsToOverwrite.ContainsKey(animClip)) animsToOverwrite[animClip] = new List<EditorCurveBinding>();
                if (!animsToRewrite.ContainsKey(animClip)) animsToRewrite[animClip] = new List<EditorCurveBinding>();

                // find animation in destination
                bool hasTargetAnimClip = targetAnims.ContainsKey(animClip);
                if (!hasTargetAnimClip) {
                    // no clips animate the target object, so we will rewrite the existing curve
                    animsToRewrite[animClip].AddRange(bindings.Where(b => CurveBindingReallyAffectsSMR(source, b)));
                    continue;
                }

                foreach (var sb in bindings) {
                    // check if the binding really affects the SMR
                    // if not, then it doesn't matter, we don't need to touch it
                    // if it does, then we already know it can be merged as that has been checked earlier
                    if (!CurveBindingReallyAffectsSMR(source, sb)) continue;

                    // check if there is a corresponding binding in the target
                    bool hasTargetBinding = targetAnims[animClip].Any(tb => tb.propertyName == sb.propertyName);
                    if (hasTargetBinding) animsToOverwrite[animClip].Add(sb);
                    else animsToRewrite[animClip].Add(sb);
                }
            }

            // specifically exclude:
            // - material swaps
            // - m_IsActive
            // easier to do this here than to do it in the above loop
            foreach (var kvp in animsToOverwrite) {
                kvp.Value.RemoveAll(b => b.isPPtrCurve);
                kvp.Value.RemoveAll(b => b.propertyName == "m_IsActive");
            }
            foreach (var kvp in animsToRewrite) {
                kvp.Value.RemoveAll(b => b.isPPtrCurve);
                kvp.Value.RemoveAll(b => b.propertyName == "m_IsActive");
            }

            // now we have a list of animations to overwrite and a list of animations to rewrite
            foreach(var kvp in animsToOverwrite) {
                var animClip = kvp.Key;
                var bindings = kvp.Value;

                foreach(var binding in bindings) {
                    var targetBinding = targetAnims[animClip].First(b => b.propertyName == binding.propertyName);

                    // overwrite the target binding with the source curve
                    var sourceCurve = AnimationUtility.GetEditorCurve(animClip, binding);
                    AnimationUtility.SetEditorCurve(animClip, targetBinding, sourceCurve);
                    // erase the source binding for ease of debugging
                    AnimationUtility.SetEditorCurve(animClip, binding, null);
                }
            }

            // stuff to rewrite
            foreach(var kvp in animsToRewrite) {
                var animClip = kvp.Key;
                var bindings = kvp.Value;

                foreach(var binding in bindings) {
                    // rewrite the source binding to point to the target object
                    var targetBindingPath = AnimationUtility.CalculateTransformPath(target.transform, avatarObject.transform);
                    var targetBinding = EditorCurveBinding.DiscreteCurve(targetBindingPath, binding.type, binding.propertyName);
                    var sourceCurve = AnimationUtility.GetEditorCurve(animClip, binding);
                    // erase the existing binding for ease of debugging
                    AnimationUtility.SetEditorCurve(animClip, binding, null);
                    AnimationUtility.SetEditorCurve(animClip, targetBinding, sourceCurve);
                }
            }

            // deal with material swaps
            foreach(var kvp in materialSwapAnims) {
                var animClip = kvp.Key;
                var bindings = kvp.Value;

                foreach(var binding in bindings) {
                    // these can contain other animations too
                    if (!binding.isPPtrCurve || !binding.propertyName.StartsWith("m_Materials.Array.data[")) continue;
                    // rewrite the source binding to point to the target object
                    var targetBindingPath = AnimationUtility.CalculateTransformPath(target.transform, avatarObject.transform);
                    int sourceBindingIndex = int.Parse(binding.propertyName.Substring("m_Materials.Array.data[".Length).Split(']')[0]);
                    int targetBindingIndex = target.sharedMesh.subMeshCount + sourceBindingIndex;
                    var targetBindingPropertyName = $"m_Materials.Array.data[{targetBindingIndex}]";
                    var targetBinding = EditorCurveBinding.PPtrCurve(targetBindingPath, binding.type, targetBindingPropertyName);
                    var sourceCurve = AnimationUtility.GetObjectReferenceCurve(animClip, binding);
                    // erase the existing binding for ease of debugging
                    AnimationUtility.SetObjectReferenceCurve(animClip, binding, null);
                    AnimationUtility.SetObjectReferenceCurve(animClip, targetBinding, sourceCurve);
                }
            }
        }

        private void MergeSameMaterials(SkinnedMeshRenderer smr) {
            var refAnims = GetAnimationsReferencingSMR(smr);

            Dictionary<AnimationClip, List<EditorCurveBinding>> materialSwapAnims = new Dictionary<AnimationClip, List<EditorCurveBinding>>();
            foreach(var kvp in refAnims) {
                var anim = kvp.Key;
                var bindings = kvp.Value;

                materialSwapAnims[anim] = new List<EditorCurveBinding>();
                foreach (var binding in bindings) {
                    if (binding.propertyName.StartsWith("m_Materials.Array.data[")) {
                        materialSwapAnims[anim].Add(binding);
                    }
                }
            }

            int numMats = smr.sharedMaterials.Length;

            // generally, this should work similarly to mesh compatibility
            // any slot without material swaps can be merged to any other slot without material swaps
            // ie. if two slots with the same material both switch to the same material
            // in the same animation clips with the same curves, then they can be merged
            // if two slots with the same material switch to different materials, then they cannot be merged

            // that is complicated, so for now we will just merge slots without material swaps

            var oldMesh = smr.sharedMesh;

            List<int> slots = Enumerable.Range(0, numMats).ToList();
            List<List<int>> compatibilityGroups = new List<List<int>>();

            while (slots.Count > 0) {
                var slot = slots[0];
                slots.RemoveAt(0);

                // find a compatibility group for this slot
                Material m = smr.sharedMaterials[slot];

                List<int> compatibilityGroup = null;

                foreach (var group in compatibilityGroups) {
                    var compatible = true;
                    if (!group.All(s => smr.sharedMaterials[s] == m)) compatible = false;
                    // TODO: check animations
                    // for now, just check if there are any material swaps
                    if (materialSwapAnims.Any(kvp => kvp.Value.Any(b => b.propertyName.StartsWith($"m_Materials.Array.data[{slot}]")))) compatible = false;
                    // also check if there are any material swaps for in the compatibility group
                    if (group.Any(s => materialSwapAnims.Any(kvp => kvp.Value.Any(b => b.propertyName.StartsWith($"m_Materials.Array.data[{s}]"))))) compatible = false;

                    if (oldMesh.GetTopology(slot) != oldMesh.GetTopology(group[0])) compatible = false;

                    if (compatible) {
                        compatibilityGroup = group;
                        break;
                    }
                }

                if (compatibilityGroup == null) {
                    compatibilityGroup = new List<int>();
                    compatibilityGroup.Add(slot);
                    compatibilityGroups.Add(compatibilityGroup);
                } else {
                    compatibilityGroup.Add(slot);
                }
            }

            foreach (var group in compatibilityGroups) {
                Debug.Log($"Grouping slots {string.Join(", ", group)} in {smr.gameObject.name}");
            }

            var newMesh = Object.Instantiate(oldMesh);
            newMesh.subMeshCount = compatibilityGroups.Count;

            // now we have a list of compatibility groups
            for (int i=0; i<compatibilityGroups.Count; i++) {
                var indices = new List<int>();
                foreach (var slot in compatibilityGroups[i]) {
                    indices.AddRange(oldMesh.GetIndices(slot));

                    // rewrite the material swap animations
                    foreach (var kvp in materialSwapAnims) {
                        var anim = kvp.Key;
                        var bindings = kvp.Value;

                        foreach (var binding in bindings) {
                            if (binding.propertyName.StartsWith($"m_Materials.Array.data[{slot}]")) {
                                int targetBindingIndex = i;
                                var targetBindingPropertyName = $"m_Materials.Array.data[{targetBindingIndex}]";
                                var targetBinding = EditorCurveBinding.PPtrCurve(binding.path, binding.type, targetBindingPropertyName);
                                var sourceCurve = AnimationUtility.GetObjectReferenceCurve(anim, binding);
                                // erase the existing binding for ease of debugging
                                AnimationUtility.SetObjectReferenceCurve(anim, binding, null);
                                AnimationUtility.SetObjectReferenceCurve(anim, targetBinding, sourceCurve);
                            }
                        }
                    }
                }
                var topo = oldMesh.GetTopology(compatibilityGroups[i][0]);
                newMesh.SetIndices(indices.ToArray(), topo, i);
            }

            smr.sharedMesh = newMesh;
            smr.sharedMaterials = compatibilityGroups.Select(g => smr.sharedMaterials[g[0]]).ToArray();
        }

        [FeatureBuilderAction(FeatureOrder.BlendshapeOptimizer - 1)]
        public void Apply() {
            // two skinned mesh renderers are 'compatible' (mergeable) if:
            // - they must both be either active or inactive (both the local and hierarchy value)
            // - they share the same root bone
            // - they have the same anchor override
            // - blendshapes with the same name have the same values
            // - the following settings are identical
            //   - quality
            //   - update when offscreen
            //   - cast shadows
            //   - receive shadows
            //   - light probes
            //   - reflection probes
            //   - skinned motion vectors
            //   - dynamic occlusion
            // There are also animation requirements
            // - isActive animation tracks both objects must have identical keyframes within the same clip
            // - blendshape animations for both objects (where both objects have a blendshape with the same name) must have identical keyframes within the same clip
            // - animated material properties must all have the same default value (if it exists on both meshes)
            // - animated material properties (where the property exists on both meshes) must have identical keyframes within the same clip

            smrBaseValuesCompatibleCache.Clear();
            relevantAnimationClips = null;

            var allSMRs = avatarObject.GetComponentsInSelfAndChildren<SkinnedMeshRenderer>().ToList();

            // exclude any SMRs that are children of an animator (except the root animator)
            // this is because we can't yet merge SMRs that are children of an animator
            allSMRs.RemoveAll(s => s.GetComponentInParent<Animator>() != null && s.GetComponentInParent<Animator>() != avatarObject.GetComponent<Animator>());

            List<List<SkinnedMeshRenderer>> compatibleSMRGroups = new List<List<SkinnedMeshRenderer>>();

            while (allSMRs.Count > 0) {
                List<SkinnedMeshRenderer> meshGroup = new List<SkinnedMeshRenderer>();

                // add the first smr to the group
                meshGroup.Add(allSMRs.First());
                allSMRs.RemoveAt(0);

                foreach(var smr in allSMRs) {
                    if (meshGroup.All(s => AreSMRsCompatible(s, smr))) {
                        meshGroup.Add(smr);
                    }
                }

                allSMRs.RemoveAll(s => meshGroup.Contains(s));

                compatibleSMRGroups.Add(meshGroup);
            }

            foreach (var group in compatibleSMRGroups) {
                if (group.Count == 1) {
                    Debug.Log($"No meshes mergable to {group.First().gameObject.name}");
                    // but we can still merge the materials, therefore don't skip this loop
                }

                // usually, just pick the first mesh as the merge target
                var mergeTarget = group.First();
                // if there is a Body mesh, use that as the merge target
                var bodyMeshInGroup = group.FirstOrDefault(s => s.gameObject.name == "Body" && s.transform.parent == avatarObject.transform);
                if (bodyMeshInGroup != null) mergeTarget = bodyMeshInGroup;

                group.Remove(mergeTarget);

                // For every mesh in the group:
                // 1. Fix up the animations to point to the merge target
                // 2. Merge the meshes
                // 3. Delete the old renderer
                foreach (var smr in group) {
                    FixUpAnimations(mergeTarget, smr);
                    MergeSMRs(mergeTarget, smr);
                    GameObject.DestroyImmediate(smr);
                }

                // merge materials that don't have animations
                MergeSameMaterials(mergeTarget);
            }

        }

        private static void MergeSMRs(SkinnedMeshRenderer smr1, SkinnedMeshRenderer smr2) {
            // SMR1 is the one that will be kept
            // SMR2 is the one that will be destroyed
            // SMR1 will be modified to include the blendshapes of SMR2
            // SMR2 submeshes will be added to SMR1

            Mesh outMesh = new Mesh();
            outMesh.name = smr1.sharedMesh.name;

            Mesh m1 = smr1.sharedMesh;
            Mesh m2 = smr2.sharedMesh;

            // Mappings to go from the old mesh to the new mesh
            Dictionary<int,int> index1ToIndexNew = new Dictionary<int, int>();
            Dictionary<int,int> index2ToIndexNew = new Dictionary<int, int>();

            // Maps a transform to one of several bone indices in the new mesh.
            // Which one actually gets used is determined by which bindpose matches.
            Dictionary<Transform, List<(int,Matrix4x4)>> transformToIndexAndBindPose = new Dictionary<Transform, List<(int,Matrix4x4)>>();
            int numBonesNew = 0;

            // there is a cost to generate these, so do it once
            var m1BindPoses = m1.bindposes;
            var m2BindPoses = m2.bindposes;

            // Function that builds the mappings from old bone index to new bone index
            Action<Matrix4x4[], Transform[], Dictionary<int,int>> BuildIndices = (bindPoses, bones, indexToIndexNew) => {
                // loop through all the bones in the mesh (bindposes.length == bones.length)
                for (int i=0; i<bindPoses.Length; i++) {
                    var bindpose = bindPoses[i];
                    var transform = bones[i];

                    if (transform == null) {
                        // mesh probably horribly broken, bind this bone to bone 0 \_(o_o)_/
                        indexToIndexNew[i] = 0;
                        continue;
                    }

                    // try to find the transform in the big list, if not make a new list for it
                    if (!transformToIndexAndBindPose.ContainsKey(transform)) transformToIndexAndBindPose[transform] = new List<(int,Matrix4x4)>();

                    // default value to check for later
                    int newIndex = -1;

                    // try to find something in the list with an identical bindpose
                    foreach (var pair in transformToIndexAndBindPose[transform]) {
                        if (pair.Item2 == bindpose) {
                            newIndex = pair.Item1;
                            break;
                        }
                    }

                    // if no bones exist with a matching bindpose, add a new one
                    if (newIndex == -1) {
                        newIndex = numBonesNew++;
                        transformToIndexAndBindPose[transform].Add((newIndex, bindpose));
                    }
                    indexToIndexNew[i] = newIndex;
                }
            };

            // build the mappings for both meshes
            BuildIndices(m1BindPoses, smr1.bones, index1ToIndexNew);
            BuildIndices(m2BindPoses, smr2.bones, index2ToIndexNew);

            // build the bind pose array
            Matrix4x4[] bindPosesNew = new Matrix4x4[numBonesNew];
            // and the index to transform mapping to use for the renderer
            Dictionary<int, Transform> indexToTransformNew = new Dictionary<int, Transform>();
            foreach (var kvp in transformToIndexAndBindPose) {
                foreach (var pair in kvp.Value) {
                    bindPosesNew[pair.Item1] = pair.Item2;
                    indexToTransformNew[pair.Item1] = kvp.Key;
                }
            }

            // set the new bindposes to the mesh
            outMesh.bindposes = bindPosesNew;

            int len1 = m1.vertices.Length;
            int len2 = m2.vertices.Length;

            outMesh.vertices = m1.vertices.Concat(m2.vertices).ToArray();

            // create arrays of the correct length, or empty arrays if the input is empty
            T[] ArrayOrEmpty<T>(T[] uvin, int len) => uvin.Length == 0 ? new T[len] : uvin;
            T[] MakeArray<T>(T[] uv1, T[] uv2, int siz1, int siz2) => (uv1.Length + uv2.Length > 0) ? ArrayOrEmpty(uv1, siz1).Concat(ArrayOrEmpty(uv2, siz2)).ToArray() : new T[0];

            // fill out all the vertex attributes
            outMesh.uv = MakeArray(m1.uv, m2.uv, len1, len2);
            outMesh.uv2 = MakeArray(m1.uv2, m2.uv2, len1, len2);
            outMesh.uv3 = MakeArray(m1.uv3, m2.uv3, len1, len2);
            outMesh.uv4 = MakeArray(m1.uv4, m2.uv4, len1, len2);
            outMesh.uv5 = MakeArray(m1.uv5, m2.uv5, len1, len2);
            outMesh.uv6 = MakeArray(m1.uv6, m2.uv6, len1, len2);
            outMesh.uv7 = MakeArray(m1.uv7, m2.uv7, len1, len2);
            outMesh.uv8 = MakeArray(m1.uv8, m2.uv8, len1, len2);
            outMesh.normals = MakeArray(m1.normals, m2.normals, len1, len2);
            outMesh.tangents = MakeArray(m1.tangents, m2.tangents, len1, len2);
            outMesh.colors = MakeArray(m1.colors, m2.colors, len1, len2);
            
            // create an initial bone weights array
            BoneWeight[] boneWeights1 = m1.boneWeights;
            BoneWeight[] boneWeights2 = m2.boneWeights;
            BoneWeight[] boneWeights = new BoneWeight[len1 + len2];

            // remap the bone weight indices to the new bone indices
            for (int i = 0; i < len1; i++) {
                boneWeights[i] = boneWeights1[i];
                boneWeights[i].boneIndex0 = index1ToIndexNew[boneWeights1[i].boneIndex0];
                boneWeights[i].boneIndex1 = index1ToIndexNew[boneWeights1[i].boneIndex1];
                boneWeights[i].boneIndex2 = index1ToIndexNew[boneWeights1[i].boneIndex2];
                boneWeights[i].boneIndex3 = index1ToIndexNew[boneWeights1[i].boneIndex3];
            }
            for (int i = 0; i < len2; i++) {
                boneWeights[i+len1] = boneWeights2[i];
                boneWeights[i+len1].boneIndex0 = index2ToIndexNew[boneWeights2[i].boneIndex0];
                boneWeights[i+len1].boneIndex1 = index2ToIndexNew[boneWeights2[i].boneIndex1];
                boneWeights[i+len1].boneIndex2 = index2ToIndexNew[boneWeights2[i].boneIndex2];
                boneWeights[i+len1].boneIndex3 = index2ToIndexNew[boneWeights2[i].boneIndex3];
            }

            // assign it back to the mesh
            outMesh.boneWeights = boneWeights;

            // deal with submeshes, this effectively sets the indices
            outMesh.subMeshCount = m1.subMeshCount + m2.subMeshCount;
            for (int i = 0; i < m1.subMeshCount; i++) {
                outMesh.SetIndices(m1.GetIndices(i), m1.GetTopology(i), i);
            }
            for (int i = 0; i < m2.subMeshCount; i++) {
                outMesh.SetIndices(m2.GetIndices(i).Select(x => x + len1).ToArray(), m2.GetTopology(i), i + m1.subMeshCount);
            }

            // deal with blendshapes
            // map from blendshape name to a list of tuples of (weight, delta vertices, delta normals, delta tangents)
            var blendShapeMap = new Dictionary<string, List<(float, Vector3[], Vector3[], Vector3[])>>();

            // create a set of all the blendshape names
            HashSet<string> blendShapeNames = new HashSet<string>();
            Enumerable.Range(0, m1.blendShapeCount).Select(i => m1.GetBlendShapeName(i)).ToList().ForEach(x => blendShapeNames.Add(x));
            Enumerable.Range(0, m2.blendShapeCount).Select(i => m2.GetBlendShapeName(i)).ToList().ForEach(x => blendShapeNames.Add(x));

            foreach (var name in blendShapeNames) {
                // get the index of the blendshape in each mesh
                // if it doesn't exist in one of them, it will be -1
                int idx1 = m1.GetBlendShapeIndex(name);
                int idx2 = m2.GetBlendShapeIndex(name);

                // the exceptions below should never happen, but they're here just in case
                
                // check the number of frames are the same if it exists in both meshes
                if (idx1 != -1 && idx2 != -1 && m1.GetBlendShapeFrameCount(idx1) != m2.GetBlendShapeFrameCount(idx2)) {
                    throw new Exception($"Blendshape {name} has different number of frames in {smr1.gameObject.name} and {smr2.gameObject.name}");
                }

                // create a new list for this blendshape
                blendShapeMap[name] = new List<(float, Vector3[], Vector3[], Vector3[])>();

                int numFrames = idx1 != -1 ? m1.GetBlendShapeFrameCount(idx1) : m2.GetBlendShapeFrameCount(idx2);
                
                // for every frame in the blendshape
                // grab deltas from both meshes, concatenate them and add a frame to the list
                for (int i = 0; i < numFrames; i++) {
                    if (idx1 != -1 && idx2 != -1 && m1.GetBlendShapeFrameWeight(idx1, i) != m2.GetBlendShapeFrameWeight(idx2, i)) {
                        throw new Exception($"Blendshape {name} has different weights in {smr1.gameObject.name} and {smr2.gameObject.name}");
                    }
                    float weight = idx1 != -1 ? m1.GetBlendShapeFrameWeight(idx1, i) : m2.GetBlendShapeFrameWeight(idx2, i);
                    Vector3[] deltaVertices1 = new Vector3[len1];
                    Vector3[] deltaVertices2 = new Vector3[len2];
                    Vector3[] deltaNormals1 = new Vector3[len1];
                    Vector3[] deltaNormals2 = new Vector3[len2];
                    Vector3[] deltaTangents1 = new Vector3[len1];
                    Vector3[] deltaTangents2 = new Vector3[len2];
                    if (idx1 != -1) m1.GetBlendShapeFrameVertices(idx1, i, deltaVertices1, deltaNormals1, deltaTangents1);
                    if (idx2 != -1) m2.GetBlendShapeFrameVertices(idx2, i, deltaVertices2, deltaNormals2, deltaTangents2);

                    // now concat the two sets of deltas
                    Vector3[] deltaVertices = deltaVertices1.Concat(deltaVertices2).ToArray();
                    Vector3[] deltaNormals = deltaNormals1.Concat(deltaNormals2).ToArray();
                    Vector3[] deltaTangents = deltaTangents1.Concat(deltaTangents2).ToArray();
                    var tup = (weight, deltaVertices, deltaNormals, deltaTangents);

                    blendShapeMap[name].Add(tup);
                }
            }

            // now have all the blendshapes, add them to the new mesh

            foreach (var bsinfo in blendShapeMap) {
                string name = bsinfo.Key;
                List<(float, Vector3[], Vector3[], Vector3[])> frames = bsinfo.Value;

                for (int i=0; i<frames.Count; i++) {
                    var frame = frames[i];
                    outMesh.AddBlendShapeFrame(name, frame.Item1, frame.Item2, frame.Item3, frame.Item4);
                }
            }

            // do I really need to do this?
            outMesh.RecalculateBounds();

            // the renderer needs a list of bones and their corresponding transforms
            var SMRBones = new Transform[numBonesNew];
            foreach (var kvp in indexToTransformNew) SMRBones[kvp.Key] = kvp.Value;

            // update values to the target SkinnedMeshRenderer
            smr1.sharedMesh = outMesh;
            smr1.bones = SMRBones;
            smr1.rootBone = smr1.rootBone;

            // create some bounds that encapsulate both meshes
            var newBounds = new Bounds(smr1.rootBone.position, Vector3.zero);
            newBounds.Encapsulate(smr1.bounds);
            newBounds.Encapsulate(smr2.bounds);
            smr1.localBounds = new Bounds(Vector3.zero, newBounds.size);

            smr1.sharedMaterials = smr1.sharedMaterials.Concat(smr2.sharedMaterials).ToArray();

            // copy blendshape values to target
            Dictionary<string, float> GetBSValues(SkinnedMeshRenderer smr) => Enumerable.Range(0, smr.sharedMesh.blendShapeCount)
                .Select(i => (idx: i, name: smr.sharedMesh.GetBlendShapeName(i)))
                .ToDictionary(tup => tup.name, tup => smr.GetBlendShapeWeight(tup.idx));
            
            var bsValues = GetBSValues(smr1).Concat(GetBSValues(smr2));

            foreach (var kvp in bsValues) {
                smr1.SetBlendShapeWeight(outMesh.GetBlendShapeIndex(kvp.Key), kvp.Value);
            }
        }

        [MenuItem("Tools/Test Combine Meshes")]
        public static void TestCombineMeshes() {
            // get all selected objects
            var objs = Selection.gameObjects;
            if (objs.Length < 2 || objs.Any(x => x.GetComponent<SkinnedMeshRenderer>() == null)) {
                Debug.LogError("Select skinnedmeshrenderers only");
                return;
            }

            var targetGameObject = objs[0];
            // if there is a Body mesh, use that as the merge target
            var bodyMeshInGroup = objs.FirstOrDefault(o => o.name == "Body");
            if (bodyMeshInGroup != null) targetGameObject = bodyMeshInGroup;

            objs = objs.ToList().Except(new[] { targetGameObject }).ToArray();

            var go = Object.Instantiate(targetGameObject);
            var outSMR = go.GetComponent<SkinnedMeshRenderer>();

            for (int i=0; i<objs.Length; i++) {
                MergeSMRs(outSMR, objs[i].GetComponent<SkinnedMeshRenderer>());
            }
        }
    }
}