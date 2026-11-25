using TMPro;
using UnityEngine;

/// <summary>LV3 QGroundControl controls and multi-height room-delivery lesson.</summary>
public sealed class Level3Manager : LevelManager
{
    private enum TutorialState { WaitingForConnection, SelectGuided, Takeoff, Scan, StructureMapped }
    private TutorialState state;

    private const string ConnectMessage =
        "LV3: HIGH-RISE ROOM DELIVERY\n" +
        "Connect the drone in QGroundControl. In Fly View, confirm the vehicle and telemetry are connected.";
    private const string GuidedMessage =
        "QGC FLY VIEW\n" +
        "1  Select Guided in the flight-mode menu. Check the status bar for GPS and vehicle health.\n" +
        "2  Use Fly Tools > Takeoff, set a safe initial height (about 12 m), then confirm the action.";
    private const string TakeoffMessage =
        "TAKEOFF IN QGC\n" +
        "Choose Guided mode, then Fly Tools > Takeoff. Set about 12 m on the altitude slider and confirm. Wait for the drone to stabilize.";
    private const string ScanMessage =
        "SCAN THE BUILDING IN QGC\n" +
        "If Upload fails with 'ROI to next waypoint' (MAV_CMD 196), this vehicle rejected a command in QGC's generated Structure Scan. Press OK and scan manually in Guided mode; do not retry that mission upload.\n" +
        "1  In Fly View, tap a point outside a building face, choose Go to Location, and confirm. Keep a safe gap from walls.\n" +
        "2  Visit all four sides at low, middle, and roof height. Use Fly Tools > Actions > Change Altitude between height bands.\n" +
        "3  Watch the STRUCTURE SCAN counter reach 12/12. Each room window turns yellow and shows a present icon when its side and height are scanned; all five drop points unlock at 12/12.";
    private const string MappedMessage =
        "FIVE ROOMS MAPPED\n" +
        "Use each room marker and floor altitude in QGC Fly View. Approach each yellow window with a present icon and deliver one present to each of the five rooms, then Land safely.";

    protected override void OnLevelInitialized()
    {
        ConfigureInstructionText();
        state = MavlinkReceiver != null && MavlinkReceiver.HasRecentTelemetry
            ? TutorialState.SelectGuided
            : TutorialState.WaitingForConnection;
        SetStatusText(state == TutorialState.SelectGuided ? GuidedMessage : ConnectMessage);
        RefreshDeliveryObjective();
    }

    protected override void OnLevelUpdate()
    {
        if (state == TutorialState.StructureMapped)
        {
            return;
        }
        if (MavlinkReceiver == null || !MavlinkReceiver.HasRecentTelemetry)
        {
            state = TutorialState.WaitingForConnection;
            SetStatusText(ConnectMessage);
            return;
        }
        if (state == TutorialState.WaitingForConnection)
        {
            state = TutorialState.SelectGuided;
            SetStatusText(GuidedMessage);
        }

        bool guided = MavlinkReceiver.IsGuidedMode;
        bool flying = MavlinkReceiver.IsArmed && MavlinkReceiver.RelativeAltitudeMeters >= 8f;
        if (!guided || !MavlinkReceiver.IsArmed)
        {
            state = TutorialState.SelectGuided;
            SetStatusText(GuidedMessage);
        }
        else if (!flying)
        {
            state = TutorialState.Takeoff;
            SetStatusText(TakeoffMessage);
        }
        else
        {
            state = TutorialState.Scan;
            SetStatusText(ScanMessage);
        }
    }

    protected override void OnDeliveryTargetDiscovered(int targetNumber, float roofAltitudeAMSL, float deliveryAltitudeAMSL)
    {
        state = TutorialState.StructureMapped;
        SetStatusText(MappedMessage);
        RefreshDeliveryObjective();
    }

    protected override string GetObjectiveText()
    {
        int delivered = deliveryScoreManager != null
            ? deliveryScoreManager.CompletedTargets
            : 0;
        int total = deliveryScoreManager != null
            ? Mathf.Max(1, deliveryScoreManager.targetBuildingCount)
            : 5;
        int scanned = deliveryScoreManager != null
            ? deliveryScoreManager.StructureScanCellsCompleted
            : 0;
        return $"Rooms delivered: {delivered}/{total}  •  Structure scan: {scanned}/12";
    }

    private void ConfigureInstructionText()
    {
        if (levelStatusText == null) return;
        RectTransform rect = levelStatusText.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(1580f, 430f);
        levelStatusText.alignment = TextAlignmentOptions.Center;
        levelStatusText.fontSize = 32f;
        levelStatusText.fontSizeMin = 24f;
        levelStatusText.fontSizeMax = 32f;
        levelStatusText.enableAutoSizing = true;
        levelStatusText.raycastTarget = false;
    }
}
