using UnityEngine;

// Camera cho việc quay video train. Hai chế độ:
//   Overview = nhìn toàn bộ bãi đỗ (camera tĩnh, tự canh vừa map)
//   Follow   = bám theo xe (zoom gần, thấy rõ góc bánh)
// Gắn vào Main Camera.
public class FollowCamera : MonoBehaviour
{
    public enum Mode { Overview, Follow }

    [Header("Chế độ")]
    public Mode mode = Mode.Overview;

    [Header("Chung")]
    [Tooltip("Góc nghiêng xuống. 90 = nhìn thẳng từ trên xuống (dễ thấy toạ độ, khó thấy hình khối)")]
    [Range(30f, 90f)] public float pitch = 62f;
    [Tooltip("1 = vừa khít map. Nhỏ hơn 1 = zoom gần hơn, lớn hơn 1 = zoom xa hơn")]
    [Range(0.4f, 2f)] public float zoom = 1f;

    [Header("Overview")]
    [Tooltip("Tên object gốc của map trong Hierarchy")]
    public string mapObjectName = "Map";

    [Header("Follow (chỉ dùng khi mode = Follow)")]
    public Transform target;
    public float height = 20f;
    public float back = 10f;
    [Tooltip("Càng lớn camera càng bám sát, 4-8 là hợp lý")]
    public float sharpness = 6f;

    Camera cam;
    Vector3 overviewPos;
    Quaternion overviewRot;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        if (mode == Mode.Overview) FitOverview();
        else if (target == null) AutoFindTarget();
    }

    // Canh camera nhìn trọn map, giữ nguyên góc nghiêng pitch
    void FitOverview()
    {
        Bounds b = GetMapBounds();
        Vector3 center = b.center;

        // Bán kính bao quanh map (nửa đường chéo)
        float radius = b.extents.magnitude;

        float fovRad = (cam != null ? cam.fieldOfView : 60f) * Mathf.Deg2Rad;
        float distance = radius / Mathf.Tan(fovRad * 0.5f) * zoom;

        // Đặt camera lùi theo hướng nghiêng, nhìn vào tâm map
        Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
        Vector3 dir = rot * Vector3.back;   // hướng từ tâm ra sau
        overviewPos = center + dir * distance;
        overviewRot = rot;

        transform.SetPositionAndRotation(overviewPos, overviewRot);
        Debug.Log($"[FollowCamera] Overview: tâm={center}, bán kính={radius:F1} m, " +
                  $"khoảng cách={distance:F1} m, pitch={pitch}");
    }

    Bounds GetMapBounds()
    {
        // Ưu tiên JsonMapBuilder vì nó là gốc của mọi tile
        var builder = FindFirstObjectByType<JsonMapBuilder>();
        Transform root = builder != null ? builder.transform : null;

        if (root == null)
        {
            var go = GameObject.Find(mapObjectName);
            if (go != null) root = go.transform;
        }

        if (root == null)
        {
            // Fallback: dùng toạ độ các ô đã biết
            if (builder != null && builder.parkingCells.Count > 0)
            {
                Bounds bb = new Bounds(builder.transform.TransformPoint(builder.parkingCells[0]), Vector3.one * 20f);
                foreach (var c in builder.parkingCells)
                    bb.Encapsulate(builder.transform.TransformPoint(c));
                foreach (var c in builder.roadCells)
                    bb.Encapsulate(builder.transform.TransformPoint(c));
                bb.Expand(6f);   // cộng thêm 1 ô
                return bb;
            }
            return new Bounds(Vector3.zero, Vector3.one * 60f);
        }

        // Gom bounds của mọi renderer con
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.position, Vector3.one * 60f);

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    void AutoFindTarget()
    {
        var go = GameObject.Find("CarAgent");
        if (go == null) return;

        var agent = go.GetComponentInChildren<ParkingAgent>(true);
        target = agent != null ? agent.transform : go.transform;
    }

    void LateUpdate()
    {
        if (mode == Mode.Overview) return;   // camera tĩnh

        if (target == null)
        {
            AutoFindTarget();
            if (target == null) return;
        }

        Vector3 desired = target.position + new Vector3(0f, height, -back);
        float k = 1f - Mathf.Exp(-sharpness * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, desired, k);
        transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // Bấm chuột phải vào component trong Inspector để canh lại
    [ContextMenu("Fit overview now")]
    public void RefitOverview()
    {
        if (cam == null) cam = Camera.main;
        FitOverview();
    }
}
