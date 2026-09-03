using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // 添加 TMPro 支援
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;

/// <summary>
/// Unity 飛行數據接收腳本 - TMPro 版本
/// 接收來自 Orin 的飛行數據並更新 UI 顯示
/// 對應話題: /unity/flight_data
/// </summary>
public class FlightDataReceiver : MonoBehaviour
{
    [Header("ROS 連接設定")]
    public RosConnector rosConnector;
    public string flightDataTopic = "/unity/flight_data";
    
    [Header("UI 元件引用 - TMPro")]
    [SerializeField] private TextMeshProUGUI imuText;
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private TextMeshProUGUI gpsText;
    [SerializeField] private TextMeshProUGUI altitudeText;
    
    [Header("狀態顯示 - TMPro")]
    [SerializeField] private TextMeshProUGUI connectionStatusText;
    [SerializeField] private TextMeshProUGUI flightModeText;
    [SerializeField] private Image armedIndicator;
    [SerializeField] private Color armedColor = Color.green;
    [SerializeField] private Color disarmedColor = Color.red;
    
    [Header("數據更新頻率")]
    [SerializeField] private float updateInterval = 0.1f; // 10Hz 更新
    
    // 內部變數
    private string subscriberID;
    private FlightData currentFlightData;
    private bool isConnected = false;
    private float lastUpdateTime = 0f;
    
    // 飛行數據結構
    [System.Serializable]
    public class FlightData
    {
        public double timestamp;
        public double mission_time;
        public GPSData gps;
        public IMUData imu;
        public SpeedData speed;
        public AltitudeData altitude;
        public StatusData status;
    }
    
    [System.Serializable]
    public class GPSData
    {
        public double lat;
        public double lon;
        public double alt;
        public double speed;
    }
    
    [System.Serializable]
    public class IMUData
    {
        public double roll;
        public double pitch;
        public double yaw;
    }
    
    [System.Serializable]
    public class SpeedData
    {
        public double ground;
        public double vertical;
    }
    
    [System.Serializable]
    public class AltitudeData
    {
        public double relative;
        public double absolute;
    }
    
    [System.Serializable]
    public class StatusData
    {
        public bool armed;
        public string mode;
        public string connection;
    }

    void Start()
    {
        InitializeFlightDataReceiver();
    }
    
    void Update()
    {
        // 控制 UI 更新頻率
        if (Time.time - lastUpdateTime >= updateInterval && currentFlightData != null)
        {
            UpdateUI();
            lastUpdateTime = Time.time;
        }
    }
    
    /// <summary>
    /// 初始化飛行數據接收器
    /// </summary>
    private void InitializeFlightDataReceiver()
    {
        try
        {
            // 檢查 ROS 連接器
            if (rosConnector == null)
            {
                rosConnector = FindObjectOfType<RosConnector>();
                if (rosConnector == null)
                {
                    Debug.LogError("未找到 RosConnector！請確保場景中有 RosConnector 組件");
                    return;
                }
            }
            
            // 等待 ROS 連接
            StartCoroutine(WaitForRosConnection());
            
            Debug.Log("飛行數據接收器初始化完成 (TMPro)");
        }
        catch (Exception e)
        {
            Debug.LogError($"飛行數據接收器初始化失敗: {e.Message}");
        }
    }
    
    /// <summary>
    /// 等待 ROS 連接並訂閱話題
    /// </summary>
    private IEnumerator WaitForRosConnection()
    {
        // 等待 ROS 連接建立
        while (rosConnector != null && !rosConnector.IsConnected.WaitOne(0))
        {
            Debug.Log("等待 ROS 連接...");
            yield return new WaitForSeconds(1f);
        }
        
        // 訂閱飛行數據話題
        SubscribeToFlightData();
    }
    
    /// <summary>
    /// 訂閱飛行數據話題
    /// </summary>
    private void SubscribeToFlightData()
    {
        try
        {
            if (rosConnector == null || rosConnector.RosSocket == null)
            {
                Debug.LogError("ROS 連接器或 Socket 為 null");
                return;
            }
            
            subscriberID = rosConnector.RosSocket.Subscribe<RosSharp.RosBridgeClient.MessageTypes.Std.String>(
                flightDataTopic, 
                OnFlightDataReceived
            );
            
            isConnected = true;
            Debug.Log($"成功訂閱飛行數據話題: {flightDataTopic}");
            Debug.Log($"訂閱者 ID: {subscriberID}");
        }
        catch (Exception e)
        {
            Debug.LogError($"訂閱飛行數據話題失敗: {e.Message}");
            isConnected = false;
        }
    }
    
    /// <summary>
    /// 飛行數據接收回調函數
    /// </summary>
    private void OnFlightDataReceived(RosSharp.RosBridgeClient.MessageTypes.Std.String message)
    {
        try
        {
            // 解析 JSON 數據
            currentFlightData = JsonUtility.FromJson<FlightData>(message.data);
            
            // 驗證數據
            if (currentFlightData != null)
            {
                // 數據接收成功
                if (!isConnected)
                {
                    isConnected = true;
                    Debug.Log("飛行數據連接已恢復");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"飛行數據解析錯誤: {e.Message}");
            isConnected = false;
        }
    }
    
    /// <summary>
    /// 更新 UI 界面 - TMPro 版本
    /// </summary>
    private void UpdateUI()
    {
        if (currentFlightData == null) return;
        
        try
        {
            // 更新 IMU 顯示
            if (imuText != null)
            {
                imuText.text = $"Roll: {currentFlightData.imu.roll:F1}°\n" +
                              $"Pitch: {currentFlightData.imu.pitch:F1}°\n" +
                              $"Yaw: {currentFlightData.imu.yaw:F1}°";
            }
            
            // 更新 Speed 顯示
            if (speedText != null)
            {
                speedText.text = $"Ground: {currentFlightData.speed.ground:F1} m/s\n" +
                                $"Vertical: {currentFlightData.speed.vertical:F1} m/s";
            }
            
            // 更新 GPS 顯示
            if (gpsText != null)
            {
                gpsText.text = $"Lat: {currentFlightData.gps.lat:F6}\n" +
                              $"Lon: {currentFlightData.gps.lon:F6}\n" +
                              $"Speed: {currentFlightData.gps.speed:F1} m/s";
            }
            
            // 更新 Altitude 顯示
            if (altitudeText != null)
            {
                altitudeText.text = $"Absolute: {currentFlightData.altitude.absolute:F1} m\n" +
                                   $"Relative: {currentFlightData.altitude.relative:F1} m";
            }
            
            // 更新狀態顯示
            UpdateStatusDisplay();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"UI 更新錯誤: {e.Message}");
        }
    }
    
    /// <summary>
    /// 更新狀態顯示 - TMPro 版本
    /// </summary>
    private void UpdateStatusDisplay()
    {
        if (currentFlightData?.status == null) return;
        
        // 連接狀態
        if (connectionStatusText != null)
        {
            string statusColor = currentFlightData.status.connection == "CONNECTED" ? "green" : 
                               currentFlightData.status.connection == "SIMULATION" ? "yellow" : "red";
            connectionStatusText.text = $"<color={statusColor}>{currentFlightData.status.connection}</color>";
        }
        
        // 飛行模式
        if (flightModeText != null)
        {
            flightModeText.text = currentFlightData.status.mode;
        }
        
        // 解鎖狀態指示器
        if (armedIndicator != null)
        {
            armedIndicator.color = currentFlightData.status.armed ? armedColor : disarmedColor;
        }
    }
    
    /// <summary>
    /// 獲取當前飛行數據 (供其他腳本使用)
    /// </summary>
    public FlightData GetCurrentFlightData()
    {
        return currentFlightData;
    }
    
    /// <summary>
    /// 檢查數據連接狀態
    /// </summary>
    public bool IsDataConnected()
    {
        return isConnected && currentFlightData != null;
    }
    
    /// <summary>
    /// 獲取任務時間
    /// </summary>
    public double GetMissionTime()
    {
        return currentFlightData?.mission_time ?? 0.0;
    }
    
    /// <summary>
    /// 手動重新連接
    /// </summary>
    public void ReconnectFlightData()
    {
        if (rosConnector != null && rosConnector.IsConnected.WaitOne(0))
        {
            // 取消訂閱
            if (!string.IsNullOrEmpty(subscriberID))
            {
                rosConnector.RosSocket.Unsubscribe(subscriberID);
            }
            
            // 重新訂閱
            SubscribeToFlightData();
        }
    }
    
    void OnDestroy()
    {
        // 清理訂閱
        if (rosConnector != null && !string.IsNullOrEmpty(subscriberID))
        {
            try
            {
                rosConnector.RosSocket.Unsubscribe(subscriberID);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"取消訂閱時發生錯誤: {e.Message}");
            }
        }
    }
    
    // 調試用方法
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        
        // 在屏幕右上角顯示連接狀態
        GUI.color = isConnected ? Color.green : Color.red;
        GUI.Label(new Rect(Screen.width - 200, 10, 190, 30), 
                 $"飛行數據: {(isConnected ? "已連接" : "未連接")}");
        GUI.color = Color.white;
        
        // 顯示數據時間戳
        if (currentFlightData != null)
        {
            GUI.Label(new Rect(Screen.width - 200, 40, 190, 30), 
                     $"任務時間: {currentFlightData.mission_time:F1}s");
        }
    }
}