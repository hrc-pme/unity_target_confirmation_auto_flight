using System;
using UnityEngine;

public class RGBDRender : MonoBehaviour
{
    [Header("資料來源（指向左機的 RGBDMerger）")]
    public RGBDMerger subscriber;

    [Header("外觀/縮放")]
    [Range(0.1f, 5f)] public float pointCloudScale = 1f;

    [Header("🎯 點雲顯示")]
    [Tooltip("點大小")]
    [Range(0.0001f, 0.05f)] public float pointSize = 0.012f;
    [Tooltip("透明度")]
    [Range(0.5f, 1.0f)] public float baseAlpha = 1.0f;

    [Header("顏色（建議設為 1.0）")]
    [Tooltip("顏色增益（1.0=原始RGB顏色）")]
    [Range(0.8f, 1.3f)] public float colorBoost = 1.0f;

    [Header("⚡ 更新頻率控制（允許延遲換完整畫面）")]
    [Tooltip("更新間隔（秒）：建議 0.33~0.50；若可接受更慢可設 1~2 秒。")]
    [Range(0f, 3f)] public float updateInterval = 0.5f;  // ← 拉高，換取完整密度
    [Tooltip("自動FPS優化：FPS<20時自動降低更新率")]
    public bool autoOptimizeFPS = false;                  // ← 關閉，固定慢速完整更新

    ParticleSystem ps;
    ParticleSystem.Particle[] particles = Array.Empty<ParticleSystem.Particle>();
    ParticleSystemRenderer rend;
    Material mat;

    private float lastUpdateTime = 0f;
    private int frameCount = 0;
    private float fpsCheckTime = 0f;
    private float currentFPS = 60f;

    void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        if (ps == null) ps = gameObject.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = 500000;
        main.startLifetime = Mathf.Infinity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize3D = false;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // 避免閃爍/裁切

        rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.alignment = ParticleSystemRenderSpace.View;
        rend.minParticleSize = 0.0001f;
        rend.maxParticleSize = 0.5f;

        mat = new Material(Shader.Find("Particles/Standard Unlit"));
        mat.renderQueue = 3000;
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        rend.material = mat;

        ps.Play();
        Debug.Log($"[RGBDRender] 點雲渲染器初始化 - { (updateInterval > 0 ? (1f / updateInterval).ToString("F1") : "∞") } fps 目標更新");
    }

    // ------ 調試狀態 ------
    private string renderDebugStatus = "NOT_STARTED";
    private int renderDebugPoints = 0;
    private int renderDebugParticles = 0;

    /*  // === DEBUG GUI 已註解 ===
    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 16;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.yellow;

        float y = 10;
        GUI.Label(new Rect(10, y, 800, 25), "=== RGBD DEBUG ===", style);
        y += 22;

        if (subscriber == null)
        {
            GUI.Label(new Rect(10, y, 800, 25), "Subscriber: NULL", style);
            return;
        }

        // 直接探查 subscriber 內部狀態
        var rgbSub = subscriber.rgbImageSub;
        var depSub = subscriber.depthImageSub;

        string rgbInfo = "NULL";
        if (rgbSub != null)
        {
            var mtd = rgbSub.MainThreadImageData;
            var raw = rgbSub.ImageData;
            rgbInfo = $"MTD={mtd?.Length ?? -1} Raw={raw?.Length ?? -1}";
        }

        string depInfo = "NULL";
        if (depSub != null)
        {
            var mtd = depSub.MainThreadImageData;
            var raw = depSub.ImageData;
            depInfo = $"MTD={mtd?.Length ?? -1} Raw={raw?.Length ?? -1}";
        }

        GUI.Label(new Rect(10, y, 800, 25), $"RGB Sub: {rgbInfo}", style);
        y += 22;
        GUI.Label(new Rect(10, y, 800, 25), $"Depth Sub: {depInfo}", style);
        y += 22;
        GUI.Label(new Rect(10, y, 800, 25), $"Merger: {subscriber.debugStatus}", style);
        y += 22;
        GUI.Label(new Rect(10, y, 800, 25), $"Updates: {subscriber.debugUpdateCount} PCL: {subscriber.debugPclCount}", style);
        y += 22;
        GUI.Label(new Rect(10, y, 800, 25), $"Render: {renderDebugStatus} pts={renderDebugPoints}", style);
        y += 22;
        GUI.Label(new Rect(10, y, 800, 25), $"Particles: {renderDebugParticles} PS={ps?.isPlaying}", style);
    }
    */  // === END DEBUG GUI ===

    void LateUpdate()
    {
        renderDebugParticles = ps != null ? ps.particleCount : -1;

        if (subscriber == null)
        {
            renderDebugStatus = "NO_SUBSCRIBER";
            return;
        }

        // FPS 監控（僅監看）
        frameCount++;
        if (Time.time - fpsCheckTime > 1f)
        {
            currentFPS = frameCount / (Time.time - fpsCheckTime);
            frameCount = 0;
            fpsCheckTime = Time.time;
        }

        // 固定更新節奏：允許幾秒延遲，但每次都送完整點雲
        float actualInterval = updateInterval;
        if (autoOptimizeFPS && currentFPS < 20f)
            actualInterval = Mathf.Max(updateInterval, 0.15f);

        if (Time.time - lastUpdateTime < actualInterval) return;
        lastUpdateTime = Time.time;

        var pos = subscriber.GetPCL(0);
        var col = subscriber.GetPCLColor(0);
        
        if (pos == null || pos.Length == 0)
        {
            renderDebugStatus = $"NO_PCL(pos={pos?.Length ?? -1})";
            return;
        }
        if (col == null || col.Length == 0)
        {
            renderDebugStatus = $"NO_COL(col={col?.Length ?? -1})";
            return;
        }

        renderDebugStatus = "RENDERING";
        renderDebugPoints = pos.Length;

        if (particles.Length != pos.Length)
            particles = new ParticleSystem.Particle[pos.Length];

        for (int i = 0; i < pos.Length; i++)
        {
            Vector3 p = pos[i] * pointCloudScale;
            Color c = col[i];
            c.a = baseAlpha;

            particles[i].position = p;
            particles[i].startSize = pointSize;
            particles[i].startColor = c;
            particles[i].remainingLifetime = 999999f;
            particles[i].velocity = Vector3.zero;
        }

        ps.SetParticles(particles, particles.Length);
    }
}