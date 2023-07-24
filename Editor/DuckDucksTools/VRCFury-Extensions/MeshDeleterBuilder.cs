using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

using VF.Inspector;
using VF.Feature.Base;
using VF.Model.Feature;
using UnityEngine.UIElements;

using DuckDuckRendererExtension;

namespace VF.Model.Feature {
    [Serializable]
    public class MeshDeleter : NewFeatureModel {
        #region FeatureModel
        // target of the deleter
        public Renderer target;
        // submesh to delete from
        public int subMesh = 0;
        // texture to sample
        public Texture2D mask;
        // positive mask or negative mask
        public DeleterMode mode = DeleterMode.DeleteWhite;

        // platform selection
        public PlatformSelection platformSelection = PlatformSelection.AllPlatforms;

        public enum PlatformSelection {
            AllPlatforms,
            DesktopOnly,
            QuestOnly
        }

        public enum DeleterMode {
            [InspectorName("Delete White Areas")]
            DeleteWhite,
            [InspectorName("Delete Black Areas")]
            DeleteBlack
        }
        #endregion
    }
}

namespace VF.Feature {
    public class MeshDeleterBuilder : FeatureBuilder<MeshDeleter> {

        #region Editor
        public override string GetEditorTitle() {
            return "Duck's Tools/Mesh Deleter";
        }

        public override bool AvailableOnProps() {
            return true;
        }

        public override VisualElement CreateEditor(SerializedProperty prop) {
            var content = new VisualElement();

            content.Add(VRCFuryEditorUtils.Info(
                "Deletes parts of a mesh based on a texture mask. " +
                "The texture is sampled in greyscale at the center of each triangle. "
            ));

            content.Add(VRCFuryEditorUtils.Prop(
                prop.FindPropertyRelative("platformSelection"),
                "Platform: "
            ));

            content.Add(VRCFuryEditorUtils.Prop(
                prop.FindPropertyRelative("target"),
                "Target: "
            ));

            content.Add(VRCFuryEditorUtils.Prop(
                prop.FindPropertyRelative("subMesh"),
                "SubMesh: "
            ));

            content.Add(VRCFuryEditorUtils.Prop(
                prop.FindPropertyRelative("mode"),
                "Mode: "
            ));

            content.Add(VRCFuryEditorUtils.Prop(
                prop.FindPropertyRelative("mask"),
                "Mask: "
            ));

            // the warnings need to update if the mask changes
            content.Add(VRCFuryEditorUtils.RefreshOnChange(() => {
                var innerContent = new VisualElement();
                OfferToFixTexture(innerContent, prop.FindPropertyRelative("mask"));
                return innerContent;
            }, prop.FindPropertyRelative("mask")));

            return content;
        }
        #endregion

        #region Texture Fix
        enum WarningType {
            Error,
            Warning,
            Info
        }

        private static void ShowWarningWithFix(
            VisualElement content,
            string warningText,
            string buttonText,
            Action fixAction,
            WarningType type = WarningType.Warning
        ) {
            Func<string,VisualElement> warningFunc;

            switch (type) {
                case WarningType.Error:
                    warningFunc = VRCFuryEditorUtils.Error;
                    break;
                case WarningType.Warning:
                    warningFunc = VRCFuryEditorUtils.Warn;
                    break;
                default:
                    warningFunc = VRCFuryEditorUtils.Info;
                    break;
            }

            // set up a horizontal flex with a button on the right
            // the warning section should expand to fill the space
            // the button should be full-height with the warning

            //  ---------------------------   ---------
            // | Warning Text              | | Button  |
            //  ---------------------------   ---------


            var contentHorizontal = new VisualElement();
            contentHorizontal.style.flexDirection = FlexDirection.Row;
            contentHorizontal.style.alignItems = Align.Stretch;
            contentHorizontal.style.marginTop = 5;

            VisualElement warningTextElement = warningFunc(warningText);

            warningTextElement.style.flexGrow = 1;
            warningTextElement.style.flexShrink = 1;
            warningTextElement.style.marginRight = 5;
            warningTextElement.style.marginTop = 0;

            contentHorizontal.Add(warningTextElement);

            contentHorizontal.Add(new Button(fixAction) {
                text = buttonText
            });

            content.Add(contentHorizontal);
        }

        public void OfferToFixTexture(VisualElement content, SerializedProperty tex) {
            if (tex.objectReferenceValue == null) {
                return;
            }

            // need to get the importer for the texture
            var path = AssetDatabase.GetAssetPath(tex.objectReferenceValue);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            // if the importer is null, something is probably very wrong
            if (importer == null) {
                throw new Exception("Could not get texture importer for " + path);
            }
            
            // this will be populated by RefreshOnTrigger
            Action triggerRefresh = null;
            
            // we need to refresh these warnings when someone clicks the button
            content.Add(VRCFuryEditorUtils.RefreshOnTrigger(() => {
                VisualElement innerContent = new VisualElement();
                // non-readable texture is a serious problem
                if (!importer.isReadable) {
                    ShowWarningWithFix(
                        innerContent,
                        "Mask texture is not marked read/write. " +
                        "This is required for the deleter to work.",
                        "Auto Fix",
                        () => {
                            importer.isReadable = true;
                            importer.SaveAndReimport();
                            triggerRefresh();
                        },
                        WarningType.Error
                    );
                }

                // non-ideal import settings are a warning
                if (
                    importer.textureCompression != TextureImporterCompression.Uncompressed ||
                    importer.maxTextureSize < 4096 ||
                    importer.sRGBTexture == true ||
                    importer.alphaSource != TextureImporterAlphaSource.None
                ) {
                    ShowWarningWithFix(
                        innerContent,
                        "Mask texture import settings are not optimal.",
                        "Auto Fix",
                        () => {
                            importer.textureCompression = TextureImporterCompression.Uncompressed;
                            importer.maxTextureSize = 4096;
                            importer.sRGBTexture = false;
                            importer.alphaSource = TextureImporterAlphaSource.None;
                            importer.SaveAndReimport();
                            triggerRefresh();
                        }
                    );
                }

                return innerContent;
            }, tex.serializedObject, out triggerRefresh));
        }
        #endregion

        #region Apply
        [FeatureBuilderAction(FeatureOrder.Default)]
        public void Apply() {
            // can't work without a target
            if (model.target == null) {
                return;
            }

            // check platform
            bool isQuestBuild = (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android);
            bool isDesktopBuild = !isQuestBuild;
            bool applyForQuest = (model.platformSelection != MeshDeleter.PlatformSelection.DesktopOnly);
            bool applyForDesktop = (model.platformSelection != MeshDeleter.PlatformSelection.QuestOnly);

            // Do not apply we are not building for the desired platform
            if (isQuestBuild && !applyForQuest || isDesktopBuild && !applyForDesktop) {
                return;
            }

            // original mesh, don't modify this
            Mesh mesh = model.target.GetMesh();

            #region Sanity Checks
            // null mask -> what is the user doing?
            if (model.mask == null) {
                return;
            }

            // not readable -> the user didn't listen to instructions
            if (!model.mask.isReadable) {
                throw new Exception("Mask texture is not marked readable.");
            }

            // no mesh -> the user has done something weird
            if (mesh == null) {
                throw new Exception("Target has no mesh.");
            }

            // submesh out of bounds -> the user isn't paying attention
            if (model.subMesh >= mesh.subMeshCount) {
                throw new Exception("SubMesh index out of bounds.");
            }
            #endregion

            // don't touch the original mesh
            mesh = mutableManager.MakeMutable(mesh);

            // list to put new indices in when we determine that
            // a specific tri needs to stay
            List<int> newIndices = new List<int>();

            // get the indices for the submesh we're working on
            int[] indices = mesh.GetTriangles(model.subMesh);
            
            // indices are in groups of 3, so we can iterate over them in 3s
            for (int i=0; i<indices.Length; i+=3) {
                int i0 = indices[i];
                int i1 = indices[i+1];
                int i2 = indices[i+2];

                // TODO: add an option to use a different UV channel
                Vector2 uv0 = mesh.uv[i0];
                Vector2 uv1 = mesh.uv[i1];
                Vector2 uv2 = mesh.uv[i2];

                // get the centre of the uv triangle
                Vector2 centre = (uv0 + uv1 + uv2) / 3f;

                // sample the mask at the centre of the triangle
                Color maskColor = model.mask.GetPixelBilinear(centre.x, centre.y);

                // if the mask is white, skip adding the triangle
                // invert the operation if we're in delete black mode
                if (
                    (maskColor.grayscale < 0.5f) && (model.mode == MeshDeleter.DeleterMode.DeleteWhite) ||
                    (maskColor.grayscale > 0.5f) && (model.mode == MeshDeleter.DeleterMode.DeleteBlack)
                ) {
                    newIndices.Add(i0);
                    newIndices.Add(i1);
                    newIndices.Add(i2);
                }
            }

            mesh.SetTriangles(newIndices, model.subMesh);

            // assign the new mesh to the target
            model.target.SetMesh(mesh);

            //TODO: is this required?
            // what does it do?
            // why does other parts of vrcf use it?
            VRCFuryEditorUtils.MarkDirty(mesh);
            VRCFuryEditorUtils.MarkDirty(model.target);
        }
        #endregion
    }
}

#region Helper Extensions
// helper extension function for getting and setting the mesh on a renderer
// since there are multiple types of renderer, and they all do it differently
namespace DuckDuckRendererExtension {
    public static class RendererExtensions {
        public static Mesh GetMesh(this Renderer renderer) {
            if (renderer is MeshRenderer mr) {
                return mr.GetComponent<MeshFilter>().sharedMesh;
            } else if (renderer is SkinnedMeshRenderer smr) {
                return smr.sharedMesh;
            } else if (renderer is ParticleSystemRenderer psr) {
                return psr.mesh;
            } else {
                throw new Exception("Renderer is not a supported type.");
            }
        }

        public static void SetMesh(this Renderer renderer, Mesh mesh) {
            if (renderer is MeshRenderer mr) {
                mr.GetComponent<MeshFilter>().sharedMesh = mesh;
            } else if (renderer is SkinnedMeshRenderer smr) {
                smr.sharedMesh = mesh;
            } else if (renderer is ParticleSystemRenderer psr) {
                psr.mesh = mesh;
            } else {
                throw new Exception("Renderer is not a supported type.");
            }
        }
    }
}
#endregion