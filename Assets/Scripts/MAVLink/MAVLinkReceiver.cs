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

    public bool IsGuidedArmed => hbGuided && hbArmed;
    public bool IsGuidedMode => hbGuided;
    public bool IsArmed => hbArmed;
    public bool HasRecentTelemetry => lastAnyPacketTime > 0f && (Time.time - lastAnyPacketTime) <= 2f;
    public MAVLink.MAV_LANDED_STATE LandedState => landedState;
    public float RelativeAltitudeMeters => targetRelativeAlt;
    public bool HasNavTargetDistance => hasNavTargetDist;
    public float NavTargetDistanceMeters => navTargetDistM;
    public Vector3 VelocityNed => new Vector3(velNorthMps, velEastMps, velDownMps);
    public float HorizontalSpeedMps => Mathf.Sqrt(velNorthMps * velNorthMps + velEastMps * velEastMps);
    public float SpeedMps => Mathf.Sqrt(velNorthMps * velNorthMps + velEastMps * velEastMps + velDownMps * velDownMps);

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

    void Update()
    {
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

    locationComponent.Position = new ArcGISPoint(
        smoothLon,
        smoothLat,
        smoothAlt,
        ArcGISSpatialReference.WGS84()
    );
    hasAppliedLocation = true;
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
    }
}
