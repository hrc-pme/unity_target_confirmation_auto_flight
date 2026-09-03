using System;
using CesiumForUnity;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Sensor;
using RosString = RosSharp.RosBridgeClient.MessageTypes.Std.String;

public class UavRosBridgeStateReceiver : MonoBehaviour
{
    [Header("ROS Bridge")]
    [SerializeField] private RosConnector rosConnector;
    [SerializeField] private string telemetryTopic = "/uav/telemetry_json";
    [SerializeField] private string imuTopic = "/uav/imu";
    [SerializeField] private float subscribeRetrySeconds = 1.0f;
    [SerializeField] private float sourceStaleTimeoutSeconds = 3.0f;

    [Header("Replay")]
    [SerializeField] private bool allowHistoricalReplayMessages = true;
    [SerializeField] private bool gpsOnlyModel = true;

    [Header("Target UAV")]
    [SerializeField] private Transform droneParent;
    [SerializeField] private Transform droneModel = null;

    [Header("GPS Position")]
    [SerializeField] private bool updatePosition = true;
    [SerializeField] private bool useAltitude = true;
    [SerializeField] private double fixedHeight = 100.0;
    [SerializeField] private double heightOffsetMeters = 0.0;

    [Header("Rotation")]
    [SerializeField] private bool updateRotation = true;
    [SerializeField] private bool yawIsRadians = false;
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Smoothing")]
    [SerializeField] private bool smoothMotion = true;
    [SerializeField] private float positionLerpSpeed = 5.0f;
    [SerializeField] private float rotationSlerpSpeed = 8.0f;

    [Header("Debug")]
    [SerializeField] private bool logReceivedData = false;

    public bool HasValidGps
    {
        get
        {
            lock (stateLock)
            {
                return hasValidGps;
            }
        }
        private set
        {
            lock (stateLock)
            {
                hasValidGps = value;
            }
        }
    }

    public double LatestLatitude
    {
        get
        {
            lock (stateLock)
            {
                return latestLatitude;
            }
        }
        private set
        {
            lock (stateLock)
            {
                latestLatitude = value;
            }
        }
    }

    public double LatestLongitude
    {
        get
        {
            lock (stateLock)
            {
                return latestLongitude;
            }
        }
        private set
        {
            lock (stateLock)
            {
                latestLongitude = value;
            }
        }
    }

    public double LatestAltitude
    {
        get
        {
            lock (stateLock)
            {
                return latestAltitude;
            }
        }
        private set
        {
            lock (stateLock)
            {
                latestAltitude = value;
            }
        }
    }

    public double LatestFlightHeight
    {
        get { lock (stateLock) { return latestFlightHeight; } }
    }

    public string LatestFlightMode
    {
        get { lock (stateLock) { return latestFlightMode; } }
    }

    public bool LatestArmed
    {
        get { lock (stateLock) { return latestArmed; } }
    }

    public int LatestSatellites
    {
        get { lock (stateLock) { return latestSatellites; } }
    }

    public double LatestGpsErrorM
    {
        get { lock (stateLock) { return latestGpsErrorM; } }
    }

    public double LatestEphM
    {
        get { lock (stateLock) { return latestEphM; } }
    }

    public double LatestGps1Latitude
    {
        get { lock (stateLock) { return latestGps1Latitude; } }
    }

    public double LatestGps1Longitude
    {
        get { lock (stateLock) { return latestGps1Longitude; } }
    }

    public double LatestGps2Latitude
    {
        get { lock (stateLock) { return latestGps2Latitude; } }
    }

    public double LatestGps2Longitude
    {
        get { lock (stateLock) { return latestGps2Longitude; } }
    }

    public double LatestYawDegrees
    {
        get { lock (stateLock) { return latestYawDegrees; } }
    }

    private readonly object stateLock = new object();

    private CesiumGlobeAnchor globeAnchor;
    private RosSocket rosSocket;
    private string telemetrySubscriptionId;
    private string imuSubscriptionId;
    private bool subscribed;
    private float nextSubscribeAttemptRealtime;
    private float lastWarningRealtime = -999.0f;
    private string latestSourceSessionId = string.Empty;
    private long lastTelemetryUtcTicks;
    private bool telemetryStateClearedForTimeout;
    private int subscriptionGeneration;
    private bool acceptMessages;
    private double sessionStartedUnixSeconds;

    private bool hasValidGps;
    private bool hasRotation;
    private double latestLatitude;
    private double latestLongitude;
    private double latestAltitude;
    private double latestFlightHeight = double.NaN;
    private string latestFlightMode = string.Empty;
    private bool latestArmed;
    private int latestSatellites;
    private double latestGpsErrorM = double.NaN;
    private double latestEphM = double.NaN;
    private double latestGps1Latitude = double.NaN;
    private double latestGps1Longitude = double.NaN;
    private double latestGps2Latitude = double.NaN;
    private double latestGps2Longitude = double.NaN;
    private double latestYawDegrees = double.NaN;
    private Quaternion targetRotation = Quaternion.identity;

    private bool currentPositionInitialized;
    private double currentLongitude;
    private double currentLatitude;
    private double currentHeight;

    private void Awake()
    {
        if (gpsOnlyModel)
        {
            imuTopic = string.Empty;
            updateRotation = false;
        }

        if (rosConnector == null)
        {
            rosConnector = FindObjectOfType<RosConnector>();
        }

        UavDashboardRos.ForceAllOrinUrls();
        UavDashboardRos.ForceOrinUrl(rosConnector);

        if (droneParent == null)
        {
            droneParent = transform;
        }

        if (droneParent != null && !droneParent.gameObject.activeSelf)
        {
            droneParent.gameObject.SetActive(true);
        }

        if (droneModel == null && droneParent != null && droneParent.childCount > 0)
        {
            droneModel = droneParent.GetChild(0);
        }

        if (droneModel != null)
        {
            droneModel.gameObject.SetActive(true);
        }

        globeAnchor = droneParent != null ? droneParent.GetComponent<CesiumGlobeAnchor>() : null;

        if (globeAnchor == null && droneParent != null)
        {
            globeAnchor = droneParent.gameObject.AddComponent<CesiumGlobeAnchor>();
        }

        if (globeAnchor != null)
        {
            globeAnchor.adjustOrientationForGlobeWhenMoving = true;
            globeAnchor.detectTransformChanges = true;
        }
    }

    private void OnEnable()
    {
        subscriptionGeneration++;
        acceptMessages = true;
        sessionStartedUnixSeconds = UavDashboardTime.GetUtcUnixSeconds();
        subscribed = false;
        nextSubscribeAttemptRealtime = 0.0f;
        lock (stateLock)
        {
            ResetTelemetryStateLocked();
            latestSourceSessionId = string.Empty;
            lastTelemetryUtcTicks = 0;
            telemetryStateClearedForTimeout = false;
            currentPositionInitialized = false;
        }
    }

    private void Update()
    {
        RefreshRosConnectionState();
        TrySubscribe();
        ClearTelemetryIfSourceStopped();

        double latitude;
        double longitude;
        double altitude;
        Quaternion rotation;
        bool gpsReady;
        bool rotationReady;

        lock (stateLock)
        {
            latitude = latestLatitude;
            longitude = latestLongitude;
            altitude = latestAltitude;
            rotation = targetRotation;
            gpsReady = hasValidGps;
            rotationReady = hasRotation;
        }

        if (updatePosition && gpsReady && globeAnchor != null)
        {
            UpdateCesiumPosition(longitude, latitude, altitude);
        }

        if (!gpsOnlyModel && updateRotation && rotationReady && droneModel != null)
        {
            UpdateDroneRotation(rotation);
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
            telemetrySubscriptionId = rosSocket.Subscribe<RosString>(
                telemetryTopic,
                message => ReceiveTelemetry(generation, message));

            if (!gpsOnlyModel && !string.IsNullOrEmpty(imuTopic))
            {
                try
                {
                    imuSubscriptionId = rosSocket.Subscribe<Imu>(
                        imuTopic,
                        message => ReceiveImu(generation, message));
                }
                catch (Exception exception)
                {
                    Warn("IMU subscribe failed: " + exception.Message);
                }
            }

            subscribed = true;

            if (logReceivedData)
            {
                Debug.Log(
                    "[UavRosBridgeStateReceiver] Subscribed: " +
                    telemetryTopic +
                    (gpsOnlyModel ? " (GPS only)" : ", " + imuTopic));
            }
        }
        catch (Exception exception)
        {
            subscribed = false;
            Warn("Subscribe failed: " + exception.Message);
        }
    }

    private void UpdateCesiumPosition(double longitude, double latitude, double height)
    {
        if (!currentPositionInitialized)
        {
            currentLongitude = longitude;
            currentLatitude = latitude;
            currentHeight = height;
            currentPositionInitialized = true;
        }
        else if (smoothMotion)
        {
            double t = 1.0 - Math.Exp(-Math.Max(0.01f, positionLerpSpeed) * Time.deltaTime);
            currentLongitude = LerpDouble(currentLongitude, longitude, t);
            currentLatitude = LerpDouble(currentLatitude, latitude, t);
            currentHeight = LerpDouble(currentHeight, height, t);
        }
        else
        {
            currentLongitude = longitude;
            currentLatitude = latitude;
            currentHeight = height;
        }

#pragma warning disable 0618
        globeAnchor.SetPositionLongitudeLatitudeHeight(
            currentLongitude,
            currentLatitude,
            currentHeight);
#pragma warning restore 0618
    }

    private void UpdateDroneRotation(Quaternion imuRotation)
    {
        Quaternion finalRotation = imuRotation * Quaternion.Euler(rotationOffsetEuler);

        if (smoothMotion)
        {
            droneModel.localRotation = Quaternion.Slerp(
                droneModel.localRotation,
                finalRotation,
                Time.deltaTime * Mathf.Max(0.01f, rotationSlerpSpeed));
        }
        else
        {
            droneModel.localRotation = finalRotation;
        }
    }

    private void ReceiveTelemetry(int generation, RosString message)
    {
        if (!acceptMessages || generation != subscriptionGeneration ||
            message == null || string.IsNullOrEmpty(message.data))
        {
            return;
        }

        UavTelemetryMessage telemetry;
        try
        {
            telemetry = JsonUtility.FromJson<UavTelemetryMessage>(message.data);
        }
        catch (Exception exception)
        {
            Warn("Telemetry JSON parse failed: " + exception.Message);
            return;
        }

        if (telemetry == null)
        {
            return;
        }

        if (!allowHistoricalReplayMessages &&
            telemetry.timestamp > 0.0 &&
            telemetry.timestamp + 1.0 < sessionStartedUnixSeconds)
        {
            return;
        }

        double latitude = telemetry.ResolveLatitude();
        double longitude = telemetry.ResolveLongitude();

        lock (stateLock)
        {
            string sourceSessionId = telemetry.source_session_id ?? string.Empty;
            if (!string.IsNullOrEmpty(sourceSessionId) &&
                !string.Equals(sourceSessionId, latestSourceSessionId, StringComparison.Ordinal))
            {
                ResetTelemetryStateLocked();
                latestSourceSessionId = sourceSessionId;
                currentPositionInitialized = false;
            }

            lastTelemetryUtcTicks = DateTime.UtcNow.Ticks;
            telemetryStateClearedForTimeout = false;
            latestFlightMode = telemetry.flight_mode ?? string.Empty;
            latestArmed = telemetry.armed;
            latestSatellites = telemetry.satellites;
            latestGpsErrorM = telemetry.gps_error_m;
            latestEphM = telemetry.eph_m;
            latestGps1Latitude = telemetry.gps1_latitude;
            latestGps1Longitude = telemetry.gps1_longitude;
            latestGps2Latitude = telemetry.gps2_latitude;
            latestGps2Longitude = telemetry.gps2_longitude;
            if (IsFinite(telemetry.relative_altitude))
            {
                // ArduPilot's relative altitude can be slightly negative on the
                // ground after its home/EKF origin is rebuilt.  Do not reject
                // that sample, otherwise the UI keeps the last airborne height.
                latestFlightHeight = telemetry.armed
                    ? Math.Max(0.0, telemetry.relative_altitude)
                    : 0.0;
            }
        }

        if (IsValidGps(latitude, longitude))
        {
            double resolvedAltitude = telemetry.ResolveAltitude(latestAltitude);
            double height = useAltitude && IsFinite(resolvedAltitude)
                ? resolvedAltitude + heightOffsetMeters
                : fixedHeight + heightOffsetMeters;

            lock (stateLock)
            {
                latestLatitude = latitude;
                latestLongitude = longitude;
                latestAltitude = height;
                hasValidGps = true;
            }
        }

        double yaw = telemetry.ResolveYaw();
        bool hasTelemetryYaw =
            message.data.Contains("\"yaw\"") ||
            message.data.Contains("\"imu_yaw\"") ||
            message.data.Contains("\"heading\"");

        if (!gpsOnlyModel && hasTelemetryYaw && IsFinite(yaw))
        {
            double yawDegrees = yawIsRadians ? yaw * Mathf.Rad2Deg : yaw;
            Quaternion yawRotation = Quaternion.Euler(0.0f, (float)yawDegrees, 0.0f);

            lock (stateLock)
            {
                latestYawDegrees = yawDegrees;
                targetRotation = yawRotation;
                hasRotation = true;
            }
        }

        if (logReceivedData && IsValidGps(latitude, longitude))
        {
            Debug.Log(
                "[UavRosBridgeStateReceiver] Telemetry lat=" +
                latitude.ToString("F8") +
                ", lon=" +
                longitude.ToString("F8") +
                ", mode=" +
                latestFlightMode);
        }
    }

    private void ClearTelemetryIfSourceStopped()
    {
        float timeout = Mathf.Max(1.0f, sourceStaleTimeoutSeconds);
        long nowTicks = DateTime.UtcNow.Ticks;

        lock (stateLock)
        {
            if (lastTelemetryUtcTicks <= 0 || telemetryStateClearedForTimeout)
            {
                return;
            }

            double ageSeconds = TimeSpan.FromTicks(nowTicks - lastTelemetryUtcTicks).TotalSeconds;
            if (ageSeconds <= timeout)
            {
                return;
            }

            ResetTelemetryStateLocked();
            latestSourceSessionId = string.Empty;
            telemetryStateClearedForTimeout = true;
            currentPositionInitialized = false;
        }
    }

    private void ResetTelemetryStateLocked()
    {
        hasValidGps = false;
        hasRotation = false;
        latestLatitude = double.NaN;
        latestLongitude = double.NaN;
        latestAltitude = double.NaN;
        latestFlightHeight = double.NaN;
        latestFlightMode = string.Empty;
        latestArmed = false;
        latestSatellites = 0;
        latestGpsErrorM = double.NaN;
        latestEphM = double.NaN;
        latestGps1Latitude = double.NaN;
        latestGps1Longitude = double.NaN;
        latestGps2Latitude = double.NaN;
        latestGps2Longitude = double.NaN;
        latestYawDegrees = double.NaN;
        targetRotation = Quaternion.identity;
    }

    private void ReceiveImu(int generation, Imu message)
    {
        if (!acceptMessages || generation != subscriptionGeneration ||
            message == null || message.orientation == null)
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

        Quaternion rosQuaternion = new Quaternion(
            (float)message.orientation.x,
            (float)message.orientation.y,
            (float)message.orientation.z,
            (float)message.orientation.w);

        float magnitudeSquared =
            rosQuaternion.x * rosQuaternion.x +
            rosQuaternion.y * rosQuaternion.y +
            rosQuaternion.z * rosQuaternion.z +
            rosQuaternion.w * rosQuaternion.w;

        if (magnitudeSquared < 0.000001f)
        {
            return;
        }

        float magnitude = Mathf.Sqrt(magnitudeSquared);
        Quaternion normalized = new Quaternion(
            rosQuaternion.x / magnitude,
            rosQuaternion.y / magnitude,
            rosQuaternion.z / magnitude,
            rosQuaternion.w / magnitude);

        Quaternion unityRotation = RosQuaternionToUnity(normalized);

        lock (stateLock)
        {
            targetRotation = unityRotation;
            latestYawDegrees = unityRotation.eulerAngles.y;
            hasRotation = true;
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
        telemetrySubscriptionId = null;
        imuSubscriptionId = null;
        subscribed = false;
        rosSocket = null;
        nextSubscribeAttemptRealtime = 0.0f;
    }

    private void Warn(string message)
    {
        if (Time.realtimeSinceStartup - lastWarningRealtime < 5.0f)
        {
            return;
        }

        lastWarningRealtime = Time.realtimeSinceStartup;
        Debug.LogWarning("[UavRosBridgeStateReceiver] " + message);
    }

    private static bool IsValidGps(double latitude, double longitude)
    {
        return IsFinite(latitude) &&
               IsFinite(longitude) &&
               latitude >= -90.0 &&
               latitude <= 90.0 &&
               longitude >= -180.0 &&
               longitude <= 180.0 &&
               !UavTelemetryMessage.IsZeroCoordinate(latitude, longitude);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static double LerpDouble(double start, double end, double t)
    {
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;
        return start + (end - start) * t;
    }

    private static Quaternion RosQuaternionToUnity(Quaternion rosQuaternion)
    {
        return new Quaternion(
            rosQuaternion.y,
            -rosQuaternion.z,
            -rosQuaternion.x,
            rosQuaternion.w);
    }

    private void OnDisable()
    {
        acceptMessages = false;
        subscriptionGeneration++;

        if (rosSocket != null)
        {
            try
            {
                if (!string.IsNullOrEmpty(telemetrySubscriptionId))
                {
                    rosSocket.Unsubscribe(telemetrySubscriptionId);
                }
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrEmpty(imuSubscriptionId))
                {
                    rosSocket.Unsubscribe(imuSubscriptionId);
                }
            }
            catch
            {
            }
        }

        telemetrySubscriptionId = null;
        imuSubscriptionId = null;
        subscribed = false;
        lock (stateLock)
        {
            ResetTelemetryStateLocked();
            latestSourceSessionId = string.Empty;
            lastTelemetryUtcTicks = 0;
            telemetryStateClearedForTimeout = false;
            currentPositionInitialized = false;
        }
    }
}
