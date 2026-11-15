using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    [Header("Wheel Meshes (KHÔNG làm con của WheelCollider)")]
    public Transform frontLeftMesh;
    public Transform frontRightMesh;
    public Transform rearLeftMesh;
    public Transform rearRightMesh;

    [Header("Thông số (chỉnh dần khi chạy thử)")]
    public float maxMotorTorque = 800f;   // lực đẩy mỗi bánh sau
    public float maxBrakeTorque = 3000f;
    public float idleBrake = 100f;        // phanh nhẹ khi thả ga, để xe không trôi mãi
    public float maxSteerAngle = 30f;     // độ
    public float maxSpeed = 6f;           // m/s, đỗ xe không cần nhanh
    public Vector3 centerOfMass = new Vector3(0f, -0.3f, 0f);

    [Header("Tắt khi train để đỡ tốn CPU")]
    public bool updateWheelVisuals = true;

    Rigidbody rb;
    float throttle, steer, brake;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMass;   // hạ trọng tâm để xe khó lật
    }

    // ParkingAgent gọi hàm này.
    // throttle: -1..1 (âm = lùi), steer: -1..1, brake: 0..1
    public void SetInputs(float throttle, float steer, float brake)
    {
        this.throttle = Mathf.Clamp(throttle, -1f, 1f);
        this.steer = Mathf.Clamp(steer, -1f, 1f);
        this.brake = Mathf.Clamp01(brake);
    }

    void FixedUpdate()
    {
        // Unity 2022 trở xuống dùng rb.velocity; Unity 6 đổi thành rb.linearVelocity
        float speed = rb.linearVelocity.magnitude;

        float torque = (speed < maxSpeed) ? throttle * maxMotorTorque : 0f;
        rearLeft.motorTorque = torque;
        rearRight.motorTorque = torque;

        float angle = steer * maxSteerAngle;
        frontLeft.steerAngle = angle;
        frontRight.steerAngle = angle;

        float b = brake * maxBrakeTorque;
        if (b <= 0f && Mathf.Abs(throttle) < 0.01f) b = idleBrake;
        frontLeft.brakeTorque = b;
        frontRight.brakeTorque = b;
        rearLeft.brakeTorque = b;
        rearRight.brakeTorque = b;
    }

    void Update()
    {
        if (!updateWheelVisuals) return;
        UpdatePose(frontLeft, frontLeftMesh);
        UpdatePose(frontRight, frontRightMesh);
        UpdatePose(rearLeft, rearLeftMesh);
        UpdatePose(rearRight, rearRightMesh);
    }

    static void UpdatePose(WheelCollider col, Transform mesh)
    {
        if (col == null || mesh == null) return;
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.SetPositionAndRotation(pos, rot);
    }
}
