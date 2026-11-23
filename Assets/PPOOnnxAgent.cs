using UnityEngine;
using Unity.InferenceEngine;

[RequireComponent(typeof(Rigidbody))]
public class PPOOnnxAgent : MonoBehaviour
{
    [Header("ONNX")]
    [SerializeField] private ModelAsset modelAsset;
    [SerializeField, Min(0.02f)] private float decisionInterval = 0.15f;

    [Header("Observation scales (Unity meters)")]
    [SerializeField, Min(0.1f)] private float sensorRangeMeters = 15f;
    [SerializeField, Min(0.1f)] private float targetDistanceScaleMeters = 89f;
    [SerializeField, Min(0.1f)] private float speedScaleMetersPerSecond = 6f;
    [SerializeField, Min(1f)] private float modelMaxSteerDegrees = 35f;
    [SerializeField, Min(0f)] private float sensorHeight = 0.8f;
    [SerializeField] private LayerMask sensorMask = ~0;

    [Header("Sensor visualization")]
    [SerializeField] private bool showSensorLines = true;
    [SerializeField, Min(0.01f)] private float sensorLineWidth = 0.1f;
    [SerializeField, Min(0.1f)] private float sensorLineLengthMeters = 5f;
    [SerializeField] private Color sensorClearColor = new Color(0.15f, 0.85f, 1f, 1f);
    [SerializeField] private Color sensorObstacleColor = new Color(1f, 0.2f, 0.05f, 1f);

    [Header("Game HUD")]
    [SerializeField] private bool showHud = true;
    [SerializeField] private Vector2 hudPosition = new Vector2(16f, 16f);

    [Header("Parking indicator")]
    [SerializeField] private Color parkingSlotColor = new Color(1f, 0.8f, 0.05f, 1f);
    [SerializeField] private Color parkedSlotColor = new Color(0.1f, 1f, 0.2f, 1f);
    [SerializeField, Min(0.1f)] private float parkedIndicatorDuration = 1.2f;

    [Header("Automatic recording episodes")]
    [SerializeField, Min(1f)] private float attemptTimeLimit = 60f;
    [SerializeField, Min(1f)] private float maxDistanceToSlot = 40f;
    [SerializeField, Min(1f)] private float successAngleDegrees = 10f;
    [SerializeField, Min(0.01f)] private float maxSpeedToPark = 0.3f;
    [SerializeField, Min(1)] private int holdDecisionsToPark = 10;

    private Worker worker;
    private CarController car;
    private Rigidbody body;
    private BoxCollider carCollider;
    private TrainingArea area;
    private ParkingSlot slot;
    private BoxCollider slotCollider;
    private LineRenderer[] sensorLines;
    private Material[] sensorLineMaterials;
    private LineRenderer parkingIndicator;
    private Material parkingIndicatorMaterial;
    private float nextDecisionTime;
    private int lastAction = -1;
    private int attemptCount;
    private int parkedDecisionCount;
    private float attemptElapsed;
    private float parkedIndicatorUntil;
    private bool obstacleCollision;
    private bool attemptEnding;
    private GUIStyle hudTitleStyle;
    private GUIStyle hudTextStyle;
#if UNITY_EDITOR
    private TrainingRecorder automaticRecorder;
#endif

    private static readonly Vector3[] SensorDirections =
    {
        new Vector3(0f, 0f, 1f),   // F
        new Vector3(1f, 0f, 1f),   // FR
        new Vector3(1f, 0f, 0f),   // R
        new Vector3(1f, 0f, -1f),  // BR
        new Vector3(0f, 0f, -1f),  // B
        new Vector3(-1f, 0f, -1f), // BL
        new Vector3(-1f, 0f, 0f),  // L
        new Vector3(-1f, 0f, 1f),  // FL
    };

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        area = GetComponentInParent<TrainingArea>();

        var manualDrive = GetComponent<ManualDrive>();
        if (manualDrive != null)
            manualDrive.enabled = false;

        var mlAgent = GetComponent<ParkingAgent>();
        if (mlAgent != null)
            mlAgent.enabled = false;

        car = GetComponent<CarController>();
        if (car == null)
            car = gameObject.AddComponent<CarController>();

        if (car.frontLeft == null || car.frontRight == null ||
            car.rearLeft == null || car.rearRight == null)
        {
            if (!AssignWheelColliders())
            {
                enabled = false;
                return;
            }
        }

        car.updateWheelVisuals = false;
        CreateSensorLines();
    }

    private void Start()
    {
        if (modelAsset == null)
        {
            Debug.LogError("[PPOOnnxAgent] Chưa gán Model Asset.", this);
            enabled = false;
            return;
        }

        if (area == null || area.slot == null)
        {
            Debug.LogError("[PPOOnnxAgent] Không tìm thấy TrainingArea hoặc ParkingSlot.", this);
            enabled = false;
            return;
        }

        slot = area.slot;
        slotCollider = slot.GetComponent<BoxCollider>();
        if (slotCollider == null)
        {
            Debug.LogError("[PPOOnnxAgent] ParkingSlot cần có BoxCollider.", this);
            enabled = false;
            return;
        }

        carCollider = GetComponent<BoxCollider>();
        if (carCollider == null)
        {
            Debug.LogError("[PPOOnnxAgent] Xe cần có BoxCollider để xác định đỗ đúng.", this);
            enabled = false;
            return;
        }

        area.ResetCar(transform);
        CreateParkingIndicator();
        Model model = ModelLoader.Load(modelAsset);
        worker = new Worker(model, BackendType.CPU);
        attemptCount = 1;
        ResetAttemptState();
        nextDecisionTime = Time.time;

#if UNITY_EDITOR
        automaticRecorder = area.GetComponent<TrainingRecorder>();
        if (automaticRecorder == null)
            automaticRecorder = area.gameObject.AddComponent<TrainingRecorder>();
        automaticRecorder.enabled = true;
        if (!automaticRecorder.BeginAutomaticCapture())
            Debug.LogError("[PPOOnnxAgent] Không thể bắt đầu ghi video tự động.", this);
#endif
    }

    private void FixedUpdate()
    {
        if (worker == null)
            return;

        UpdateParkingIndicator();
        if (attemptEnding)
            return;

        attemptElapsed += Time.fixedDeltaTime;
        if (ShouldFailAttempt())
        {
            FinishAttempt(false);
            return;
        }

        if (Time.time < nextDecisionTime)
            return;

        nextDecisionTime = Time.time + decisionInterval;
        RunPolicy();
    }

    private void RunPolicy()
    {
        float[] observation = BuildObservation();
        using (var input = new Tensor<float>(
            new TensorShape(1, observation.Length), observation))
        {
            worker.Schedule(input);

            var output = worker.PeekOutput() as Tensor<int>;
            if (output == null)
            {
                Debug.LogError("[PPOOnnxAgent] Output của ONNX không phải Tensor<int>.", this);
                enabled = false;
                return;
            }

            int action = output.DownloadToArray()[0];
            ApplyAction(action);

            bool parked = IsParked();
            if (parked)
                parkedDecisionCount++;
            else
                parkedDecisionCount = 0;

            if (parkedDecisionCount >= holdDecisionsToPark)
                FinishAttempt(true);
        }
    }

    private bool ShouldFailAttempt()
    {
        if (obstacleCollision || attemptElapsed >= attemptTimeLimit ||
            transform.up.y < 0.3f || GetDistanceToSlot() > maxDistanceToSlot)
            return true;

        if (area.builder == null)
            return false;

        Bounds bounds = area.builder.GetLocalBounds();
        Vector3 local = area.builder.transform.InverseTransformPoint(transform.position);
        return local.x < bounds.min.x || local.x > bounds.max.x ||
               local.z < bounds.min.z || local.z > bounds.max.z;
    }

    private void FinishAttempt(bool succeeded)
    {
        if (attemptEnding)
            return;

        attemptEnding = true;
        car.SetInputs(0f, 0f, 1f);
        bool captureComplete = false;
#if UNITY_EDITOR
        if (automaticRecorder != null)
            captureComplete = automaticRecorder.RecordAttemptResult(succeeded);
#endif
        Debug.Log("[PPOOnnxAgent] Lượt kết thúc: " + (succeeded ? "đỗ thành công" : "thất bại"), this);

        if (captureComplete)
        {
            Debug.Log("[PPOOnnxAgent] Ghi video đã dừng; xe được giữ nguyên vị trí.", this);
            return;
        }

        area.ResetCar(transform);
        UpdateParkingIndicatorGeometry();
        attemptCount++;
        ResetAttemptState();
        car.SetInputs(0f, 0f, 0f);
        nextDecisionTime = Time.time + decisionInterval;
    }

    private void ResetAttemptState()
    {
        attemptElapsed = 0f;
        parkedDecisionCount = 0;
        obstacleCollision = false;
        attemptEnding = false;
    }

    private bool IsParked()
    {
        if (Mathf.Abs(AngleToSlotAxis()) > successAngleDegrees ||
            GetVelocity().magnitude > maxSpeedToPark)
            return false;

        Vector3 center = carCollider.center;
        Vector3 half = carCollider.size * 0.5f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 corner = transform.TransformPoint(
                    center + new Vector3(sx * half.x, 0f, sz * half.z));
                if (!slot.Contains(corner))
                    return false;
            }
        }

        return true;
    }

    private float AngleToSlotAxis()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        Vector3 axis = slotCollider.size.x >= slotCollider.size.z
            ? slot.transform.right
            : slot.transform.forward;
        axis.y = 0f;

        float angle = Vector3.SignedAngle(forward, axis, Vector3.up);
        if (angle > 90f) angle -= 180f;
        else if (angle < -90f) angle += 180f;
        return angle;
    }

    private void OnCollisionEnter(Collision collision)
    {
        string tag = collision.collider.tag;
        if (tag == "Obstacle" || tag == "Wall")
            obstacleCollision = true;
    }

    private float[] BuildObservation()
    {
        var observation = new float[13];
        Vector3 origin = transform.position + Vector3.up * sensorHeight;

        for (int i = 0; i < SensorDirections.Length; i++)
        {
            Vector3 localDirection = SensorDirections[i].normalized;
            Vector3 direction =
                transform.forward * localDirection.z +
                transform.right * localDirection.x;
            float distance = MeasureSensorDistance(origin, direction);
            observation[i] = 1f - Mathf.Clamp01(distance / sensorRangeMeters);

            if (sensorLines != null)
            {
                Color lineColor = distance < sensorRangeMeters
                    ? sensorObstacleColor
                    : sensorClearColor;
                sensorLineMaterials[i].color = lineColor;
                Vector3 visualOffset = Vector3.up * 0.6f;
                float visualDistance = Mathf.Min(distance, sensorLineLengthMeters);
                Vector3 visualEndPoint = origin + direction.normalized * visualDistance;
                sensorLines[i].SetPosition(0, origin + visualOffset);
                sensorLines[i].SetPosition(1, visualEndPoint + visualOffset);
            }
        }

        float forwardSpeed = Vector3.Dot(GetVelocity(), transform.forward);
        observation[8] = Mathf.Clamp(
            forwardSpeed / speedScaleMetersPerSecond, -1f, 1f);

        float currentSteer = car.frontLeft != null
            ? car.frontLeft.steerAngle
            : 0f;
        observation[9] = Mathf.Clamp(
            currentSteer / modelMaxSteerDegrees, -1f, 1f);

        Vector3 slotCenter = slot.transform.TransformPoint(slotCollider.center);
        Vector3 toSlot = slotCenter - transform.position;
        toSlot.y = 0f;
        float distanceToSlot = toSlot.magnitude;

        if (distanceToSlot > 0.0001f)
        {
            Vector3 targetDirection = toSlot / distanceToSlot;
            observation[10] = Vector3.Dot(targetDirection, transform.forward);
            observation[11] = Vector3.Dot(targetDirection, transform.right);
        }

        observation[12] = Mathf.Clamp01(
            distanceToSlot / targetDistanceScaleMeters);

        return observation;
    }

    private float MeasureSensorDistance(Vector3 origin, Vector3 direction)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            sensorRangeMeters,
            sensorMask,
            QueryTriggerInteraction.Ignore);

        float closest = sensorRangeMeters;
        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            if (hit.distance < closest)
                closest = hit.distance;
        }

        return closest;
    }

    private void CreateSensorLines()
    {
        if (!showSensorLines)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("[PPOOnnxAgent] Không tìm thấy shader để vẽ sensor.", this);
            return;
        }

        sensorLines = new LineRenderer[SensorDirections.Length];
        sensorLineMaterials = new Material[SensorDirections.Length];

        for (int i = 0; i < sensorLines.Length; i++)
        {
            var lineObject = new GameObject("SensorRay_" + i);
            lineObject.transform.SetParent(transform, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = sensorLineWidth;
            line.endWidth = sensorLineWidth;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            sensorLineMaterials[i] = new Material(shader);
            sensorLineMaterials[i].color = sensorClearColor;
            line.material = sensorLineMaterials[i];
            sensorLines[i] = line;
        }
    }

    private void ApplyAction(int action)
    {
        float throttle;
        float steer;

        switch (action)
        {
            case 0: throttle = 1f;  steer = -1f; break;
            case 1: throttle = 1f;  steer =  0f; break;
            case 2: throttle = 1f;  steer =  1f; break;
            case 3: throttle = -1f; steer = -1f; break;
            case 4: throttle = -1f; steer =  0f; break;
            case 5: throttle = -1f; steer =  1f; break;
            default:
                Debug.LogError("[PPOOnnxAgent] Action ngoài phạm vi 0-5: " + action, this);
                car.SetInputs(0f, 0f, 1f);
                enabled = false;
                return;
        }

        lastAction = action;
        car.SetInputs(throttle, steer, 0f);
    }

    private void OnGUI()
    {
        if (!showHud)
            return;

        if (hudTitleStyle == null)
        {
            hudTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            hudTitleStyle.normal.textColor = Color.yellow;

            hudTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            hudTextStyle.normal.textColor = Color.white;
        }

        const float labelWidth = 280f;
        DrawHudLabel(new Rect(hudPosition.x, hudPosition.y, labelWidth, 20f),
            "PPO ONNX  |  XE TỰ ĐỖ", hudTitleStyle);

        string actionText = lastAction < 0
            ? "Đang khởi tạo"
            : lastAction + " - " + GetActionName(lastAction);
        DrawHudLabel(new Rect(hudPosition.x, hudPosition.y + 19f, labelWidth, 16f),
            "Lượt thử: " + attemptCount + "  |  Hành động: " + actionText, hudTextStyle);

        float speed = body != null ? GetVelocity().magnitude : 0f;
        float distance = GetDistanceToSlot();
        float steeringAngle = car != null && car.frontLeft != null
            ? car.frontLeft.steerAngle
            : 0f;

        DrawHudLabel(new Rect(hudPosition.x, hudPosition.y + 36f, labelWidth, 16f),
            "Tốc độ: " + speed.ToString("F2") + " m/s  |  Ô đỗ: " + distance.ToString("F1") + " m",
            hudTextStyle);
        DrawHudLabel(new Rect(hudPosition.x, hudPosition.y + 53f, labelWidth, 16f),
            "Góc lái: " + steeringAngle.ToString("F1") + "°  |  Sensor: 8", hudTextStyle);
    }

    private void DrawHudLabel(Rect rect, string text, GUIStyle style)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        Rect shadowRect = rect;
        shadowRect.position += Vector2.one;
        GUI.Label(shadowRect, text, style);
        GUI.color = previousColor;
        GUI.Label(rect, text, style);
    }

    private void CreateParkingIndicator()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("[PPOOnnxAgent] Không tìm thấy shader để vẽ viền ô đỗ.", this);
            return;
        }

        var indicatorObject = new GameObject("ParkingSuccessIndicator");
        indicatorObject.transform.SetParent(slot.transform, false);

        parkingIndicator = indicatorObject.AddComponent<LineRenderer>();
        parkingIndicator.useWorldSpace = false;
        parkingIndicator.loop = true;
        parkingIndicator.positionCount = 4;
        parkingIndicator.startWidth = 0.12f;
        parkingIndicator.endWidth = 0.12f;
        parkingIndicator.startColor = Color.white;
        parkingIndicator.endColor = Color.white;
        parkingIndicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        parkingIndicator.receiveShadows = false;
        parkingIndicator.enabled = true;

        parkingIndicatorMaterial = new Material(shader);
        parkingIndicatorMaterial.color = parkingSlotColor;
        parkingIndicator.material = parkingIndicatorMaterial;
        UpdateParkingIndicatorGeometry();
    }

    private void UpdateParkingIndicatorGeometry()
    {
        if (parkingIndicator == null)
            return;

        Vector3 half = slotCollider.size * 0.5f;
        float y = slotCollider.center.y + 0.05f;
        Vector3 center = slotCollider.center;
        parkingIndicator.SetPosition(0, new Vector3(center.x - half.x, y, center.z - half.z));
        parkingIndicator.SetPosition(1, new Vector3(center.x - half.x, y, center.z + half.z));
        parkingIndicator.SetPosition(2, new Vector3(center.x + half.x, y, center.z + half.z));
        parkingIndicator.SetPosition(3, new Vector3(center.x + half.x, y, center.z - half.z));
    }

    private void UpdateParkingIndicator()
    {
        bool parked = IsParked();
        if (parked)
            parkedIndicatorUntil = Time.time + parkedIndicatorDuration;

        if (parkingIndicator != null)
        {
            bool showSuccessColor = parked || Time.time < parkedIndicatorUntil;
            parkingIndicatorMaterial.color = showSuccessColor
                ? parkedSlotColor
                : parkingSlotColor;
        }
    }

    private float GetDistanceToSlot()
    {
        if (slot == null || slotCollider == null)
            return 0f;

        Vector3 slotCenter = slot.transform.TransformPoint(slotCollider.center);
        Vector3 offset = slotCenter - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private static string GetActionName(int action)
    {
        switch (action)
        {
            case 0: return "tiến-trái";
            case 1: return "tiến";
            case 2: return "tiến-phải";
            case 3: return "lùi-trái";
            case 4: return "lùi";
            case 5: return "lùi-phải";
            default: return "không hợp lệ";
        }
    }

    private Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return body.linearVelocity;
#else
        return body.velocity;
#endif
    }

    private bool AssignWheelColliders()
    {
        WheelCollider[] colliders = GetComponentsInChildren<WheelCollider>(true);
        foreach (WheelCollider wheel in colliders)
        {
            switch (wheel.name)
            {
                case "FL": car.frontLeft = wheel; break;
                case "FR": car.frontRight = wheel; break;
                case "RL": car.rearLeft = wheel; break;
                case "RR": car.rearRight = wheel; break;
            }
        }

        bool foundAll = car.frontLeft != null && car.frontRight != null &&
                        car.rearLeft != null && car.rearRight != null;
        if (!foundAll)
            Debug.LogError("[PPOOnnxAgent] Không tìm thấy đủ WheelCollider FL/FR/RL/RR.", this);

        return foundAll;
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        if (automaticRecorder != null)
            automaticRecorder.CancelAutomaticCapture();
#endif
        if (worker != null)
            worker.Dispose();

        if (sensorLineMaterials != null)
        {
            foreach (Material material in sensorLineMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }

        if (parkingIndicator != null)
            Destroy(parkingIndicator.gameObject);
        if (parkingIndicatorMaterial != null)
            Destroy(parkingIndicatorMaterial);
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        if (automaticRecorder != null)
            automaticRecorder.CancelAutomaticCapture();
#endif
    }
}
