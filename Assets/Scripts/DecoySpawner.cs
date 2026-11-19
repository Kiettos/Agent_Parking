using System.Collections.Generic;
using UnityEngine;

public class DecoySpawner : MonoBehaviour
{
    [Header("Xe mồi: kéo 1-4 prefab xe vào đây")]
    public GameObject[] decoyPrefabs;

    [Header("Màu sơn xịt tự động (mặc định là các tông xanh dương)")]
    public bool autoPaint = true;
    public Color[] paintColors = new Color[]
    {
        new Color(0.10f, 0.45f, 0.95f), // Xanh dương tươi
        new Color(0.05f, 0.20f, 0.55f), // Xanh dương đậm (Navy)
        new Color(0.20f, 0.65f, 1.00f), // Xanh dương nhạt (Sky blue)
    };

    [Header("Tự động nhận diện ô đỗ 'P' từ JsonMapBuilder")]
    public bool autoUseParkingTiles = true;

    [Header("Xác suất lấp ô đỗ")]
    [Range(0f, 1f)] public float fillChance = 0.5f;
    public int alwaysFillNearest = 2;
    public float minTargetClearance = 2.0f;

    [Header("Biến thiên ngẫu nhiên")]
    public float positionJitter = 0.15f;
    public float yawJitter = 4f;
    public Vector2 scaleRange = new Vector2(0.95f, 1.08f);
    public float yOffset = 0.05f;

    [Header("Tag gán cho collider của xe mồi")]
    public string obstacleTag = "Obstacle";

    readonly List<Transform> slotTransforms = new List<Transform>();
    Transform root;
    readonly List<Stack<GameObject>> pools = new List<Stack<GameObject>>();
    readonly List<GameObject> activeGo = new List<GameObject>();
    readonly List<int> activeType = new List<int>();
    MaterialPropertyBlock propBlock;

    void Start()
    {
        if (activeGo.Count == 0)
        {
            var area = GetComponent<TrainingArea>();
            Vector3 tPos = (area != null && area.slot != null) ? area.slot.transform.position : transform.position;
            Respawn(tPos);
        }
    }

    void RefreshMarkers()
    {
        slotTransforms.Clear();

        var markers = GetComponentsInChildren<DecoyMarker>(true);
        for (int i = 0; i < markers.Length; i++)
            slotTransforms.Add(markers[i].transform);

        if (autoUseParkingTiles)
        {
            var allChildren = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allChildren.Length; i++)
            {
                if (allChildren[i].name.EndsWith("_P") && !slotTransforms.Contains(allChildren[i]))
                    slotTransforms.Add(allChildren[i]);
            }
        }

        while (pools.Count < decoyPrefabs.Length)
            pools.Add(new Stack<GameObject>());
    }

    public void Respawn(Vector3 targetPos)
    {
        if (decoyPrefabs == null || decoyPrefabs.Length == 0) return;

        if (slotTransforms.Count == 0)
            RefreshMarkers();

        for (int i = 0; i < activeGo.Count; i++)
        {
            activeGo[i].SetActive(false);
            pools[activeType[i]].Push(activeGo[i]);
        }
        activeGo.Clear();
        activeType.Clear();

        if (slotTransforms.Count == 0) return;

        slotTransforms.Sort((a, b) =>
            (a.position - targetPos).sqrMagnitude.CompareTo((b.position - targetPos).sqrMagnitude));

        float minClearanceSqr = minTargetClearance * minTargetClearance;
        int validSlotIndex = 0;

        for (int i = 0; i < slotTransforms.Count; i++)
        {
            if ((slotTransforms[i].position - targetPos).sqrMagnitude < minClearanceSqr)
                continue;

            bool fill = validSlotIndex < alwaysFillNearest || Random.value < fillChance;
            validSlotIndex++;

            if (!fill) continue;

            int t = Random.Range(0, decoyPrefabs.Length);
            if (decoyPrefabs[t] == null) continue;

            GameObject go = Get(t);
            Place(go, slotTransforms[i], t);
            activeGo.Add(go);
            activeType.Add(t);
        }
    }

    GameObject Get(int t)
    {
        if (root == null)
        {
            var r = new GameObject("Decoys");
            r.transform.SetParent(transform, false);
            root = r.transform;
        }

        GameObject go;
        if (pools[t].Count > 0) go = pools[t].Pop();
        else
        {
            go = Instantiate(decoyPrefabs[t], root);
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                c.gameObject.tag = obstacleTag;
        }
        go.SetActive(true);
        return go;
    }

    void Place(GameObject go, Transform marker, int t)
    {
        Vector3 pos = marker.position
                      + marker.right * Random.Range(-positionJitter, positionJitter)
                      + marker.forward * Random.Range(-positionJitter, positionJitter)
                      + Vector3.up * yOffset;
        Quaternion rot = marker.rotation * Quaternion.Euler(0f, Random.Range(-yawJitter, yawJitter), 0f);

        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = decoyPrefabs[t].transform.localScale * Random.Range(scaleRange.x, scaleRange.y);

        if (autoPaint && paintColors != null && paintColors.Length > 0)
            ApplyPaint(go, paintColors[Random.Range(0, paintColors.Length)]);
    }

    void ApplyPaint(GameObject go, Color color)
    {
        if (propBlock == null) propBlock = new MaterialPropertyBlock();
        propBlock.SetColor("_BaseColor", color); // Hỗ trợ URP
        propBlock.SetColor("_Color", color);     // Hỗ trợ Built-in Standard Shader

        var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            // Bỏ qua nếu tên mesh là bánh xe hoặc kính (nếu model có tách tên)
            string lowerName = renderers[i].name.ToLower();
            if (lowerName.Contains("wheel") || lowerName.Contains("tire") || lowerName.Contains("glass"))
                continue;

            renderers[i].SetPropertyBlock(propBlock);
        }
    }
}
