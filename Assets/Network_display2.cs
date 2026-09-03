using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using TMPro;

public class NetworkDisplay : MonoBehaviour
{
    [Header("Configuration")]
    public string pingTarget = "192.168.1.100";  // Robot IP for ping
    public float pingInterval = 1.0f;

    [Header("UI Components")]
    public TextMeshProUGUI throughputText;
    public TextMeshProUGUI latencyText;
    public TextMeshProUGUI packetLossText;
    public TextMeshProUGUI jitterText;
    public TextMeshProUGUI fpsText;

    private Process iperfProcess;
    private List<NetworkStat> statList = new List<NetworkStat>();
    private float deltaTime = 0.0f;

    void Start()
    {
        TryStartIperfServer();
        StartCoroutine(PingRoutine());
        Application.quitting += OnApplicationQuit;
    }

    void Update()
    {
        // FPS calculation
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        float fps = 1.0f / deltaTime;
        if (fpsText != null)
            fpsText.text = $"FPS: {fps:F1}";
    }

    private void TryStartIperfServer()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "iperf",
                Arguments              = "-s -u -i 1 -y C",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            iperfProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            iperfProcess.OutputDataReceived += OnIperfCsvOutput;
            iperfProcess.ErrorDataReceived  += OnIperfCsvOutput;
            iperfProcess.Start();
            iperfProcess.BeginOutputReadLine();
            iperfProcess.BeginErrorReadLine();
            UnityEngine.Debug.Log("iperf v2 server started (CSV mode)");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to start iperf: {e.Message}");
        }
    }

    private IEnumerator PingRoutine()
    {
        while (true)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "ping",
                Arguments              = $"-c 1 -W 1 {pingTarget}",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            float latency = -1f;
            try
            {
                var pingProc = Process.Start(psi);
                string output = pingProc.StandardOutput.ReadToEnd();
                pingProc.WaitForExit();
                // parse line containing 'time='
                foreach (var line in output.Split('\n'))
                {
                    if (line.Contains("time="))
                    {
                        int idx = line.IndexOf("time=");
                        int msIdx = line.IndexOf(" ms", idx);
                        if (idx > -1 && msIdx > idx)
                        {
                            string val = line.Substring(idx + 5, msIdx - (idx + 5));
                            if (float.TryParse(val, out float parsed))
                                latency = parsed;
                        }
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Ping failed: {e.Message}");
            }
            if (latencyText != null)
                latencyText.text = latency >= 0 ? $"Latency: {latency:F1} ms" : "Latency: N/A";

            // update last stat entry if exists
            if (statList.Count > 0)
                statList[statList.Count - 1].Latency = latency;

            yield return new WaitForSeconds(pingInterval);
        }
    }

    private void OnIperfCsvOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        string line = e.Data.Trim();
        var parts = line.Split(',');
        if (parts.Length < 12 || !parts[5].Equals("UDP", StringComparison.OrdinalIgnoreCase))
            return;
        if (float.TryParse(parts[8], out float bps) &&
            float.TryParse(parts[9], out float jitter) &&
            float.TryParse(parts[10], out float lost) &&
            float.TryParse(parts[11], out float total))
        {
            float mbps    = bps / 1e6f;
            float lossPct = total > 0 ? (lost / total * 100f) : 0f;
            // Latency updated by PingRoutine
            float latency = statList.Count > 0 ? statList[statList.Count - 1].Latency : -1f;

            // Update UI
            if (throughputText != null)   throughputText.text = $"Throughput: {mbps:F1} Mbps";
            if (jitterText != null)       jitterText.text     = $"Jitter: {jitter:F1} ms";
            if (packetLossText != null)   packetLossText.text = $"Packet Loss: {lossPct:F1}%";

            // Record new entry
            statList.Add(new NetworkStat
            {
                Timestamp  = DateTime.Now,
                Throughput = mbps,
                Latency    = latency,
                PacketLoss = lossPct,
                Jitter     = jitter
            });
        }
    }

    private void OnApplicationQuit()
    {
        string path = Path.Combine(Application.persistentDataPath, "network_stats.csv");
        try
        {
            using (var sw = new StreamWriter(path))
            {
                sw.WriteLine("Timestamp,Throughput(Mbps),Latency(ms),PacketLoss(%),Jitter(ms)");
                foreach (var s in statList)
                {
                    sw.WriteLine($"{s.Timestamp:yyyy-MM-dd HH:mm:ss},{s.Throughput:F1},{s.Latency:F1},{s.PacketLoss:F1},{s.Jitter:F1}");
                }
            }
            UnityEngine.Debug.Log($"Saved network stats to: {path}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"CSV save failed: {e.Message}");
        }
        if (iperfProcess != null && !iperfProcess.HasExited)
        {
            iperfProcess.Kill();
            UnityEngine.Debug.Log("iperf server stopped");
        }
    }

    private class NetworkStat
    {
        public DateTime Timestamp;
        public float Throughput;
        public float Latency;
        public float PacketLoss;
        public float Jitter;
    }
}
