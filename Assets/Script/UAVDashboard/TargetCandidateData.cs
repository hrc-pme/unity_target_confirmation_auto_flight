using System;

[Serializable]
public class TargetCandidatePacket
{
    public string source_session_id;
    public double timestamp;
    public bool ready_for_confirm;
    public int candidate_count;
    public bool auto_select_latest;
    public string[] confirm_blockers;
    public TargetCandidateData[] candidates;
    public UavApproachStateData approach;
}

[Serializable]
public class UavApproachStateData
{
    public double timestamp;
    public string state;
    public string message;
    public bool ready_for_unity_confirm;
    public string selected_candidate_id;
    public int selected_track_id;
    public double target_latitude;
    public double target_longitude;
    public double target_position_error_m;
    public double verification_altitude_m;
    public double final_altitude_m;
    public int verification_sample_count;
}

[Serializable]
public class TargetCandidateData
{
    public int track_id;
    public string candidate_id;
    public string class_name;
    public double latitude;
    public double longitude;
    public double wgs84_latitude;
    public double wgs84_longitude;
    public double twd97_e;
    public double twd97_n;
    public double twd97_x;
    public double twd97_y;
    public double position_error_m;
    public double stable_for_s;
    public double display_hold_remaining_s;
    public double confidence;
    public string image_base64;

    public double DisplayLatitude => HasWgs84Position() ? wgs84_latitude : latitude;
    public double DisplayLongitude => HasWgs84Position() ? wgs84_longitude : longitude;
    public double DisplayTwd97E => IsFinite(twd97_e) && Math.Abs(twd97_e) > 0.000001 ? twd97_e : twd97_x;
    public double DisplayTwd97N => IsFinite(twd97_n) && Math.Abs(twd97_n) > 0.000001 ? twd97_n : twd97_y;

    public bool IsValid()
    {
        return track_id >= 0 &&
               IsValidLatitude(DisplayLatitude) &&
               IsValidLongitude(DisplayLongitude) &&
               !UavTelemetryMessage.IsZeroCoordinate(DisplayLatitude, DisplayLongitude) &&
               IsOptionalFinite(twd97_e) &&
               IsOptionalFinite(twd97_n) &&
               IsOptionalFinite(twd97_x) &&
               IsOptionalFinite(twd97_y) &&
               IsOptionalFinite(position_error_m) &&
               IsOptionalFinite(stable_for_s) &&
               IsOptionalFinite(display_hold_remaining_s) &&
               (confidence == 0.0 || (IsFinite(confidence) && confidence >= 0.0 && confidence <= 1.0));
    }

    private static bool IsOptionalFinite(double value)
    {
        return value == 0.0 || IsFinite(value);
    }

    private static bool IsValidLatitude(double value)
    {
        return IsFinite(value) && value >= -90.0 && value <= 90.0;
    }

    private static bool IsValidLongitude(double value)
    {
        return IsFinite(value) && value >= -180.0 && value <= 180.0;
    }

    private bool HasWgs84Position()
    {
        return IsValidLatitude(wgs84_latitude) &&
               IsValidLongitude(wgs84_longitude) &&
               !UavTelemetryMessage.IsZeroCoordinate(wgs84_latitude, wgs84_longitude);
    }

    public static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

[Serializable]
public class TargetCommandMessage
{
    public string command_id;
    public string action;
    public int track_id;
    public string candidate_id;
    public double target_latitude;
    public double target_longitude;
}

[Serializable]
public class TargetCancelCommandMessage
{
    public string command_id;
    public string action;
    public double timestamp;
    public int track_id;
}

[Serializable]
public class TargetCommandStatusMessage
{
    public string command_id;
    public int track_id;
    public string candidate_id;
    public string status;
    public string message;
    public double distance_remaining_m;
}

[Serializable]
public class UavTelemetryMessage
{
    public string source_session_id;
    public double timestamp;
    public double latitude;
    public double longitude;
    public double altitude;
    public double relative_altitude;
    public double display_latitude;
    public double display_longitude;
    public double display_altitude;
    public double gps1_latitude;
    public double gps1_longitude;
    public double gps1_altitude;
    public double gps2_latitude;
    public double gps2_longitude;
    public double gps2_altitude;
    public double gps_error_m;
    public string flight_mode;
    public bool armed;
    public int satellites;
    public double eph_m;
    public double yaw;
    public double heading;
    public double imu_yaw;
    public bool camera_ok;
    public string detector_backend;
    public bool flight_control_enabled;
    public bool flight_control_ready;
    public string[] flight_control_blockers;
    public UavApproachStateData approach;

    public bool HasDisplayPosition()
    {
        return IsValidLatitude(display_latitude) &&
               IsValidLongitude(display_longitude) &&
               !IsZeroCoordinate(display_latitude, display_longitude);
    }

    public double ResolveLatitude()
    {
        return HasDisplayPosition() ? display_latitude : latitude;
    }

    public double ResolveLongitude()
    {
        return HasDisplayPosition() ? display_longitude : longitude;
    }

    public double ResolveAltitude(double fallbackAltitude)
    {
        if (IsFinite(display_altitude) && Math.Abs(display_altitude) > 0.000001) return display_altitude;
        if (IsFinite(altitude) && Math.Abs(altitude) > 0.000001) return altitude;
        if (IsFinite(relative_altitude) && Math.Abs(relative_altitude) > 0.000001) return relative_altitude;
        return fallbackAltitude;
    }

    public double ResolveYaw()
    {
        if (IsFinite(yaw)) return yaw;
        if (IsFinite(imu_yaw)) return imu_yaw;
        if (IsFinite(heading)) return heading;
        return double.NaN;
    }

    public static bool IsValidLatitude(double value)
    {
        return IsFinite(value) && value >= -90.0 && value <= 90.0;
    }

    public static bool IsValidLongitude(double value)
    {
        return IsFinite(value) && value >= -180.0 && value <= 180.0;
    }

    public static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static bool IsZeroCoordinate(double latitude, double longitude)
    {
        return Math.Abs(latitude) < 0.000001 && Math.Abs(longitude) < 0.000001;
    }
}

public static class UavDashboardRos
{
    public const string OrinRosBridgeUrl = "ws://10.0.0.8:9090";

    public static void ForceAllOrinUrls()
    {
        RosSharp.RosBridgeClient.RosConnector[] connectors =
            UnityEngine.Object.FindObjectsOfType<RosSharp.RosBridgeClient.RosConnector>();

        for (int i = 0; i < connectors.Length; i++)
        {
            ForceOrinUrl(connectors[i]);
        }
    }

    public static void ForceOrinUrl(RosSharp.RosBridgeClient.RosConnector rosConnector)
    {
        if (rosConnector == null)
        {
            return;
        }

        Type connectorType = rosConnector.GetType();
        System.Reflection.FieldInfo field = connectorType.GetField("RosBridgeServerUrl");
        if (field != null && field.FieldType == typeof(string))
        {
            field.SetValue(rosConnector, OrinRosBridgeUrl);
            return;
        }

        System.Reflection.PropertyInfo property = connectorType.GetProperty("RosBridgeServerUrl");
        if (property != null && property.CanWrite && property.PropertyType == typeof(string))
        {
            property.SetValue(rosConnector, OrinRosBridgeUrl, null);
        }
    }
}

public static class UavDashboardTime
{
    private static readonly DateTime UnixEpoch =
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static double GetUtcUnixSeconds()
    {
        return DateTime.UtcNow.Subtract(UnixEpoch).TotalSeconds;
    }
}
