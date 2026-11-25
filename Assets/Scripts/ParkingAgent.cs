using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

public class ParkingAgent : Agent
{
    [Header("Reward - Thành công & Dẫn đường")]
    public float successReward = 20f;
    public float distanceShaping = 0.05f;
    public float alignShaping = 0.05f;

    [Header("Penalty - Va chạm chi tiết ")]
    public float decoyPenalty = -1.5f;         // Xe decoy
    public float lampPenalty = -1.5f;          // Cột đèn (L)
    public float boundaryPenalty = -1.2f;      // Ra khỏi biên map
    public float bushPenalty = -1.0f;          // Bụi cây (B)
    public float plantingPenalty = -1.0f;      // Bồn cây / thảm cỏ (G)
    public float sidewalkPenalty = -0.8f;      // Vỉa hè (H)
    public float obstaclePenalty = -1.0f;      // Tường & vật cản mặc định

    [Header("Penalty - Chi phí di chuyển theo mặt sàn (mỗi Decision)")]
    public float roadStepPenalty = -0.001f;    // Đường chính (R)
    public float courtStepPenalty = -0.002f;   // Sân bãi (C)
    public float wrongSlotPenalty = -0.015f;   // Đè lên ô đỗ P khác không phải đích
    public float wrongSlotDistThreshold = 4.5f;// Khoảng cách (m) coi như đang ở ô P khác

    [Header("Điều kiện đỗ thành công")]
    public float maxAngleDeg = 10f;
    public float maxSpeedToPark = 0.3f;
    public int holdDecisions = 10;

    [Header("Khác")]
    public float obsScale = 15f;
    public float maxDistance = 40f;

    [Header("Object Observation: 8 tia")]
    public float rayLength = 15f;
    public float rayHeight = 0.5f;
    public bool debugRays = true;
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
            car.updateWheelVisuals = false;
        }

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

    // ---------- OBSERVATION: 11 số xe + 32 số từ 8 tia (Space Size = 43) ----------
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 local = slot.transform.InverseTransformPoint(transform.position);
        sensor.AddObservation(local.x / obsScale);
        sensor.AddObservation(local.z / obsScale);

        Vector3 fwd = slot.transform.InverseTransformDirection(transform.forward);
        sensor.AddObservation(fwd.x);
        sensor.AddObservation(fwd.z);

        Vector3 v = transform.InverseTransformDirection(Vel());
        sensor.AddObservation(v.x / car.maxSpeed);
        sensor.AddObservation(v.z / car.maxSpeed);

        sensor.AddObservation(rb.angularVelocity.y / 2f);
        sensor.AddObservation(car.frontLeft.steerAngle / car.maxSteerAngle);

        Vector3 toSlot = transform.InverseTransformPoint(SlotCenter());
        sensor.AddObservation(toSlot.x / obsScale);
        sensor.AddObservation(toSlot.z / obsScale);

        sensor.AddObservation(AngleToAxis() / 90f);

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            Vector3 origin = transform.position + Vector3.up * rayHeight;

            if (CastRay(origin, dir, out RaycastHit hit))
            {
                bool isObstacle = IsObstacleCollider(hit.collider);
                bool isSlot = hit.collider == slotBox;

                sensor.AddObservation(isObstacle ? 1f : 0f);
                sensor.AddObservation(isSlot ? 1f : 0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(hit.distance / rayLength);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(1f);
                sensor.AddObservation(1f);
            }
        }
    }

    // ---------- ACTION + REWARD ----------
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (episodeDone) return;

        var a = actions.ContinuousActions;
        car.SetInputs(a[0], a[1], a[2]);

        Measure(out float dist, out float align);
        AddReward((prevDist - dist) * distanceShaping);
        AddReward((align - prevAlign) * alignShaping);
        prevDist = dist;
        prevAlign = align;

        // Kiểm tra loại mặt sàn bên dưới xe (R, P, C, hoặc tile cấm H, G nếu không có collider chặn ngang)
        if (!EvaluateGroundTile(dist)) return;

        // Ra ngoài biên map
        if (OutsideMap())
        {
            Fail("ra khỏi biên map", boundaryPenalty);
            return;
        }

        // Lật xe hoặc đi quá xa
        if (transform.up.y < 0.3f || dist > maxDistance)
        {
            Fail("lật xe / đi quá xa", obstaclePenalty);
            return;
        }

        // Đỗ thành công
        if (IsParked())
        {
            parkedCount++;
            if (parkedCount >= holdDecisions) Succeed();
        }
        else parkedCount = 0;
    }

    // Chiếu tia xuống dưới gầm xe để đọc loại Tile đang chạy qua
    bool EvaluateGroundTile(float distToTargetSlot)
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.0f, ~0, QueryTriggerInteraction.Collide))
        {
            string n = hit.collider.name.ToLower();
            string t = hit.collider.tag;

            // Nếu bánh xe lăn lên tile cấm (H: vỉa hè, G: bồn cây)
            if (t == "Sidewalk" || n.Contains("sidewalk") || n.Contains("tile_h"))
            {
                Fail("leo vỉa hè (H)", sidewalkPenalty);
                return false;
            }
            if (t == "Planting" || n.Contains("planting") || n.Contains("grass") || n.Contains("tile_g"))
            {
                Fail("đi vào bồn cây (G)", plantingPenalty);
                return false;
            }

            // Đang nằm trong ô đỗ (P)
            if (hit.collider == slotBox || t == "ParkingSlot" || n.Contains("slot") || n.Contains("tile_p"))
            {
                if (distToTargetSlot < wrongSlotDistThreshold)
                    AddReward(roadStepPenalty);      // Đúng ô đích -> phạt nhẹ như đường chính (-0.001)
                else
                    AddReward(wrongSlotPenalty);     // Đè lên ô đỗ khác -> phạt nặng (-0.015)
                return true;
            }

            // Đường chính (R)
            if (t == "Road" || n.Contains("road") || n.Contains("tile_r"))
            {
                AddReward(roadStepPenalty);
                return true;
            }
        }

        // Mặc định: sân bãi (C)
        AddReward(courtStepPenalty);
        return true;
    }

    void OnCollisionEnter(Collision c)
    {
        if (episodeDone) return;

        Collider col = c.collider;
        string n = col.name.ToLower();
        string t = col.tag;

        // Bỏ qua mặt đường phẳng xe đang chạy lên
        if (t == "Road" || t == "Court" || n.Contains("road") || n.Contains("court") || n.Contains("tile_r") || n.Contains("tile_c"))
            return;

        if (t == "Decoy" || n.Contains("decoy"))
            Fail("va chạm xe decoy", decoyPenalty);
        else if (t == "Lamp" || n.Contains("lamp") || n.Contains("light"))
            Fail("va chạm cột đèn (L)", lampPenalty);
        else if (t == "Bush" || n.Contains("bush") || n.Contains("tree"))
            Fail("va chạm bụi cây (B)", bushPenalty);
        else if (t == "Planting" || n.Contains("planting") || n.Contains("tile_g"))
            Fail("va chạm bồn cây (G)", plantingPenalty);
        else if (t == "Sidewalk" || n.Contains("sidewalk") || n.Contains("tile_h"))
            Fail("va chạm vỉa hè (H)", sidewalkPenalty);
        else if (IsObstacleCollider(col))
            Fail("va chạm vật cản: " + col.name, obstaclePenalty);
    }

    bool IsObstacleCollider(Collider col)
    {
        string t = col.tag;
        string n = col.name.ToLower();
        return t == "Obstacle" || t == "Wall" || t == "Decoy" || t == "Lamp" || t == "Bush" || t == "Sidewalk" || t == "Planting"
            || n.Contains("wall") || n.Contains("obstacle") || n.Contains("decoy") || n.Contains("lamp") || n.Contains("bush") || n.Contains("sidewalk") || n.Contains("planting");
    }

    bool OutsideMap()
    {
        if (map == null) return false;
        Bounds b = map.GetLocalBounds();
        Vector3 local = map.transform.InverseTransformPoint(transform.position);
        return local.x < b.min.x || local.x > b.max.x ||
               local.z < b.min.z || local.z > b.max.z;
    }

    void Succeed()
    {
        episodeDone = true;
        AddReward(successReward);
        if (debugLog) Debug.Log($"[{name}] THÀNH CÔNG, reward = {GetCumulativeReward():F2}");
        EndEpisode();
    }

    void Fail(string why, float penalty)
    {
        episodeDone = true;
        AddReward(penalty);
        if (debugLog) Debug.Log($"[{name}] THẤT BẠI ({why} | {penalty}), reward = {GetCumulativeReward():F2}");
        EndEpisode();
    }

    // ---------- HEURISTIC ----------
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
    Vector3 LongAxis() => slotBox.size.x >= slotBox.size.z ? slot.transform.right : slot.transform.forward;

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
        align = 1f - Mathf.Abs(AngleToAxis()) / 90f;
    }

    bool IsParked()
    {
        if (Mathf.Abs(AngleToAxis()) > maxAngleDeg) return false;
        if (Vel().magnitude > maxSpeedToPark) return false;

        Vector3 c = carBox.center;
        Vector3 h = carBox.size * 0.5f;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                if (!slot.Contains(transform.TransformPoint(c + new Vector3(sx * h.x, 0f, sz * h.z))))
                    return false;
        return true;
    }

    static readonly RaycastHit[] rayBuffer = new RaycastHit[16];

    bool CastRay(Vector3 origin, Vector3 dir, out RaycastHit best)
    {
        best = default;
        bool found = false;
        float min = float.MaxValue;
        int n = Physics.RaycastNonAlloc(origin, dir, rayBuffer, rayLength, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            if (rayBuffer[i].collider.transform.IsChildOf(transform)) continue;
            if (rayBuffer[i].distance < min)
            {
                min = rayBuffer[i].distance;
                best = rayBuffer[i];
                found = true;
            }
        }
        return found;
    }

    Vector3 Vel()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    void OnDrawGizmosSelected()
    {
        if (!debugRays) return;

        Vector3 origin = transform.position + Vector3.up * rayHeight;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * transform.forward;
            if (CastRay(origin, dir, out RaycastHit hit))
            {
                Gizmos.color = IsObstacleCollider(hit.collider) ? Color.red : Color.yellow;
                Gizmos.DrawLine(origin, hit.point);
            }
            else
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(origin, origin + dir * rayLength);
            }
        }
    }
}