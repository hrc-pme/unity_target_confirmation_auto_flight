using RosSharp.RosBridgeClient;
using System;
using System.Collections.Generic;
using UnityEngine;
using OpenCvSharp;

public class RGBDMerger : MonoBehaviour
{
    [Header("是否為左相機（主輸出 / 做融合的一邊）")]
    public bool isLeftCamera = true;

    [Header("雙相機融合（D435 640x480）")]
    public RGBDMerger rightCameraMerger;
    [Tooltip("右相機座標相對於左相機的位移（公尺）")]
    public Vector3 rightCameraOffset = new Vector3(0.48f, 0f, 0f);
    public bool enableDualCameraFusion = true;

    [Header("🔄 外翻角度（避免重疊覆蓋）")]
    [Tooltip("左相機外翻（向左）：-5度")]
    public float leftYawDeg = -5f;
    [Tooltip("右相機外翻（向右）：+5度")]
    public float rightYawDeg = 5f;
    public bool applyYawRotation = true;

    [Header("相機參數")]
    public float fx = 452.8687f, fy = 452.6750f, cx = 320f, cy = 240f;

    [Header("ROS Topics")]
    public ImageSubscriberPointCloud rgbImageSub;
    public DepthImageSubscriber      depthImageSub;
    public RosConnector rosConnector;
    public string cameraInfoTopic = "/unity/cam1/rgb/camera_info";

    [Header("🌞 戶外完整 RGB 點雲")]
    [Tooltip("深度=0 或過遠時使用的固定距離（m）")]
    public float fixedDepthForFar = 10f;
    [Tooltip("強制使用所有 RGB 像素（保證完整覆蓋）")]
    public bool forceUseAllRGBPixels = true;
    [Tooltip("深度超過此值壓縮到固定距離（m）")]
    public float farThreshold = 10f;
    [Tooltip("最近距離（mm）")]
    public int MinDepthMm = 200;

    [Header("⚡ 平衡採樣（效能優化）")]
    [Tooltip("下採樣：1=全解析度(~30萬點)，2≈7.7萬點/機，3≈3.4萬點/機")]
    public int downsample = 1;  // ← 改為 1，最高密度

    [Header("📉 點數預算（可關閉）")]
    [Tooltip("啟用後依影像尺寸自動估算步進，將總點數壓在預算內（雙機合計）。預設關閉以保留完整點雲。")]
    public bool autoPointBudget = false;  // ← 預設關閉，保留完整密度
    [Range(40000, 200000)]
    public int pointBudgetTotal = 100000;

    [Header("🎨 自然 RGB 顏色（建議全設為 1.0）")]
    [Range(0.8f, 1.5f)] public float brightness = 1.0f;
    [Range(0.8f, 2.0f)] public float contrast = 1.0f;
    [Range(0.8f, 1.2f)] public float gamma = 1.0f;
    [Range(0.8f, 1.5f)] public float saturation = 1.0f;

    public enum YFlipMode { None, Flip }
    [Header("座標對齊")]
    public YFlipMode yFlip = YFlipMode.Flip;

    [Header("🔧 融合去重控制")]
    [Tooltip("啟用智能去重（避免右相機覆蓋左相機）")]
    public bool enableSmartDeduplication = true;
    [Tooltip("去重距離閾值（m）：右機點與左機點距離小於此值則丟棄")]
    [Range(0.02f, 0.2f)] public float deduplicationThreshold = 0.08f;
    [Tooltip("去重檢查步進（越大越快但精度降低）")]
    [Range(5, 50)] public int deduplicationCheckStep = 15;

    // 點雲數據
    private Mat rgb_img, depth_img;
    public readonly List<Vector3> pcl = new();
    public readonly List<Color>  pclColor = new();
    public readonly List<float>  pclDist  = new();

    // 融合數據
    private readonly List<Vector3> fusedPcl = new();
    private readonly List<Color>   fusedCol = new();
    private readonly List<float>   fusedDis = new();

    private Quaternion qLeft, qRight;
    private double lastLogT = 0;
    private int rgbFilledCount = 0;
    private int depthClampedCount = 0;

    void Start()
    {
        Debug.LogWarning($"[RGBDMerger] ===== START 被調用 ({(isLeftCamera ? "左" : "右")}相機) =====");
        
        qLeft  = Quaternion.Euler(0f, leftYawDeg, 0f);
        qRight = Quaternion.Euler(0f, rightYawDeg, 0f);

        TrySubscribeCameraInfo();

        int expectedPoints = (640 / Mathf.Max(1, downsample)) * (480 / Mathf.Max(1, downsample));
        Debug.Log($"[RGBDMerger] {(isLeftCamera ? "左" : "右")}相機啟動");
        Debug.Log($"  預期單機點數(固定downsample): ~{expectedPoints:N0} (downsample={downsample})");
        Debug.Log($"  外翻角度: 左{leftYawDeg}° 右{rightYawDeg}°");
        Debug.Log($"  遠處壓縮: >{farThreshold}m → {fixedDepthForFar}m");
        if (autoPointBudget) Debug.Log($"  自動點數預算開啟：融合上限 ≈ {pointBudgetTotal:N0}");
        
        // 確認組件連結
        Debug.LogWarning($"[RGBDMerger] rgbImageSub = {(rgbImageSub != null ? "已設定" : "NULL")}");
        Debug.LogWarning($"[RGBDMerger] depthImageSub = {(depthImageSub != null ? "已設定" : "NULL")}");
        Debug.LogWarning($"[RGBDMerger] rosConnector = {(rosConnector != null ? "已設定" : "NULL")}");
        
        if (rgbImageSub == null) Debug.LogError($"[RGBDMerger] RGB訂閱器未設定！");
        if (depthImageSub == null) Debug.LogError($"[RGBDMerger] Depth訂閱器未設定！");
        if (rosConnector == null) Debug.LogError($"[RGBDMerger] ROS連接器未設定！");
    }

    void TrySubscribeCameraInfo()
    {
        try
        {
            if (rosConnector?.RosSocket == null || string.IsNullOrEmpty(cameraInfoTopic)) return;
            rosConnector.RosSocket.Subscribe<RosSharp.RosBridgeClient.MessageTypes.Sensor.CameraInfo>(
                cameraInfoTopic,
                msg =>
                {
                    if (msg?.k?.Length >= 9)
                    {
                        fx = (float)msg.k[0]; fy = (float)msg.k[4];
                        cx = (float)msg.k[2]; cy = (float)msg.k[5];
                        Debug.Log($"[RGBDMerger] 相機參數: fx={fx:F1}, fy={fy:F1}");
                    }
                }, 1);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RGBDMerger] CameraInfo失敗: {e.Message}");
        }
    }

    // ------ 調試狀態 ------
    [HideInInspector] public string debugStatus = "NOT_STARTED";
    [HideInInspector] public int debugPclCount = 0;
    [HideInInspector] public int debugUpdateCount = 0;

    void Update()
    {
        debugUpdateCount++;

        // 同時讀取主線程和原始數據，確保能拿到數據
        byte[] rgbData = null;
        byte[] depthData = null;

        if (rgbImageSub != null)
        {
            rgbData = rgbImageSub.MainThreadImageData;
            if (rgbData == null) rgbData = rgbImageSub.ImageData;
        }

        if (depthImageSub != null)
        {
            depthData = depthImageSub.MainThreadImageData;
            if (depthData == null) depthData = depthImageSub.ImageData;
        }

        if (rgbData == null || rgbData.Length == 0)
        {
            debugStatus = "WAIT_RGB";
            return;
        }
        
        if (depthData == null || depthData.Length == 0)
        {
            debugStatus = "WAIT_DEPTH";
            return;
        }

        debugStatus = $"DATA rgb={rgbData.Length} dep={depthData.Length}";

        try
        {
            // --- RGB 解碼 ---
            rgb_img = Mat.FromImageData(rgbData, ImreadModes.Color);

            // --- Depth 解碼：ROS compressedDepth 格式 ---
            // 格式：8-byte header (quantA + quantB float32) + JPEG/PNG 數據
            // JPEG 數據可能缺少 FF D8 (SOI) 標記
            depth_img = null;

            // 嘗試 1: 直接解碼（適用於標準 PNG/JPEG）
            {
                var d = Mat.FromImageData(depthData, ImreadModes.Unchanged);
                if (d != null && !d.Empty()) depth_img = d;
            }

            // 嘗試 2: 跳過 8-byte header（ROS compressedDepth 標準）
            if (depth_img == null && depthData.Length > 8)
            {
                byte[] trimmed = new byte[depthData.Length - 8];
                System.Array.Copy(depthData, 8, trimmed, 0, trimmed.Length);

                // 2a: 直接解碼
                var d = Mat.FromImageData(trimmed, ImreadModes.Unchanged);
                if (d != null && !d.Empty()) depth_img = d;

                // 2b: JPEG 缺少 SOI 標記，補上 FF D8
                if (depth_img == null && trimmed.Length > 2 && trimmed[0] == 0xFF && trimmed[1] != 0xD8)
                {
                    byte[] withSOI = new byte[trimmed.Length + 2];
                    withSOI[0] = 0xFF;
                    withSOI[1] = 0xD8;
                    System.Array.Copy(trimmed, 0, withSOI, 2, trimmed.Length);
                    d = Mat.FromImageData(withSOI, ImreadModes.Unchanged);
                    if (d != null && !d.Empty()) depth_img = d;
                }
            }

            // 嘗試 3: 跳過 12-byte header
            if (depth_img == null && depthData.Length > 12)
            {
                byte[] trimmed = new byte[depthData.Length - 12];
                System.Array.Copy(depthData, 12, trimmed, 0, trimmed.Length);

                var d = Mat.FromImageData(trimmed, ImreadModes.Unchanged);
                if (d != null && !d.Empty()) depth_img = d;

                // 補 SOI
                if (depth_img == null && trimmed.Length > 2 && trimmed[0] == 0xFF && trimmed[1] != 0xD8)
                {
                    byte[] withSOI = new byte[trimmed.Length + 2];
                    withSOI[0] = 0xFF;
                    withSOI[1] = 0xD8;
                    System.Array.Copy(trimmed, 0, withSOI, 2, trimmed.Length);
                    d = Mat.FromImageData(withSOI, ImreadModes.Unchanged);
                    if (d != null && !d.Empty()) depth_img = d;
                }
            }

            // 嘗試 4: 掃描 FF D8 位置
            if (depth_img == null)
            {
                int jpegStart = -1;
                for (int i = 0; i < System.Math.Min(64, depthData.Length - 1); i++)
                {
                    if (depthData[i] == 0xFF && depthData[i + 1] == 0xD8)
                    {
                        jpegStart = i;
                        break;
                    }
                }
                if (jpegStart >= 0)
                {
                    byte[] trimmed = new byte[depthData.Length - jpegStart];
                    System.Array.Copy(depthData, jpegStart, trimmed, 0, trimmed.Length);
                    var d = Mat.FromImageData(trimmed, ImreadModes.Unchanged);
                    if (d != null && !d.Empty()) depth_img = d;
                }
            }

            // 嘗試 5: 掃描 PNG 簽名 (89 50 4E 47)
            if (depth_img == null)
            {
                int pngStart = -1;
                for (int i = 0; i < System.Math.Min(64, depthData.Length - 3); i++)
                {
                    if (depthData[i] == 0x89 && depthData[i + 1] == 0x50 &&
                        depthData[i + 2] == 0x4E && depthData[i + 3] == 0x47)
                    {
                        pngStart = i;
                        break;
                    }
                }
                if (pngStart >= 0)
                {
                    byte[] trimmed = new byte[depthData.Length - pngStart];
                    System.Array.Copy(depthData, pngStart, trimmed, 0, trimmed.Length);
                    var d = Mat.FromImageData(trimmed, ImreadModes.Unchanged);
                    if (d != null && !d.Empty()) depth_img = d;
                }
            }
            
            bool rgbOK = rgb_img != null && !rgb_img.Empty();
            bool depOK = depth_img != null && !depth_img.Empty();
            
            if (!rgbOK || !depOK)
            {
                // 顯示原始數據前幾個Byte幫助分析格式
                string depHex = "";
                for (int i = 0; i < System.Math.Min(16, depthData.Length); i++)
                    depHex += depthData[i].ToString("X2") + " ";
                debugStatus = $"MAT_FAIL rgb={rgbOK} dep={depOK} depHex={depHex}";
                return;
            }

            string dt = depth_img.Type().ToString();
            debugStatus = $"DEP_OK {depth_img.Width}x{depth_img.Height} {dt}";

            // 深度圖格式處理：統一轉為 16UC1 (mm)
            if (depth_img.Type() == MatType.CV_32FC1)
            {
                // 32FC1 浮點深度(m) → 16UC1(mm)
                depth_img.ConvertTo(depth_img, MatType.CV_16UC1, 1000.0);
            }
            else if (depth_img.Type() == MatType.CV_8UC1)
            {
                // 8-bit 灰度深度圖（JPEG compressedDepth）→ 映射到 mm
                // 8-bit 值 0-255 映射到 0-10000mm (0-10m)
                depth_img.ConvertTo(depth_img, MatType.CV_16UC1, 39.2); // 255 * 39.2 ≈ 10000mm
            }
            else if (depth_img.Type() == MatType.CV_8UC3)
            {
                // 彩色深度圖 → 轉灰度 → 映射到 mm
                Cv2.CvtColor(depth_img, depth_img, ColorConversionCodes.BGR2GRAY);
                depth_img.ConvertTo(depth_img, MatType.CV_16UC1, 39.2);
            }
            else if (depth_img.Type() != MatType.CV_16UC1)
            {
                depth_img.ConvertTo(depth_img, MatType.CV_16UC1);
            }

            if (rgb_img.Width != depth_img.Width || rgb_img.Height != depth_img.Height)
                Cv2.Resize(rgb_img, rgb_img, new OpenCvSharp.Size(depth_img.Width, depth_img.Height), 0, 0, InterpolationFlags.Nearest);

            BuildBalancedPointCloud();

            if (isLeftCamera && enableDualCameraFusion && rightCameraMerger != null)
                FuseWithYawRotation();

            debugPclCount = pcl.Count;
            debugStatus = $"PCL_OK:{pcl.Count}";
        }
        catch (System.Exception ex)
        {
            debugStatus = $"ERR {ex.GetType().Name}";
            Debug.LogError($"[RGBDMerger] Exception: {ex}");
        }

        if (Time.timeAsDouble - lastLogT > 2.0)
        {
            string msg = $"[RGBDMerger-{(isLeftCamera ? "L" : "R")}] PCL={pcl.Count:N0}點";
            msg += $" RGB填補:{rgbFilledCount:N0}({100f * rgbFilledCount / Mathf.Max(1, pcl.Count):F1}%)";
            if (isLeftCamera && enableDualCameraFusion && fusedPcl.Count > 0)
            {
                int rightCount = Mathf.Max(0, fusedPcl.Count - pcl.Count);
                msg += $"\n  融合: 左{pcl.Count:N0} + 右{rightCount:N0} = {fusedPcl.Count:N0}";
            }
            Debug.Log(msg);
            lastLogT = Time.timeAsDouble;
        }
    }

    void BuildBalancedPointCloud()
    {
        int h = depth_img.Height, w = depth_img.Width;
        
        pcl.Clear(); pclColor.Clear(); pclDist.Clear();
        rgbFilledCount = 0;
        depthClampedCount = 0;

        var rgbIdx = new Mat<Vec3b>(rgb_img).GetIndexer();
        var depIdx = new Mat<ushort>(depth_img).GetIndexer();

        // 依需求選：自動預算 or 固定 downsample（預設用固定 downsample=2 保持高密度）
        int step;
        if (autoPointBudget)
        {
            int cams = (enableDualCameraFusion && rightCameraMerger != null) ? 2 : 1;
            int desiredPerCam = Mathf.Max(20000, pointBudgetTotal / Mathf.Max(1, cams));
            float s = Mathf.Sqrt((w * h) / (float)desiredPerCam);
            step = Mathf.Clamp(Mathf.CeilToInt(s), 1, 10);
        }
        else
        {
            step = Mathf.Max(1, downsample);
        }

        if (Time.timeAsDouble - lastLogT > 2.0)
        {
            int estPerCam = Mathf.CeilToInt(w / (float)step) * Mathf.CeilToInt(h / (float)step);
            int cams = (enableDualCameraFusion && rightCameraMerger != null) ? 2 : 1;
            int estTotal = estPerCam * cams;
            Debug.Log($"[RGBDMerger-{(isLeftCamera ? "L" : "R")}] step={step} 估算每機≈{estPerCam:N0} 總計≈{estTotal:N0}");
        }

        for (int v = 0; v < h; v += step)
        {
            for (int u = 0; u < w; u += step)
            {
                ushort dmm = depIdx[v, u];
                float z;

                // 深度處理
                if (dmm == 0 || dmm < MinDepthMm || dmm > 65000)
                {
                    z = fixedDepthForFar;
                    rgbFilledCount++;
                }
                else
                {
                    z = dmm * 0.001f;
                    if (z > farThreshold)
                    {
                        z = fixedDepthForFar;
                        depthClampedCount++;
                    }
                }

                // 計算 3D 座標
                float x = (u - cx) / fx * z;
                float y_im = (v - cy) / fy * z;
                float y = (yFlip == YFlipMode.Flip) ? -y_im : y_im;

                Vector3 p = new Vector3(x, y, z);

                // 自然 RGB 顏色
                var bgr = rgbIdx[v, u];
                float r = bgr[2] / 255f;
                float g = bgr[1] / 255f;
                float b = bgr[0] / 255f;

                if (Mathf.Abs(brightness - 1.0f) > 0.01f)
                {
                    r *= brightness; g *= brightness; b *= brightness;
                }

                Color c = new Color(
                    Mathf.Clamp01(r),
                    Mathf.Clamp01(g),
                    Mathf.Clamp01(b),
                    1.0f
                );

                pcl.Add(p);
                pclColor.Add(c);
                pclDist.Add(z);
            }
        }
        
        debugPclCount = pcl.Count;
        debugStatus = $"PCL_OK:{pcl.Count}";
    }

    void FuseWithYawRotation()
    {
        fusedPcl.Clear(); fusedCol.Clear(); fusedDis.Clear();

        // 左機點雲
        if (pcl.Count > 0)
        {
            for (int i = 0; i < pcl.Count; i++)
            {
                Vector3 p = applyYawRotation ? (qLeft * pcl[i]) : pcl[i];
                fusedPcl.Add(p);
                fusedCol.Add(pclColor[i]);
                fusedDis.Add(p.z);
            }
        }

        // 右機點雲（智能去重）
        var r = rightCameraMerger;
        if (r?.pcl != null && r.pcl.Count > 0)
        {
            int addedCount = 0, skippedCount = 0;

            for (int i = 0; i < r.pcl.Count; i++)
            {
                Vector3 pr = applyYawRotation ? (qRight * r.pcl[i]) : r.pcl[i];
                pr += rightCameraOffset;

                if (pr.z > farThreshold) pr.z = fixedDepthForFar;

                bool shouldAdd = true;
                if (enableSmartDeduplication && pr.x < rightCameraOffset.x * 0.3f)
                {
                    for (int j = 0; j < pcl.Count; j += deduplicationCheckStep)
                    {
                        Vector3 pl = applyYawRotation ? (qLeft * pcl[j]) : pcl[j];
                        if (Vector3.Distance(pr, pl) < deduplicationThreshold)
                        {
                            shouldAdd = false; skippedCount++; break;
                        }
                    }
                }

                if (shouldAdd)
                {
                    fusedPcl.Add(pr);
                    fusedCol.Add(r.pclColor[i]);
                    fusedDis.Add(pr.z);
                    addedCount++;
                }
            }

            if (Time.timeAsDouble - lastLogT > 2.0 && enableSmartDeduplication)
            {
                float keepRate = r.pcl.Count > 0 ? (addedCount / (float)r.pcl.Count * 100f) : 0f;
                Debug.Log($"  右機去重: 保留{addedCount:N0} 過濾{skippedCount:N0} ({keepRate:F1}%)");
            }
        }

        // ⚠️ 不做融合後下採樣（保持完整點雲）
    }

    public Vector3[] GetPCL(int _)
    {
        if (isLeftCamera && enableDualCameraFusion && fusedPcl.Count > 0)
            return fusedPcl.ToArray();

        if (applyYawRotation)
        {
            var arr = new Vector3[pcl.Count];
            var q = isLeftCamera ? qLeft : qRight;
            for (int i = 0; i < pcl.Count; i++) arr[i] = q * pcl[i];
            return arr;
        }
        return pcl.ToArray();
    }

    public Color[] GetPCLColor(int _)
    {
        if (isLeftCamera && enableDualCameraFusion && fusedCol.Count > 0)
            return fusedCol.ToArray();
        return pclColor.ToArray();
    }

    public float[] GetPCLDis(int _)
    {
        if (isLeftCamera && enableDualCameraFusion && fusedDis.Count > 0)
            return fusedDis.ToArray();
        return pclDist.ToArray();
    }
}