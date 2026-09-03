using System;
using UnityEngine;
using UnityEngine.UI;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;

public class CompressedImageUIReceiver : MonoBehaviour
{
    [Header("ROS")]
    [SerializeField] private RosConnector rosConnector;
    [SerializeField] private string imageTopic = "/d455i/color/image_annotated/compressed";
    [SerializeField] private float subscribeRetrySeconds = 1.0f;

    [Header("UI")]
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private bool preserveAspect = true;
    [SerializeField] private float maxDecodeFps = 6.0f;
    [SerializeField] private int maxImageBytes = 2097152;
    [SerializeField] private float staleImageTimeoutSeconds = 2.0f;

    [Header("Replay")]
    [SerializeField] private bool allowHistoricalReplayMessages = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    public Texture LatestTexture => mockTexture != null ? mockTexture : runtimeTexture;
    public bool HasImage => LatestTexture != null;
    public RawImage TargetRawImage => targetRawImage;

    private readonly object imageLock = new object();
    private byte[] pendingImageBytes;
    private bool hasPendingImage;

    private Texture2D runtimeTexture;
    private Texture mockTexture;
    private RosSocket rosSocket;
    private string subscriptionId;
    private bool subscribed;
    private float nextSubscribeAttemptRealtime;
    private float nextDecodeRealtime;
    private float lastWarningRealtime = -999.0f;
    private long lastImageUtcTicks;
    private int subscriptionGeneration;
    private bool acceptImages;
    private double sessionStartedUnixSeconds;

    private void Awake()
    {
        if (rosConnector == null)
        {
            rosConnector = FindObjectOfType<RosConnector>();
        }

        UavDashboardRos.ForceAllOrinUrls();
        UavDashboardRos.ForceOrinUrl(rosConnector);

        if (targetRawImage == null)
        {
            targetRawImage = GetComponent<RawImage>();
        }
    }

    private void OnEnable()
    {
        subscriptionGeneration++;
        acceptImages = true;
        sessionStartedUnixSeconds = UavDashboardTime.GetUtcUnixSeconds();
        subscribed = false;
        nextSubscribeAttemptRealtime = 0.0f;
        nextDecodeRealtime = 0.0f;
        mockTexture = null;
        ApplyTexture(null);
        DestroyRuntimeTexture();
        lock (imageLock)
        {
            pendingImageBytes = null;
            hasPendingImage = false;
            lastImageUtcTicks = 0;
        }
        DisableD455RawImageDisplays();
    }

    private void Update()
    {
        RefreshRosConnectionState();
        TrySubscribe();
        ApplyPendingImage();
        ClearStaleImage();
    }

    public void SetMockImage(Texture texture)
    {
        mockTexture = texture;
        ApplyTexture(texture);
    }

    public void ShowLiveStream()
    {
        mockTexture = null;
        if (runtimeTexture != null)
        {
            ApplyTexture(runtimeTexture);
        }
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
            Warn("ROS NOT CONNECTED");
            return;
        }

        try
        {
            rosSocket = rosConnector.RosSocket;
            int generation = subscriptionGeneration;
            subscriptionId = rosSocket.Subscribe<CompressedImage>(
                imageTopic,
                message => ReceiveImage(generation, message));
            subscribed = true;

            if (logDebug)
            {
                Debug.Log("[CompressedImageUIReceiver] Subscribed: " + imageTopic);
            }
        }
        catch (Exception exception)
        {
            subscribed = false;
            Warn("Subscribe failed: " + exception.Message);
        }
    }

    private void ReceiveImage(int generation, CompressedImage message)
    {
        if (!acceptImages || generation != subscriptionGeneration ||
            message == null || message.data == null || message.data.Length == 0)
        {
            return;
        }

        if (message.header != null && message.header.stamp != null)
        {
            double messageUnixSeconds =
                message.header.stamp.sec + message.header.stamp.nanosec / 1000000000.0;
            if (!allowHistoricalReplayMessages &&
                messageUnixSeconds > 0.0 &&
                messageUnixSeconds + 1.0 < sessionStartedUnixSeconds)
            {
                return;
            }
        }

        byte[] copy = new byte[message.data.Length];
        Buffer.BlockCopy(message.data, 0, copy, 0, message.data.Length);

        lock (imageLock)
        {
            pendingImageBytes = copy;
            hasPendingImage = true;
            lastImageUtcTicks = DateTime.UtcNow.Ticks;
        }
    }

    private void ApplyPendingImage()
    {
        byte[] bytes = null;

        if (Time.realtimeSinceStartup < nextDecodeRealtime)
        {
            return;
        }

        lock (imageLock)
        {
            if (hasPendingImage)
            {
                bytes = pendingImageBytes;
                pendingImageBytes = null;
                hasPendingImage = false;
            }
        }

        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        if (maxImageBytes > 0 && bytes.Length > maxImageBytes)
        {
            Warn("Compressed image skipped: " + bytes.Length + " bytes");
            return;
        }

        float fps = Mathf.Max(1.0f, maxDecodeFps);
        nextDecodeRealtime = Time.realtimeSinceStartup + 1.0f / fps;

        if (runtimeTexture == null)
        {
            runtimeTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        }

        if (!runtimeTexture.LoadImage(bytes, false))
        {
            Warn("Texture2D.LoadImage failed");
            return;
        }

        mockTexture = null;
        ApplyTexture(runtimeTexture);
    }

    private void ClearStaleImage()
    {
        if (mockTexture != null || runtimeTexture == null || targetRawImage == null ||
            targetRawImage.texture != runtimeTexture)
        {
            return;
        }

        long receivedTicks;
        lock (imageLock)
        {
            receivedTicks = lastImageUtcTicks;
        }

        if (receivedTicks <= 0)
        {
            return;
        }

        double ageSeconds = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - receivedTicks).TotalSeconds;
        if (ageSeconds > Mathf.Max(0.5f, staleImageTimeoutSeconds))
        {
            ApplyTexture(null);
        }
    }

    private void ApplyTexture(Texture texture)
    {
        if (targetRawImage == null)
        {
            return;
        }

        targetRawImage.texture = texture;

        if (preserveAspect && texture != null)
        {
            targetRawImage.uvRect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            RectTransform rectTransform = targetRawImage.rectTransform;
            float displayWidth = rectTransform.rect.width;
            if (displayWidth <= 0.0f)
            {
                displayWidth = rectTransform.sizeDelta.x;
            }

            if (displayWidth > 0.0f && texture.width > 0 && texture.height > 0)
            {
                float sourceAspect = (float)texture.width / texture.height;
                rectTransform.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    displayWidth / sourceAspect);
            }
        }
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
        lock (imageLock)
        {
            pendingImageBytes = null;
            hasPendingImage = false;
        }
    }

    private void DisableD455RawImageDisplays()
    {
        MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == this)
            {
                continue;
            }

            Type type = behaviour.GetType();
            if (type.FullName != "RosSharp.RosBridgeClient.ImageSubscriberPointCloud")
            {
                continue;
            }

            System.Reflection.FieldInfo topicField = type.GetField("Topic");
            string topic = topicField != null ? topicField.GetValue(behaviour) as string : null;
            if (topic != "/d455i/color/image_raw")
            {
                continue;
            }

            behaviour.enabled = false;
            if (logDebug)
            {
                Debug.Log("[CompressedImageUIReceiver] Disabled raw image display subscriber: " + topic);
            }
        }
    }

    private void Warn(string message)
    {
        if (Time.realtimeSinceStartup - lastWarningRealtime < 5.0f)
        {
            return;
        }

        lastWarningRealtime = Time.realtimeSinceStartup;
        Debug.LogWarning("[CompressedImageUIReceiver] " + message);
    }

    private void OnDisable()
    {
        acceptImages = false;
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

        lock (imageLock)
        {
            pendingImageBytes = null;
            hasPendingImage = false;
            lastImageUtcTicks = 0;
        }

        mockTexture = null;
        ApplyTexture(null);
        DestroyRuntimeTexture();
    }

    private void OnDestroy()
    {
        DestroyRuntimeTexture();
    }

    private void DestroyRuntimeTexture()
    {
        if (runtimeTexture != null)
        {
            Destroy(runtimeTexture);
            runtimeTexture = null;
        }
    }
}
