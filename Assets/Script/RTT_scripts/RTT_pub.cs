using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;   // Header
using Newtonsoft.Json;
using System;
using System.Text;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class PingPublisher : UnityPublisher<Header>
{
    ROSConnection ros;
    const double PING_INTERVAL = 0.1;  // 10 Hz

    Header pingMsg;
    double nextPingTime;

    // --- TX throughput 累加 ---
    double txBytesThisSec;

    static readonly DateTime UnixEpoch =
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    protected override void Start()
    {
        base.Start();
        pingMsg = new Header { frame_id = "UnityPing" };
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<StringMsg>("/network_test/request");
    }

    void Update()
    {
        if (Time.time >= nextPingTime)
        {
            WriteNowToStamp(pingMsg);

            // ★ 估算訊息大小（用 JSON 長度）
            string json = JsonConvert.SerializeObject(pingMsg);
            txBytesThisSec += Encoding.UTF8.GetByteCount(json);

            Publish(pingMsg);
            nextPingTime = Time.time + PING_INTERVAL;
        }
    }

    // 讓別人（ThroughputDisplay）來拿這秒累積多少 bytes
    public double GetAndResetBytesPerSec()
    {
        double tmp = txBytesThisSec;
        txBytesThisSec = 0;
        return tmp;
    }

    // ------ 共用工具：寫現在 UTC 到 stamp ------
    static void WriteNowToStamp(Header hdr)
    {
        var utc = DateTime.UtcNow;
        var diff = utc - UnixEpoch;

        hdr.stamp.sec  = (int)diff.TotalSeconds;                                   // int32
        hdr.stamp.nanosec =
            (uint)((diff.Ticks % TimeSpan.TicksPerSecond) * 100);                  // uint32
    }
    public void SendCommand()
    {
        StringMsg cmd = new StringMsg("trigger");
        ros.Publish("/control_command", cmd);
        Debug.Log("發送 trigger 至 ROS2");
    }
}
