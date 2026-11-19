using UnityEngine;

// Gắn vào prefab marker (object rỗng) của ký hiệu 'D' (và E, F, G nếu cần) trong ObjectMapping.
// Vị trí + hướng của marker = chỗ và hướng đầu xe mồi. Mũi tên xanh (Z) = hướng mũi xe.
public class DecoyMarker : MonoBehaviour
{
    // Hình hộp vàng trong Scene view cho thấy xe mồi sẽ nằm ở đâu (đủ cỡ 1.9 x 4 m)
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0f, 1f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, 0.7f, 0f), new Vector3(1.9f, 1.4f, 4f));
        Gizmos.DrawLine(new Vector3(0f, 0.7f, 0f), new Vector3(0f, 0.7f, 2.5f));
    }
}
