using System.Collections.Generic;
using UnityEngine;

public class TrainingArea : MonoBehaviour
{
    [Header("Map (JsonMapBuilder nằm trên object con 'Map', KHÔNG phải trên Car hay ParkingSlot)")]
    public JsonMapBuilder builder;

    [Header("Xe mồi (tự động lấp ngẫu nhiên các vị trí DecoyMarker trên map)")]
    public DecoySpawner decoys;

    [Header("Ô đỗ (tự đặt vào vị trí T của map)")]
    public ParkingSlot slot;
    public Vector3 slotSize = new Vector3(2.6f, 1f, 4.8f);
    public float slotYaw = 0f;

    [Header("Điểm xuất phát (tự đặt vào vị trí S của map)")]
    public Transform spawnPoint;
    public float spawnYaw = 0f;
    public float spawnHeight = 0.3f;
    public float spawnRadius = 3f;
    public float spawnYawRange = 30f;

    [Header("Sinh ngẫu nhiên mỗi episode (thay cho S/T cố định)")]
    public bool randomizeEveryEpisode = true;
    public float minSpawnDistance = 8f;    // m, không sinh quá gần đích
    public float maxSpawnDistance = 25f;   // m, phải < ParkingAgent.maxDistance (40)

    [Header("Giữ đích trong K episode (học xong mới đổi chỗ đỗ)")]
    public int episodesPerTarget = 50;     // K

    bool placed;

    // --- Trạng thái của block K episode hiện tại ---
    int episodesAtTarget;
    Vector3 currentTargetCell;              // toạ độ local (trong Map) của ô P đang dùng
    Vector3 currentSpawnCell;               // toạ độ local của ô R đang dùng
    readonly List<Vector3> targetBag = new List<Vector3>();   // túi xáo trộn các ô P
    int bagIndex;

    [ContextMenu("Place slot and spawn from map")]
    public void PlaceFromMap()
    {
        if (builder == null) builder = GetComponentInChildren<JsonMapBuilder>();
        if (builder == null || slot == null || spawnPoint == null)
        {
            Debug.LogError("[TrainingArea] Thiếu builder, slot hoặc spawnPoint!");
            return;
        }

        Transform mt = builder.transform;
        Vector3 target = mt.TransformPoint(builder.TargetPosition);
        Vector3 spawn = mt.TransformPoint(builder.SpawnPosition) + Vector3.up * spawnHeight;

        slot.transform.SetPositionAndRotation(target, mt.rotation * Quaternion.Euler(0f, slotYaw, 0f));
        var box = slot.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = slotSize;
        box.center = new Vector3(0f, slotSize.y * 0.5f, 0f);

        spawnPoint.SetPositionAndRotation(spawn, mt.rotation * Quaternion.Euler(0f, spawnYaw, 0f));
        placed = true;
    }

    public void ResetCar(Transform car)
    {
        // Mỗi episode bốc lại đích + điểm xuất phát ngẫu nhiên (nếu có dữ liệu)
        if (randomizeEveryEpisode && CanRandomize()) PlaceForEpisode();
        else if (!placed) PlaceFromMap();

        Vector2 off = Random.insideUnitCircle * spawnRadius;
        Vector3 pos = spawnPoint.position + new Vector3(off.x, 0f, off.y);
        Quaternion rot = spawnPoint.rotation * Quaternion.Euler(0f, Random.Range(-spawnYawRange, spawnYawRange), 0f);

        car.SetPositionAndRotation(pos, rot);
        var rb = car.GetComponent<Rigidbody>();
        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    bool CanRandomize()
    {
        if (builder == null) builder = GetComponentInChildren<JsonMapBuilder>();
        return builder != null && builder.parkingCells.Count > 0 && builder.roadCells.Count > 0;
    }

    // Bốc đích mới từ "túi" đã xáo trộn, để mọi ô P đều được luyện qua.
    void PickNewTarget()
    {
        if (targetBag.Count == 0 || bagIndex >= targetBag.Count)
        {
            targetBag.Clear();
            targetBag.AddRange(builder.parkingCells);

            // Fisher-Yates shuffle: đảm bảo mọi ô đỗ đều xuất hiện 1 lần / vòng
            for (int i = targetBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Vector3 tmp = targetBag[i];
                targetBag[i] = targetBag[j];
                targetBag[j] = tmp;
            }
            bagIndex = 0;
        }

        currentTargetCell = targetBag[bagIndex++];
        currentSpawnCell = PickRoadCell(currentTargetCell);   // spawn mới theo đích mới
        episodesAtTarget = 0;

        Debug.Log($"[TrainingArea] Đích mới {currentTargetCell} — giữ {episodesPerTarget} episode");
    }

    // Ép đổi đích ngay (dùng nút chuột phải trong Inspector khi test).
    [ContextMenu("Force new target now")]
    public void ForceNewTarget()
    {
        episodesAtTarget = 0;
        PlaceForEpisode();
    }

    // Đặt slot + spawn cho episode hiện tại.
    // Đích và spawn chỉ đổi sau mỗi K episode; nhiễu nhỏ (±spawnRadius, ±spawnYawRange)
    // vẫn đổi mỗi episode nên K episode không giống hệt nhau.
    [ContextMenu("Randomize slot and spawn from map")]
    public void PlaceForEpisode()
    {
        if (builder == null || slot == null || spawnPoint == null)
        {
            Debug.LogError("[TrainingArea] Thiếu builder, slot hoặc spawnPoint!");
            return;
        }

        // Chưa build map (ví dụ bấm chuột phải ở edit mode) → quay về S/T cố định
        if (builder.parkingCells.Count == 0 || builder.roadCells.Count == 0)
        {
            Debug.LogWarning("[TrainingArea] Chưa có roadCells/parkingCells — dùng S/T cố định.");
            PlaceFromMap();
            return;
        }

        Transform mt = builder.transform;

        // Hết K episode (hoặc chưa có đích nào) thì bốc đích mới
        if (episodesAtTarget >= episodesPerTarget || episodesAtTarget == 0)
            PickNewTarget();
        episodesAtTarget++;

        // 1. Slot đặt tại ô 'P' đang dùng
        slot.transform.SetPositionAndRotation(
            mt.TransformPoint(currentTargetCell),
            mt.rotation * Quaternion.Euler(0f, slotYaw, 0f));

        var box = slot.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = slotSize;
        box.center = new Vector3(0f, slotSize.y * 0.5f, 0f);

        // 2. Spawn đặt tại ô 'R' đang dùng
        spawnPoint.SetPositionAndRotation(
            mt.TransformPoint(currentSpawnCell) + Vector3.up * spawnHeight,
            mt.rotation * Quaternion.Euler(0f, spawnYaw, 0f));

        placed = true;
        Physics.SyncTransforms();
    }

    Vector3 PickRoadCell(Vector3 localTarget)
    {
        // Thử ngẫu nhiên trước
        for (int i = 0; i < 60; i++)
        {
            Vector3 cell = builder.roadCells[Random.Range(0, builder.roadCells.Count)];
            float d = Vector3.Distance(cell, localTarget);
            if (d >= minSpawnDistance && d <= maxSpawnDistance) return cell;
        }

        // Fallback: ô có khoảng cách gần với khoảng mong muốn nhất
        Vector3 best = builder.roadCells[0];
        float bestDelta = float.MaxValue;
        for (int i = 0; i < builder.roadCells.Count; i++)
        {
            float d = Vector3.Distance(builder.roadCells[i], localTarget);
            float delta = Mathf.Abs(d - Mathf.Clamp(d, minSpawnDistance, maxSpawnDistance));
            if (delta < bestDelta) { bestDelta = delta; best = builder.roadCells[i]; }
        }
        return best;
    }

    void OnDrawGizmosSelected()
    {
        if (spawnPoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(spawnPoint.position, spawnRadius);
        Gizmos.DrawRay(spawnPoint.position, spawnPoint.forward * 2f);
    }
}