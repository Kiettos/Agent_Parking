#if UNITY_EDITOR
// Quay video từng episode khi train (chỉ chạy trong Editor).
// Cần package com.unity.recorder (đã cài 5.1.8).
// Gắn component này vào object TrainingArea.
using System.IO;
using Unity.MLAgents;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

[DisallowMultipleComponent]
public class TrainingRecorder : MonoBehaviour
{
    [Header("Lịch quay")]
    [Tooltip("Trong khoảng episode đầu tiên này thì quay dày hơn")]
    public int denseUntilEpisode = 50;
    [Tooltip("Quay mỗi N episode trong giai đoạn đầu")]
    public int denseEvery = 5;
    [Tooltip("Sau giai đoạn đầu thì quay mỗi N episode")]
    public int sparseEvery = 20;

    [Header("Chất lượng (thấp = file nhỏ)")]
    public int outputWidth = 480;
    public int outputHeight = 270;
    public float frameRate = 10f;

    [Header("Tốc độ khi quay")]
    [Tooltip("mlagents-learn chạy timeScale 20; để 1 khi quay nếu muốn video xem được")]
    public float recordTimeScale = 1f;

    [Header("Nơi lưu")]
    public string folder = "Recordings";

    Camera followCamera;
    RecorderController controller;
    MovieRecorderSettings movie;
    int lastEpisode = -1;
    bool recording;
    float savedTimeScale = 1f;

    void Start()
    {
        Build();
        lastEpisode = Academy.Instance.EpisodeCount;
        Debug.Log($"[TrainingRecorder] Sẵn sàng. denseUntil={denseUntilEpisode}, " +
                  $"denseEvery={denseEvery}, sparseEvery={sparseEvery}, " +
                  $"size={outputWidth}x{outputHeight}@{frameRate}fps");
    }

    void Build()
    {
        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.SetRecordModeToManual();
        settings.FrameRate = frameRate;
        settings.CapFrameRate = true;

        controller = new RecorderController(settings);

        movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "TrainingClip";
        movie.Enabled = true;
        movie.OutputFile = folder + "/ep_<Take>";
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

        settings.AddRecorderSettings(movie);
        controller.PrepareRecording();

        followCamera = Camera.main;
    }

    void Update()
    {
        int episode = Academy.Instance.EpisodeCount;
        if (episode == lastEpisode) return;
        lastEpisode = episode;

        if (recording) StopClip();
        if (ShouldRecord(episode)) StartClip(episode);
    }

    bool ShouldRecord(int episode)
    {
        if (denseEvery <= 0 || sparseEvery <= 0) return false;
        if (episode <= denseUntilEpisode) return episode % denseEvery == 0;
        return episode % sparseEvery == 0;
    }

    void StartClip(int episode)
    {
        if (controller == null) return;

        // Bật camera để recorder có hình
        if (followCamera != null) followCamera.enabled = true;

        savedTimeScale = Time.timeScale;
        Time.timeScale = recordTimeScale;

        if (controller.StartRecording())
        {
            recording = true;
            Debug.Log($"[TrainingRecorder] ● REC episode {episode} (timeScale={recordTimeScale})");
        }
    }

    void StopClip()
    {
        if (!recording) return;
        recording = false;

        controller.StopRecording();
        Time.timeScale = savedTimeScale;

        Debug.Log("[TrainingRecorder] ■ STOP");
    }

    void OnDisable()
    {
        if (recording) StopClip();
        if (followCamera != null) followCamera.enabled = false;
    }
}
#endif
