using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

public class MeshDeleter : EditorWindow
{
    [MenuItem("Tools/MeshDeleter")]
    public static void ShowWindow() {
        GetWindow<MeshDeleter>("Mesh Deleter");
    }

    private SkinnedMeshRenderer smr;
    private Texture2D mask;
    private string saveLocation = "Assets/Generated/";

    private void OnGUI() {
        // object field
        smr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Skinned Mesh Renderer", smr, typeof(SkinnedMeshRenderer), true);
        // texture field
        mask = (Texture2D)EditorGUILayout.ObjectField("Mask", mask, typeof(Texture2D), false);
        // save location
        saveLocation = EditorGUILayout.TextField("Save Location", saveLocation);

        if (smr == null || mask == null) {
            EditorGUILayout.HelpBox("Please select a Skinned Mesh Renderer and a Mask texture.", MessageType.Error);
            return;
        }

        if (!mask.isReadable) {
            EditorGUILayout.HelpBox("Mask Texture is not marked as read/write.", MessageType.Error);
            return;
        }

        if (GUILayout.Button("Mask Mesh")) {
            Mesh mesh = Instantiate(smr.sharedMesh);

            int numSubmeshes = mesh.subMeshCount;

            if (!mask.isReadable) {

            }

            for (int sm=0; sm<numSubmeshes; sm++) {
                List<int> newIndices = new List<int>();
                int[] indices = mesh.GetTriangles(sm);

                for (int i=0; i<indices.Length; i+=3) {
                    int i0 = indices[i];
                    int i1 = indices[i+1];
                    int i2 = indices[i+2];

                    Vector2 uv0 = mesh.uv[i0];
                    Vector2 uv1 = mesh.uv[i1];
                    Vector2 uv2 = mesh.uv[i2];

                    // get the centre of the uv triangle
                    Vector2 centre = (uv0 + uv1 + uv2) / 3f;

                    // sample the mask at the centre of the triangle
                    Color maskColor = mask.GetPixelBilinear(centre.x, centre.y);

                    // if the mask is black, add the triangle to the new indices
                    if (maskColor.grayscale < 0.5f) {
                        newIndices.Add(i0);
                        newIndices.Add(i1);
                        newIndices.Add(i2);
                    }
                }

                mesh.SetTriangles(newIndices, sm);
            }

            // save the mesh
            // first ensure the folder exists
            System.IO.Directory.CreateDirectory(saveLocation);
            string path = saveLocation + "/" + smr.name + "_masked";
            // if path exists already, add a number to the end
            int counter = 1;
            while (System.IO.File.Exists(path + ".asset")) {
                path = saveLocation + "/" + smr.name + "_masked" + counter;
                counter++;
            }
            // then save the mesh
            AssetDatabase.CreateAsset(mesh, path + ".asset");

            smr.sharedMesh = mesh;
        }
    }
}
