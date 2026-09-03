using UnityEngine;
using RosSharp.RosBridgeClient;

/// <summary>
/// 診斷腳本：檢查 ROS 連接和數據接收狀態
/// </summary>
public class DebugRosConnection : MonoBehaviour
{
    public ImageSubscriberPointCloud cam1RGB;
    public ImageSubscriberPointCloud cam2RGB;
    public DepthImageSubscriber cam1Depth;
    public DepthImageSubscriber cam2Depth;

    private float checkInterval = 2f;
    private float lastCheckTime = 0f;

    void Update()
    {
        if (Time.time - lastCheckTime > checkInterval)
        {
            lastCheckTime = Time.time;
            
            Debug.Log("========== ROS 連接診斷 ==========");
            
            CheckSubscriber("Cam1 RGB", cam1RGB);
            CheckSubscriber("Cam2 RGB", cam2RGB);
            CheckDepthSubscriber("Cam1 Depth", cam1Depth);
            CheckDepthSubscriber("Cam2 Depth", cam2Depth);
            
            Debug.Log("==================================");
        }
    }

    void CheckSubscriber(string name, ImageSubscriberPointCloud sub)
    {
        if (sub == null)
        {
            Debug.LogError($"[{name}] 訂閱器為 NULL！");
            return;
        }

        bool hasData = sub.ImageData != null && sub.ImageData.Length > 0;
        string status = hasData ? $"✓ 有數據 ({sub.ImageData.Length} bytes)" : "✗ 無數據";
        
        if (hasData)
            Debug.Log($"[{name}] {status}");
        else
            Debug.LogWarning($"[{name}] {status} - Topic 可能沒有發布數據");
    }

    void CheckDepthSubscriber(string name, DepthImageSubscriber sub)
    {
        if (sub == null)
        {
            Debug.LogError($"[{name}] 訂閱器為 NULL！");
            return;
        }

        bool hasData = sub.ImageData != null && sub.ImageData.Length > 0;
        string status = hasData ? $"✓ 有數據 ({sub.ImageData.Length} bytes)" : "✗ 無數據";
        
        if (hasData)
            Debug.Log($"[{name}] {status}");
        else
            Debug.LogWarning($"[{name}] {status} - Topic 可能沒有發布數據");
    }
}
