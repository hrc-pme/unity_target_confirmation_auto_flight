using UnityEngine;
using TMPro;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;   // Header
using System;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Std;
public class PongLatencyDisplay : UnitySubscriber<Header>
{
    ROSConnection ros;
    [Header("UI")]
    [Tooltip("拖曳一個 TextMeshPro 文字元件到這裡")]
    public TMP_Text rttText;
    [Header("Smoothing")]
    [Tooltip("0 表示不平滑；0.1–0.3 之間可稍微去抖動")]
    [Range(0f, 1f)]
    public float smoothFactor = 0.2f;
    // --- 內部狀態 ---
    const long TicksPerNs = 100L;  // 1 tick = 100 ns
    static readonly DateTime UnixEpoch =
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    double emaLatency;   // 指數移動平均 (Exponential Moving Average)
    // --------------------------------------------------
    // UnitySubscriber 生命周期
    // --------------------------------------------------
    protected override void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<StringMsg>("/network_test/response", ReceiveControlResponse);
        base.Start();     // 必要：向 RosConnector 註冊
        if (rttText == null)
            Debug.LogWarning("[PongLatencyDisplay] rttText 未指定，畫面上不會看到 RTT 數字。");
    }
    // 每次收到 /rtt_pong 訊息就會呼叫這裡
    protected override void ReceiveMessage(Header msg)
    {
        // 1. 解析 ROS time → DateTime
        long rosNs = (long)msg.stamp.sec * 1_000_000_000L + (long)msg.stamp.nanosec;
        DateTime sendUtc = UnixEpoch.AddTicks(rosNs / TicksPerNs);
        // 2. 計算 RTT（毫秒）
        double rttMs = (DateTime.UtcNow - sendUtc).TotalMilliseconds;
        // 3. 平滑處理（可選）
        if (smoothFactor > 0f && smoothFactor < 1f)
            emaLatency = emaLatency == 0 ? rttMs :
                         (1 - smoothFactor) * emaLatency + smoothFactor * rttMs;
        else
            emaLatency = rttMs;
        // 4. 更新 UI
        if (rttText != null)
            rttText.text = $"RTT: {emaLatency:F1} ms";
    }
    void ReceiveControlResponse(StringMsg msg)
    {
        Debug.Log("回應資料收到：" + msg.data);
        // 可以將 msg.data 拆解後更新 Unity 顯示的 UI 或狀態
    }

}