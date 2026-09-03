using UnityEngine;

public class TargetCandidateMockTester : MonoBehaviour
{
    [SerializeField] private TargetCandidateSubscriber candidateSubscriber;
    [SerializeField] private TargetCommandPublisher commandPublisher;
    [SerializeField] private Texture2D[] mockImages = null;
    [SerializeField] private bool injectOnStart = false;
    [SerializeField] private bool automaticallyEnablePublisherMockMode = true;
    [SerializeField] private KeyCode injectCandidatesKey = KeyCode.F6;
    [SerializeField] private KeyCode acceptedKey = KeyCode.F7;
    [SerializeField] private KeyCode executingKey = KeyCode.F8;
    [SerializeField] private KeyCode arrivedKey = KeyCode.F9;
    [SerializeField] private KeyCode rejectedKey = KeyCode.F10;
    [SerializeField] private KeyCode clearKey = KeyCode.F11;

    private void Awake()
    {
        if (candidateSubscriber == null) candidateSubscriber = FindObjectOfType<TargetCandidateSubscriber>();
        if (commandPublisher == null) commandPublisher = FindObjectOfType<TargetCommandPublisher>();
    }

    private void Start()
    {
        if (commandPublisher != null && automaticallyEnablePublisherMockMode)
        {
            commandPublisher.mockMode = true;
        }

        if (injectOnStart)
        {
            InjectCandidates();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(injectCandidatesKey))
        {
            InjectCandidates();
        }
        else if (Input.GetKeyDown(acceptedKey))
        {
            InjectStatus("ACCEPTED", string.Empty, double.NaN);
        }
        else if (Input.GetKeyDown(executingKey))
        {
            InjectStatus("EXECUTING", string.Empty, 25.3);
        }
        else if (Input.GetKeyDown(arrivedKey))
        {
            InjectStatus("ARRIVED", string.Empty, 0.0);
        }
        else if (Input.GetKeyDown(rejectedKey))
        {
            InjectStatus("REJECTED", "Mock target rejected", double.NaN);
        }
        else if (Input.GetKeyDown(clearKey))
        {
            ClearCandidates();
        }
    }

    private void InjectCandidates()
    {
        if (candidateSubscriber == null)
        {
            Debug.LogWarning("[TargetCandidateMockTester] Candidate subscriber missing");
            return;
        }

        TargetCandidatePacket packet = new TargetCandidatePacket
        {
            timestamp = UavDashboardTime.GetUtcUnixSeconds(),
            ready_for_confirm = true,
            candidates = new[]
            {
                CreateCandidate(101, "car", 24.7960000, 120.9950000, 250100.10, 2745100.10, 1.8, 0.94, 0),
                CreateCandidate(102, "car", 24.7998907, 121.0296557, 253000.12, 2745000.34, 2.4, 0.91, 1),
                CreateCandidate(103, "target", 24.7985000, 121.0287000, 252900.20, 2744900.50, 3.1, 0.87, 2)
            }
        };

        candidateSubscriber.InjectMockPacket(packet);
    }

    private void ClearCandidates()
    {
        if (candidateSubscriber == null)
        {
            return;
        }

        candidateSubscriber.InjectMockPacket(new TargetCandidatePacket
        {
            timestamp = UavDashboardTime.GetUtcUnixSeconds(),
            candidates = new TargetCandidateData[0]
        });
    }

    private TargetCandidateData CreateCandidate(
        int trackId,
        string className,
        double latitude,
        double longitude,
        double twd97E,
        double twd97N,
        double errorMeters,
        double confidence,
        int imageIndex)
    {
        return new TargetCandidateData
        {
            track_id = trackId,
            candidate_id = "mock-" + trackId.ToString(),
            class_name = className,
            latitude = latitude,
            longitude = longitude,
            wgs84_latitude = latitude,
            wgs84_longitude = longitude,
            twd97_e = twd97E,
            twd97_n = twd97N,
            position_error_m = errorMeters,
            stable_for_s = 3.5,
            display_hold_remaining_s = 8.0,
            confidence = confidence,
            image_base64 = EncodeMockImage(imageIndex)
        };
    }

    private string EncodeMockImage(int imageIndex)
    {
        if (mockImages == null ||
            imageIndex < 0 ||
            imageIndex >= mockImages.Length ||
            mockImages[imageIndex] == null)
        {
            return string.Empty;
        }

        byte[] png = mockImages[imageIndex].EncodeToPNG();
        return png == null || png.Length == 0
            ? string.Empty
            : System.Convert.ToBase64String(png);
    }

    private void InjectStatus(string status, string message, double distance)
    {
        if (commandPublisher == null || !commandPublisher.HasPendingCommand)
        {
            Debug.LogWarning("[TargetCandidateMockTester] No pending command for mock status");
            return;
        }

        commandPublisher.InjectMockStatus(new TargetCommandStatusMessage
        {
            command_id = commandPublisher.PendingCommandId,
            track_id = commandPublisher.PendingTrackId,
            candidate_id = string.Empty,
            status = status,
            message = message,
            distance_remaining_m = distance
        });
    }
}
