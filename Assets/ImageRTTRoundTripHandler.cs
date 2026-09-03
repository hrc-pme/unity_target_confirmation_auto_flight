using UnityEngine;
using TMPro;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using System.Collections;

public class ImageRTTRoundTripHandler : MonoBehaviour
{
    [Header("TextMeshPro UI 元件")]
    public TextMeshProUGUI rttText;
    public TextMeshProUGUI fpsText;

    private ROSConnection ros;

    private string startTopic = "/twist_image_start";
    private string ackTopic = "/twist_image_ack";
    private string resultTopic = "/image_rtt_result";

    // FPS 統計變數
    private int rttMsgCount = 0;
    private float fpsTimer = 0f;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();

        if (ros == null)
        {
            Debug.LogError("❌ ROSConnection 尚未啟動！");
            return;
        }

        ros.Subscribe<TwistStampedMsg>(startTopic, OnReceiveStart);
        ros.Subscribe<Float32Msg>(resultTopic, OnReceiveRTT);
        ros.RegisterPublisher<TwistStampedMsg>(ackTopic);

        Debug.Log($"🟡 已註冊 publisher: {ackTopic}，已訂閱: {startTopic}、{resultTopic}");
    }

    void OnReceiveStart(TwistStampedMsg msg)
    {
        Debug.Log($"✅ 收到 {startTopic}，時間戳：{msg.header.stamp.sec}.{msg.header.stamp.nanosec}");
        StartCoroutine(DelayedAckPublish(msg));
    }

    IEnumerator DelayedAckPublish(TwistStampedMsg msg)
    {
        yield return new WaitForSeconds(0.05f); // 延遲 50 毫秒
        ros.Publish(ackTopic, msg);
        Debug.Log($"📤 延遲回傳 {ackTopic}，frame_id: {msg.header.frame_id}");
    }

    void OnReceiveRTT(Float32Msg msg)
    {
        float rtt = msg.data;

        // 顯示 RTT
        if (rttText != null)
        {
            rttText.text = $"RTT (image): {rtt:F1} ms";
        }
        else
        {
            Debug.LogWarning("⚠️ rttText 尚未綁定！");
        }

        // 統計 FPS
        rttMsgCount++;

        Debug.Log($"📥 收到 {resultTopic}：{rtt:F1} ms");
    }

    void Update()
    {
        fpsTimer += Time.deltaTime;

        if (fpsTimer >= 1.0f)
        {
            if (fpsText != null)
            {
                if (rttMsgCount > 0)
                {
                    float avgFrameTimeMs = 1000f / rttMsgCount;
                    fpsText.text = $"FPS: {avgFrameTimeMs:F1} ms";
                }
                else
                {
                    fpsText.text = $"FPS: -- ms";
                }
            }
            else
            {
                Debug.LogWarning("⚠️ fpsText 尚未綁定！");
            }

            // 重設計數器
            rttMsgCount = 0;
            fpsTimer = 0f;
        }
    }
}
