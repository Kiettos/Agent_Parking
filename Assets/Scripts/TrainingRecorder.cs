#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

[DisallowMultipleComponent]
public class TrainingRecorder : MonoBehaviour
{
    [Header("Automatic ONNX capture")]
    [Min(1)] public int firstAttemptsToRecord = 10;
    [Min(1)] public int successfulParksToRecord = 10;
    [Min(1)] public int maxAttemptsForSuccessClip = 100;

    [Header("Video")]
    [Min(320)] public int outputWidth = 1280;
    [Min(180)] public int outputHeight = 720;
    [Min(1f)] public float frameRate = 30f;
    [Min(0.1f)] public float recordTimeScale = 1f;
    public string folder = "Recordings";

    private enum CapturePhase
    {
        Idle,
        FirstAttempts,
        SuccessfulParks,
        Complete
    }

    private CapturePhase phase;
    private RecorderController controller;
    private RecorderControllerSettings controllerSettings;
    private MovieRecorderSettings movie;
    private bool recording;
    private float savedTimeScale = 1f;
    private int firstAttemptCount;
    private int successPhaseAttemptCount;
    private int successCount;
    private string runTimestamp;

    public bool BeginAutomaticCapture()
    {
        if (phase == CapturePhase.FirstAttempts || phase == CapturePhase.SuccessfulParks)
            return false;

        firstAttemptCount = 0;
        successPhaseAttemptCount = 0;
        successCount = 0;
        runTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        phase = CapturePhase.FirstAttempts;

        bool started = StartClip("first_10_attempts");
        if (!started)
            phase = CapturePhase.Complete;
        return started;
    }

    public bool RecordAttemptResult(bool succeeded)
    {
        if (phase == CapturePhase.FirstAttempts)
        {
            firstAttemptCount++;
            Debug.Log($"[TrainingRecorder] Lượt đầu: {firstAttemptCount}/{firstAttemptsToRecord}");

            if (firstAttemptCount >= firstAttemptsToRecord)
            {
                StopClip();
                phase = CapturePhase.SuccessfulParks;
                if (!StartClip("successes_until_10"))
                    phase = CapturePhase.Complete;
            }

            return phase == CapturePhase.Complete;
        }

        if (phase != CapturePhase.SuccessfulParks)
            return phase == CapturePhase.Complete;

        successPhaseAttemptCount++;
        if (succeeded)
            successCount++;

        Debug.Log($"[TrainingRecorder] Lượt {successPhaseAttemptCount}/{maxAttemptsForSuccessClip}; " +
                  $"đỗ thành công {successCount}/{successfulParksToRecord}");
        if (successCount >= successfulParksToRecord)
        {
            StopClip();
            phase = CapturePhase.Complete;
            Debug.Log("[TrainingRecorder] Đã ghi xong 10 lần đỗ thành công.");
            return true;
        }

        if (successPhaseAttemptCount < maxAttemptsForSuccessClip)
            return false;

        StopClip();
        phase = CapturePhase.Complete;
        Debug.LogWarning("[TrainingRecorder] Đã chạm giới hạn lượt thử; video thứ hai được lưu dù chưa đủ số lần đỗ.");
        return true;
    }

    public void CancelAutomaticCapture()
    {
        StopClip();
        phase = CapturePhase.Complete;
    }

    private bool StartClip(string clipName)
    {
        controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = frameRate;
        controllerSettings.CapFrameRate = true;

        controller = new RecorderController(controllerSettings);
        movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = clipName;
        movie.Enabled = true;

        string outputFolder = string.IsNullOrWhiteSpace(folder) ? "Recordings" : folder.Trim();
        Directory.CreateDirectory(outputFolder);
        outputFolder = outputFolder.Replace("\\", "/").TrimEnd('/');
        movie.OutputFile = outputFolder + "/onnx_" + clipName + "_" + runTimestamp;
        movie.FrameRate = frameRate;
        movie.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = outputWidth,
            OutputHeight = outputHeight
        };
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Low
        };

        controllerSettings.AddRecorderSettings(movie);
        controller.PrepareRecording();

        savedTimeScale = Time.timeScale;
        Time.timeScale = recordTimeScale;
        if (!controller.StartRecording())
        {
            Time.timeScale = savedTimeScale;
            Debug.LogError("[TrainingRecorder] Không thể bắt đầu ghi " + clipName);
            return false;
        }

        recording = true;
        Debug.Log("[TrainingRecorder] Đang ghi: " + movie.OutputFile + ".mp4");
        return true;
    }

    private void StopClip()
    {
        if (!recording)
            return;

        recording = false;
        controller.StopRecording();
        Time.timeScale = savedTimeScale;
        Debug.Log("[TrainingRecorder] Đã dừng ghi video.");

        if (movie != null)
            Destroy(movie);
        if (controllerSettings != null)
            Destroy(controllerSettings);

        movie = null;
        controllerSettings = null;
        controller = null;
    }

    private void OnDisable()
    {
        StopClip();
        if (phase != CapturePhase.Complete)
            phase = CapturePhase.Idle;
    }
}
#endif
