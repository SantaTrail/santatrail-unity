using System;
using TMPro;
using UnityEngine;

/// <summary>LV2 QGroundControl survey-planning lesson.</summary>
public sealed class Level2Manager : LevelManager
{
    private enum TutorialState { WaitingForConnection, PlanningSurvey, MissionUploaded, Complete }
    private TutorialState state;
    private bool allHousesDiscovered;
    private bool hadMissionAtStart;
    private ushort initialMissionCount;
    private ushort initialMissionSequence;
    private bool wasInAutoMode;
    private int initialMissionUpdateCount;

    private const string ConnectMessage =
        "BASIC SURVEY\nConnect the vehicle in QGroundControl. Open Plan View to begin.";
    private const string PlanMessage =
        "BASIC SURVEY\n" +
        "1  In QGC, choose Plan Flight, then Survey > Basic to draw a four-corner area over the village.\n" +
        "2  Keep the area roughly within 150 m of home. Set the terrain-following altitude to Calculated Above Terrain.\n" +
        "3  Leave Grid and Tolerance at their defaults; camera setup is optional.\n" +
        "4  Upload the plan, review it in Fly View, then start the mission.";
    private const string MissionMessage =
        "SURVEY MISSION READY\nReview and start the uploaded mission in QGC Fly View. After landing, resume from the current waypoint if needed.";
    private const string FoundMessage =
        "ALL DELIVERY HOUSES FOUND\nSwitch to Guided mode in QGC, then deliver each present one at a time.";

    protected override void OnLevelInitialized()
    {
        ConfigureInstructionText();
        CaptureMissionState();
        state = MavlinkReceiver != null && MavlinkReceiver.HasRecentTelemetry
            ? TutorialState.PlanningSurvey
            : TutorialState.WaitingForConnection;
        SetStatusText(state == TutorialState.PlanningSurvey ? PlanMessage : ConnectMessage);
        RefreshDeliveryObjective();
    }

    protected override void OnLevelUpdate()
    {
        if (allHousesDiscovered || state == TutorialState.Complete)
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
            state = TutorialState.PlanningSurvey;
            CaptureMissionState();
            SetStatusText(PlanMessage);
        }
        if (MavlinkReceiver.IsAutoMode && MavlinkReceiver.IsArmed)
        {
            state = TutorialState.Complete;
            SetStatusText("");
            return;
        }
        if (HasNewMission())
        {
            state = TutorialState.MissionUploaded;
            SetStatusText(MissionMessage);
        }
        else if (state == TutorialState.MissionUploaded)
        {
            SetStatusText(MissionMessage);
        }
    }

    protected override void OnDeliveryTargetDiscovered(int targetNumber, float roofAltitudeAMSL, float deliveryAltitudeAMSL)
    {
        if (deliveryScoreManager == null)
        {
            return;
        }
        int found = deliveryScoreManager.DiscoveredDeliveryTargetCount;
        int total = Mathf.Max(1, deliveryScoreManager.targetBuildingCount);
        if (found >= total)
        {
            allHousesDiscovered = true;
            state = TutorialState.Complete;
            SetStatusText(FoundMessage);
        }
        else
        {
            SetStatusText($"HOUSE FOUND  |  {found}/{total} delivery houses discovered");
        }
        RefreshDeliveryObjective();
    }

    protected override string GetObjectiveText()
    {
        int total = deliveryScoreManager != null
            ? Mathf.Max(1, deliveryScoreManager.targetBuildingCount)
            : 1;
        int found = deliveryScoreManager != null
            ? deliveryScoreManager.DiscoveredDeliveryTargetCount
            : 0;
        return $"Houses to deliver found: {found}/{total}";
    }

    private void CaptureMissionState()
    {
        hadMissionAtStart = MavlinkReceiver != null && MavlinkReceiver.HasMissionCurrent;
        initialMissionCount = hadMissionAtStart ? MavlinkReceiver.MissionCurrentTotal : (ushort)0;
        initialMissionSequence = hadMissionAtStart ? MavlinkReceiver.MissionCurrentSequence : (ushort)0;
        wasInAutoMode = MavlinkReceiver != null && MavlinkReceiver.IsAutoMode;
        initialMissionUpdateCount = MavlinkReceiver != null ? MavlinkReceiver.MissionCurrentUpdateCount : 0;
    }

    private bool HasNewMission()
    {
        if (MavlinkReceiver == null || !MavlinkReceiver.HasMissionCurrent)
        {
            return false;
        }
        ushort total = MavlinkReceiver.MissionCurrentTotal;
        bool changed = !hadMissionAtStart || total != initialMissionCount ||
            MavlinkReceiver.MissionCurrentSequence != initialMissionSequence ||
            (!wasInAutoMode && MavlinkReceiver.IsAutoMode);
        return total > 0 && total < ushort.MaxValue && changed &&
            MavlinkReceiver.MissionCurrentUpdateCount > initialMissionUpdateCount;
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
        levelStatusText.fontSize = 34f;
        levelStatusText.fontSizeMin = 26f;
        levelStatusText.fontSizeMax = 34f;
        levelStatusText.enableAutoSizing = true;
        levelStatusText.raycastTarget = false;
    }
}
