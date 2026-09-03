using System;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

public class TargetCandidateSubscriber : MonoBehaviour
{
    [Header("ROS")]
    [SerializeField] private RosConnector rosConnector;
    [SerializeField] private string detectionTopic = "/unity/detections_json";
    [SerializeField] private float subscribeRetrySeconds = 1.0f;

    [Header("Replay")]
    [SerializeField] private bool allowHistoricalReplayMessages = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    public event Action<TargetCandidatePacket> OnCandidatesUpdated;

    public bool HasReceivedPacket { get; private set; }
    public float LastPacketReceivedRealtime { get; private set; } = -1.0f;

    private readonly object queueLock = new object();
    private readonly Queue<string> pendingJsonMessages = new Queue<string>();

    private RosSocket rosSocket;
    private string subscriptionId;
    private bool subscribed;
    private float nextSubscribeAttemptRealtime;
    private float lastJsonWarningRealtime = -999.0f;
    private float lastRosWarningRealtime = -999.0f;
    private int subscriptionGeneration;
    private bool acceptMessages;
    private double sessionStartedUnixSeconds;
    private string currentSourceSessionId = string.Empty;

    public string CurrentSourceSessionId => currentSourceSessionId;

    private void Awake()
    {
        if (rosConnector == null)
        {
            rosConnector = FindObjectOfType<RosConnector>();
        }

        UavDashboardRos.ForceAllOrinUrls();
        UavDashboardRos.ForceOrinUrl(rosConnector);
    }

    private void OnEnable()
    {
        subscriptionGeneration++;
        acceptMessages = true;
        sessionStartedUnixSeconds = UavDashboardTime.GetUtcUnixSeconds();
        currentSourceSessionId = string.Empty;
        HasReceivedPacket = false;
        LastPacketReceivedRealtime = -1.0f;
        subscribed = false;
        nextSubscribeAttemptRealtime = 0.0f;
        lock (queueLock)
        {
            pendingJsonMessages.Clear();
        }
    }

    private void Update()
    {
        RefreshRosConnectionState();
        TrySubscribe();
        DrainMessages();
    }

    public bool HasFreshData(float timeoutSeconds)
    {
        return HasReceivedPacket &&
               Time.realtimeSinceStartup - LastPacketReceivedRealtime <= timeoutSeconds;
    }

    public void InjectMockPacket(TargetCandidatePacket packet)
    {
        ApplyPacket(packet);
    }

    private void TrySubscribe()
    {
        if (subscribed || Time.realtimeSinceStartup < nextSubscribeAttemptRealtime)
        {
            return;
        }

        nextSubscribeAttemptRealtime =
            Time.realtimeSinceStartup + Mathf.Max(0.5f, subscribeRetrySeconds);

        if (!IsRosConnected())
        {
            WarnRos("ROS NOT CONNECTED");
            return;
        }

        try
        {
            rosSocket = rosConnector.RosSocket;
            int generation = subscriptionGeneration;
            subscriptionId = rosSocket.Subscribe<RosString>(
                detectionTopic,
                message => ReceiveMessage(generation, message));
            subscribed = true;

            if (logDebug)
            {
                Debug.Log("[TargetCandidateSubscriber] Subscribed: " + detectionTopic);
            }
        }
        catch (Exception exception)
        {
            subscribed = false;
            WarnRos("Subscribe failed: " + exception.Message);
        }
    }

    private void ReceiveMessage(int generation, RosString message)
    {
        if (!acceptMessages || generation != subscriptionGeneration ||
            message == null || string.IsNullOrEmpty(message.data))
        {
            return;
        }

        lock (queueLock)
        {
            pendingJsonMessages.Enqueue(message.data);
            while (pendingJsonMessages.Count > 5)
            {
                pendingJsonMessages.Dequeue();
            }
        }
    }

    private void DrainMessages()
    {
        string json = null;

        lock (queueLock)
        {
            // The dashboard is a latest-state UI. Drain to the newest packet
            // instead of displaying the oldest packet and discarding newer
            // approach/candidate state from the same Unity frame.
            while (pendingJsonMessages.Count > 0)
            {
                json = pendingJsonMessages.Dequeue();
            }
        }

        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            TargetCandidatePacket packet = JsonUtility.FromJson<TargetCandidatePacket>(json);
            ApplyPacket(packet);
        }
        catch (Exception exception)
        {
            WarnJson("JSON parse failed: " + exception.Message);
        }
    }

    private void ApplyPacket(TargetCandidatePacket packet)
    {
        if (packet == null)
        {
            WarnJson("Candidate packet is null");
            return;
        }

        if (packet.candidates == null)
        {
            packet.candidates = new TargetCandidateData[0];
        }

        if (!allowHistoricalReplayMessages &&
            packet.timestamp > 0.0 &&
            packet.timestamp + 1.0 < sessionStartedUnixSeconds)
        {
            return;
        }

        string sourceSessionId = packet.source_session_id ?? string.Empty;
        if (!string.IsNullOrEmpty(sourceSessionId) &&
            !string.Equals(sourceSessionId, currentSourceSessionId, StringComparison.Ordinal))
        {
            currentSourceSessionId = sourceSessionId;
            HasReceivedPacket = false;
            LastPacketReceivedRealtime = -1.0f;
        }

        HasReceivedPacket = true;
        LastPacketReceivedRealtime = Time.realtimeSinceStartup;
        OnCandidatesUpdated?.Invoke(packet);
    }

    private bool IsRosConnected()
    {
        return rosConnector != null &&
               rosConnector.RosSocket != null &&
               rosConnector.IsConnected != null &&
               rosConnector.IsConnected.WaitOne(0);
    }

    private void RefreshRosConnectionState()
    {
        if (!subscribed)
        {
            return;
        }

        if (IsRosConnected() && ReferenceEquals(rosConnector.RosSocket, rosSocket))
        {
            return;
        }

        subscriptionGeneration++;
        subscriptionId = null;
        subscribed = false;
        rosSocket = null;
        nextSubscribeAttemptRealtime = 0.0f;
        lock (queueLock)
        {
            pendingJsonMessages.Clear();
        }
    }

    private void WarnRos(string message)
    {
        if (Time.realtimeSinceStartup - lastRosWarningRealtime < 5.0f)
        {
            return;
        }

        lastRosWarningRealtime = Time.realtimeSinceStartup;
        Debug.LogWarning("[TargetCandidateSubscriber] " + message);
    }

    private void WarnJson(string message)
    {
        if (Time.realtimeSinceStartup - lastJsonWarningRealtime < 5.0f)
        {
            return;
        }

        lastJsonWarningRealtime = Time.realtimeSinceStartup;
        Debug.LogWarning("[TargetCandidateSubscriber] " + message);
    }

    private void OnDisable()
    {
        acceptMessages = false;
        subscriptionGeneration++;

        if (rosSocket != null && !string.IsNullOrEmpty(subscriptionId))
        {
            try
            {
                rosSocket.Unsubscribe(subscriptionId);
            }
            catch
            {
            }
        }

        subscriptionId = null;
        subscribed = false;

        lock (queueLock)
        {
            pendingJsonMessages.Clear();
        }

        currentSourceSessionId = string.Empty;
        HasReceivedPacket = false;
        LastPacketReceivedRealtime = -1.0f;
    }
}
