using System;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

public class TargetCommandPublisher : MonoBehaviour
{
    [Header("ROS")]
    [SerializeField] private RosConnector rosConnector;
    [SerializeField] private string commandTopic = "/unity/target_command";
    [SerializeField] private string statusTopic = "/unity/target_command_status";
    [SerializeField] private float subscribeRetrySeconds = 1.0f;

    [Header("Command")]
    [SerializeField] private float hoverHeightRelativeM = 20.0f;
    [SerializeField] private float commandTimeoutSeconds = 10.0f;
    public bool mockMode = false;

    [Header("Replay")]
    [SerializeField] private bool allowUnsolicitedReplayStatus = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    public event Action<TargetCommandStatusMessage> OnCommandStatusChanged;
    public event Action<string> OnLocalCommandMessage;

    public string PendingCommandId { get; private set; }
    public int PendingTrackId { get; private set; } = -1;
    public bool HasPendingCommand => !string.IsNullOrEmpty(PendingCommandId);
    public float HoverHeightRelativeM => hoverHeightRelativeM;

    private readonly object queueLock = new object();
    private readonly Queue<string> pendingStatusJson = new Queue<string>();

    private RosSocket rosSocket;
    private string publicationId;
    private string statusSubscriptionId;
    private bool advertised;
    private bool subscribed;
    private string lastCancelCommandId;
    private float pendingStartRealtime;
    private float nextRosSetupAttemptRealtime;
    private float lastWarningRealtime = -999.0f;
    private int subscriptionGeneration;
    private bool acceptStatuses;

    private void Awake()
    {
        // Keep old scenes/prefabs from displaying a serialized legacy 15 m.
        // The Orin bridge independently enforces the same 20 m final height.
        hoverHeightRelativeM = 20.0f;

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
        acceptStatuses = true;
        PendingCommandId = null;
        PendingTrackId = -1;
        lastCancelCommandId = null;
        pendingStartRealtime = 0.0f;
        lock (queueLock)
        {
            pendingStatusJson.Clear();
        }
        nextRosSetupAttemptRealtime = 0.0f;
    }

    private void Update()
    {
        RefreshRosConnectionState();
        TrySetupRos();
        DrainStatuses();
        CheckTimeout();
    }

    public bool TryPublishGotoTarget(TargetCandidateData candidate, out string errorMessage)
    {
        return TryPublishConfirmTarget(candidate, out errorMessage);
    }

    public bool TryPublishConfirmTarget(TargetCandidateData candidate, out string errorMessage)
    {
        errorMessage = null;

        if (candidate == null)
        {
            errorMessage = "NO TARGET";
            EmitLocal(errorMessage);
            return false;
        }

        if (!candidate.IsValid())
        {
            errorMessage = "INVALID TARGET";
            EmitLocal(errorMessage);
            return false;
        }

        if (HasPendingCommand)
        {
            errorMessage = "COMMAND PENDING";
            EmitLocal(errorMessage);
            return false;
        }

        if (!mockMode && !CanPublish())
        {
            errorMessage = "ROS NOT CONNECTED";
            EmitLocal(errorMessage);
            return false;
        }

        TargetCommandMessage command = new TargetCommandMessage
        {
            command_id = Guid.NewGuid().ToString(),
            action = "CONFIRM",
            track_id = candidate.track_id,
            candidate_id = candidate.candidate_id ?? string.Empty,
            target_latitude = candidate.DisplayLatitude,
            target_longitude = candidate.DisplayLongitude
        };

        string json = JsonUtility.ToJson(command);

        if (!mockMode && !PublishJson(json, out errorMessage))
        {
            EmitLocal(errorMessage);
            return false;
        }

        PendingCommandId = command.command_id;
        PendingTrackId = command.track_id;
        pendingStartRealtime = Time.realtimeSinceStartup;
        EmitLocal("SENDING TARGET...");
        return true;
    }

    public bool TryPublishCancel(int trackId, out string errorMessage)
    {
        errorMessage = null;

        TargetCancelCommandMessage command = new TargetCancelCommandMessage
        {
            command_id = Guid.NewGuid().ToString(),
            action = "CANCEL",
            timestamp = UavDashboardTime.GetUtcUnixSeconds(),
            track_id = trackId
        };

        string json = JsonUtility.ToJson(command);

        if (!mockMode && !PublishJson(json, out errorMessage))
        {
            EmitLocal(errorMessage);
            return false;
        }

        lastCancelCommandId = command.command_id;
        EmitLocal("CANCELLING...");
        return true;
    }

    public void InjectMockStatus(TargetCommandStatusMessage statusMessage)
    {
        ApplyStatus(statusMessage);
    }

    private void TrySetupRos()
    {
        if (mockMode || Time.realtimeSinceStartup < nextRosSetupAttemptRealtime)
        {
            return;
        }

        nextRosSetupAttemptRealtime =
            Time.realtimeSinceStartup + Mathf.Max(0.5f, subscribeRetrySeconds);

        if (!IsRosConnected())
        {
            return;
        }

        rosSocket = rosConnector.RosSocket;

        if (!advertised)
        {
            try
            {
                publicationId = rosSocket.Advertise<RosString>(commandTopic);
                advertised = true;
            }
            catch (Exception exception)
            {
                Warn("Advertise failed: " + exception.Message);
            }
        }

        if (!subscribed)
        {
            try
            {
                int generation = subscriptionGeneration;
                statusSubscriptionId = rosSocket.Subscribe<RosString>(
                    statusTopic,
                    message => ReceiveStatus(generation, message));
                subscribed = true;
            }
            catch (Exception exception)
            {
                Warn("Status subscribe failed: " + exception.Message);
            }
        }
    }

    private bool CanPublish()
    {
        TrySetupRos();
        return IsRosConnected() && advertised && !string.IsNullOrEmpty(publicationId);
    }

    private bool PublishJson(string json, out string errorMessage)
    {
        errorMessage = null;

        try
        {
            rosSocket.Publish(publicationId, new RosString(json));
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = "ROS NOT CONNECTED";
            Warn("Publish failed: " + exception.Message);
            return false;
        }
    }

    private void ReceiveStatus(int generation, RosString message)
    {
        if (!acceptStatuses || generation != subscriptionGeneration ||
            message == null || string.IsNullOrEmpty(message.data))
        {
            return;
        }

        lock (queueLock)
        {
            pendingStatusJson.Enqueue(message.data);
            while (pendingStatusJson.Count > 5)
            {
                pendingStatusJson.Dequeue();
            }
        }
    }

    private void DrainStatuses()
    {
        string json = null;

        lock (queueLock)
        {
            // ROS callbacks can enqueue RECEIVED, ACCEPTED and ARRIVED between
            // two Unity frames. Always apply the newest state; taking the
            // oldest and clearing the queue could discard the terminal result.
            while (pendingStatusJson.Count > 0)
            {
                json = pendingStatusJson.Dequeue();
            }
        }

        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            ApplyStatus(JsonUtility.FromJson<TargetCommandStatusMessage>(json));
        }
        catch (Exception exception)
        {
            Warn("Status JSON parse failed: " + exception.Message);
        }
    }

    private void ApplyStatus(TargetCommandStatusMessage statusMessage)
    {
        if (statusMessage == null)
        {
            return;
        }

        bool hasStatusCommandId = !string.IsNullOrEmpty(statusMessage.command_id);
        bool matchesConfirm = HasPendingCommand &&
                              ((hasStatusCommandId && statusMessage.command_id == PendingCommandId) ||
                               (!hasStatusCommandId && statusMessage.track_id == PendingTrackId));
        bool matchesCancel = !string.IsNullOrEmpty(lastCancelCommandId) &&
                             hasStatusCommandId &&
                             statusMessage.command_id == lastCancelCommandId;

        if (!matchesConfirm && !matchesCancel)
        {
            if (allowUnsolicitedReplayStatus)
            {
                OnCommandStatusChanged?.Invoke(statusMessage);
            }
            return;
        }

        if (IsTerminalStatus(statusMessage.status))
        {
            PendingCommandId = null;
            PendingTrackId = -1;
        }
        else if (matchesConfirm)
        {
            pendingStartRealtime = Time.realtimeSinceStartup;
        }

        OnCommandStatusChanged?.Invoke(statusMessage);
    }

    private void CheckTimeout()
    {
        if (!HasPendingCommand || commandTimeoutSeconds <= 0.0f)
        {
            return;
        }

        if (Time.realtimeSinceStartup - pendingStartRealtime < commandTimeoutSeconds)
        {
            return;
        }

        PendingCommandId = null;
        PendingTrackId = -1;
        EmitLocal("COMMAND TIMEOUT");
    }

    private void EmitLocal(string message)
    {
        if (logDebug)
        {
            Debug.Log("[TargetCommandPublisher] " + message);
        }

        OnLocalCommandMessage?.Invoke(message);
    }

    private static bool IsTerminalStatus(string status)
    {
        return status == "ARRIVED" ||
               status == "CANCELLED" ||
               status == "REJECTED" ||
               status == "FAILED" ||
               status == "ERROR";
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
        if (!advertised && !subscribed)
        {
            return;
        }

        if (IsRosConnected() && ReferenceEquals(rosConnector.RosSocket, rosSocket))
        {
            return;
        }

        subscriptionGeneration++;
        publicationId = null;
        statusSubscriptionId = null;
        advertised = false;
        subscribed = false;
        rosSocket = null;
        nextRosSetupAttemptRealtime = 0.0f;
        lock (queueLock)
        {
            pendingStatusJson.Clear();
        }
    }

    private void Warn(string message)
    {
        if (Time.realtimeSinceStartup - lastWarningRealtime < 5.0f)
        {
            return;
        }

        lastWarningRealtime = Time.realtimeSinceStartup;
        Debug.LogWarning("[TargetCommandPublisher] " + message);
    }

    private void OnDisable()
    {
        acceptStatuses = false;
        subscriptionGeneration++;

        if (rosSocket != null)
        {
            try
            {
                if (!string.IsNullOrEmpty(statusSubscriptionId))
                {
                    rosSocket.Unsubscribe(statusSubscriptionId);
                }
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrEmpty(publicationId))
                {
                    rosSocket.Unadvertise(publicationId);
                }
            }
            catch
            {
            }
        }

        publicationId = null;
        statusSubscriptionId = null;
        advertised = false;
        subscribed = false;
        PendingCommandId = null;
        PendingTrackId = -1;
        lastCancelCommandId = null;
        pendingStartRealtime = 0.0f;
        lock (queueLock)
        {
            pendingStatusJson.Clear();
        }
    }
}
