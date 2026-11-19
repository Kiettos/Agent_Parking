using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

// Gắn vào object Car (cùng chỗ với Rigidbody, CarController, Behavior Parameters, Decision Requester).
// Cần: TrainingArea ở object cha, ParkingSlot có Box Collider, tag "Wall" và "Obstacle" đã tạo.
public class ParkingAgent : Agent
{
    [Header("Reward")]
    public float successReward = 2f;
    public float collisionPenalty = -1f;
    public float timePenalty = -0.002f;     // phạt nhỏ mỗi quyết định, để xe không đứng ì
    public float distanceShaping = 0.05f;   // thưởng cho mỗi mét lại gần slot
    public float alignShaping = 0.05f;      // thưởng khi xe xoay thẳng hàng với slot

    [Header("Điều kiện đỗ thành công")]
    public float maxAngleDeg = 10f;         // lệch tối đa so với trục dài của slot
    public float maxSpeedToPark = 0.3f;     // m/s, phải gần như đứng yên
    public int holdDecisions = 10;          // phải giữ đúng trạng thái này ~10 quyết định liên tiếp

    [Header("Khác")]
    public float obsScale = 15f;            // chia observation vị trí cho số này (mét)
    public float maxDistance = 40f;         // đi xa hơn thì kết thúc episode
    public bool debugLog = false;

    CarController car;
    Rigidbody rb;
    BoxCollider carBox;
    TrainingArea area;
    JsonMapBuilder map;
    ParkingSlot slot;
    BoxCollider slotBox;

    float prevDist, prevAlign;
    int parkedCount;
    bool episodeDone;

    public override void Initialize()
    {
        car = GetComponent<CarController>();
        if (car == null)
        {
            // Tự gắn CarController nếu prefab chưa có (chống NullReferenceException)
            car = gameObject.AddComponent<CarController>();
            WheelCollider[] cols = GetComponentsInChildren<WheelCollider>(true);
            foreach (var wc in cols)
            {
                switch (wc.name)
                {
                    case "FL": car.frontLeft = wc; break;
                    case "FR": car.frontRight = wc; break;
                    case "RL": car.rearLeft = wc; break;
                    case "RR": car.rearRight = wc; break;
                }
            }
            car.updateWheelVisuals = false;   // CarWheels đã lo việc đồng bộ mesh bánh
        }

        // ManualDrive ghi đè motorTorque mỗi FixedUpdate → tắt khi agent điều khiển
        var manual = GetComponent<ManualDrive>();
        if (manual != null) manual.enabled = false;

        rb = GetComponent<Rigidbody>();
        carBox = GetComponent<BoxCollider>();
        area = GetComponentInParent<TrainingArea>();
        if (area == null) { Debug.LogError("[ParkingAgent] Không tìm thấy TrainingArea ở object cha!"); return; }
        map = area.builder != null ? area.builder : area.GetComponentInChildren<JsonMapBuilder>();
        slot = area.slot;
        slotBox = slot.GetComponent<BoxCollider>();
    }

    public override void OnEpisodeBegin()
    {
        episodeDone = false;
        parkedCount = 0;
        car.SetInputs(0f, 0f, 0f);
        area.ResetCar(transform);
        Measure(out prevDist, out prevAlign);
    }

    // ---------- OBSERVATION: tổng cộng 11 số (đặt Space Size = 11) ----------
    public override void CollectObservations(VectorSensor sensor)
    {
        // Vị trí xe trong hệ tọa độ của slot (không phụ thuộc khu train nằm ở đâu): 2 số
        Vector3 local = slot.transform.InverseTransformPoint(transform.position);
        sensor.AddObservation(local.x / obsScale);
        sensor.AddObservation(local.z / obsScale);

        // Hướng mũi xe trong hệ tọa độ của slot: 2 số
        Vector3 fwd = slot.transform.InverseTransformDirection(transform.forward);
        sensor.AddObservation(fwd.x);
        sensor.AddObservation(fwd.z);

        // Vận tốc theo hệ tọa độ của xe (tiến/lùi và trượt ngang): 2 số
        Vector3 v = transform.InverseTransformDirection(Vel());
        sensor.AddObservation(v.x / car.maxSpeed);
        sensor.AddObservation(v.z / car.maxSpeed);

        // Tốc độ xoay thân xe: 1 số
        sensor.AddObservation(rb.angularVelocity.y / 2f);

        // Góc lái hiện tại: 1 số
        sensor.AddObservation(car.frontLeft.steerAngle / car.maxSteerAngle);

        // Tâm slot nằm ở đâu so với xe (trái/phải, trước/sau): 2 số
        Vector3 toSlot = transform.InverseTransformPoint(SlotCenter());
        sensor.AddObservation(toSlot.x / obsScale);
        sensor.AddObservation(toSlot.z / obsScale);

        // Xe lệch bao nhiêu độ so với trục dài của slot (-1..1): 1 số
        sensor.AddObservation(AngleToAxis() / 90f);
    }

    // ---------- ACTION + REWARD ----------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (episodeDone) return;

        var a = actions.ContinuousActions;
        car.SetInputs(a[0], a[1], a[2]);   // ga/lùi, lái, phanh (phanh < 0 = không phanh)

        // Reward dẫn đường dạng "hiệu thế năng": chỉ thưởng khi tiến bộ, không thể ngồi nhặt điểm
        Measure(out float dist, out float align);
        AddReward((prevDist - dist) * distanceShaping);
        AddReward((align - prevAlign) * alignShaping);
        AddReward(timePenalty);
        prevDist = dist;
        prevAlign = align;

        // Ra ngoài biên map → kết thúc (lưới an toàn nếu tường bị lọt)
        if (OutsideMap())
        {
            Fail("ra khỏi biên map");
            return;
        }

        // Lật xe hoặc đi quá xa
        if (transform.up.y < 0.3f || dist > maxDistance)
        {
            Fail("lật xe / đi quá xa");
            return;
        }

        // Đỗ thành công: đúng chỗ, đúng góc, đứng yên liên tục holdDecisions quyết định
        if (IsParked())
        {
            parkedCount++;
            if (parkedCount >= holdDecisions) Succeed();
        }
        else parkedCount = 0;
    }

    // Xe có ra ngoài vùng map không (xét trên mặt phẳng X-Z, trong không gian local của Map)
    bool OutsideMap()
    {
        if (map == null) return false;

        Bounds b = map.GetLocalBounds();
        Vector3 local = map.transform.InverseTransformPoint(transform.position);

        return local.x < b.min.x || local.x > b.max.x ||
               local.z < b.min.z || local.z > b.max.z;
    }

    void OnCollisionEnter(Collision c)
    {
        if (episodeDone) return;
        if (c.collider.CompareTag("Obstacle"))
            Fail("va chạm " + c.collider.name);
    }

    void Succeed()
    {
        episodeDone = true;
        AddReward(successReward);
        if (debugLog) Debug.Log($"[{name}] THÀNH CÔNG, reward = {GetCumulativeReward():F2}");
        EndEpisode();
    }

    void Fail(string why)
    {
        episodeDone = true;
        AddReward(collisionPenalty);
        if (debugLog) Debug.Log($"[{name}] THẤT BẠI ({why}), reward = {GetCumulativeReward():F2}");
        EndEpisode();
    }

    // ---------- HEURISTIC (tự lái để test: đặt Behavior Type = Heuristic Only) ----------
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        var k = Keyboard.current;
        if (k == null) return;
        ca[0] = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        ca[1] = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        ca[2] = k.spaceKey.isPressed ? 1f : 0f;
    }

    // ---------- HÀM PHỤ ----------
    Vector3 SlotCenter() => slot.transform.TransformPoint(slotBox.center);

    // Trục dài của slot: tự nhận theo kích thước Box Collider (X hoặc Z)
    Vector3 LongAxis() => slotBox.size.x >= slotBox.size.z ? slot.transform.right : slot.transform.forward;

    // Độ lệch giữa mũi xe và trục dài của slot, -90..90 độ (tiến hay lùi vào đều tính như nhau)
    float AngleToAxis()
    {
        Vector3 f = transform.forward; f.y = 0f;
        Vector3 ax = LongAxis(); ax.y = 0f;
        float ang = Vector3.SignedAngle(f, ax, Vector3.up);
        if (ang > 90f) ang -= 180f;
        else if (ang < -90f) ang += 180f;
        return ang;
    }

    void Measure(out float dist, out float align)
    {
        Vector3 d = SlotCenter() - transform.position;
        d.y = 0f;
        dist = d.magnitude;
        align = 1f - Mathf.Abs(AngleToAxis()) / 90f;   // 1 = thẳng hàng hoàn toàn
    }

    bool IsParked()
    {
        if (Mathf.Abs(AngleToAxis()) > maxAngleDeg) return false;
        if (Vel().magnitude > maxSpeedToPark) return false;

        // Cả 4 góc của thân xe phải nằm trong slot
        Vector3 c = carBox.center;
        Vector3 h = carBox.size * 0.5f;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                if (!slot.Contains(transform.TransformPoint(c + new Vector3(sx * h.x, 0f, sz * h.z))))
                    return false;
        return true;
    }

    Vector3 Vel()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }
}
