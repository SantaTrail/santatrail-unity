using UnityEngine;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using Esri.ArcGISMapsSDK.Components;
using Esri.ArcGISMapsSDK.Utils.GeoCoord;
using Esri.GameEngine.Geometry;
using Esri.GameEngine.Extent;

[DefaultExecutionOrder(9000)]
public class MAVLinkReceiver : MonoBehaviour
{
    public static MAVLinkReceiver Active { get; private set; }

    private UdpClient client;
    private IPEndPoint endPoint;

    [Header("MAVLink Connection")]
    [Tooltip("Local UDP port receiving telemetry from ArduPilot SITL.")]
    [SerializeField] int udpListenPort = 14551;

    [Min(0.1f)]
    [Tooltip("How often Unity retries the telemetry socket after a startup bind failure.")]
    [SerializeField] float udpReconnectSeconds = 1f;

    [Min(1f)]
    [Tooltip("Rate Unity requests for GLOBAL_POSITION_INT telemetry from ArduPilot.")]
    [SerializeField] float requestedPositionRateHz = 10f;

    private float nextSocketOpenAttemptTime;

    private MAVLink.MavlinkParse parser;
    private MAVLink.MavlinkParse txParser;

    [Header("ArcGIS")]
    public ArcGISLocationComponent locationComponent;
    [Tooltip("If enabled, the drone starts at the ArcGIS map's origin position.")]
    public bool alignToMapOriginOnStart = true;
    private bool hasAlignedToMapOrigin;
    private bool hasConfirmedHomeSync;
    private float lastHomeSyncRequestTime = -1000f;
    private float lastPositionStreamRequestTime = -1000f;
    private bool hasLoggedPositionStreamRequest;

    [Header("Debug Overlay")]
    public bool showDebugOverlay = true;
    public KeyCode toggleDebugKey = KeyCode.F8;

    [Header("Attitude")]
    public float roll;
    public float pitch;
    public float yaw;

    public float headingDeg;
    public bool hasHeadingDeg;
    public float lastHeadingTime { get; private set; }
    public float lastAttitudeTime { get; private set; }
    public float lastPositionTime { get; private set; }

    [Header("ArcGIS Rotation Smoothing")]
    [Tooltip("Apply MAVLink heading to ArcGIS Location rotation. This keeps rotation active even before the drone starts moving.")]
    public bool applyTelemetryRotation = true;

    [Tooltip("Use GLOBAL_POSITION_INT heading when available. Leave OFF to prefer ATTITUDE yaw, which also updates while stationary.")]
    public bool useGpsHeadingForRotation = true;

    [Tooltip("Model heading correction. Start with 180 for the current drone asset; use 0 if the model faces backward.")]
    public float modelHeadingOffsetDegrees = 0.1f;

    [Tooltip("The imported drone mesh needs ArcGIS pitch 90 to keep its nose level before optional MAVLink attitude tilt.")]
    public float modelBasePitchDegrees = 90f;

    public float modelBaseRollDegrees = 0f;

    [Tooltip("Keep OFF for the smoothest result. Enable later to show MAVLink pitch and roll.")]
    public bool applyTelemetryPitchAndRoll = false;

    [Min(0.01f)]
    [Tooltip("Smaller values rotate faster. Try 0.12 to 0.25 seconds.")]
    public float headingSmoothTime = 0.22f;

    [Min(1f)]
    public float maximumHeadingSpeed = 140f;

    [Min(0f)]
    [Tooltip("Ignores very small heading changes that can look like vibration.")]
    public float headingDeadZoneDegrees = 0.35f;

    [Min(0.001f)]
    [Tooltip(
        "Avoids writing an exact 0/360 heading to the ArcGIS rotation path. " +
        "0.1 degrees is visually indistinguishable from 0."
    )]
    public float zeroHeadingSafetyEpsilon = 0.1f;

    [Min(0f)]
    [Tooltip(
        "Do not rewrite ArcGIS rotation when the value has barely changed."
    )]
    public float rotationWriteThresholdDegrees = 0.02f;

    private float smoothedArcGisHeading;
    private float headingSmoothVelocity;
    private bool hasSmoothedHeading;

    private float lastWrittenHeading;
    private float lastWrittenPitch;
    private float lastWrittenRoll;
    private bool hasWrittenArcGisRotation;

    [Header("Smoothing")]
    [Tooltip("Higher = faster response")]
    public float positionSmoothSpeed = 8f;

    [Tooltip("How quickly the visible position eases toward incoming telemetry")]
    public float positionSmoothTime = 0.20f;

    [Tooltip("Predicts motion between MAVLink packets")]
    public float extrapolationTime = 0.04f;

    [Tooltip("Max seconds to continue dead-reckoning when GPS packets pause")]
    public float maxExtrapolationGap = 0.25f;

    [Tooltip("Snap when close to target position (meters) to avoid apparent short-stop lag")]
    public float snapDistanceMeters = 0.02f;

    [Header("Velocity Recovery")]
    [Tooltip("Treat speeds below this as potentially stale (m/s)")]
    public float staleSpeedThreshold = 0.15f;

    [Tooltip("Continue using last non-zero velocity for this long when stream reports near-zero speed")]
    public float coastSeconds = 0.20f;

    [Header("Guided Assist")]
    [Tooltip("When enabled, Unity can keep moving visually if GUIDED stalls. Keep OFF to match QGroundControl exactly.")]
    public bool enableGuidedAssist = false;

    [Tooltip("When GUIDED is stalled but target is still far, resend reposition command to wake ArduPilot motion.")]
    public bool enableGuidedRepositionNudge = false;

    [Tooltip("Seconds of near-zero speed before sending a reposition nudge.")]
    public float guidedStallSecondsBeforeNudge = 2.0f;

    [Tooltip("Minimum seconds between reposition nudges.")]
    public float guidedNudgeIntervalSeconds = 0.5f;

    [Header("Map Boundary Stop")]
    [Tooltip("Request BRAKE from ArduPilot when the vehicle reaches the configured map boundary.")]
    [SerializeField] bool enforceMapGeofence = true;

    [Tooltip("Use the active SceneLoaderArcgis map radius instead of duplicating the radius here.")]
    [SerializeField] bool useSceneMapExtentForGeofence = true;

    [Tooltip("Use the fallback radius only when no SceneLoaderArcgis map extent is available.")]
    [SerializeField] bool useFallbackGeofenceRadius = false;

    [Min(1f)]
    [Tooltip("Fallback playable radius in metres when no scene map extent is available.")]
    [SerializeField] float fallbackGeofenceRadiusMeters = 500f;

    [Min(0f)]
    [Tooltip("Stop before the exact map edge by this many metres.")]
    [SerializeField] float geofenceBufferMeters = 20f;

    [Min(0.2f)]
    [Tooltip("Minimum seconds between automatic BRAKE requests.")]
    [SerializeField] float geofenceCommandIntervalSeconds = 1f;

    [Tooltip(
        "When a GUIDED destination is outside the playable map, replace it " +
        "with a hold at the boundary instead of switching to BRAKE."
    )]
    [SerializeField] bool preserveGuidedModeOnMapBoundary = true;

    [Header("Building Collision")]
    [Tooltip("Stop the visible drone before it enters a generated building.")]
    [SerializeField] bool enforceBuildingCollision = true;

    [Min(0.1f)]
    [Tooltip("Collision radius around the drone in metres.")]
    [SerializeField] float droneCollisionRadiusMeters = 0.35f;

    [Tooltip("Physics layers checked for generated building colliders.")]
    [SerializeField] LayerMask buildingCollisionLayers = ~0;

    [Tooltip(
        "When a GUIDED destination intersects a building, replace it with a " +
        "short retreat from the last safe position instead of switching to BRAKE."
    )]
    [SerializeField] bool preserveGuidedModeOnBuildingCollision = true;

    [Min(0f)]
    [Tooltip(
        "Move the replacement GUIDED target this far back from the impact " +
        "so the physical vehicle clears the collider before control resumes."
    )]
    [SerializeField] float buildingCollisionRetreatMeters = 1f;

    [Header("Altitude Limit")]
    [Tooltip("Request BRAKE from ArduPilot when the drone reaches the maximum relative altitude.")]
    [SerializeField] bool enforceMaximumAltitude = true;

    [Min(1f)]
    [Tooltip("Maximum altitude above the ArduPilot home position in metres.")]
    [SerializeField] float maximumRelativeAltitudeMeters = 45f;

    [Min(0f)]
    [Tooltip("Stop before the exact altitude limit by this many metres.")]
    [SerializeField] float altitudeLimitBufferMeters = 3f;

    private double targetLat;
    private double targetLon;
    private double targetAlt;
    private float targetRelativeAlt;

    private double smoothLat;
    private double smoothLon;
    private double smoothAlt;

    private bool hasTargetPosition;
    private bool hasSmoothedPosition;
    private float lastGpsPacketTime;
    private float velNorthMps;
    private float velEastMps;
    private float velDownMps;
    private float lastNonZeroVelTime;
    private float lastNonZeroNorthMps;
    private float lastNonZeroEastMps;
    private float lastNonZeroDownMps;
    private bool hasPrevGpsSample;
    private double prevLat;
    private double prevLon;
    private double prevAlt;
    private float prevGpsSampleTime;

    private bool hasLoggedInvalidGps;
    private bool hasLoggedInvalidAttitude;
    private bool hasAppliedLocation;
    private string debugVelocitySource = "raw";
    private bool usingGuidedAssist;
    private string debugAttitudeSource = "none";
    private float lastAnyPacketTime;
    private int gpsMsgCount;
    private int attMsgCount;
    private int quatMsgCount;
    private StringBuilder debugSb;
    private bool hbArmed;
    private bool hbGuided;
    private uint hbCustomMode;
    private byte hbSystemStatus;
    private MAVLink.MAV_LANDED_STATE landedState = MAVLink.MAV_LANDED_STATE.UNDEFINED;
    private string lastStatusText = "";
    private bool hasNavTargetDist;
    private float navTargetDistM;
    private float navTargetBearingDeg;
    private float guidedStallTimer;
    private float lastGuidedNudgeTime = -1000f;
    private ushort lastCommandAckCommand;
    private byte lastCommandAckResult;
    private bool hasCommandAck;
    private byte vehicleSystemId = 1;
    private byte vehicleComponentId = 1;
    private bool mapGeofenceTriggered;
    private bool boundaryGuidedHoldSent;
    private double mapGeofenceDistanceMeters;
    private bool altitudeLimitTriggered;
    private float lastSafetyBrakeCommandTime = -1000f;
    private bool hasLastSafePosition;
    private double lastSafeLat;
    private double lastSafeLon;
    private double lastSafeAlt;
    private Vector3 lastSafeWorldPosition;
    private bool buildingCollisionTriggered;
    private bool buildingGuidedHoldSent;
    private double buildingCollisionContactLat;
    private double buildingCollisionContactLon;
    private double buildingCollisionContactAlt;
    private Vector3 lastBlockedMovementWorldDirection;

    public bool IsGuidedArmed => hbGuided && hbArmed;
    public bool IsGuidedMode => hbGuided;
    public bool IsArmed => hbArmed;
    public bool HasRecentTelemetry => lastAnyPacketTime > 0f && (Time.time - lastAnyPacketTime) <= 2f;
    public MAVLink.MAV_LANDED_STATE LandedState => landedState;
    public float RelativeAltitudeMeters => targetRelativeAlt;
    public double CurrentLatitude => smoothLat;
    public double CurrentLongitude => smoothLon;
    public bool HasPosition => hasSmoothedPosition;
    public bool HasNavTargetDistance => hasNavTargetDist;
    public float NavTargetDistanceMeters => navTargetDistM;
    public Vector3 VelocityNed => new Vector3(velNorthMps, velEastMps, velDownMps);
    public float HorizontalSpeedMps => Mathf.Sqrt(velNorthMps * velNorthMps + velEastMps * velEastMps);
    public float SpeedMps => Mathf.Sqrt(velNorthMps * velNorthMps + velEastMps * velEastMps + velDownMps * velDownMps);
    public bool MapGeofenceTriggered => mapGeofenceTriggered;
    public double MapGeofenceDistanceMeters => mapGeofenceDistanceMeters;
    public bool AltitudeLimitTriggered => altitudeLimitTriggered;
    public bool BuildingCollisionTriggered => buildingCollisionTriggered;

    public static void PrepareForSceneRestart()
    {
        MAVLinkReceiver receiver = Active;
        if (receiver == null)
        {
            return;
        }

        // Disabling invokes OnDisable synchronously, which closes UDP 14551
        // before the next scene starts its replacement receiver.
        receiver.enabled = false;
        Debug.Log("MAVLinkReceiver: released UDP 14551 for scene restart.");
    }

    public bool TryGetPlayableBoundaryRadius(out double radiusMeters)
    {
        if (!enforceMapGeofence || !TryGetGeofenceRadius(out radiusMeters))
        {
            radiusMeters = 0.0;
            return false;
        }

        radiusMeters = System.Math.Max(
            1.0,
            radiusMeters - System.Math.Max(0.0, geofenceBufferMeters)
        );
        return true;
    }

    string GetCopterModeName(uint customMode)
    {
        switch (customMode)
        {
            case 0: return "STABILIZE";
            case 1: return "ACRO";
            case 2: return "ALT_HOLD";
            case 3: return "AUTO";
            case 4: return "GUIDED";
            case 5: return "LOITER";
            case 6: return "RTL";
            case 7: return "CIRCLE";
            case 9: return "LAND";
            case 11: return "DRIFT";
            case 13: return "SPORT";
            case 14: return "FLIP";
            case 15: return "AUTOTUNE";
            case 16: return "POSHOLD";
            case 17: return "BRAKE";
            case 18: return "THROW";
            case 19: return "AVOID_ADSB";
            case 20: return "GUIDED_NOGPS";
            case 21: return "SMART_RTL";
            case 22: return "FLOWHOLD";
            case 23: return "FOLLOW";
            case 24: return "ZIGZAG";
            case 25: return "SYSTEMID";
            case 26: return "AUTOROTATE";
            case 27: return "AUTO_RTL";
            default: return "UNKNOWN";
        }
    }

    void Awake()
    {
        zeroHeadingSafetyEpsilon =
            Mathf.Max(0.001f, zeroHeadingSafetyEpsilon);

        // Treat an Inspector value of 0 as an almost-zero heading correction.
        // This keeps the intended direction without writing an exact zero.
        if (Mathf.Abs(modelHeadingOffsetDegrees) < 0.0001f)
        {
            modelHeadingOffsetDegrees = zeroHeadingSafetyEpsilon;
        }
    }

    void Start()
    {
        if (Active != null && Active != this)
        {
            Debug.LogWarning($"⚠️ Duplicate MAVLinkReceiver on {name} disabled (active: {Active.name})");
            enabled = false;
            return;
        }

        Active = this;

        parser = new MAVLink.MavlinkParse();
        txParser = new MAVLink.MavlinkParse();
        debugSb = new StringBuilder(512);

        EnsureUdpSocketOpen();

        if (locationComponent == null)
        {
            locationComponent = GetComponentInChildren<ArcGISLocationComponent>(true);
        }

        if (locationComponent == null)
        {
            Debug.LogError("❌ ArcGISLocationComponent NOT FOUND");
        }

        if (GetComponent<DroneBoundaryWarning>() == null)
        {
            gameObject.AddComponent<DroneBoundaryWarning>();
        }

        if (alignToMapOriginOnStart && locationComponent != null)
        {
            StartCoroutine(AlignToMapOriginWhenReady());
        }

    }

    IEnumerator AlignToMapOriginWhenReady()
    {
        while (!hasAlignedToMapOrigin && enabled)
        {
            if (AlignToMapOrigin())
            {
                hasAlignedToMapOrigin = true;
                TrySyncHomeLocation();
                yield break;
            }

            yield return null;
        }
    }

    bool AlignToMapOrigin()
    {
        if (locationComponent == null)
        {
            return false;
        }

        ArcGISMapComponent mapComponent = locationComponent.GetComponentInParent<ArcGISMapComponent>();
        if (mapComponent == null || mapComponent.OriginPosition == null)
        {
            return false;
        }

        locationComponent.Position = mapComponent.OriginPosition;
        Debug.Log($"🧭 Drone aligned to ArcGIS map origin: {mapComponent.OriginPosition}");
        return true;
    }

    void TrySyncHomeLocation()
    {
        if (hasConfirmedHomeSync ||
            client == null ||
            endPoint == null ||
            txParser == null ||
            !GameManager.hasHomeLocation)
        {
            return;
        }

        if ((Time.time - lastHomeSyncRequestTime) < 1.0f)
        {
            return;
        }

        lastHomeSyncRequestTime = Time.time;

        var setHome = new MAVLink.mavlink_command_long_t(
            0f,
            0f,
            0f,
            0f,
            (float)GameManager.homeLat,
            (float)GameManager.homeLon,
            (float)GameManager.homeAlt,
            (ushort)MAVLink.MAV_CMD.DO_SET_HOME,
            vehicleSystemId,
            vehicleComponentId,
            0
        );

        byte[] setHomePacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.COMMAND_LONG,
            setHome,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        var requestHome = new MAVLink.mavlink_command_long_t(
            242f,
            0f,
            0f,
            0f,
            0f,
            0f,
            0f,
            (ushort)MAVLink.MAV_CMD.REQUEST_MESSAGE,
            vehicleSystemId,
            vehicleComponentId,
            0
        );

        byte[] requestHomePacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.COMMAND_LONG,
            requestHome,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        if (SendToVehicle(setHomePacket) && SendToVehicle(requestHomePacket))
        {
            Debug.Log(
                $"🧭 Requested home sync: {GameManager.homeLat}, {GameManager.homeLon}, {GameManager.homeAlt}"
            );
        }
    }

    void RequestPositionStream()
    {
        if (client == null || endPoint == null || txParser == null)
        {
            return;
        }

        // MAV_CMD_SET_MESSAGE_INTERVAL is idempotent, so retrying also recovers
        // if ArduPilot restarts after Unity has opened its telemetry socket.
        bool hasFreshPosition =
            hasTargetPosition &&
            lastPositionTime > 0f &&
            (Time.time - lastPositionTime) < 2f;

        if (hasFreshPosition ||
            (Time.time - lastPositionStreamRequestTime) < 2f)
        {
            return;
        }

        lastPositionStreamRequestTime = Time.time;
        float intervalMicroseconds = 1000000f / Mathf.Max(1f, requestedPositionRateHz);

        var requestPosition = new MAVLink.mavlink_command_long_t(
            (float)MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT,
            intervalMicroseconds,
            0f,
            0f,
            0f,
            0f,
            0f,
            (ushort)MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL,
            vehicleSystemId,
            vehicleComponentId,
            0
        );

        byte[] requestPacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.COMMAND_LONG,
            requestPosition,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        if (SendToVehicle(requestPacket) && !hasLoggedPositionStreamRequest)
        {
            Debug.Log(
                $"Requested GLOBAL_POSITION_INT at {requestedPositionRateHz:0.#} Hz from ArduPilot."
            );
            hasLoggedPositionStreamRequest = true;
        }
    }

    bool SendToVehicle(byte[] packet)
    {
        if (client == null || endPoint == null || packet == null || packet.Length == 0)
        {
            return false;
        }

        try
        {
            // serial1 uses ArduPilot's bidirectional udpclient backend. Commands
            // must reply to the endpoint that sent telemetry, from Unity's 14551 socket.
            client.Send(packet, packet.Length, endPoint);
            return true;
        }
        catch (SocketException exception)
        {
            Debug.LogWarning($"⚠️ MAVLink command send failed: {exception.Message}");
            return false;
        }
    }

    public void StopDrone()
    {
        if (SendBrakeMode())
        {
            Debug.Log("MAVLinkReceiver: BRAKE requested from StopDrone().");
        }
        else
        {
            Debug.LogWarning("MAVLinkReceiver: could not send BRAKE; MAVLink is not connected.");
        }
    }

    void CheckMapGeofence()
    {
        if (!enforceMapGeofence ||
            !hasTargetPosition ||
            !GameManager.hasHomeLocation)
        {
            return;
        }

        if (!TryGetGeofenceRadius(out double radiusMeters))
        {
            return;
        }

        mapGeofenceDistanceMeters = CalculateDistanceMeters(
            GameManager.homeLat,
            GameManager.homeLon,
            targetLat,
            targetLon
        );

        double triggerRadiusMeters = System.Math.Max(
            1.0,
            radiusMeters - System.Math.Max(0.0, geofenceBufferMeters)
        );

        double boundaryResetRadiusMeters = System.Math.Max(
            1.0,
            triggerRadiusMeters - System.Math.Max(2.0, geofenceBufferMeters * 0.1)
        );

        if (mapGeofenceTriggered &&
            mapGeofenceDistanceMeters <= boundaryResetRadiusMeters)
        {
            mapGeofenceTriggered = false;
            boundaryGuidedHoldSent = false;
        }

        if (mapGeofenceDistanceMeters < triggerRadiusMeters || !hbArmed)
        {
            return;
        }

        // LateUpdate clamps the visible location to the edge and replaces the
        // unreachable destination with that clamped position. Do not enter
        // BRAKE first, otherwise Guided input remains locked behind BRAKE.
        if (CanPreserveGuidedControl(preserveGuidedModeOnMapBoundary))
        {
            mapGeofenceTriggered = true;
            return;
        }

        if (hbCustomMode == 17u)
        {
            mapGeofenceTriggered = true;
            return;
        }

        if (Time.time - lastSafetyBrakeCommandTime <
            Mathf.Max(0.2f, geofenceCommandIntervalSeconds))
        {
            return;
        }

        lastSafetyBrakeCommandTime = Time.time;

        if (!SendBrakeMode())
        {
            return;
        }

        mapGeofenceTriggered = true;
        Debug.LogWarning(
            $"MAP BOUNDARY: vehicle is {mapGeofenceDistanceMeters:F1}m " +
            $"from home outside the {radiusMeters:F1}m map radius. " +
            "BRAKE requested from ArduPilot."
        );
    }

    void CheckAltitudeLimit()
    {
        if (!enforceMaximumAltitude ||
            !hasTargetPosition ||
            !hbArmed ||
            float.IsNaN(targetRelativeAlt) ||
            float.IsInfinity(targetRelativeAlt))
        {
            return;
        }

        float maximumAltitude = Mathf.Max(1f, maximumRelativeAltitudeMeters);
        float triggerAltitude = Mathf.Max(
            0.1f,
            maximumAltitude - Mathf.Max(0f, altitudeLimitBufferMeters)
        );

        if (altitudeLimitTriggered &&
            targetRelativeAlt <= maximumAltitude * 0.85f)
        {
            altitudeLimitTriggered = false;
        }

        if (targetRelativeAlt < triggerAltitude)
        {
            return;
        }

        if (hbCustomMode == 17u)
        {
            altitudeLimitTriggered = true;
            return;
        }

        if (Time.time - lastSafetyBrakeCommandTime <
            Mathf.Max(0.2f, geofenceCommandIntervalSeconds))
        {
            return;
        }

        lastSafetyBrakeCommandTime = Time.time;

        if (!SendBrakeMode())
        {
            return;
        }

        altitudeLimitTriggered = true;
        Debug.LogWarning(
            $"ALTITUDE LIMIT: vehicle is at {targetRelativeAlt:F1}m " +
            $"relative altitude and the limit is {maximumAltitude:F1}m. " +
            "BRAKE requested from ArduPilot."
        );
    }

    bool TryGetGeofenceRadius(out double radiusMeters)
    {
        radiusMeters = 0.0;

        if (useSceneMapExtentForGeofence &&
            SceneLoaderArcgis.TryGetConfiguredMapExtentRadius(out radiusMeters))
        {
            return true;
        }

        if (!useFallbackGeofenceRadius)
        {
            return false;
        }

        radiusMeters = System.Math.Max(1.0, fallbackGeofenceRadiusMeters);
        return true;
    }

    bool ClampToPlayableMap(ref double latitude, ref double longitude)
    {
        if (!enforceMapGeofence ||
            !GameManager.hasHomeLocation ||
            !TryGetGeofenceRadius(out double radiusMeters))
        {
            return false;
        }

        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Max(
                0.0001,
                System.Math.Cos(GameManager.homeLat * System.Math.PI / 180.0)
            );

        double northMeters =
            (latitude - GameManager.homeLat) * metersPerDegreeLatitude;
        double eastMeters =
            (longitude - GameManager.homeLon) * metersPerDegreeLongitude;
        double distanceMeters = System.Math.Sqrt(
            northMeters * northMeters + eastMeters * eastMeters
        );
        double allowedRadiusMeters = System.Math.Max(
            1.0,
            radiusMeters - System.Math.Max(0.0, geofenceBufferMeters)
        );

        mapGeofenceDistanceMeters = distanceMeters;
        if (distanceMeters <= allowedRadiusMeters || distanceMeters <= 0.0001)
        {
            return false;
        }

        double scale = allowedRadiusMeters / distanceMeters;
        latitude =
            GameManager.homeLat +
            (northMeters * scale) / metersPerDegreeLatitude;
        longitude =
            GameManager.homeLon +
            (eastMeters * scale) / metersPerDegreeLongitude;
        mapGeofenceTriggered = true;

        if (TryHoldGuidedAtMapBoundary(latitude, longitude, smoothAlt))
        {
            return true;
        }

        RequestSafetyBrake(
            $"MAP BOUNDARY: movement was limited to the {allowedRadiusMeters:F1}m playable radius."
        );
        return true;
    }

    bool TryHoldGuidedAtMapBoundary(
        double latitude,
        double longitude,
        double absoluteAltitude)
    {
        if (!CanPreserveGuidedControl(preserveGuidedModeOnMapBoundary))
        {
            return false;
        }

        hasNavTargetDist = false;
        navTargetDistM = 0f;
        navTargetBearingDeg = 0f;

        if (boundaryGuidedHoldSent)
        {
            return true;
        }

        boundaryGuidedHoldSent = true;
        float safeRelativeAltitude = Mathf.Max(
            0f,
            targetRelativeAlt + (float)(absoluteAltitude - targetAlt)
        );

        if (SendGuidedHoldPosition(
            latitude,
            longitude,
            safeRelativeAltitude))
        {
            lastStatusText =
                "Map boundary: GUIDED target replaced with boundary hold";
            Debug.LogWarning(
                "MAP BOUNDARY: outside GUIDED destination was replaced " +
                "with a hold at the playable edge."
            );
        }

        // Returning true after the first hold prevents a fallback BRAKE request
        // from taking control away during the same boundary contact.
        return true;
    }

    bool CanPreserveGuidedControl(bool enabledForSafetyLimit)
    {
        return enabledForSafetyLimit &&
               hbArmed &&
               (hbGuided || hbCustomMode == 4u);
    }

    bool WouldHitBuilding(double latitude, double longitude, double altitude)
    {
        if (!enforceBuildingCollision || !hasLastSafePosition)
        {
            return false;
        }

        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Max(
                0.0001,
                System.Math.Cos(lastSafeLat * System.Math.PI / 180.0)
            );

        Vector3 candidateWorldPosition = lastSafeWorldPosition + new Vector3(
            (float)((longitude - lastSafeLon) * metersPerDegreeLongitude),
            (float)(altitude - lastSafeAlt),
            (float)((latitude - lastSafeLat) * metersPerDegreeLatitude)
        );
        Vector3 movement = candidateWorldPosition - lastSafeWorldPosition;
        float distance = movement.magnitude;
        float radius = Mathf.Max(0.1f, droneCollisionRadiusMeters);

        if (distance > 0.0001f)
        {
            Vector3 movementDirection = movement / distance;
            RaycastHit[] hits = Physics.SphereCastAll(
                lastSafeWorldPosition,
                radius,
                movementDirection,
                distance,
                buildingCollisionLayers,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < hits.Length; i++)
            {
                if (IsGeneratedBuildingCollider(hits[i].collider))
                {
                    // A sphere cast can report a collider that the start point
                    // is merely touching. Permit travel away from that surface;
                    // otherwise one impact can permanently trap the drone.
                    Vector3 closestStartPoint =
                        hits[i].collider.ClosestPoint(lastSafeWorldPosition);
                    Vector3 awayFromCollider =
                        lastSafeWorldPosition - closestStartPoint;

                    if (awayFromCollider.sqrMagnitude > 0.0001f &&
                        Vector3.Dot(
                            movementDirection,
                            awayFromCollider.normalized) > 0.05f)
                    {
                        continue;
                    }

                    lastBlockedMovementWorldDirection = movementDirection;
                    return true;
                }
            }
        }

        Collider[] overlaps = Physics.OverlapSphere(
            candidateWorldPosition,
            radius,
            buildingCollisionLayers,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (IsGeneratedBuildingCollider(overlaps[i]))
            {
                if (movement.sqrMagnitude > 0.0001f)
                {
                    // If numerical/ArcGIS timing leaves the saved point just
                    // inside a collider, allow candidates that increase their
                    // distance from its centre. This guarantees an escape path.
                    Bounds colliderBounds = overlaps[i].bounds;
                    float startDistanceSqr =
                        (lastSafeWorldPosition - colliderBounds.center).sqrMagnitude;
                    float candidateDistanceSqr =
                        (candidateWorldPosition - colliderBounds.center).sqrMagnitude;

                    if (candidateDistanceSqr > startDistanceSqr + 0.0001f)
                    {
                        continue;
                    }

                    lastBlockedMovementWorldDirection = movement.normalized;
                }
                return true;
            }
        }

        return false;
    }

    bool IsGeneratedBuildingCollider(Collider candidate)
    {
        if (candidate == null ||
            candidate.transform == transform ||
            candidate.transform.IsChildOf(transform))
        {
            return false;
        }

        Transform current = candidate.transform;
        while (current != null)
        {
            if (current.name.Contains("_ArcGISBuilding_") ||
                current.name.StartsWith("VisibleBuilding_") ||
                current.name.StartsWith("ModularHouse_"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    void RejectBuildingMovement()
    {
        bool isNewCollision = !buildingCollisionTriggered;
        double holdLatitude = lastSafeLat;
        double holdLongitude = lastSafeLon;
        double holdAltitude = lastSafeAlt;
        Vector3 holdWorldPosition = lastSafeWorldPosition;

        if (isNewCollision)
        {
            buildingGuidedHoldSent = false;
            buildingCollisionContactLat = lastSafeLat;
            buildingCollisionContactLon = lastSafeLon;
            buildingCollisionContactAlt = lastSafeAlt;

            GetBuildingCollisionRetreatPosition(
                out holdLatitude,
                out holdLongitude,
                out holdAltitude,
                out holdWorldPosition
            );

            // Move the presentation back immediately. Waiting for the SITL
            // vehicle to return left the visible drone pressed against the
            // collider and made every escape command look unresponsive.
            lastSafeLat = holdLatitude;
            lastSafeLon = holdLongitude;
            lastSafeAlt = holdAltitude;
            lastSafeWorldPosition = holdWorldPosition;
        }

        smoothLat = holdLatitude;
        smoothLon = holdLongitude;
        smoothAlt = holdAltitude;
        usingGuidedAssist = false;
        guidedStallTimer = 0f;
        buildingCollisionTriggered = true;

        locationComponent.Position = new ArcGISPoint(
            holdLongitude,
            holdLatitude,
            holdAltitude,
            ArcGISSpatialReference.WGS84()
        );

        // BRAKE cancels stick/guided control and used to leave the old Guided
        // destination waiting behind it. Holding at the last collision-free
        // point replaces that unreachable destination while keeping the pilot
        // in GUIDED so another target can be selected immediately.
        bool canKeepGuidedControl =
            preserveGuidedModeOnBuildingCollision &&
            hbArmed &&
            (hbGuided || hbCustomMode == 4u || hbCustomMode == 17u);

        if (canKeepGuidedControl)
        {
            hasNavTargetDist = false;
            navTargetDistM = 0f;
            navTargetBearingDeg = 0f;

            // Send exactly once for this contact. Repeating the hold used to
            // overwrite every new QGC command and made control appear stuck.
            if (!buildingGuidedHoldSent)
            {
                buildingGuidedHoldSent = true;
                float safeRelativeAltitude = Mathf.Max(
                    0f,
                    targetRelativeAlt + (float)(holdAltitude - targetAlt)
                );

                if (SendGuidedHoldPosition(
                    holdLatitude,
                    holdLongitude,
                    safeRelativeAltitude))
                {
                    lastStatusText =
                        "Building collision: target cancelled; retreating to safety";
                    Debug.LogWarning(
                        "BUILDING COLLISION: unreachable GUIDED destination " +
                        "was cancelled. The drone is retreating to a safe " +
                        "position and remains in GUIDED."
                    );
                }
            }

            return;
        }

        // A Unity-only building must never force a flight-mode change. In a
        // non-Guided mode the visible movement is rejected, but the pilot's
        // current mode and controls are left untouched so they can move away.
        if (isNewCollision)
        {
            lastStatusText =
                "Building collision: movement blocked; flight mode unchanged";
            Debug.LogWarning(
                "BUILDING COLLISION: movement was stopped before the " +
                "building. Flight mode was left unchanged."
            );
        }
    }

    void GetBuildingCollisionRetreatPosition(
        out double latitude,
        out double longitude,
        out double altitude,
        out Vector3 worldPosition)
    {
        latitude = lastSafeLat;
        longitude = lastSafeLon;
        altitude = lastSafeAlt;
        worldPosition = lastSafeWorldPosition;

        float retreatMeters = Mathf.Max(0f, buildingCollisionRetreatMeters);
        Vector3 blockedDirection = lastBlockedMovementWorldDirection;

        if (retreatMeters <= 0.001f ||
            blockedDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        blockedDirection.Normalize();
        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Max(
                0.0001,
                System.Math.Cos(lastSafeLat * System.Math.PI / 180.0)
            );

        latitude -=
            blockedDirection.z * retreatMeters / metersPerDegreeLatitude;
        longitude -=
            blockedDirection.x * retreatMeters / metersPerDegreeLongitude;
        altitude -= blockedDirection.y * retreatMeters;
        ClampToPlayableMap(ref latitude, ref longitude);

        worldPosition = lastSafeWorldPosition + new Vector3(
            (float)((longitude - lastSafeLon) * metersPerDegreeLongitude),
            (float)(altitude - lastSafeAlt),
            (float)((latitude - lastSafeLat) * metersPerDegreeLatitude)
        );
    }

    bool SendGuidedHoldPosition(
        double latitude,
        double longitude,
        float relativeAltitude)
    {
        if (client == null ||
            endPoint == null ||
            txParser == null ||
            !IsValidGeo(latitude, longitude, lastSafeAlt) ||
            !IsFinite(relativeAltitude))
        {
            return false;
        }

        const ushort positionOnlyTypeMask =
            (1 << 3) | (1 << 4) | (1 << 5) |
            (1 << 6) | (1 << 7) | (1 << 8) |
            (1 << 10) | (1 << 11);

        int latitudeE7 = (int)System.Math.Round(latitude * 1e7);
        int longitudeE7 = (int)System.Math.Round(longitude * 1e7);

        var holdPosition =
            new MAVLink.mavlink_set_position_target_global_int_t(
                (uint)(Time.realtimeSinceStartup * 1000f),
                latitudeE7,
                longitudeE7,
                relativeAltitude,
                0f, 0f, 0f,
                0f, 0f, 0f,
                0f, 0f,
                positionOnlyTypeMask,
                vehicleSystemId,
                vehicleComponentId,
                (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT_INT
            );

        byte[] holdPositionPacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.SET_POSITION_TARGET_GLOBAL_INT,
            holdPosition,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        var replaceDestination = new MAVLink.mavlink_command_int_t(
            -1f,
            1f,
            0f,
            float.NaN,
            latitudeE7,
            longitudeE7,
            relativeAltitude,
            (ushort)MAVLink.MAV_CMD.DO_REPOSITION,
            vehicleSystemId,
            vehicleComponentId,
            (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
            0,
            0
        );

        byte[] replaceDestinationPacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.COMMAND_INT,
            replaceDestination,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        bool sentHold = SendToVehicle(holdPositionPacket);
        bool sentReplacement = SendToVehicle(replaceDestinationPacket);
        return sentHold || sentReplacement;
    }

    void RequestSafetyBrake(string warning)
    {
        if (hbCustomMode == 17u ||
            Time.time - lastSafetyBrakeCommandTime <
            Mathf.Max(0.2f, geofenceCommandIntervalSeconds))
        {
            return;
        }

        lastSafetyBrakeCommandTime = Time.time;
        if (SendBrakeMode())
        {
            Debug.LogWarning(warning + " BRAKE requested from ArduPilot.");
        }
    }

    static double CalculateDistanceMeters(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double earthRadiusMeters = 6371000.0;
        double latitude1Radians = latitude1 * System.Math.PI / 180.0;
        double latitude2Radians = latitude2 * System.Math.PI / 180.0;
        double deltaLatitude = (latitude2 - latitude1) * System.Math.PI / 180.0;
        double deltaLongitude = (longitude2 - longitude1) * System.Math.PI / 180.0;

        double sinLatitude = System.Math.Sin(deltaLatitude * 0.5);
        double sinLongitude = System.Math.Sin(deltaLongitude * 0.5);
        double haversine =
            sinLatitude * sinLatitude +
            System.Math.Cos(latitude1Radians) *
            System.Math.Cos(latitude2Radians) *
            sinLongitude * sinLongitude;

        haversine = System.Math.Max(0.0, System.Math.Min(1.0, haversine));
        return earthRadiusMeters * 2.0 * System.Math.Atan2(
            System.Math.Sqrt(haversine),
            System.Math.Sqrt(1.0 - haversine)
        );
    }

    bool SendBrakeMode()
    {
        if (txParser == null)
        {
            return false;
        }

        MAVLink.mavlink_set_mode_t setMode =
            new MAVLink.mavlink_set_mode_t(
                17u,
                vehicleSystemId,
                (byte)MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED
            );

        byte[] brakePacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.SET_MODE,
            setMode,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        return SendToVehicle(brakePacket);
    }

    void Update()
    {
        if (PauseMenuController.IsPaused) return;
        if (!EnsureUdpSocketOpen())
            return;

        if (IsTogglePressed())
        {
            showDebugOverlay = !showDebugOverlay;
        }

        while (client.Available > 0)
        {
            byte[] data = client.Receive(ref endPoint);
            lastAnyPacketTime = Time.time;

            if (data == null || data.Length == 0)
                return;

            using (MemoryStream stream = new MemoryStream(data))
            {
                try
                {
                    MAVLink.MAVLinkMessage msg;

                    while ((msg = parser.ReadPacket(stream)) != null)
                    {
                        HandleMessage(msg);
                    }
                }
                catch
                {
                }
            }
        }
    }

    bool EnsureUdpSocketOpen()
    {
        if (client != null)
        {
            return true;
        }

        if (Time.unscaledTime < nextSocketOpenAttemptTime)
        {
            return false;
        }

        nextSocketOpenAttemptTime =
            Time.unscaledTime + Mathf.Max(0.1f, udpReconnectSeconds);

        UdpClient newClient = null;

        try
        {
            newClient = new UdpClient();
            newClient.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true
            );
            newClient.Client.Bind(
                new IPEndPoint(IPAddress.Any, udpListenPort)
            );

            client = newClient;
            Debug.Log(
                $"MAVLink receiver listening on UDP {udpListenPort}."
            );
            return true;
        }
        catch (System.Exception exception)
        {
            newClient?.Close();

            if (client != null)
            {
                client.Close();
                client = null;
            }

            Debug.LogWarning(
                $"MAVLink receiver could not bind UDP {udpListenPort}; " +
                $"retrying: {exception.Message}"
            );
            return false;
        }
    }

    void LateUpdate()
    {
        if (PauseMenuController.IsPaused) return;
        // Position and rotation are applied together after MAVLink packets are
        // processed in Update. This prevents physics/render timing mismatch.
        ApplySmoothedTransform();
        ApplySmoothedRotation();
    }

    void ApplySmoothedRotation()
    {
        if (!applyTelemetryRotation || locationComponent == null)
        {
            return;
        }

        bool attitudeFresh =
            lastAttitudeTime > 0f &&
            (Time.time - lastAttitudeTime) <= 0.5f;

        bool headingFresh =
            hasHeadingDeg &&
            (Time.time - lastHeadingTime) <= 1.0f;

        float mavHeadingDegrees;

        // Use one stable source instead of switching between ATTITUDE and GPS
        // every frame. GLOBAL_POSITION_INT.hdg is also the heading normally
        // represented by the vehicle arrow in QGroundControl.
        if (useGpsHeadingForRotation && headingFresh)
        {
            mavHeadingDegrees = headingDeg;
        }
        else if (attitudeFresh)
        {
            mavHeadingDegrees = Mathf.Repeat(
                yaw * Mathf.Rad2Deg,
                360f
            );
        }
        else if (headingFresh)
        {
            mavHeadingDegrees = headingDeg;
        }
        else
        {
            return;
        }

        // Apply the heading directly. The model offset is the only place that
        // should correct a model whose forward axis is reversed.
        float targetArcGisHeading = MakeHeadingSafe(
            Mathf.Repeat(
                mavHeadingDegrees + modelHeadingOffsetDegrees,
                360f
            )
        );

        if (!hasSmoothedHeading)
        {
            smoothedArcGisHeading = targetArcGisHeading;
            headingSmoothVelocity = 0f;
            hasSmoothedHeading = true;
        }

        float headingError = Mathf.Abs(
            Mathf.DeltaAngle(smoothedArcGisHeading, targetArcGisHeading)
        );

        if (headingError > headingDeadZoneDegrees)
        {
            smoothedArcGisHeading = Mathf.SmoothDampAngle(
                smoothedArcGisHeading,
                targetArcGisHeading,
                ref headingSmoothVelocity,
                Mathf.Max(0.01f, headingSmoothTime),
                Mathf.Max(1f, maximumHeadingSpeed),
                Mathf.Max(Time.deltaTime, 0.0001f)
            );
        }

        float arcGisPitch = modelBasePitchDegrees;
        float arcGisRoll = modelBaseRollDegrees;

        if (applyTelemetryPitchAndRoll && attitudeFresh)
        {
            arcGisPitch += -pitch * Mathf.Rad2Deg;
            arcGisRoll += roll * Mathf.Rad2Deg;
        }

        smoothedArcGisHeading =
            MakeHeadingSafe(smoothedArcGisHeading);

        if (!IsFinite(smoothedArcGisHeading) ||
            !IsFinite(arcGisPitch) ||
            !IsFinite(arcGisRoll))
        {
            return;
        }

        bool headingChanged =
            !hasWrittenArcGisRotation ||
            Mathf.Abs(
                Mathf.DeltaAngle(
                    lastWrittenHeading,
                    smoothedArcGisHeading
                )
            ) >= rotationWriteThresholdDegrees;

        bool pitchChanged =
            !hasWrittenArcGisRotation ||
            Mathf.Abs(lastWrittenPitch - arcGisPitch) >= 0.01f;

        bool rollChanged =
            !hasWrittenArcGisRotation ||
            Mathf.Abs(lastWrittenRoll - arcGisRoll) >= 0.01f;

        if (!headingChanged && !pitchChanged && !rollChanged)
        {
            return;
        }

        locationComponent.Rotation = new ArcGISRotation(
            smoothedArcGisHeading,
            arcGisPitch,
            arcGisRoll
        );

        lastWrittenHeading = smoothedArcGisHeading;
        lastWrittenPitch = arcGisPitch;
        lastWrittenRoll = arcGisRoll;
        hasWrittenArcGisRotation = true;
    }

    float MakeHeadingSafe(float heading)
    {
        float normalized = Mathf.Repeat(heading, 360f);
        float epsilon = Mathf.Max(0.001f, zeroHeadingSafetyEpsilon);

        if (normalized < epsilon ||
            normalized > 360f - epsilon)
        {
            return epsilon;
        }

        return normalized;
    }

    void HandleMessage(MAVLink.MAVLinkMessage msg)
    {
        if (msg != null)
        {
            if (msg.sysid != 0)
                vehicleSystemId = msg.sysid;
            if (msg.compid != 0)
                vehicleComponentId = msg.compid;
        }

        // =========================================================
        // GPS POSITION
        // =========================================================

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT)
        {
            var gps = (MAVLink.mavlink_global_position_int_t)msg.data;

            double lat = gps.lat / 1e7;
            double lon = gps.lon / 1e7;
            double alt = gps.alt / 1000.0;

            if (!IsValidGeo(lat, lon, alt))
            {
                if (!hasLoggedInvalidGps)
                {
                    Debug.LogWarning(
                        $"⚠️ Invalid GPS: {lat}, {lon}, {alt}"
                    );

                    hasLoggedInvalidGps = true;
                }

                return;
            }

            hasLoggedInvalidGps = false;

            targetLat = lat;
            targetLon = lon;
            targetAlt = alt;
            targetRelativeAlt = gps.relative_alt * 0.001f;
            lastGpsPacketTime = Time.time;
            lastPositionTime = Time.time;
            hasLoggedPositionStreamRequest = false;
            TrySyncHomeLocation();


            float rawNorthMps = gps.vx * 0.01f;
            float rawEastMps = gps.vy * 0.01f;
            float rawDownMps = gps.vz * 0.01f;

            float derivedNorthMps = 0f;
            float derivedEastMps = 0f;
            float derivedDownMps = 0f;
            bool hasDerivedVel = false;
            bool hasGpsMotion = false;

            if (hasPrevGpsSample)
            {
                float sampleDt = Time.time - prevGpsSampleTime;
                if (sampleDt > 0.02f)
                {
                    double metersPerDegLat = 111320.0;
                    double cosLat = System.Math.Cos(lat * System.Math.PI / 180.0);
                    double metersPerDegLon = 111320.0 * System.Math.Max(0.0001, cosLat);

                    derivedNorthMps = (float)(((lat - prevLat) * metersPerDegLat) / sampleDt);
                    derivedEastMps = (float)(((lon - prevLon) * metersPerDegLon) / sampleDt);
                    derivedDownMps = (float)(-(alt - prevAlt) / sampleDt);
                    hasDerivedVel = true;

                    float deltaNorthM = (float)((lat - prevLat) * metersPerDegLat);
                    float deltaEastM = (float)((lon - prevLon) * metersPerDegLon);
                    float deltaUpM = (float)(alt - prevAlt);
                    hasGpsMotion =
                        (deltaNorthM * deltaNorthM +
                         deltaEastM * deltaEastM +
                         deltaUpM * deltaUpM) > 0.0025f;
                }
            }

            float rawSpeed = Mathf.Sqrt(rawNorthMps * rawNorthMps + rawEastMps * rawEastMps + rawDownMps * rawDownMps);
            float derivedSpeed = Mathf.Sqrt(derivedNorthMps * derivedNorthMps + derivedEastMps * derivedEastMps + derivedDownMps * derivedDownMps);

            float useNorthMps = rawNorthMps;
            float useEastMps = rawEastMps;
            float useDownMps = rawDownMps;

            if (hasDerivedVel && rawSpeed < staleSpeedThreshold && derivedSpeed > staleSpeedThreshold)
            {
                useNorthMps = derivedNorthMps;
                useEastMps = derivedEastMps;
                useDownMps = derivedDownMps;
            }
            else if (!hasGpsMotion &&
                     rawSpeed < staleSpeedThreshold &&
                     (Time.time - lastNonZeroVelTime) <= coastSeconds)
            {
                useNorthMps = lastNonZeroNorthMps;
                useEastMps = lastNonZeroEastMps;
                useDownMps = lastNonZeroDownMps;
            }

            velNorthMps = useNorthMps;
            velEastMps = useEastMps;
            velDownMps = useDownMps;

            float useSpeed = Mathf.Sqrt(useNorthMps * useNorthMps + useEastMps * useEastMps + useDownMps * useDownMps);
            if (useSpeed >= staleSpeedThreshold)
            {
                lastNonZeroVelTime = Time.time;
                lastNonZeroNorthMps = useNorthMps;
                lastNonZeroEastMps = useEastMps;
                lastNonZeroDownMps = useDownMps;
            }

            if (hasDerivedVel && rawSpeed < staleSpeedThreshold && derivedSpeed > staleSpeedThreshold)
            {
                debugVelocitySource = "derived";
            }
            else if (!hasGpsMotion &&
                     rawSpeed < staleSpeedThreshold &&
                     (Time.time - lastNonZeroVelTime) <= coastSeconds)
            {
                debugVelocitySource = "coast";
            }
            else
            {
                debugVelocitySource = "raw";
            }

            hasPrevGpsSample = true;
            prevLat = lat;
            prevLon = lon;
            prevAlt = alt;
            prevGpsSampleTime = Time.time;
            gpsMsgCount++;

            hasTargetPosition = true;
            CheckMapGeofence();
            CheckAltitudeLimit();

            // =====================================================
            // GPS HEADING
            // =====================================================

            if (gps.hdg != ushort.MaxValue)
            {
                headingDeg = gps.hdg / 100.0f;
                hasHeadingDeg = true;
                lastHeadingTime = Time.time;
            }

        }

        // =========================================================
        // ATTITUDE
        // =========================================================

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.ATTITUDE)
        {
            var att = (MAVLink.mavlink_attitude_t)msg.data;

            if (!IsFinite(att.roll) ||
                !IsFinite(att.pitch) ||
                !IsFinite(att.yaw))
            {
                if (!hasLoggedInvalidAttitude)
                {
                    Debug.LogWarning(
                        $"⚠️ Invalid ATTITUDE"
                    );

                    hasLoggedInvalidAttitude = true;
                }

                return;
            }

            hasLoggedInvalidAttitude = false;

            roll = att.roll;
            pitch = att.pitch;
            yaw = att.yaw;
            lastAttitudeTime = Time.time;
            debugAttitudeSource = "ATTITUDE";
            attMsgCount++;
        }

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.ATTITUDE_QUATERNION)
        {
            var attQ = (MAVLink.mavlink_attitude_quaternion_t)msg.data;

            if (!IsFinite(attQ.q1) || !IsFinite(attQ.q2) || !IsFinite(attQ.q3) || !IsFinite(attQ.q4))
                return;

            float w = attQ.q1;
            float x = attQ.q2;
            float y = attQ.q3;
            float z = attQ.q4;

            float sinrCosp = 2f * (w * x + y * z);
            float cosrCosp = 1f - 2f * (x * x + y * y);
            float newRoll = Mathf.Atan2(sinrCosp, cosrCosp);

            float sinp = 2f * (w * y - z * x);
            float newPitch = Mathf.Abs(sinp) >= 1f
                ? Mathf.Sign(sinp) * Mathf.PI * 0.5f
                : Mathf.Asin(sinp);

            float sinyCosp = 2f * (w * z + x * y);
            float cosyCosp = 1f - 2f * (y * y + z * z);
            float newYaw = Mathf.Atan2(sinyCosp, cosyCosp);

            if (IsFinite(newRoll) && IsFinite(newPitch) && IsFinite(newYaw))
            {
                roll = newRoll;
                pitch = newPitch;
                yaw = newYaw;
                lastAttitudeTime = Time.time;
                debugAttitudeSource = "ATT_QUAT";
                quatMsgCount++;
            }
        }

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.NAV_CONTROLLER_OUTPUT)
        {
            var nav = (MAVLink.mavlink_nav_controller_output_t)msg.data;
            navTargetDistM = nav.wp_dist;
            navTargetBearingDeg = nav.target_bearing;
            hasNavTargetDist = true;
        }

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_ACK)
        {
            var ack = (MAVLink.mavlink_command_ack_t)msg.data;
            lastCommandAckCommand = ack.command;
            lastCommandAckResult = ack.result;
            hasCommandAck = true;
            lastStatusText = $"CMD_ACK cmd={ack.command} result={ack.result}";

            if (ack.command == (ushort)MAVLink.MAV_CMD.DO_SET_HOME &&
                ack.result == (byte)MAVLink.MAV_RESULT.ACCEPTED)
            {
                hasConfirmedHomeSync = true;
            }
        }

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
        {
            var hb = (MAVLink.mavlink_heartbeat_t)msg.data;
            hbCustomMode = hb.custom_mode;
            hbSystemStatus = hb.system_status;
            hbArmed = (hb.base_mode & (byte)MAVLink.MAV_MODE_FLAG.SAFETY_ARMED) != 0;
            hbGuided = (hb.base_mode & (byte)MAVLink.MAV_MODE_FLAG.GUIDED_ENABLED) != 0;
            TrySyncHomeLocation();
            RequestPositionStream();
        }

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE)
        {
            var ex = (MAVLink.mavlink_extended_sys_state_t)msg.data;
            landedState = (MAVLink.MAV_LANDED_STATE)ex.landed_state;
        }

        if (msg.msgid == (uint)MAVLink.MAVLINK_MSG_ID.STATUSTEXT)
        {
            var st = (MAVLink.mavlink_statustext_t)msg.data;
            if (st.text != null && st.text.Length > 0)
            {
                int zero = System.Array.IndexOf(st.text, (byte)0);
                int len = zero >= 0 ? zero : st.text.Length;
                if (len > 0)
                {
                    lastStatusText = System.Text.Encoding.UTF8.GetString(st.text, 0, len);
                }
            }
        }
    }

    bool IsTogglePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(toggleDebugKey))
            return true;
#endif

        return false;
    }

    void ApplySmoothedTransform()
    {
        if (!hasTargetPosition || locationComponent == null)
            return;

        if (!hasSmoothedPosition)
        {
            smoothLat = targetLat;
            smoothLon = targetLon;
            smoothAlt = targetAlt;
            hasSmoothedPosition = true;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float smoothTime = Mathf.Max(0.01f, positionSmoothTime);

        float dataAge = Mathf.Max(0f, Time.time - lastGpsPacketTime);
        float forwardTime = extrapolationTime + Mathf.Min(dataAge, Mathf.Max(0f, maxExtrapolationGap));

        float useNorthMps = velNorthMps;
        float useEastMps = velEastMps;
        float useDownMps = velDownMps;
        usingGuidedAssist = false;

        double metersPerDegLat = 111320.0;
        double cosLat = System.Math.Cos(targetLat * System.Math.PI / 180.0);
        double metersPerDegLon = 111320.0 * System.Math.Max(0.0001, cosLat);

        float speed = Mathf.Sqrt(velNorthMps * velNorthMps + velEastMps * velEastMps + velDownMps * velDownMps);
        bool guidedStall =
            enableGuidedAssist &&
            hbGuided &&
            hbArmed &&
            hasNavTargetDist &&
            navTargetDistM > 20f &&
            speed < 0.5f;

        bool guidedStallForNudge =
            enableGuidedRepositionNudge &&
            hbGuided &&
            hbArmed &&
            hasNavTargetDist &&
            navTargetDistM > 20f &&
            speed < 0.3f;

        if (guidedStallForNudge)
        {
            guidedStallTimer += dt;
            if (guidedStallTimer >= guidedStallSecondsBeforeNudge &&
                (Time.time - lastGuidedNudgeTime) >= guidedNudgeIntervalSeconds)
            {
                SendGuidedRepositionNudge();
                lastGuidedNudgeTime = Time.time;
                guidedStallTimer = 0f;
            }
        }
        else
        {
            guidedStallTimer = 0f;
        }

        if (guidedStall)
        {
            float assistSpeed = Mathf.Clamp(navTargetDistM * 0.06f, 2.5f, 8.0f);
            float brgRad = navTargetBearingDeg * Mathf.Deg2Rad;
            useNorthMps = Mathf.Cos(brgRad) * assistSpeed;
            useEastMps = Mathf.Sin(brgRad) * assistSpeed;
            useDownMps = 0f;
            usingGuidedAssist = true;
        }

        double predictedLat = targetLat + (useNorthMps * forwardTime) / metersPerDegLat;
        double predictedLon = targetLon + (useEastMps * forwardTime) / metersPerDegLon;
        double predictedAlt = targetAlt - (useDownMps * forwardTime);

        if (usingGuidedAssist)
        {
            double cosSmoothLat = System.Math.Cos(smoothLat * System.Math.PI / 180.0);
            double metersPerDegLonSmooth = 111320.0 * System.Math.Max(0.0001, cosSmoothLat);
            smoothLat += (useNorthMps * dt) / metersPerDegLat;
            smoothLon += (useEastMps * dt) / metersPerDegLonSmooth;
            float guidedSmoothT = 1f - Mathf.Exp(-positionSmoothSpeed * dt);
            smoothAlt = Mathf.Lerp((float)smoothAlt, (float)predictedAlt, guidedSmoothT);
        }
        else
        {
            // Keep latitude and longitude as doubles. Converting coordinates
            // around 13 / 100 degrees to float causes metre-sized quantisation
            // and visible jitter.
            double blend = 1.0 - System.Math.Exp(
                -dt / System.Math.Max(0.01, smoothTime)
            );

            smoothLat += (predictedLat - smoothLat) * blend;
            smoothLon += (predictedLon - smoothLon) * blend;
            smoothAlt += (predictedAlt - smoothAlt) * blend;
        }

    double dLatM = (predictedLat - smoothLat) * metersPerDegLat;
    double dLonM = (predictedLon - smoothLon) * metersPerDegLon;
    double dAltM = predictedAlt - smoothAlt;
    double distM = System.Math.Sqrt(dLatM * dLatM + dLonM * dLonM + dAltM * dAltM);

    bool nearlyStopped = speed < 0.05f;

    if (nearlyStopped && distM <= snapDistanceMeters)
    {
        smoothLat = predictedLat;
        smoothLon = predictedLon;
        smoothAlt = predictedAlt;
    }

    ClampToPlayableMap(ref smoothLat, ref smoothLon);

    if (!IsValidGeo((double)smoothLat, (double)smoothLon, (double)smoothAlt))
    {
        return;
    }

    if (!hasAppliedLocation &&
        System.Math.Abs(smoothLat) < 0.000001 &&
        System.Math.Abs(smoothLon) < 0.000001 &&
        System.Math.Abs(smoothAlt) < 0.0001)
    {
        return;
    }

    if (WouldHitBuilding(smoothLat, smoothLon, smoothAlt))
    {
        RejectBuildingMovement();
        return;
    }

    locationComponent.Position = new ArcGISPoint(
        smoothLon,
        smoothLat,
        smoothAlt,
        ArcGISSpatialReference.WGS84()
    );
    hasAppliedLocation = true;
    hasLastSafePosition = true;
    lastSafeLat = smoothLat;
    lastSafeLon = smoothLon;
    lastSafeAlt = smoothAlt;
    lastSafeWorldPosition = locationComponent.transform.position;

    if (buildingCollisionTriggered)
    {
        double horizontalClearanceMeters = CalculateDistanceMeters(
            buildingCollisionContactLat,
            buildingCollisionContactLon,
            smoothLat,
            smoothLon
        );
        double verticalClearanceMeters =
            smoothAlt - buildingCollisionContactAlt;
        double totalClearanceMeters = System.Math.Sqrt(
            horizontalClearanceMeters * horizontalClearanceMeters +
            verticalClearanceMeters * verticalClearanceMeters
        );

        if (totalClearanceMeters >= System.Math.Max(
                0.25,
                droneCollisionRadiusMeters))
        {
            buildingCollisionTriggered = false;
            buildingGuidedHoldSent = false;
        }
    }
}

    void SendGuidedRepositionNudge()
    {
        if (client == null || endPoint == null || txParser == null || !hasNavTargetDist)
            return;

        float brgRad = navTargetBearingDeg * Mathf.Deg2Rad;
        float nudgeSpeed = Mathf.Clamp(navTargetDistM * 0.02f, 1.5f, 4.0f);
        float velN = Mathf.Cos(brgRad) * nudgeSpeed;
        float velE = Mathf.Sin(brgRad) * nudgeSpeed;

        const ushort velocityOnlyTypeMask =
            (1 << 0) | (1 << 1) | (1 << 2) |
            (1 << 6) | (1 << 7) | (1 << 8) |
            (1 << 10) | (1 << 11);

        var setVel = new MAVLink.mavlink_set_position_target_global_int_t(
            (uint)(Time.time * 1000f),
            (int)(targetLat * 1e7),
            (int)(targetLon * 1e7),
            targetRelativeAlt,
            velN, velE, 0f,
            0f, 0f, 0f,
            0f, 0f,
            velocityOnlyTypeMask,
            vehicleSystemId,
            vehicleComponentId,
            (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT_INT
        );

        byte[] setVelPacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.SET_POSITION_TARGET_GLOBAL_INT,
            setVel,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );
        SendToVehicle(setVelPacket);

        double metersPerDegLat = 111320.0;
        double cosLat = System.Math.Cos(targetLat * System.Math.PI / 180.0);
        double metersPerDegLon = 111320.0 * System.Math.Max(0.0001, cosLat);
        double northM = System.Math.Cos(brgRad) * navTargetDistM;
        double eastM = System.Math.Sin(brgRad) * navTargetDistM;
        int cmdLatE7 = (int)((targetLat + northM / metersPerDegLat) * 1e7);
        int cmdLonE7 = (int)((targetLon + eastM / metersPerDegLon) * 1e7);

        var cmdInt = new MAVLink.mavlink_command_int_t(
            nudgeSpeed,
            0f,
            0f,
            navTargetBearingDeg,
            cmdLatE7,
            cmdLonE7,
            targetRelativeAlt,
            (ushort)MAVLink.MAV_CMD.DO_REPOSITION,
            vehicleSystemId,
            vehicleComponentId,
            (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT,
            0,
            0
        );

        byte[] cmdIntPacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.COMMAND_INT,
            cmdInt,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        SendToVehicle(cmdIntPacket);

        var cmdLong = new MAVLink.mavlink_command_long_t(
            nudgeSpeed,
            0f,
            0f,
            navTargetBearingDeg,
            (float)(cmdLatE7 / 1e7),
            (float)(cmdLonE7 / 1e7),
            targetRelativeAlt,
            (ushort)MAVLink.MAV_CMD.DO_REPOSITION,
            vehicleSystemId,
            vehicleComponentId,
            0
        );

        byte[] cmdLongPacket = txParser.GenerateMAVLinkPacket20(
            MAVLink.MAVLINK_MSG_ID.COMMAND_LONG,
            cmdLong,
            false,
            255,
            (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER
        );

        SendToVehicle(cmdLongPacket);
        lastStatusText = $"Guided nudge: sys={vehicleSystemId} comp={vehicleComponentId} SET_VEL+CMD_INT+CMD_LONG";
    }

    void OnGUI()
    {
        if (!showDebugOverlay || !enabled)
            return;

        if (debugSb == null)
            debugSb = new StringBuilder(512);

        float now = Time.time;
        float gpsAge = hasTargetPosition ? now - lastPositionTime : -1f;
        float attAge = lastAttitudeTime > 0f ? now - lastAttitudeTime : -1f;
        float linkAge = lastAnyPacketTime > 0f ? now - lastAnyPacketTime : -1f;
        float speed = Mathf.Sqrt(velNorthMps * velNorthMps + velEastMps * velEastMps + velDownMps * velDownMps);

        debugSb.Length = 0;
        debugSb.Append("MAVLink Debug (F8)\n");
        debugSb.Append("active receiver: ").Append(Active == this ? "yes" : "no").Append('\n');
        debugSb.Append("packet age: ").Append(linkAge.ToString("F2")).Append(" s\n");
        debugSb.Append("gps age: ").Append(gpsAge.ToString("F2")).Append(" s\n");
        debugSb.Append("att age: ").Append(attAge.ToString("F2")).Append(" s\n");
        debugSb.Append("speed source: ").Append(debugVelocitySource).Append('\n');
        if (usingGuidedAssist)
        {
            debugSb.Append("guided assist: ON\n");
        }
        debugSb.Append("speed N/E/D: ")
            .Append(velNorthMps.ToString("F2")).Append(" / ")
            .Append(velEastMps.ToString("F2")).Append(" / ")
            .Append(velDownMps.ToString("F2")).Append(" m/s\n");
        debugSb.Append("speed mag: ").Append(speed.ToString("F2")).Append(" m/s\n");
        debugSb.Append("yaw src: ").Append(debugAttitudeSource).Append('\n');
        debugSb.Append("yaw(att): ").Append((yaw * Mathf.Rad2Deg).ToString("F1")).Append(" deg\n");
        debugSb.Append("heading(gps): ").Append(hasHeadingDeg ? headingDeg.ToString("F1") : "N/A").Append(" deg\n");
        debugSb.Append("heading(applied): ").Append(hasSmoothedHeading ? smoothedArcGisHeading.ToString("F1") : "N/A").Append(" deg\n");
        debugSb.Append("target dist(nav): ").Append(hasNavTargetDist ? navTargetDistM.ToString("F1") : "N/A").Append(" m\n");
        debugSb.Append("target brg(nav): ").Append(hasNavTargetDist ? navTargetBearingDeg.ToString("F1") : "N/A").Append(" deg\n");
        debugSb.Append("armed/guided: ").Append(hbArmed ? "Y" : "N").Append(" / ").Append(hbGuided ? "Y" : "N").Append('\n');
        debugSb.Append("custom mode: ").Append(hbCustomMode).Append(" (").Append(GetCopterModeName(hbCustomMode)).Append(")\n");
        if (enforceMapGeofence)
        {
            debugSb.Append("map boundary: ")
                .Append(mapGeofenceDistanceMeters.ToString("F1"))
                .Append("m / ")
                .Append(mapGeofenceTriggered ? "BRAKE" : "clear")
                .Append('\n');
        }
        if (enforceMaximumAltitude)
        {
            debugSb.Append("altitude limit: ")
                .Append(targetRelativeAlt.ToString("F1"))
                .Append("m / ")
                .Append(maximumRelativeAltitudeMeters.ToString("F1"))
                .Append("m / ")
                .Append(altitudeLimitTriggered ? "BRAKE" : "clear")
                .Append('\n');
        }
        debugSb.Append("landed: ").Append(landedState.ToString()).Append('\n');
        debugSb.Append("sys status: ").Append(hbSystemStatus).Append('\n');
        if (hasCommandAck)
        {
            debugSb.Append("cmd ack: cmd=").Append(lastCommandAckCommand).Append(" res=").Append(lastCommandAckResult).Append('\n');
        }
        debugSb.Append("msg count gps/att/quat: ")
            .Append(gpsMsgCount).Append(" / ")
            .Append(attMsgCount).Append(" / ")
            .Append(quatMsgCount).Append('\n');
        debugSb.Append("last status: ").Append(string.IsNullOrEmpty(lastStatusText) ? "-" : lastStatusText);

        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.Box(new Rect(12, 12, 390, 345), GUIContent.none);
        GUI.color = Color.white;
        GUI.Label(new Rect(22, 20, 370, 330), debugSb.ToString());
    }
    void OnDisable()
    {
        if (client != null)
        {
            client.Close();
            client = null;
        }

        if (Active == this)
        {
            Active = null;
        }
    }

    bool IsFinite(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    bool IsFinite(double value)
    {
        return !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }

    bool IsValidGeo(double lat, double lon, double alt)
    {
        if (!IsFinite(lat) ||
            !IsFinite(lon) ||
            !IsFinite(alt))
            return false;

        if (lat < -90 || lat > 90)
            return false;

        if (lon < -180 || lon > 180)
            return false;

        if (Mathf.Abs((float)lat) < 0.000001f &&
            Mathf.Abs((float)lon) < 0.000001f)
            return false;

        return true;
    }

    void OnValidate()
    {
        udpListenPort = Mathf.Clamp(udpListenPort, 1, 65535);
        udpReconnectSeconds = Mathf.Max(0.1f, udpReconnectSeconds);
        requestedPositionRateHz = Mathf.Max(1f, requestedPositionRateHz);
        headingSmoothTime = Mathf.Max(0.01f, headingSmoothTime);
        maximumHeadingSpeed = Mathf.Max(1f, maximumHeadingSpeed);
        headingDeadZoneDegrees = Mathf.Max(0f, headingDeadZoneDegrees);
        zeroHeadingSafetyEpsilon =
            Mathf.Max(0.001f, zeroHeadingSafetyEpsilon);
        rotationWriteThresholdDegrees =
            Mathf.Max(0f, rotationWriteThresholdDegrees);
        fallbackGeofenceRadiusMeters =
            Mathf.Max(1f, fallbackGeofenceRadiusMeters);
        geofenceBufferMeters = Mathf.Max(0f, geofenceBufferMeters);
        geofenceCommandIntervalSeconds =
            Mathf.Max(0.2f, geofenceCommandIntervalSeconds);
        droneCollisionRadiusMeters =
            Mathf.Max(0.1f, droneCollisionRadiusMeters);
        buildingCollisionRetreatMeters =
            Mathf.Max(0f, buildingCollisionRetreatMeters);
    }
}
