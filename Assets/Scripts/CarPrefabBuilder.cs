using System.IO;
using UnityEditor;
using UnityEngine;

// Menu: Tools > Build CarAgent Prefab
// Builds Assets/Prefabs/CarAgent.prefab from LowPolyCar_Body.obj and LowPolyWheel.obj.
public static class CarPrefabBuilder
{
    // Numbers measured from the two .obj files
    const float WheelX = 0.72f;          // wheel centre, left/right of the car centre
    const float WheelZ = 1.35f;          // wheel centre, front/back (middle of the wheel arch)
    const float WheelRadius = 0.38f;     // tire radius; wheel centre height = radius (wheel touches y = 0)
    const float SuspDistance = 0.2f;

    const string PrefabFolder = "Assets/Prefabs";
    const string PrefabPath = PrefabFolder + "/CarAgent.prefab";

    [MenuItem("Tools/Build CarAgent Prefab")]
    public static void Build()
    {
        GameObject bodyModel = FindModel("LowPolyCar_Body");
        GameObject wheelModel = FindModel("LowPolyWheel");
        if (bodyModel == null || wheelModel == null)
        {
            EditorUtility.DisplayDialog("CarAgent",
                "Cannot find LowPolyCar_Body.obj and/or LowPolyWheel.obj in the project.\n" +
                "Drag both files into the Assets folder first, then run this again.", "OK");
            return;
        }

        // Root: Rigidbody + one box collider around the body
        GameObject root = new GameObject("CarAgent");
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.mass = 1200f;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.865f, 0f);
        box.size = new Vector3(1.84f, 1.17f, 4.36f);

        // Visible body
        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(bodyModel, root.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;

        // Parents
        Transform colParent = new GameObject("WheelColliders").transform;
        colParent.SetParent(root.transform, false);
        Transform meshParent = new GameObject("WheelMeshes").transform;
        meshParent.SetParent(root.transform, false);

        string[] names = { "FL", "FR", "RL", "RR" };
        float[] sideX = { -1f, 1f, -1f, 1f };
        float[] sideZ = { 1f, 1f, -1f, -1f };
        WheelCollider[] cols = new WheelCollider[4];
        Transform[] meshes = new Transform[4];

        for (int i = 0; i < 4; i++)
        {
            float x = sideX[i] * WheelX;
            float z = sideZ[i] * WheelZ;

            // Wheel collider: its origin sits half the suspension travel above the wheel centre
            GameObject c = new GameObject(names[i]);
            c.transform.SetParent(colParent, false);
            c.transform.localPosition = new Vector3(x, WheelRadius + SuspDistance * 0.5f, z);
            WheelCollider wc = c.AddComponent<WheelCollider>();
            wc.radius = WheelRadius;
            wc.mass = 20f;
            wc.wheelDampingRate = 0.25f;
            wc.suspensionDistance = SuspDistance;
            JointSpring spring = wc.suspensionSpring;
            spring.spring = 35000f;
            spring.damper = 4500f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;
            cols[i] = wc;

            // Visible wheel
            GameObject m = (GameObject)PrefabUtility.InstantiatePrefab(wheelModel, meshParent);
            m.name = names[i] + "_Mesh";
            m.transform.localPosition = new Vector3(x, WheelRadius, z);
            m.transform.localRotation = Quaternion.identity;
            meshes[i] = m.transform;
        }

        CarWheels cw = root.AddComponent<CarWheels>();
        cw.colliders = cols;
        cw.meshes = meshes;

        ManualDrive md = root.AddComponent<ManualDrive>();
        md.fl = cols[0];
        md.fr = cols[1];
        md.rl = cols[2];
        md.rr = cols[3];

        // Save as prefab
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);
        Debug.Log("Built " + PrefabPath);
    }

    static GameObject FindModel(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == name && path.ToLower().EndsWith(".obj"))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }
}
