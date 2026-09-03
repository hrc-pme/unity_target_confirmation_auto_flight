using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using System;
using System.IO;
using System.Text.RegularExpressions;

public class DetectionDataReceiver : UnitySubscriber<RosSharp.RosBridgeClient.MessageTypes.Std.String>
{
    [Header("檢測數據記錄")]
    public bool saveDetectionLog = true;
    public string logFileName = "detection_log.json";
    
    [Header("調試資訊")]
    public bool showDebugInfo = true;
    
    [Header("📊 統計信息")]
    public int totalDetectionsReceived = 0;
    public int parseErrorCount = 0;
    public float lastUpdateTime = 0f;
    public int latestDetectionCount = 0;
    
    private List<DetectionData> detectionHistory = new List<DetectionData>();
    private double lastLogT = 0;
    
    protected override void Start()
    {
        base.Start();
        Debug.Log("✅ DetectionDataReceiver: 開始接收Orin檢測數據");
        Debug.Log($"📡 DetectionDataReceiver: 訂閱話題 - {Topic}");
    }
    
    protected override void ReceiveMessage(RosSharp.RosBridgeClient.MessageTypes.Std.String message)
    {
        try
        {
            if (message == null || string.IsNullOrEmpty(message.data))
            {
                Debug.LogWarning("⚠️ DetectionDataReceiver: 收到空消息");
                return;
            }

            // ✅ 原始數據日誌（調試用，每 2 秒輸出一次）
            if (Time.timeAsDouble - lastLogT > 2.0)
            {
                Debug.Log($"[DetectionDataReceiver] 原始 JSON:\n{message.data}");
                lastLogT = Time.timeAsDouble;
            }

            // ✅ 使用健壯的 JSON 解析
            DetectionMessage detectionMsg = ParseDetectionData(message.data);

            if (detectionMsg == null)
            {
                parseErrorCount++;
                Debug.LogError($"❌ DetectionDataReceiver: 解析失敗 (第 {parseErrorCount} 次)");
                return;
            }

            // 處理檢測數據
            ProcessDetectionData(detectionMsg);

            // 記錄到檔案（可選）
            if (saveDetectionLog)
            {
                SaveDetectionToLog(detectionMsg);
            }

            totalDetectionsReceived++;
            lastUpdateTime = Time.time;

            // ✅ 成功日誌（每 2 秒輸出一次）
            if (Time.timeAsDouble - lastLogT > 2.0)
            {
                Debug.Log(
                    $"✅ [DetectionDataReceiver] 成功解析\n" +
                    $"  相機: {detectionMsg.camera_id}\n" +
                    $"  檢測數: {latestDetectionCount}\n" +
                    $"  時戳: {detectionMsg.timestamp:F4}\n" +
                    $"  總收到: {totalDetectionsReceived} | 錯誤: {parseErrorCount}"
                );
                lastLogT = Time.timeAsDouble;
            }
        }
        catch (Exception e)
        {
            parseErrorCount++;
            Debug.LogError(
                $"❌ DetectionDataReceiver: 異常 - {e.GetType().Name}\n" +
                $"  消息: {e.Message}\n" +
                $"  堆棧: {e.StackTrace}"
            );
        }
    }

    /// <summary>
    /// 健壯的 JSON 解析 - 先用 JsonUtility，失敗則手動解析
    /// </summary>
    private DetectionMessage ParseDetectionData(string jsonStr)
    {
        try
        {
            // 第 1 層：嘗試 JsonUtility（快速）
            var wrapper = JsonUtility.FromJson<DetectionMessageWrapper>(jsonStr);
            if (wrapper?.data != null)
            {
                return wrapper.data;
            }

            // 第 2 層：嘗試直接解析
            DetectionMessage result = JsonUtility.FromJson<DetectionMessage>(jsonStr);
            if (result != null)
            {
                return result;
            }

            Debug.LogWarning("⚠️ JsonUtility 解析失敗，嘗試手動解析");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"⚠️ JsonUtility 解析失敗: {e.Message}");
        }

        // 降級方案：手動解析
        try
        {
            return ParseDetectionDataManual(jsonStr);
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 手動解析也失敗: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 手動解析 JSON - 當 JsonUtility 失敗時的降級方案
    /// </summary>
    private DetectionMessage ParseDetectionDataManual(string jsonStr)
    {
        var data = new DetectionMessage();

        // 提取基本欄位
        data.camera_id = ExtractString(jsonStr, "camera_id");
        data.timestamp = ExtractDouble(jsonStr, "timestamp");
        data.utc_time = ExtractString(jsonStr, "utc_time");
        data.mission_elapsed = ExtractDouble(jsonStr, "mission_elapsed");

        // 提取 detections 陣列
        data.detections = ParseDetectionsArray(ExtractJsonArray(jsonStr, "detections"));

        // 提取 flight_integration
        var flightJson = ExtractJsonObject(jsonStr, "flight_integration");
        if (!string.IsNullOrEmpty(flightJson))
        {
            data.flight_integration = new FlightIntegration
            {
                enabled = ExtractBool(flightJson, "enabled"),
                sync_method = ExtractString(flightJson, "sync_method"),
                time_format = ExtractString(flightJson, "time_format")
            };
        }

        return data;
    }

    /// <summary>
    /// 解析 detections 陣列
    /// </summary>
    private Detection[] ParseDetectionsArray(string arrayJson)
    {
        if (string.IsNullOrEmpty(arrayJson))
            return Array.Empty<Detection>();

        var detections = new List<Detection>();
        arrayJson = arrayJson.Trim();

        if (arrayJson.StartsWith("[") && arrayJson.EndsWith("]"))
        {
            arrayJson = arrayJson.Substring(1, arrayJson.Length - 2);
        }

        // 按物體分割
        string[] objects = arrayJson.Split(new string[] { "},{" }, StringSplitOptions.None);

        foreach (var obj in objects)
        {
            try
            {
                var cleaned = obj.Trim();
                if (!cleaned.StartsWith("{")) cleaned = "{" + cleaned;
                if (!cleaned.EndsWith("}")) cleaned = cleaned + "}";

                var det = new Detection
                {
                    class_name = ExtractString(cleaned, "class_name"),
                    class_id = ExtractInt(cleaned, "class_id"),
                    confidence = ExtractFloat(cleaned, "confidence"),
                    bbox = ExtractFloatArray(cleaned, "bbox"),
                    center_pixel = ExtractFloatArray(cleaned, "center_pixel"),
                    camera_id = ExtractString(cleaned, "camera_id"),
                    timestamp = ExtractDouble(cleaned, "timestamp"),
                    mission_time = ExtractDouble(cleaned, "mission_time")
                };

                detections.Add(det);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"⚠️ 解析單個檢測失敗: {e.Message}");
            }
        }

        latestDetectionCount = detections.Count;
        return detections.ToArray();
    }

    // ==================== 輔助函數 ====================

    private string ExtractString(string json, string key)
    {
        try
        {
            string pattern = $"\"{key}\":\"([^\"]*)\"";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : "";
        }
        catch
        {
            return "";
        }
    }

    private double ExtractDouble(string json, string key)
    {
        try
        {
            string pattern = $"\"{key}\":([\\d.]+)";
            var match = Regex.Match(json, pattern);
            return match.Success && double.TryParse(match.Groups[1].Value, out var val) ? val : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    private float ExtractFloat(string json, string key)
    {
        try
        {
            string pattern = $"\"{key}\":([\\d.]+)";
            var match = Regex.Match(json, pattern);
            return match.Success && float.TryParse(match.Groups[1].Value, out var val) ? val : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private int ExtractInt(string json, string key)
    {
        try
        {
            string pattern = $"\"{key}\":(\\d+)";
            var match = Regex.Match(json, pattern);
            return match.Success && int.TryParse(match.Groups[1].Value, out var val) ? val : 0;
        }
        catch
        {
            return 0;
        }
    }

    private bool ExtractBool(string json, string key)
    {
        try
        {
            string pattern = $"\"{key}\":(true|false)";
            var match = Regex.Match(json, pattern);
            return match.Success && bool.TryParse(match.Groups[1].Value, out var val) ? val : false;
        }
        catch
        {
            return false;
        }
    }

    private float[] ExtractFloatArray(string json, string key)
    {
        try
        {
            string pattern = $"\"{key}\":\\[([^\\]]*)\\]";
            var match = Regex.Match(json, pattern);

            if (!match.Success)
                return Array.Empty<float>();

            var values = match.Groups[1].Value.Split(',');
            var result = new float[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                if (float.TryParse(values[i].Trim(), out var val))
                    result[i] = val;
            }

            return result;
        }
        catch
        {
            return Array.Empty<float>();
        }
    }

    private string ExtractJsonObject(string json, string key)
    {
        try
        {
            int startIdx = json.IndexOf($"\"{key}\":");
            if (startIdx == -1)
                return "";

            startIdx = json.IndexOf('{', startIdx);
            if (startIdx == -1)
                return "";

            int braceCount = 0;
            int endIdx = startIdx;

            for (int i = startIdx; i < json.Length; i++)
            {
                if (json[i] == '{')
                    braceCount++;
                else if (json[i] == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        endIdx = i + 1;
                        break;
                    }
                }
            }

            return json.Substring(startIdx, endIdx - startIdx);
        }
        catch
        {
            return "";
        }
    }

    private string ExtractJsonArray(string json, string key)
    {
        try
        {
            int startIdx = json.IndexOf($"\"{key}\":");
            if (startIdx == -1)
                return "";

            startIdx = json.IndexOf('[', startIdx);
            if (startIdx == -1)
                return "";

            int bracketCount = 0;
            int endIdx = startIdx;

            for (int i = startIdx; i < json.Length; i++)
            {
                if (json[i] == '[')
                    bracketCount++;
                else if (json[i] == ']')
                {
                    bracketCount--;
                    if (bracketCount == 0)
                    {
                        endIdx = i + 1;
                        break;
                    }
                }
            }

            return json.Substring(startIdx, endIdx - startIdx);
        }
        catch
        {
            return "";
        }
    }

    // ==================== 數據處理 ====================

    private void ProcessDetectionData(DetectionMessage data)
    {
        if (data.detections == null || data.detections.Length == 0)
        {
            Debug.Log($"📷 {data.camera_id}: 0 個物體 | 時間: {data.timestamp:F4}");
            latestDetectionCount = 0;
            return;
        }

        latestDetectionCount = data.detections.Length;

        if (Time.timeAsDouble - lastLogT > 2.0)
        {
            Debug.Log($"📷 {data.camera_id}: {data.detections.Length} 個物體 | 時間: {data.timestamp:F4}");

            foreach (var detection in data.detections)
            {
                Debug.Log(
                    $"  ✓ {detection.class_name} (ID:{detection.class_id}) | " +
                    $"信心度: {detection.confidence:F2} | " +
                    $"位置: [{detection.bbox[0]:F0},{detection.bbox[1]:F0},{detection.bbox[2]:F0},{detection.bbox[3]:F0}]"
                );
            }
        }
    }

    private void SaveDetectionToLog(DetectionMessage data)
    {
        if (data.detections == null)
            return;

        DetectionData logData = new DetectionData
        {
            timestamp = data.timestamp,
            camera_id = data.camera_id,
            detections = data.detections,
            utc_time = data.utc_time,
            mission_elapsed = data.mission_elapsed
        };

        detectionHistory.Add(logData);

        // 每 50 筆記錄就存檔一次
        if (detectionHistory.Count % 50 == 0)
        {
            WriteLogToFile();
        }
    }

    private void WriteLogToFile()
    {
        try
        {
            DetectionLogWrapper wrapper = new DetectionLogWrapper
            {
                detections = detectionHistory.ToArray(),
                total_count = detectionHistory.Count,
                last_updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            string jsonLog = JsonUtility.ToJson(wrapper, true);
            string filePath = Path.Combine(Application.persistentDataPath, logFileName);
            File.WriteAllText(filePath, jsonLog);

            Debug.Log($"✅ 檢測記錄已保存: {detectionHistory.Count} 筆");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 保存記錄失敗: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        if (detectionHistory.Count > 0)
        {
            WriteLogToFile();
            Debug.Log($"✅ 程式結束 - 最終保存 {detectionHistory.Count} 筆記錄");
        }
    }

    [ContextMenu("手動保存記錄")]
    public void ManualSave()
    {
        if (detectionHistory.Count > 0)
        {
            WriteLogToFile();
            Debug.Log($"✅ 手動保存成功: {detectionHistory.Count} 筆");
        }
        else
        {
            Debug.Log("⚠️ 沒有記錄可保存");
        }
    }

    [ContextMenu("清除記錄")]
    public void ClearHistory()
    {
        detectionHistory.Clear();
        totalDetectionsReceived = 0;
        parseErrorCount = 0;
        latestDetectionCount = 0;
        Debug.Log("✅ 記錄已清除");
    }

    [ContextMenu("顯示統計")]
    public void ShowStats()
    {
        Debug.Log($"📊 DetectionDataReceiver 統計:");
        Debug.Log($"  ✓ 收到訊息: {totalDetectionsReceived}");
        Debug.Log($"  ✗ 解析錯誤: {parseErrorCount}");
        Debug.Log($"  📝 記錄總數: {detectionHistory.Count}");
        Debug.Log($"  📷 最後檢測: {latestDetectionCount} 個物體");
        Debug.Log($"  📁 檔案位置: {Path.Combine(Application.persistentDataPath, logFileName)}");
    }
}

// ==================== 數據結構定義 ====================

[System.Serializable]
public class DetectionMessage
{
    public string camera_id;
    public double timestamp;
    public string utc_time;
    public double mission_elapsed;
    public Detection[] detections;
    public FlightIntegration flight_integration;
}

[System.Serializable]
public class Detection
{
    public string class_name;
    public int class_id;
    public float confidence;
    public float[] bbox;           // [x1, y1, x2, y2]
    public float[] center_pixel;   // [center_x, center_y]
    public string camera_id;
    public double timestamp;
    public double mission_time;
}

[System.Serializable]
public class FlightIntegration
{
    public bool enabled;
    public string sync_method;
    public string time_format;
}

[System.Serializable]
public class DetectionData
{
    public double timestamp;
    public string camera_id;
    public Detection[] detections;
    public string utc_time;
    public double mission_elapsed;
}

[System.Serializable]
public class DetectionLogWrapper
{
    public DetectionData[] detections;
    public int total_count;
    public string last_updated;
}

// 用於 JsonUtility 的包裝類
[System.Serializable]
public class DetectionMessageWrapper
{
    public DetectionMessage data;
}