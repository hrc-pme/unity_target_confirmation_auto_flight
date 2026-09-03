using RosSharp.RosBridgeClient;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using OpenCvSharp;

public class RGBDMerge_v1 : MonoBehaviour
{
    public ImageSubscriberPointCloud rgbImageSub;
    public DepthImageSubscriber       depthImageSub;

    RosSharp.RosBridgeClient.MessageTypes.Sensor.Image rgbImage;
    RosSharp.RosBridgeClient.MessageTypes.Sensor.Image depthImage;

    Mat rgb_img, depth_img;

    [Header("Camera Intrinsics (pixels)")]
    public float fx = 615f, fy = 615f, cx = 424f, cy = 240f; // 848x480 預設值，之後請換成實機

    [Header("ROI (inclusive start, inclusive end in inspector)")]
    public int height_start = 0, height_end = 480;
    public int width_start  = 0, width_end  = 640;

    [Header("Depth / Render")]
    public int   depth_limit       = 10000;  // 設為 10000 mm (即 10m)
    public int   downsample_factor = 1;      // step
    public float point_size_factor = 1f;
    [Tooltip("If the depth value is too far, move the point to the depth_limit of the point cloud")]
    public bool  move_too_far_points = true;  // 啟用這個選項，將過遠的點移動到 depth_limit

    private readonly List<Vector3> pcl       = new List<Vector3>();
    private readonly List<float>   dis       = new List<float>();
    private readonly List<Color>   pcl_color = new List<Color>();

    void Start()
    {
        rgbImage   = new RosSharp.RosBridgeClient.MessageTypes.Sensor.Image();
        depthImage = new RosSharp.RosBridgeClient.MessageTypes.Sensor.Image();
    }

    void Update()
    {
        if (rgbImageSub.ImageData != null && depthImageSub.ImageData != null)
        {
            DecompressImages();
        }
    }

    void DecompressRGB()
    {
        // BGR 8UC3
        rgb_img = Mat.FromImageData(rgbImageSub.ImageData, ImreadModes.Color);
    }

    void DecompressDepth()
    {
        // 16UC1 (mm)
        depth_img = Mat.FromImageData(depthImageSub.ImageData, ImreadModes.Unchanged);
        if (depth_img.Type() != MatType.CV_16UC1)
            depth_img.ConvertTo(depth_img, MatType.CV_16UC1);
    }

    void DecompressImages()
    {
        DecompressRGB();
        DecompressDepth();
        PointCloudRendering();
    }

    void PointCloudRendering()
    {
        if (depth_img == null || depth_img.Empty() || rgb_img == null || rgb_img.Empty())
            return;

        pcl.Clear();
        pcl_color.Clear();
        dis.Clear();

        // 實際尺寸
        int rgbW = rgb_img.Width,  rgbH = rgb_img.Height;
        int depW = depth_img.Width, depH = depth_img.Height;

        // 使用兩路交集尺寸，避免尺寸不一致造成越界
        int useW = Mathf.Min(rgbW, depW);
        int useH = Mathf.Min(rgbH, depH);

        // 將 Inspector 的 ROI 夾在合法範圍（含邊界語意 → 最大 index = size-1）
        int hStart = Mathf.Clamp(height_start, 0, useH - 1);
        int hEnd   = Mathf.Clamp(height_end,   0, useH - 1);
        int wStart = Mathf.Clamp(width_start,  0, useW - 1);
        int wEnd   = Mathf.Clamp(width_end,    0, useW - 1);

        // 保證 start <= end
        if (hEnd < hStart) (hStart, hEnd) = (0, useH - 1);
        if (wEnd < wStart) (wStart, wEnd) = (0, useW - 1);

        // 轉為「不含邊界」的 endExclusive，避免最後一步跨界
        int hEndEx = hEnd + 1;
        int wEndEx = wEnd + 1;

        // 構建 indexer（比 Mat.At 快）
        var rgb_mat      = new Mat<Vec3b>(rgb_img);
        var rgb_indexer  = rgb_mat.GetIndexer();
        var depth_mat    = new Mat<ushort>(depth_img);
        var depth_indexer= depth_mat.GetIndexer();

        // 若 fx,fy,cx,cy 沒填，依目前尺寸給個穩定預設
        if (fx <= 0 || fy <= 0)
        {
            // 粗略近似：D435 在 848x480 ≈ 615px，縮放到當前寬度
            float scale = (useW >= 800) ? (useW / 848f) : (useW / 640f);
            fx = fy = 615f * scale;
        }
        if (cx <= 0 || cx >= useW) cx = (useW - 1) * 0.5f;
        if (cy <= 0 || cy >= useH) cy = (useH - 1) * 0.5f;

        for (int j = hStart; j < hEndEx; j += Mathf.Max(1, downsample_factor))
        {
            for (int k = wStart; k < wEndEx; k += Mathf.Max(1, downsample_factor))
            {
                ushort d = depth_indexer[j, k]; // mm

                if (d == 0)
                {
                    if (move_too_far_points)
                        d = (ushort)Mathf.Min(depth_limit, ushort.MaxValue);
                    else
                        continue; // 跳過無效點
                }
                if (d > depth_limit)
                {
                    if (move_too_far_points)
                        d = (ushort)Mathf.Min(depth_limit, ushort.MaxValue);
                    else
                        continue;
                }

                float z = d * 0.001f; // m
                float x = (k - cx) / fx * z;
                float y = (j - cy) / fy * z;

                var c  = rgb_indexer[j, k];
                float r = c[2] / 255f, g = c[1] / 255f, b = c[0] / 255f;

                var col = new Color(r, g, b);
                if (d < 1000)           col.a = 0.012f * point_size_factor;
                else if (d <= 5000)     col.a = 0.020f * point_size_factor;
                else                    col.a = 0.050f * point_size_factor;

                // 注意：這裡採用 (x, z, y) 以符合你原本座標需求
                pcl.Add(new Vector3(x, z, y));
                pcl_color.Add(col);
            }
        }
    }

    public Vector3[] GetPCL(int index)      => pcl.ToArray();
    public Color[]   GetPCLColor(int index) => pcl_color.ToArray();
    public float[]   GetPCLDis(int index)   => dis.ToArray();
}
