using UnityEngine;

// Ô đỗ xe. Gắn vào object "ParkingSlot" trong TrainingArea.
// Box Collider = vùng xe phải nằm trọn bên trong. Hai xe hàng xóm được dời dọc theo trục Z cục bộ của slot.
[RequireComponent(typeof(BoxCollider))]
public class ParkingSlot : MonoBehaviour
{
    public Transform carBefore, carAfter;
    public float minGap = 0.8f, maxGap = 2.0f;
    BoxCollider box;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    BoxCollider Box()
    {
        if (box == null) box = GetComponent<BoxCollider>();
        return box;
    }

    // worldPos có nằm trong ô đỗ không (chỉ xét mặt phẳng X-Z)
    public bool Contains(Vector3 worldPos)
    {
        var b = Box();
        Vector3 local = transform.InverseTransformPoint(worldPos) - b.center;
        Vector3 half = b.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    // Xáo trộn khoảng cách 2 xe hàng xóm (gọi mỗi đầu episode)
    public void RandomizeNeighbours()
    {
        if (!carBefore || !carAfter) return;
        var b = Box();
        float gap = Random.Range(minGap, maxGap);
        float half = b.size.z * 0.5f;

        carBefore.localPosition = new Vector3(carBefore.localPosition.x, carBefore.localPosition.y, -(half + gap));
        carAfter.localPosition = new Vector3(carAfter.localPosition.x, carAfter.localPosition.y, half + gap);
    }

    void OnDrawGizmos()
    {
        var b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.color = Color.green;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
