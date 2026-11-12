using UnityEngine;

public class WheelVisual : MonoBehaviour
{
    public WheelCollider wheelCollider;
    public Transform visualTransform;

    void Update()
    {
        if (wheelCollider == null || visualTransform == null) return;

        // Lấy vị trí và góc xoay thực tế từ bánh tàng hình
        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        
        // Gán vào bánh xe 3D
        visualTransform.position = pos;
        visualTransform.rotation = rot;
    }
}