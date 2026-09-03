using UnityEngine;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using System.Text.RegularExpressions;

public class NetworkTrafficDisplay : MonoBehaviour
{
    public TextMeshProUGUI rxText;
    public TextMeshProUGUI txText;

    void Start()
    {
        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<StringMsg>("/network_traffic", UpdateNetworkDisplay);
        Debug.Log("✅ 已訂閱 /network_traffic");
    }

    void UpdateNetworkDisplay(StringMsg msg)
    {
        Debug.Log("📨 收到 ROS 訊息：" + msg.data);

        string pattern = @"RX: ([0-9.]+) Mbps, TX: ([0-9.]+) Mbps";
        Match match = Regex.Match(msg.data, pattern);

        if (match.Success)
        {
            string rx = match.Groups[1].Value;
            string tx = match.Groups[2].Value;

            rxText.text = $"RX: {rx} Mbps";
            txText.text = $"TX: {tx} Mbps";

            Debug.Log($"✅ 已更新 HUD：RX={rx} TX={tx}");
        }
        else
        {
            Debug.LogWarning("⚠️ ROS 訊息格式錯誤：" + msg.data);
        }
    }
}
