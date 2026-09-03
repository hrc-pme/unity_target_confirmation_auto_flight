using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TargetSelectionUIController : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] private TargetCandidateSubscriber candidateSubscriber;
    [SerializeField] private TargetCommandPublisher commandPublisher;
    [SerializeField] private UavRosBridgeStateReceiver uavStateReceiver;
    [SerializeField] private CompressedImageUIReceiver liveImageReceiver;

    [Header("UAV UI")]
    [SerializeField] private TMP_Text gpsValueText = null;
    [SerializeField] private TMP_Text flightHeightValueText = null;

    [Header("Target UI")]
    [SerializeField] private TMP_Text trackIdValueText = null;
    [SerializeField] private TMP_Text targetPositionValueText = null;
    [SerializeField] private TMP_Text positionErrorValueText = null;

    [Header("Confirmation UI")]
    [SerializeField] private TMP_Text confirmationInfoText = null;
    [SerializeField] private TMP_Text candidateCounterText = null;
    [SerializeField] private RawImage targetPreviewImage = null;
    [SerializeField] private Button previousButton = null;
    [SerializeField] private Button nextButton = null;
    [SerializeField] private Button cancelButton = null;
    [SerializeField] private Button confirmButton = null;

    [Header("Settings")]
    [SerializeField] private float candidateTimeoutSeconds = 5.0f;
    [SerializeField] private float uiRefreshIntervalSeconds = 0.1f;
    [SerializeField] private float terminalStatusHoldSeconds = 5.0f;
    [SerializeField] private float previewDecodeIntervalSeconds = 0.33f;
    [SerializeField] private bool logDebug = false;

    [Header("Compact Stacked Layout")]
    [SerializeField] private bool applyCompactStackedLayout = true;
    [SerializeField] private float layoutRightMargin = 24.0f;
    [SerializeField] private float layoutTopMargin = 24.0f;
    [SerializeField] private float layoutGap = 12.0f;
    [SerializeField] private Vector2 liveImageSize = new Vector2(400.0f, 300.0f);
    [SerializeField] private Vector2 confirmationWindowSize = new Vector2(400.0f, 512.0f);

    private readonly List<TargetCandidateData> candidates = new List<TargetCandidateData>();
    private readonly Dictionary<string, Texture2D> previewTextures = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, string> previewSources = new Dictionary<string, string>();
    private readonly HashSet<string> previewDecodeRequests = new HashSet<string>();
    private readonly Queue<PreviewDecodeResult> completedPreviewDecodes = new Queue<PreviewDecodeResult>();
    private readonly object previewDecodeLock = new object();
    private int currentIndex;
    private float nextUiRefreshRealtime;
    private string latestInfoText = "WAITING FOR TARGET";
    private float latestInfoSetRealtime;
    private bool readyForConfirm;
    private string latestConfirmBlockersText = string.Empty;
    private UavApproachStateData latestApproach;
    private string activeSourceSessionId = string.Empty;
    private int previewGeneration;
    private float nextPreviewDecodeRealtime;
    private bool hasRetainedTargetGps;
    private double retainedTargetLatitude;
    private double retainedTargetLongitude;

    private sealed class PreviewDecodeResult
    {
        public string TargetKey;
        public string SourceBase64;
        public byte[] Bytes;
        public int Generation;
    }

    private void Awake()
    {
        if (candidateSubscriber == null) candidateSubscriber = FindObjectOfType<TargetCandidateSubscriber>();
        if (commandPublisher == null) commandPublisher = FindObjectOfType<TargetCommandPublisher>();
        if (uavStateReceiver == null) uavStateReceiver = FindObjectOfType<UavRosBridgeStateReceiver>();
        if (liveImageReceiver == null) liveImageReceiver = FindObjectOfType<CompressedImageUIReceiver>();

        ApplyCompactStackedLayout();
    }

    private void ApplyCompactStackedLayout()
    {
        if (!applyCompactStackedLayout || targetPreviewImage == null)
        {
            return;
        }

        RectTransform confirmationWindow =
            targetPreviewImage.transform.parent as RectTransform;
        RawImage liveRawImage =
            liveImageReceiver != null ? liveImageReceiver.TargetRawImage : null;

        if (confirmationWindow == null || liveRawImage == null)
        {
            Debug.LogWarning(
                "[TargetSelectionUIController] Compact layout skipped because " +
                "the confirmation window or live image is missing.");
            return;
        }

        RectTransform liveRect = liveRawImage.rectTransform;
        SetTopRightRect(
            liveRect,
            new Vector2(-layoutRightMargin, -layoutTopMargin),
            liveImageSize);

        float panelTop = layoutTopMargin + liveImageSize.y + layoutGap;
        SetTopRightRect(
            confirmationWindow,
            new Vector2(-layoutRightMargin, -panelTop),
            confirmationWindowSize);

        SetTopStretchRect(confirmationInfoText, -48.0f, -24.0f, 190.0f);
        SetTopStretchRect(candidateCounterText, -245.0f, -24.0f, 28.0f);

        RectTransform previewRect = targetPreviewImage.rectTransform;
        previewRect.anchorMin = new Vector2(0.5f, 0.0f);
        previewRect.anchorMax = new Vector2(0.5f, 0.0f);
        previewRect.pivot = new Vector2(0.5f, 0.0f);
        previewRect.anchoredPosition = new Vector2(0.0f, 130.0f);
        previewRect.sizeDelta = new Vector2(340.0f, 104.0f);

        SetBottomButton(previousButton, false, 14.0f, 88.0f);
        SetBottomButton(nextButton, true, 14.0f, 88.0f);
        SetBottomButton(cancelButton, false, 14.0f, 12.0f);
        SetBottomButton(confirmButton, true, 14.0f, 12.0f);

        TMP_Text titleText = confirmationWindow
            .Find("ConfirmationTitle")
            ?.GetComponent<TMP_Text>();
        if (titleText != null)
        {
            titleText.fontSize = 22.0f;
        }
        if (confirmationInfoText != null)
        {
            confirmationInfoText.fontSize = 15.5f;
        }
        if (candidateCounterText != null)
        {
            candidateCounterText.fontSize = 15.5f;
        }
    }

    private static void SetTopRightRect(
        RectTransform rect,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void SetTopStretchRect(
        TMP_Text text,
        float y,
        float horizontalInset,
        float height)
    {
        if (text == null)
        {
            return;
        }

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.0f, 1.0f);
        rect.anchorMax = new Vector2(1.0f, 1.0f);
        rect.pivot = new Vector2(0.5f, 1.0f);
        rect.anchoredPosition = new Vector2(0.0f, y);
        rect.sizeDelta = new Vector2(horizontalInset, height);
    }

    private static void SetBottomButton(
        Button button,
        bool alignRight,
        float horizontalMargin,
        float y)
    {
        if (button == null)
        {
            return;
        }

        RectTransform rect = button.GetComponent<RectTransform>();
        float anchorX = alignRight ? 1.0f : 0.0f;
        rect.anchorMin = new Vector2(anchorX, 0.0f);
        rect.anchorMax = new Vector2(anchorX, 0.0f);
        rect.pivot = new Vector2(anchorX, 0.0f);
        rect.anchoredPosition =
            new Vector2(alignRight ? -horizontalMargin : horizontalMargin, y);
        rect.sizeDelta = new Vector2(128.0f, 26.0f);

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.enableWordWrapping = false;
            label.fontSize = 12.0f;
            label.color = new Color32(16, 16, 16, 255);
        }
    }

    private void OnEnable()
    {
        ResetForNewSession(string.Empty);

        if (previousButton != null) previousButton.onClick.AddListener(SelectPrevious);
        if (nextButton != null) nextButton.onClick.AddListener(SelectNext);
        if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);

        if (candidateSubscriber != null) candidateSubscriber.OnCandidatesUpdated += HandleCandidatesUpdated;
        if (commandPublisher != null)
        {
            commandPublisher.OnCommandStatusChanged += HandleCommandStatusChanged;
            commandPublisher.OnLocalCommandMessage += HandleLocalCommandMessage;
        }

        RefreshEmptyState();
    }

    private void OnDisable()
    {
        if (previousButton != null) previousButton.onClick.RemoveListener(SelectPrevious);
        if (nextButton != null) nextButton.onClick.RemoveListener(SelectNext);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(Cancel);
        if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);

        if (candidateSubscriber != null) candidateSubscriber.OnCandidatesUpdated -= HandleCandidatesUpdated;
        if (commandPublisher != null)
        {
            commandPublisher.OnCommandStatusChanged -= HandleCommandStatusChanged;
            commandPublisher.OnLocalCommandMessage -= HandleLocalCommandMessage;
        }

        previewGeneration++;
        ClearPreviewCache();
        candidates.Clear();
        readyForConfirm = false;
        if (targetPreviewImage != null)
        {
            targetPreviewImage.texture = null;
            targetPreviewImage.enabled = false;
        }
    }

    private void Update()
    {
        ApplyCompletedPreviewDecodes();

        if (candidates.Count > 0 && IsCandidateExpired() && !IsCommandPending())
        {
            ClearStaleCandidates();
        }

        if (Time.realtimeSinceStartup < nextUiRefreshRealtime)
        {
            return;
        }

        nextUiRefreshRealtime =
            Time.realtimeSinceStartup + Mathf.Max(0.02f, uiRefreshIntervalSeconds);
        RefreshAll();
    }

    private void HandleCandidatesUpdated(TargetCandidatePacket packet)
    {
        string sourceSessionId = packet != null ? packet.source_session_id ?? string.Empty : string.Empty;
        if (!string.IsNullOrEmpty(sourceSessionId) &&
            !string.Equals(sourceSessionId, activeSourceSessionId, StringComparison.Ordinal))
        {
            ResetForNewSession(sourceSessionId);
        }

        int previousTrackId = GetCurrentCandidate()?.track_id ?? -1;
        int previousCandidateCount = candidates.Count;

        candidates.Clear();
        readyForConfirm = packet != null && packet.ready_for_confirm;
        latestConfirmBlockersText = BuildBlockersText(packet);
        latestApproach = packet != null ? packet.approach : null;

        if (packet != null && packet.candidates != null)
        {
            HashSet<string> seenTargets = new HashSet<string>();

            for (int i = 0; i < packet.candidates.Length; i++)
            {
                TargetCandidateData candidate = packet.candidates[i];
                if (candidate != null && candidate.IsValid())
                {
                    string targetKey = BuildTargetKey(candidate);
                    if (seenTargets.Add(targetKey))
                    {
                        candidates.Add(candidate);
                    }
                }
            }
        }

        PrunePreviewCache();

        if (packet != null && packet.auto_select_latest &&
            candidates.Count > previousCandidateCount)
        {
            currentIndex = candidates.Count - 1;
        }
        else
        {
            currentIndex = FindTrackIndex(previousTrackId);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }
        }

        RememberTargetGps(GetCurrentCandidate());
        RememberApproachTargetGps(latestApproach);

        if (ShouldReplaceInfoWithReadyState())
        {
            latestInfoText = BuildApproachInfoText(candidates.Count > 0);
        }

        RefreshAll();
    }

    private void SelectPrevious()
    {
        currentIndex = Mathf.Max(0, currentIndex - 1);
        RefreshAll();
    }

    private void SelectNext()
    {
        currentIndex = Mathf.Min(candidates.Count - 1, currentIndex + 1);
        RefreshAll();
    }

    private void Confirm()
    {
        TargetCandidateData candidate = GetCurrentCandidate();

        if (candidate == null)
        {
            SetInfo("NO TARGET");
            return;
        }

        if (IsCandidateExpired())
        {
            SetInfo("TARGET DATA EXPIRED");
            return;
        }

        if (commandPublisher == null)
        {
            SetInfo("COMMAND PUBLISHER MISSING");
            return;
        }

        if (!readyForConfirm)
        {
            SetInfo("TARGET NOT READY");
            return;
        }

        if (!commandPublisher.TryPublishConfirmTarget(candidate, out string errorMessage))
        {
            SetInfo(errorMessage);
            return;
        }

        if (liveImageReceiver != null)
        {
            liveImageReceiver.ShowLiveStream();
        }

        SetInfo("SENDING TARGET...");
    }

    private void Cancel()
    {
        if (commandPublisher == null)
        {
            SetInfo("COMMAND PUBLISHER MISSING");
            return;
        }

        int trackId = commandPublisher.HasPendingCommand
            ? commandPublisher.PendingTrackId
            : GetCurrentCandidate()?.track_id ?? -1;

        if (!commandPublisher.TryPublishCancel(trackId, out string errorMessage))
        {
            SetInfo(errorMessage);
            return;
        }

        SetInfo("CANCELLING...");
    }

    private void HandleCommandStatusChanged(TargetCommandStatusMessage statusMessage)
    {
        if (statusMessage == null)
        {
            return;
        }

        string status = statusMessage.status ?? string.Empty;
        string message = statusMessage.message ?? string.Empty;

        if (status == "ACCEPTED")
        {
            ShowLiveFlightImage();
            SetInfo("ACCEPTED");
        }
        else if (status == "RECEIVED")
        {
            ShowLiveFlightImage();
            SetInfo("RECEIVED");
        }
        else if (status == "EXECUTING")
        {
            ShowLiveFlightImage();
            if (IsFinite(statusMessage.distance_remaining_m) &&
                statusMessage.distance_remaining_m >= 0.0)
            {
                SetInfo(
                    "EXECUTING\nDISTANCE: " +
                    statusMessage.distance_remaining_m.ToString("F1", CultureInfo.InvariantCulture) +
                    " m");
            }
            else
            {
                SetInfo("EXECUTING");
            }
        }
        else if (status == "ARRIVED")
        {
            ShowLiveFlightImage();
            SetInfo("ARRIVED");
        }
        else if (status == "CANCELLED")
        {
            SetInfo("COMMAND CANCELLED");
        }
        else if (status == "REJECTED")
        {
            SetInfo(AppendMessage("REJECTED", message));
        }
        else if (status == "FAILED")
        {
            SetInfo(AppendMessage("FAILED", message));
        }
        else if (status == "ERROR")
        {
            SetInfo(AppendMessage("COMMAND ERROR", message));
        }
    }

    private void HandleLocalCommandMessage(string message)
    {
        SetInfo(message);
    }

    private void RefreshAll()
    {
        RefreshUavInfo();
        RefreshTargetInfo();
        RefreshButtons();
        RefreshPreviewImage();
    }

    private void RefreshEmptyState()
    {
        SetText(gpsValueText, "--.-------, ---.-------");
        SetText(flightHeightValueText, "--.- m");
        SetText(candidateCounterText, "CANDIDATE 0 / 0");
        SetText(trackIdValueText, "NO TARGET");
        SetText(targetPositionValueText, "--.-------, ---.-------");
        SetText(positionErrorValueText, string.Empty);
        SetText(
            confirmationInfoText,
            "UAV WGS84 GPS\n--.-------, ---.-------" +
            "\n\nFLIGHT HEIGHT\n--.- m" +
            "\n\nWAITING FOR TARGET");
        if (targetPreviewImage != null)
        {
            targetPreviewImage.texture = null;
            targetPreviewImage.enabled = false;
        }
        if (previousButton != null) previousButton.interactable = false;
        if (nextButton != null) nextButton.interactable = false;
        if (confirmButton != null) confirmButton.interactable = false;
        if (cancelButton != null) cancelButton.interactable = false;
    }

    private void RefreshUavInfo()
    {
        if (uavStateReceiver != null && uavStateReceiver.HasValidGps)
        {
            SetText(
                gpsValueText,
                uavStateReceiver.LatestLatitude.ToString("F7", CultureInfo.InvariantCulture) +
                ", " +
                uavStateReceiver.LatestLongitude.ToString("F7", CultureInfo.InvariantCulture));
            SetText(
                flightHeightValueText,
                ResolveFlightHeight().ToString("F1", CultureInfo.InvariantCulture) +
                " m");
        }
        else
        {
            SetText(gpsValueText, "--.-------, ---.-------");
            SetText(flightHeightValueText, "--.- m");
        }
    }

    private double ResolveFlightHeight()
    {
        double flightHeight = uavStateReceiver.LatestFlightHeight;
        return IsFinite(flightHeight) ? flightHeight : uavStateReceiver.LatestAltitude;
    }

    private void RefreshTargetInfo()
    {
        TargetCandidateData candidate = GetCurrentCandidate();

        if (candidate == null)
        {
            SetText(candidateCounterText, "CANDIDATE 0 / 0");
            SetText(trackIdValueText, "NO TARGET");
            SetText(
                targetPositionValueText,
                hasRetainedTargetGps
                    ? FormatLatLon(retainedTargetLatitude, retainedTargetLongitude)
                    : "--.-------, ---.-------");
            SetText(positionErrorValueText, string.Empty);
            string noCandidateInfo = BuildNoCandidateInfoText();
            if (hasRetainedTargetGps)
            {
                noCandidateInfo +=
                    "\n\nTARGET GPS\n" +
                    FormatLatLon(retainedTargetLatitude, retainedTargetLongitude);
            }
            SetText(confirmationInfoText, noCandidateInfo);
            return;
        }

        SetText(
            candidateCounterText,
            "CANDIDATE " + (currentIndex + 1) + " / " + candidates.Count);
        SetText(
            trackIdValueText,
            (currentIndex + 1).ToString(CultureInfo.InvariantCulture) +
            " / " +
            candidates.Count.ToString(CultureInfo.InvariantCulture) +
            "  " +
            BuildTargetIdText(candidate));
        SetText(
            targetPositionValueText,
            FormatLatLon(candidate.DisplayLatitude, candidate.DisplayLongitude));
        SetText(positionErrorValueText, string.Empty);

        RememberTargetGps(candidate);
        string uavSummary = BuildUavSummary();
        string targetSummary = BuildConfirmationTargetSummary(candidate);

        if (IsCandidateExpired() && !IsCommandPending())
        {
            SetText(
                confirmationInfoText,
                uavSummary + "\n\nTARGET DATA EXPIRED\n\n" + targetSummary);
        }
        else if (latestInfoText == "TARGET READY" ||
                 latestInfoText == "WAITING FOR TARGET" ||
                 latestInfoText == "NO TARGET")
        {
            SetText(confirmationInfoText, uavSummary + "\n\n" + targetSummary);
        }
        else
        {
            SetText(
                confirmationInfoText,
                uavSummary + "\n\n" + latestInfoText + "\n\n" + targetSummary);
        }
    }

    private void RefreshButtons()
    {
        bool hasCandidate = GetCurrentCandidate() != null;
        bool expired = IsCandidateExpired();
        bool pending = IsCommandPending();

        if (previousButton != null) previousButton.interactable = currentIndex > 0;
        if (nextButton != null) nextButton.interactable = currentIndex < candidates.Count - 1;
        if (confirmButton != null) confirmButton.interactable = readyForConfirm && hasCandidate && !expired && !pending;
        if (cancelButton != null)
        {
            cancelButton.interactable = pending || IsAutonomousApproachActive();
        }
    }

    private void RefreshPreviewImage()
    {
        if (targetPreviewImage == null)
        {
            return;
        }

        Texture texture = ResolvePreviewTexture();
        targetPreviewImage.enabled = texture != null;
        targetPreviewImage.texture = texture;
    }

    private Texture ResolvePreviewTexture()
    {
        TargetCandidateData candidate = GetCurrentCandidate();

        if (candidate != null)
        {
            string targetKey = BuildTargetKey(candidate);
            if (!string.IsNullOrEmpty(candidate.image_base64))
            {
                if (previewTextures.TryGetValue(targetKey, out Texture2D texture) &&
                    previewSources.TryGetValue(targetKey, out string source) &&
                    source == candidate.image_base64)
                {
                    return texture;
                }

                RequestPreviewDecode(targetKey, candidate.image_base64);
            }

            // Backend sends the same target snapshot only every few seconds.
            // Keep showing the decoded crop during lightweight packets.
            if (previewTextures.TryGetValue(targetKey, out Texture2D cachedTexture))
            {
                return cachedTexture;
            }
        }

        // The small confirmation panel is target-only. The annotated live
        // stream stays in the large center view; never duplicate it here.
        return null;
    }

    private void RequestPreviewDecode(string targetKey, string base64)
    {
        if (Time.realtimeSinceStartup < nextPreviewDecodeRealtime)
        {
            return;
        }

        nextPreviewDecodeRealtime =
            Time.realtimeSinceStartup + Mathf.Max(0.1f, previewDecodeIntervalSeconds);
        int generation = previewGeneration;
        string requestKey = generation.ToString(CultureInfo.InvariantCulture) + ":" + targetKey;
        lock (previewDecodeLock)
        {
            if (!previewDecodeRequests.Add(requestKey))
            {
                return;
            }
        }

        Task.Run(() =>
        {
            byte[] bytes = null;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch
            {
            }

            lock (previewDecodeLock)
            {
                previewDecodeRequests.Remove(requestKey);
                completedPreviewDecodes.Enqueue(new PreviewDecodeResult
                {
                    TargetKey = targetKey,
                    SourceBase64 = base64,
                    Bytes = bytes,
                    Generation = generation
                });
            }
        });
    }

    private void ApplyCompletedPreviewDecodes()
    {
        PreviewDecodeResult result = null;

        lock (previewDecodeLock)
        {
            while (completedPreviewDecodes.Count > 0)
            {
                PreviewDecodeResult candidate = completedPreviewDecodes.Dequeue();
                if (candidate.Generation == previewGeneration && IsCurrentPreview(candidate))
                {
                    result = candidate;
                }
            }
        }

        if (result == null || result.Bytes == null || result.Bytes.Length == 0)
        {
            return;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(result.Bytes, false))
            {
                Destroy(texture);
                return;
            }

            if (previewTextures.TryGetValue(result.TargetKey, out Texture2D oldTexture) &&
                oldTexture != null)
            {
                Destroy(oldTexture);
            }

            previewTextures[result.TargetKey] = texture;
            previewSources[result.TargetKey] = result.SourceBase64;
        }
        catch (Exception exception)
        {
            Destroy(texture);
            if (logDebug)
            {
                Debug.LogWarning("[TargetSelectionUIController] Preview decode failed: " + exception.Message);
            }
        }
    }

    private TargetCandidateData GetCurrentCandidate()
    {
        if (currentIndex < 0 || currentIndex >= candidates.Count)
        {
            return null;
        }

        return candidates[currentIndex];
    }

    private int FindTrackIndex(int trackId)
    {
        if (trackId < 0)
        {
            return -1;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].track_id == trackId)
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsCandidateExpired()
    {
        return candidateSubscriber == null ||
               !candidateSubscriber.HasFreshData(candidateTimeoutSeconds);
    }

    private bool IsCommandPending()
    {
        return commandPublisher != null && commandPublisher.HasPendingCommand;
    }

    private bool IsAutonomousApproachActive()
    {
        if (latestApproach == null || string.IsNullOrEmpty(latestApproach.state))
        {
            return false;
        }

        return latestApproach.state == "DESCENDING_TO_VERIFY" ||
               latestApproach.state == "VERIFYING" ||
               latestApproach.state == "READY_FOR_UNITY_CONFIRM" ||
               latestApproach.state == "FINAL_APPROACH";
    }

    private void ShowLiveFlightImage()
    {
        if (liveImageReceiver != null)
        {
            liveImageReceiver.ShowLiveStream();
        }
    }

    private void SetInfo(string text)
    {
        latestInfoText = string.IsNullOrEmpty(text) ? string.Empty : text;
        latestInfoSetRealtime = Time.realtimeSinceStartup;
        SetText(confirmationInfoText, latestInfoText);
        RefreshButtons();
    }

    private bool ShouldReplaceInfoWithReadyState()
    {
        if (!IsCommandOrResultMessage(latestInfoText))
        {
            return true;
        }

        return IsTerminalDisplayMessage(latestInfoText) &&
               Time.realtimeSinceStartup - latestInfoSetRealtime >= terminalStatusHoldSeconds;
    }

    private static bool IsCommandOrResultMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message == "SENDING TARGET..." ||
               message == "CANCELLING..." ||
               message == "RECEIVED" ||
               message == "ACCEPTED" ||
               message.StartsWith("EXECUTING", StringComparison.Ordinal) ||
               message == "ARRIVED" ||
               message == "COMMAND CANCELLED" ||
               message.StartsWith("REJECTED", StringComparison.Ordinal) ||
               message.StartsWith("FAILED", StringComparison.Ordinal) ||
               message.StartsWith("COMMAND ERROR", StringComparison.Ordinal) ||
               message == "COMMAND TIMEOUT" ||
               message == "TARGET DATA EXPIRED" ||
               message == "ROS NOT CONNECTED" ||
               message == "COMMAND PENDING" ||
               message == "INVALID TARGET" ||
               message == "COMMAND PUBLISHER MISSING" ||
               message == "TARGET NOT READY";
    }

    private static bool IsTerminalDisplayMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message == "ARRIVED" ||
               message == "COMMAND CANCELLED" ||
               message.StartsWith("REJECTED", StringComparison.Ordinal) ||
               message.StartsWith("FAILED", StringComparison.Ordinal) ||
               message.StartsWith("COMMAND ERROR", StringComparison.Ordinal) ||
               message == "COMMAND TIMEOUT";
    }

    private static void SetText(TMP_Text textComponent, string value)
    {
        if (textComponent != null && textComponent.text != value)
        {
            textComponent.text = value;
        }
    }

    private string BuildNoCandidateInfoText()
    {
        string uavSummary = BuildUavSummary();

        if (latestApproach != null &&
            (latestApproach.state == "INDOOR_SAFE" ||
             latestApproach.state == "INDOOR_MAP_PREVIEW"))
        {
            return uavSummary + "\n\n" + latestInfoText;
        }

        if (!string.IsNullOrEmpty(latestConfirmBlockersText))
        {
            return
                uavSummary +
                "\n\n" +
                latestInfoText +
                "\n\nCHECK\n" +
                latestConfirmBlockersText;
        }

        return string.IsNullOrEmpty(latestInfoText)
            ? uavSummary
            : uavSummary + "\n\n" + latestInfoText;
    }

    private string BuildNotReadyText(bool hasCandidate)
    {
        if (hasCandidate)
        {
            return "TARGET NOT READY";
        }

        return string.IsNullOrEmpty(latestConfirmBlockersText)
            ? "NO TARGET"
            : "NO CONFIRM TARGET";
    }

    private string BuildApproachInfoText(bool hasCandidate)
    {
        if (latestApproach == null || string.IsNullOrEmpty(latestApproach.state))
        {
            return readyForConfirm && hasCandidate
                ? "CAR VERIFIED - PRESS CONFIRM\nFINAL HEIGHT: 20 m"
                : BuildNotReadyText(hasCandidate);
        }

        string message = latestApproach.message ?? string.Empty;
        switch (latestApproach.state)
        {
            case "INDOOR_SAFE":
                return "INDOOR SAFE\nFLIGHT COMMANDS OFF";
            case "INDOOR_MAP_PREVIEW":
                return "MAP PREVIEW - FAKE GPS\nFLIGHT COMMANDS OFF";
            case "SEARCHING":
                return string.Empty;
            case "DESCENDING_TO_VERIFY":
                return "AUTO VERIFY\nDESCENDING TO " +
                       FormatApproachHeight(latestApproach.verification_altitude_m, 60.0) +
                       " m";
            case "VERIFYING":
                return "VERIFYING CAR AT " +
                       FormatApproachHeight(latestApproach.verification_altitude_m, 60.0) +
                       " m\nFIXES " +
                       Mathf.Max(0, latestApproach.verification_sample_count)
                           .ToString(CultureInfo.InvariantCulture) +
                       "/5 - KEEP CAR VISIBLE";
            case "READY_FOR_UNITY_CONFIRM":
                return "ONE CAR LOCKED - PRESS CONFIRM\nDIRECT DESCENT TO " +
                       FormatApproachHeight(latestApproach.final_altitude_m, 20.0) +
                       " m";
            case "FINAL_APPROACH":
                return "POSITION LOCKED - DESCENDING\nTARGET HEIGHT: " +
                       FormatApproachHeight(latestApproach.final_altitude_m, 20.0) +
                       " m";
            case "COMPLETE":
                return "ARRIVED ABOVE CAR\nMISSION COMPLETE";
            case "CANCELLED":
                return "APPROACH CANCELLED";
            case "FAILED":
                return AppendMessage("APPROACH FAILED", message);
            default:
                return string.IsNullOrEmpty(message)
                    ? BuildNotReadyText(hasCandidate)
                    : message;
        }
    }

    private static string FormatApproachHeight(double value, double fallback)
    {
        double resolved = IsFinite(value) && value > 0.0 ? value : fallback;
        return resolved.ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string BuildBlockersText(TargetCandidatePacket packet)
    {
        if (packet == null || packet.confirm_blockers == null || packet.confirm_blockers.Length == 0)
        {
            return string.Empty;
        }

        List<string> blockers = new List<string>();
        for (int i = 0; i < packet.confirm_blockers.Length; i++)
        {
            string blocker = FormatBlocker(packet.confirm_blockers[i]);
            if (!string.IsNullOrEmpty(blocker) && !blockers.Contains(blocker))
            {
                blockers.Add(blocker);
                if (blockers.Count >= 2)
                {
                    break;
                }
            }
        }

        return blockers.Count == 0 ? string.Empty : string.Join("\n", blockers.ToArray());
    }

    private static string FormatBlocker(string blocker)
    {
        switch (blocker ?? string.Empty)
        {
            case "camera_unavailable":
                return "CAMERA NOT READY";
            case "detector_not_ready":
                return "AI MODEL NOT READY";
            case "mavlink_disconnected":
                return "MAVLINK NOT CONNECTED";
            case "gps_invalid_or_stale":
                return "WAITING FOR GPS";
            case "insufficient_satellites":
                return "WAITING FOR 8+ SATELLITES";
            case "gps_eph_too_high":
                return "GPS ACCURACY TOO LOW";
            case "aircraft_not_armed":
                return "WAITING FOR ARM";
            case "flight_mode_not_allowed":
                return "MODE MUST BE GUIDED";
            case "below_minimum_altitude":
                return "ALTITUDE BELOW 10 m";
            case "flight_control_disabled_by_config":
            case "no_confirmed_car_target":
            case "no_confirmed_white_car_target":
            case "low_altitude_car_verification_not_ready":
            case "verified_car_fix_unavailable":
                return string.Empty;
            default:
                return string.Empty;
        }
    }

    private string BuildConfirmationTargetSummary(TargetCandidateData candidate)
    {
        return
            "TARGET GPS\n" +
            FormatLatLon(candidate.DisplayLatitude, candidate.DisplayLongitude);
    }

    private string BuildUavSummary()
    {
        string gps = "--.-------, ---.-------";
        string height = "--.- m";

        if (uavStateReceiver != null && uavStateReceiver.HasValidGps)
        {
            gps = FormatLatLon(
                uavStateReceiver.LatestLatitude,
                uavStateReceiver.LatestLongitude);

            double flightHeight = ResolveFlightHeight();
            if (IsFinite(flightHeight))
            {
                height =
                    flightHeight.ToString("F1", CultureInfo.InvariantCulture) +
                    " m";
            }
        }

        return "UAV WGS84 GPS\n" + gps + "\n\nFLIGHT HEIGHT\n" + height;
    }

    private void RememberTargetGps(TargetCandidateData candidate)
    {
        if (candidate != null)
        {
            RememberTargetGps(candidate.DisplayLatitude, candidate.DisplayLongitude);
        }
    }

    private void RememberApproachTargetGps(UavApproachStateData approach)
    {
        if (approach != null)
        {
            RememberTargetGps(approach.target_latitude, approach.target_longitude);
        }
    }

    private void RememberTargetGps(double latitude, double longitude)
    {
        if (!UavTelemetryMessage.IsValidLatitude(latitude) ||
            !UavTelemetryMessage.IsValidLongitude(longitude) ||
            UavTelemetryMessage.IsZeroCoordinate(latitude, longitude))
        {
            return;
        }

        retainedTargetLatitude = latitude;
        retainedTargetLongitude = longitude;
        hasRetainedTargetGps = true;
    }

    private static string AppendMessage(string title, string message)
    {
        return string.IsNullOrEmpty(message) ? title : title + "\n" + message;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string FormatLatLon(double latitude, double longitude)
    {
        if (!UavTelemetryMessage.IsValidLatitude(latitude) ||
            !UavTelemetryMessage.IsValidLongitude(longitude) ||
            UavTelemetryMessage.IsZeroCoordinate(latitude, longitude))
        {
            return "--.-------, ---.-------";
        }

        return latitude.ToString("F7", CultureInfo.InvariantCulture) +
               ", " +
               longitude.ToString("F7", CultureInfo.InvariantCulture);
    }

    private static string FormatMeters(double value)
    {
        return IsFinite(value)
            ? value.ToString("F2", CultureInfo.InvariantCulture) + " m"
            : "--.-- m";
    }

    private static string BuildTargetKey(TargetCandidateData candidate)
    {
        if (!string.IsNullOrEmpty(candidate.candidate_id))
        {
            return "candidate:" + candidate.candidate_id;
        }

        if (candidate.track_id >= 0)
        {
            return "track:" + candidate.track_id.ToString(CultureInfo.InvariantCulture);
        }

        return "gps:" +
               candidate.DisplayLatitude.ToString("F7", CultureInfo.InvariantCulture) +
               "," +
               candidate.DisplayLongitude.ToString("F7", CultureInfo.InvariantCulture);
    }

    private static string BuildTargetIdText(TargetCandidateData candidate)
    {
        if (!string.IsNullOrEmpty(candidate.candidate_id))
        {
            return candidate.candidate_id;
        }

        return candidate.track_id >= 0
            ? candidate.track_id.ToString(CultureInfo.InvariantCulture)
            : "NO TARGET";
    }

    private static string FormatOptionalNumber(double value, string format)
    {
        return IsFinite(value)
            ? value.ToString(format, CultureInfo.InvariantCulture)
            : "--.--";
    }

    private void OnDestroy()
    {
        previewGeneration++;
        ClearPreviewCache();
    }

    private void ResetForNewSession(string sourceSessionId)
    {
        previewGeneration++;
        ClearPreviewCache();
        candidates.Clear();
        currentIndex = 0;
        readyForConfirm = false;
        latestConfirmBlockersText = string.Empty;
        latestApproach = null;
        latestInfoText = "WAITING FOR TARGET";
        latestInfoSetRealtime = Time.realtimeSinceStartup;
        nextUiRefreshRealtime = 0.0f;
        nextPreviewDecodeRealtime = 0.0f;
        activeSourceSessionId = sourceSessionId ?? string.Empty;
        hasRetainedTargetGps = false;
        retainedTargetLatitude = double.NaN;
        retainedTargetLongitude = double.NaN;
    }

    private void ClearStaleCandidates()
    {
        previewGeneration++;
        ClearPreviewCache();
        candidates.Clear();
        currentIndex = 0;
        readyForConfirm = false;
        latestConfirmBlockersText = string.Empty;
        latestApproach = null;
        latestInfoText = "WAITING FOR TARGET";
        latestInfoSetRealtime = Time.realtimeSinceStartup;
    }

    private bool IsCurrentPreview(PreviewDecodeResult result)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            TargetCandidateData candidate = candidates[i];
            if (candidate != null && BuildTargetKey(candidate) == result.TargetKey &&
                (string.IsNullOrEmpty(candidate.image_base64) ||
                 candidate.image_base64 == result.SourceBase64))
            {
                return true;
            }
        }

        return false;
    }

    private void PrunePreviewCache()
    {
        HashSet<string> activeKeys = new HashSet<string>();
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] != null)
            {
                activeKeys.Add(BuildTargetKey(candidates[i]));
            }
        }

        List<string> removeKeys = new List<string>();
        foreach (string key in previewTextures.Keys)
        {
            if (!activeKeys.Contains(key))
            {
                removeKeys.Add(key);
            }
        }

        for (int i = 0; i < removeKeys.Count; i++)
        {
            string key = removeKeys[i];
            if (previewTextures.TryGetValue(key, out Texture2D texture) && texture != null)
            {
                Destroy(texture);
            }
            previewTextures.Remove(key);
            previewSources.Remove(key);
        }
    }

    private void ClearPreviewCache()
    {
        foreach (Texture2D texture in previewTextures.Values)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }

        previewTextures.Clear();
        previewSources.Clear();
        lock (previewDecodeLock)
        {
            previewDecodeRequests.Clear();
            completedPreviewDecodes.Clear();
        }
    }
}
