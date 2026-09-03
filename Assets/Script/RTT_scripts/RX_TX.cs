using UnityEngine;
using TMPro;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using Newtonsoft.Json;
using System.Text;

public class ThroughputDisplay : MonoBehaviour
{
    [Header("What to monitor")]
    public string topicName = "/rtt_pong";   // RX topic
    public PingPublisher txSource;           // 拖 PingPublisher 進來

    [Header("UI")]
    public TMP_Text throughputText;          // 同一塊 Text

    // ----- internal -----
    RosConnector ros;
    double rxBytesThisSec;
    double lastSecTime;

    void Start()
    {
        ros = FindObjectOfType<RosConnector>();
        if (ros == null)
        {
            Debug.LogError("[ThroughputDisplay] 沒找到 RosConnector");
            enabled = false; return;
        }

        // 指定 Header 為型別參數 <T>
        ros.RosSocket.Subscribe<Header>(
            topicName,
            msg => OnRawMessageArrived(msg),
            throttle_rate: 0);

        lastSecTime = Time.time;
    }

    void OnRawMessageArrived(Header msg)
    {
        // 估算訊息 JSON bytes
        string json = JsonConvert.SerializeObject(msg);
        rxBytesThisSec += Encoding.UTF8.GetByteCount(json);
    }

    void Update()
    {
        if (Time.time - lastSecTime >= 1f)
        {
            double rxKBs = rxBytesThisSec / 1024.0;
            double txKBs = txSource != null
                ? txSource.GetAndResetBytesPerSec() / 1024.0
                : 0.0;

            throughputText.text =
                $"RTT: {throughputText.text.Split(' ')[1]}\n" +
                $"RX: {rxKBs:F1} KB/s | TX: {txKBs:F1} KB/s";

            rxBytesThisSec = 0;
            lastSecTime = Time.time;
        }
    }
}

