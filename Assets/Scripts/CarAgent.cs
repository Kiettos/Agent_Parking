using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CarAgent : Agent
{
    [Header("Thông số di chuyển")]
    public float moveSpeed = 10f;
    public float turnSpeed = 150f;
    public float rayDistance = 15f;
    public LayerMask obstacleLayer; // Nhớ chọn Layer Tường (Wall) trên Inspector

    private Rigidbody rb;
    private Vector3 targetPos;
    private float lastDistance;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        // 1. Tìm Map Builder để lấy vị trí S (Spawn) và T (Target)
        JsonMapBuilder mapBuilder = FindObjectOfType<JsonMapBuilder>();
        if (mapBuilder != null)
        {
            // Nếu MapBuilder đã tìm thấy chữ S và T trong file JSON
            if (mapBuilder.SpawnPosition != Vector3.zero)
            {
                transform.position = mapBuilder.SpawnPosition + Vector3.up * 1f;
            }
            targetPos = mapBuilder.TargetPosition;
        }

        // 2. Reset vật lý của xe
        transform.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        // 3. Reset khoảng cách
        lastDistance = Vector3.Distance(transform.position, targetPos);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Quét 8 tia Raycast xung quanh xe (8 thông số)
        AddRay(sensor, transform.forward);                      // Thẳng trước
        AddRay(sensor, Quaternion.Euler(0, 45, 0) * transform.forward);  // Chéo phải
        AddRay(sensor, Quaternion.Euler(0, -45, 0) * transform.forward); // Chéo trái
        AddRay(sensor, Quaternion.Euler(0, 90, 0) * transform.forward);  // Phải
        AddRay(sensor, Quaternion.Euler(0, -90, 0) * transform.forward); // Trái
        AddRay(sensor, Quaternion.Euler(0, 135, 0) * transform.forward); // Sau phải
        AddRay(sensor, Quaternion.Euler(0, -135, 0) * transform.forward);// Sau trái
        AddRay(sensor, -transform.forward);                     // Thẳng sau

        // 2. Vận tốc hiện tại (2 thông số)
        sensor.AddObservation(rb.linearVelocity.magnitude / moveSpeed); 
        sensor.AddObservation(transform.InverseTransformDirection(rb.linearVelocity).z / moveSpeed); 

        // 3. Khoảng cách tới đích (1 thông số)
        float dist = Vector3.Distance(transform.position, targetPos);
        sensor.AddObservation(dist / 50f); 

        // 4. Góc lệch so với đích (3 thông số)
        Vector3 dirToTarget = (targetPos - transform.position).normalized;
        sensor.AddObservation(Vector3.Dot(transform.forward, dirToTarget)); // Đang hướng về đích ko
        sensor.AddObservation(transform.forward.x);
        sensor.AddObservation(transform.forward.z);
        
        // Tổng cộng: 8 + 2 + 1 + 3 = 14 thông số (Khớp với Space Size = 14)
    }

    private void AddRay(VectorSensor sensor, Vector3 dir)
    {
        if (Physics.Raycast(transform.position, dir, out RaycastHit hit, rayDistance, obstacleLayer))
        {
            sensor.AddObservation(hit.distance / rayDistance);
        }
        else
        {
            sensor.AddObservation(1f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Nhận lệnh từ AI (-1 đến 1)
        float throttle = actions.ContinuousActions[0]; 
        float steer = actions.ContinuousActions[1];    

        // Di chuyển xe
        Vector3 move = transform.forward * throttle * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);

        // Xoay xe
        Quaternion turn = Quaternion.Euler(0f, steer * turnSpeed * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turn);

        // --- TÍNH ĐIỂM (REWARD) ---
        
        // Phạt thời gian (sống càng lâu càng mất điểm)
        AddReward(-0.001f);

        float currentDistance = Vector3.Distance(transform.position, targetPos);
        
        // Thưởng nếu tiến gần đích hơn
        if (currentDistance < lastDistance)
            AddReward(0.01f);
        else
            AddReward(-0.01f); // Phạt nếu đi xa đích
            
        lastDistance = currentDistance;

        // Nếu đỗ đúng đích (khoảng cách < 2m)
        if (currentDistance < 2.0f)
        {
            AddReward(1.0f); // Thưởng lớn
            EndEpisode();    // Kết thúc lượt
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Nếu đâm vào tường (Layer Obstacle)
        if (((1 << collision.gameObject.layer) & obstacleLayer) != 0)
        {
            AddReward(-1.0f); // Phạt nặng
            EndEpisode();     // Kết thúc lượt
        }
    }

    // Cho phép tự lái bằng bàn phím để test
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        continuousActionsOut[0] = Input.GetAxis("Vertical");   // W/S
        continuousActionsOut[1] = Input.GetAxis("Horizontal"); // A/D
    }
}