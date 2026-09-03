using UnityEngine;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.UI;

public class CustomTCPReceiver : MonoBehaviour
{
    [Header("=== 連接設定 ===")]
    [Tooltip("Orin Bridge IP")]
    public string orinIP = "10.0.0.8";
    
    [Tooltip("Orin Bridge 端口")]
    public int orinPort = 10000;
    
    [Tooltip("啟動時自動連接")]
    public bool autoConnect = true;

    [Header("=== 影像顯示 ===")]
    public RawImage cam1RGBDisplay;
    public RawImage cam1DepthDisplay;
    public RawImage cam2RGBDisplay;
    public RawImage cam2DepthDisplay;

    [Header("=== 3D 材質 ===")]
    public Material cam1RGBMaterial;
    public Material cam2RGBMaterial;

    [Header("=== 統計顯示 ===")]
    public Text statusText;
    public Text fpsText;

    [Header("=== 狀態 ===")]
    public bool isConnected = false;
    public string connectionStatus = "未連接";
    public float cam1RGBFPS = 0f;
    public float cam2RGBFPS = 0f;
    public int totalDetections = 0;

    // TCP 客戶端
    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool shouldRun = false;

    // 紋理
    private Texture2D cam1RGBTexture;
    private Texture2D cam1DepthTexture;
    private Texture2D cam2RGBTexture;
    private Texture2D cam2DepthTexture;

    // 主執行緒更新隊列
    private Queue<Action> mainThreadActions = new Queue<Action>();
    private object actionLock = new object();

    // FPS 計算
    private Dictionary<string, int> frameCounters = new Dictionary<string, int>();
    private float lastFPSUpdate = 0f;

    // 統計
    private int totalMessagesReceived = 0;
    private float lastStatsPrint = 0f;

    void Start()
    {
        Debug.Log("╔════════════════════════════════════════╗");
        Debug.Log("║    Unity Custom TCP Receiver 啟動     ║");
        Debug.Log("╚════════════════════════════════════════╝");
        Debug.Log($"Target: {orinIP}:{orinPort}");

        InitializeTextures();
        InitializeFrameCounters();

        if (autoConnect)
            ConnectToOrin();
    }

    void InitializeTextures()
    {
        cam1RGBTexture = new Texture2D(640, 480, TextureFormat.RGB24, false);
        cam1DepthTexture = new Texture2D(640, 480, TextureFormat.R16, false);
        cam2RGBTexture = new Texture2D(640, 480, TextureFormat.RGB24, false);
        cam2DepthTexture = new Texture2D(640, 480, TextureFormat.R16, false);

        if (cam1RGBDisplay) cam1RGBDisplay.texture = cam1RGBTexture;
        if (cam1DepthDisplay) cam1DepthDisplay.texture = cam1DepthTexture;
        if (cam2RGBDisplay) cam2RGBDisplay.texture = cam2RGBTexture;
        if (cam2DepthDisplay) cam2DepthDisplay.texture = cam2DepthTexture;

        if (cam1RGBMaterial) cam1RGBMaterial.mainTexture = cam1RGBTexture;
        if (cam2RGBMaterial) cam2RGBMaterial.mainTexture = cam2RGBTexture;

        Debug.Log("✓ 紋理初始化完成");
    }

    void InitializeFrameCounters()
    {
        frameCounters["cam1_rgb"] = 0;
        frameCounters["cam2_rgb"] = 0;
    }

    public void ConnectToOrin()
    {
        if (isConnected)
        {
            Debug.LogWarning("已經連接");
            return;
        }

        try
        {
            Debug.Log($"正在連接到 Orin: {orinIP}:{orinPort}...");
            
            tcpClient = new TcpClient();
            tcpClient.Connect(orinIP, orinPort);
            stream = tcpClient.GetStream();

            isConnected = true;
            shouldRun = true;
            connectionStatus = "已連接";

            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("✓ 成功連接到 Orin");
            Debug.Log("✓ 開始接收影像數據...");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 連接失敗: {e.Message}");
            isConnected = false;
            connectionStatus = $"錯誤: {e.Message}";
        }
    }

    void ReceiveData()
    {
        Debug.Log("接收執行緒啟動");
        byte[] lengthBuffer = new byte[4];

        while (shouldRun && stream != null)
        {
            try
            {
                int bytesRead = stream.Read(lengthBuffer, 0, 4);
                if (bytesRead != 4) break;

                int messageLength = (lengthBuffer[0] << 24) |
                                    (lengthBuffer[1] << 16) |
                                    (lengthBuffer[2] << 8) |
                                    lengthBuffer[3];

                if (messageLength <= 0 || messageLength > 10 * 1024 * 1024)
                {
                    Debug.LogError($"異常訊息長度: {messageLength}");
                    continue;
                }

                byte[] messageBuffer = new byte[messageLength];
                int totalRead = 0;
                while (totalRead < messageLength)
                {
                    int read = stream.Read(messageBuffer, totalRead, messageLength - totalRead);
                    if (read == 0)
                    {
                        shouldRun = false;
                        break;
                    }
                    totalRead += read;
                }

                if (totalRead == messageLength)
                {
                    string json = Encoding.UTF8.GetString(messageBuffer);
                    ProcessMessage(json);
                    totalMessagesReceived++;
                }
            }
            catch (Exception e)
            {
                if (shouldRun)
                    Debug.LogError($"接收錯誤: {e.Message}");
                break;
            }
        }

        Debug.Log("接收執行緒結束");
        isConnected = false;
        connectionStatus = "已斷線";
    }

    void ProcessMessage(string json)
    {
        try
        {
            var msg = JsonConvert.DeserializeObject<ROSMessage>(json);
            if (msg == null) return;

            switch (msg.type)
            {
                case "CompressedImage":
                    ProcessImageMessage(msg);
                    break;
                case "String":
                    ProcessStringMessage(msg);
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"訊息處理錯誤: {e.Message}");
        }
    }

    void ProcessImageMessage(ROSMessage msg)
    {
        byte[] imageData = Convert.FromBase64String(msg.data);

        EnqueueMainThreadAction(() =>
        {
            try
            {
                if (msg.topic == "/unity/cam_1/rgb")
                {
                    cam1RGBTexture.LoadImage(imageData);
                    cam1RGBTexture.Apply();
                    frameCounters["cam1_rgb"]++;
                }
                else if (msg.topic == "/unity/cam_2/rgb")
                {
                    cam2RGBTexture.LoadImage(imageData);
                    cam2RGBTexture.Apply();
                    frameCounters["cam2_rgb"]++;
                }
                else if (msg.topic == "/unity/cam_1/depth")
                {
                    var tempTex = new Texture2D(2, 2);
                    if (tempTex.LoadImage(imageData))
                        Graphics.CopyTexture(tempTex, cam1DepthTexture);
                    Destroy(tempTex);
                }
                else if (msg.topic == "/unity/cam_2/depth")
                {
                    var tempTex = new Texture2D(2, 2);
                    if (tempTex.LoadImage(imageData))
                        Graphics.CopyTexture(tempTex, cam2DepthTexture);
                    Destroy(tempTex);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"影像更新錯誤: {e.Message}");
            }
        });
    }

    void ProcessStringMessage(ROSMessage msg)
    {
        if (msg.topic != "/unity/detections_json") return;

        EnqueueMainThreadAction(() =>
        {
            try
            {
                var detectionData = JsonConvert.DeserializeObject<DetectionMessage>(msg.data);
                totalDetections = detectionData.detections != null ? detectionData.detections.Length : 0;
                Debug.Log($"收到檢測: {totalDetections} 個物體");
            }
            catch (Exception e)
            {
                Debug.LogError($"檢測解析錯誤: {e.Message}");
            }
        });
    }

    void EnqueueMainThreadAction(Action action)
    {
        lock (actionLock)
            mainThreadActions.Enqueue(action);
    }

    void Update()
    {
        lock (actionLock)
        {
            while (mainThreadActions.Count > 0)
                mainThreadActions.Dequeue()?.Invoke();
        }

        if (Time.time - lastFPSUpdate >= 1.0f)
        {
            cam1RGBFPS = frameCounters["cam1_rgb"];
            cam2RGBFPS = frameCounters["cam2_rgb"];
            InitializeFrameCounters();
            lastFPSUpdate = Time.time;
            UpdateUI();
        }

        if (Time.time - lastStatsPrint >= 5.0f)
        {
            Debug.Log($"[Stats] 已接收: {totalMessagesReceived} 訊息 | Cam1: {cam1RGBFPS}fps | Cam2: {cam2RGBFPS}fps");
            lastStatsPrint = Time.time;
        }
    }

    void UpdateUI()
    {
        if (statusText)
            statusText.text = $"Status: {connectionStatus}\nOrin: {orinIP}:{orinPort}\nMessages: {totalMessagesReceived}";

        if (fpsText)
            fpsText.text = $"Cam1: {cam1RGBFPS:F1} fps\nCam2: {cam2RGBFPS:F1} fps\nDetections: {totalDetections}";
    }

    void OnApplicationQuit()
    {
        shouldRun = false;

        stream?.Close();
        tcpClient?.Close();

        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Join(1000);

        Debug.Log("✓ TCP 連接已關閉");
    }
}

#region 數據結構
[System.Serializable]
public class ROSMessage
{
    public string topic;
    public double timestamp;
    public string type;
    public string format;
    public string data;
    public int data_length;
}
#endregion
