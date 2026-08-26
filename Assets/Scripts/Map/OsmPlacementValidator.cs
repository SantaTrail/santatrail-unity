using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class OsmPlacementValidator : MonoBehaviour
{
    [Header("Building Clearance")]
    [Min(0f)]
    [Tooltip("Extra clearance around the final building footprint and a road centerline.")]
    [SerializeField] float roadClearanceMeters = 0.25f;

    [Min(0f)]
    [Tooltip("Extra clearance around the final building footprint and a river line.")]
    [SerializeField] float waterClearanceMeters = 0.5f;

    [Header("OSM Footprint Matching")]
    [Tooltip(
        "Keep each house at its exact OSM footprint even when OSM polygons " +
        "touch or overlap. Disable only when a simplified collision-safe map " +
        "is preferred over matching the map shadows."
    )]
    [SerializeField] bool useExactOsmFootprints = true;

    [Range(0.15f, 1f)]
    [Tooltip(
        "Smallest X/Z footprint scale allowed to keep adjacent OSM houses " +
        "from colliding. Buildings are only omitted when their real footprint " +
        "still collides at this scale."
    )]
    [SerializeField] float minimumConflictResolutionScale = 0.15f;

    readonly List<List<Vector3>> acceptedFootprints =
        new List<List<Vector3>>();

    struct GeographicPoint
    {
        public readonly double latitude;
        public readonly double longitude;

        public GeographicPoint(double latitude, double longitude)
        {
            this.latitude = latitude;
            this.longitude = longitude;
        }
    }

    public void ResetPlacements()
    {
        acceptedFootprints.Clear();
    }

    public bool CanPlaceBuilding(
        List<Vector3> worldFootprint,
        GPSPoint[] sourceFootprint,
        Result result,
        float requestedFootprintScale,
        out float placementFootprintScale,
        out string rejection)
    {
        rejection = null;
        placementFootprintScale = 1f;

        List<GeographicPoint> geographicFootprint =
            BuildGeographicFootprint(sourceFootprint);

        if (worldFootprint == null || worldFootprint.Count < 3 ||
            geographicFootprint.Count < 3)
        {
            rejection = "invalid footprint";
            return false;
        }

        if (useExactOsmFootprints)
        {
            placementFootprintScale = Mathf.Max(0.01f, requestedFootprintScale);
            return CanPlaceFootprint(
                worldFootprint,
                geographicFootprint,
                result,
                placementFootprintScale,
                out rejection
            );
        }

        float largestSafeScale = Mathf.Max(0.01f, requestedFootprintScale);
        float smallestAllowedScale = Mathf.Min(
            Mathf.Clamp(minimumConflictResolutionScale, 0.15f, 1f),
            largestSafeScale
        );

        for (float scale = largestSafeScale;
             scale >= smallestAllowedScale - 0.0001f;
             scale -= 0.025f)
        {
            if (CanPlaceFootprint(
                    worldFootprint,
                    geographicFootprint,
                    result,
                    scale,
                    out rejection))
            {
                placementFootprintScale =
                    Mathf.Max(smallestAllowedScale, scale);
                return true;
            }
        }

        return false;
    }

    bool CanPlaceFootprint(
        List<Vector3> worldFootprint,
        List<GeographicPoint> geographicFootprint,
        Result result,
        float footprintScale,
        out string rejection)
    {
        rejection = null;

        float safeFootprintScale = Mathf.Max(0.01f, footprintScale);
        List<Vector3> finalWorldFootprint =
            ScaleWorldFootprint(worldFootprint, safeFootprintScale);
        List<GeographicPoint> finalGeographicFootprint =
            ScaleGeographicFootprint(
                geographicFootprint,
                safeFootprintScale
            );

        if (IsFootprintNearRoad(
                finalGeographicFootprint,
                result != null ? result.roads : null))
        {
            rejection = "footprint is too close to a road";
            return false;
        }

        if (IsFootprintNearWater(
                finalGeographicFootprint,
                result != null ? result.rivers : null))
        {
            rejection = "footprint is too close to water";
            return false;
        }

        if (!useExactOsmFootprints)
        {
            for (int i = 0; i < acceptedFootprints.Count; i++)
            {
                if (PolygonsOverlapXZ(
                        finalWorldFootprint,
                        acceptedFootprints[i]))
                {
                    rejection = "footprint overlaps an existing building";
                    return false;
                }
            }
        }

        return true;
    }

    public void RegisterBuildingFootprint(
        List<Vector3> footprint,
        float footprintScale)
    {
        if (footprint == null || footprint.Count < 3)
        {
            return;
        }

        acceptedFootprints.Add(
            ScaleWorldFootprint(
                footprint,
                Mathf.Max(0.01f, footprintScale)
            )
        );
    }

    void OnValidate()
    {
        roadClearanceMeters = Mathf.Max(0f, roadClearanceMeters);
        waterClearanceMeters = Mathf.Max(0f, waterClearanceMeters);
        minimumConflictResolutionScale = Mathf.Clamp(
            minimumConflictResolutionScale,
            0.15f,
            1f
        );
    }

    List<GeographicPoint> BuildGeographicFootprint(GPSPoint[] sourcePoints)
    {
        List<GeographicPoint> footprint = new List<GeographicPoint>();
        if (sourcePoints == null)
        {
            return footprint;
        }

        for (int i = 0; i < sourcePoints.Length; i++)
        {
            GPSPoint point = sourcePoints[i];
            if (!IsValid(point.lat) || !IsValid(point.lon))
            {
                continue;
            }

            GeographicPoint geographicPoint =
                new GeographicPoint(point.lat, point.lon);
            if (footprint.Count > 0 &&
                IsSamePoint(footprint[footprint.Count - 1], geographicPoint))
            {
                continue;
            }

            footprint.Add(geographicPoint);
        }

        if (footprint.Count > 1 &&
            IsSamePoint(footprint[0], footprint[footprint.Count - 1]))
        {
            footprint.RemoveAt(footprint.Count - 1);
        }

        return footprint;
    }

    List<Vector3> ScaleWorldFootprint(
        List<Vector3> footprint,
        float scale)
    {
        Vector3 center = Vector3.zero;
        for (int i = 0; i < footprint.Count; i++)
        {
            center += footprint[i];
        }

        center /= footprint.Count;

        List<Vector3> scaledFootprint =
            new List<Vector3>(footprint.Count);
        for (int i = 0; i < footprint.Count; i++)
        {
            Vector3 point = footprint[i];
            scaledFootprint.Add(new Vector3(
                center.x + (point.x - center.x) * scale,
                point.y,
                center.z + (point.z - center.z) * scale
            ));
        }

        return scaledFootprint;
    }

    List<GeographicPoint> ScaleGeographicFootprint(
        List<GeographicPoint> footprint,
        float scale)
    {
        double latitude = 0.0;
        double longitude = 0.0;
        for (int i = 0; i < footprint.Count; i++)
        {
            latitude += footprint[i].latitude;
            longitude += footprint[i].longitude;
        }

        latitude /= footprint.Count;
        longitude /= footprint.Count;

        List<GeographicPoint> scaledFootprint =
            new List<GeographicPoint>(footprint.Count);
        for (int i = 0; i < footprint.Count; i++)
        {
            GeographicPoint point = footprint[i];
            scaledFootprint.Add(new GeographicPoint(
                latitude + (point.latitude - latitude) * scale,
                longitude + (point.longitude - longitude) * scale
            ));
        }

        return scaledFootprint;
    }

    bool IsFootprintNearRoad(
        List<GeographicPoint> footprint,
        Road[] roads)
    {
        if (roads == null || roadClearanceMeters <= 0f)
        {
            return false;
        }

        for (int i = 0; i < roads.Length; i++)
        {
            Road road = roads[i];
            if (road == null || road.points == null || road.points.Length < 2)
            {
                continue;
            }

            List<GeographicPoint> line =
                BuildGeographicFootprint(road.points);
            if (IsPolygonNearLine(footprint, line, roadClearanceMeters))
            {
                return true;
            }
        }

        return false;
    }

    bool IsFootprintNearWater(
        List<GeographicPoint> footprint,
        GPSWaypoint[][] rivers)
    {
        if (rivers == null || waterClearanceMeters <= 0f)
        {
            return false;
        }

        for (int riverIndex = 0; riverIndex < rivers.Length; riverIndex++)
        {
            GPSWaypoint[] river = rivers[riverIndex];
            if (river == null || river.Length < 2)
            {
                continue;
            }

            List<GeographicPoint> line = new List<GeographicPoint>();
            for (int pointIndex = 0; pointIndex < river.Length; pointIndex++)
            {
                GPSWaypoint point = river[pointIndex];
                if (point == null || !IsValid(point.lat) || !IsValid(point.lon))
                {
                    continue;
                }

                GeographicPoint geographicPoint =
                    new GeographicPoint(point.lat, point.lon);
                if (line.Count == 0 ||
                    !IsSamePoint(line[line.Count - 1], geographicPoint))
                {
                    line.Add(geographicPoint);
                }
            }

            if (IsPolygonNearLine(footprint, line, waterClearanceMeters))
            {
                return true;
            }
        }

        return false;
    }

    bool IsPolygonNearLine(
        List<GeographicPoint> polygon,
        List<GeographicPoint> line,
        float clearanceMeters)
    {
        if (polygon == null || polygon.Count < 3 ||
            line == null || line.Count < 2)
        {
            return false;
        }

        double referenceLatitude = polygon[0].latitude;
        double referenceLongitude = polygon[0].longitude;
        List<Vector2> localPolygon =
            ToLocalMeters(polygon, referenceLatitude, referenceLongitude);
        List<Vector2> localLine =
            ToLocalMeters(line, referenceLatitude, referenceLongitude);
        float clearanceSqr = clearanceMeters * clearanceMeters;

        for (int lineIndex = 0; lineIndex < localLine.Count - 1; lineIndex++)
        {
            Vector2 lineStart = localLine[lineIndex];
            Vector2 lineEnd = localLine[lineIndex + 1];

            if (IsPointInsidePolygon(lineStart, localPolygon) ||
                IsPointInsidePolygon(lineEnd, localPolygon))
            {
                return true;
            }

            for (int polygonIndex = 0;
                 polygonIndex < localPolygon.Count;
                 polygonIndex++)
            {
                Vector2 polygonStart = localPolygon[polygonIndex];
                Vector2 polygonEnd = localPolygon[
                    (polygonIndex + 1) % localPolygon.Count
                ];

                if (SegmentSqrDistance(
                        lineStart,
                        lineEnd,
                        polygonStart,
                        polygonEnd) <= clearanceSqr)
                {
                    return true;
                }
            }
        }

        return false;
    }

    List<Vector2> ToLocalMeters(
        List<GeographicPoint> points,
        double referenceLatitude,
        double referenceLongitude)
    {
        const double metersPerDegreeLatitude = 111320.0;
        double metersPerDegreeLongitude =
            metersPerDegreeLatitude *
            System.Math.Cos(referenceLatitude * System.Math.PI / 180.0);
        List<Vector2> localPoints = new List<Vector2>(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            GeographicPoint point = points[i];
            localPoints.Add(new Vector2(
                (float)((point.longitude - referenceLongitude) *
                    metersPerDegreeLongitude),
                (float)((point.latitude - referenceLatitude) *
                    metersPerDegreeLatitude)
            ));
        }

        return localPoints;
    }

    bool PolygonsOverlapXZ(List<Vector3> first, List<Vector3> second)
    {
        if (first == null || second == null ||
            first.Count < 3 || second.Count < 3)
        {
            return false;
        }

        List<Vector2> firstPolygon = ToXZPolygon(first);
        List<Vector2> secondPolygon = ToXZPolygon(second);

        for (int firstIndex = 0; firstIndex < firstPolygon.Count; firstIndex++)
        {
            Vector2 firstStart = firstPolygon[firstIndex];
            Vector2 firstEnd = firstPolygon[
                (firstIndex + 1) % firstPolygon.Count
            ];

            for (int secondIndex = 0;
                 secondIndex < secondPolygon.Count;
                 secondIndex++)
            {
                Vector2 secondStart = secondPolygon[secondIndex];
                Vector2 secondEnd = secondPolygon[
                    (secondIndex + 1) % secondPolygon.Count
                ];

                if (SegmentsIntersect(
                        firstStart,
                        firstEnd,
                        secondStart,
                        secondEnd))
                {
                    return true;
                }
            }
        }

        return IsPointInsidePolygon(firstPolygon[0], secondPolygon) ||
               IsPointInsidePolygon(secondPolygon[0], firstPolygon);
    }

    List<Vector2> ToXZPolygon(List<Vector3> points)
    {
        List<Vector2> polygon = new List<Vector2>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            polygon.Add(new Vector2(points[i].x, points[i].z));
        }

        return polygon;
    }

    bool IsPointInsidePolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        for (int current = 0, previous = polygon.Count - 1;
             current < polygon.Count;
             previous = current++)
        {
            Vector2 currentPoint = polygon[current];
            Vector2 previousPoint = polygon[previous];

            if (IsPointOnSegment(point, previousPoint, currentPoint))
            {
                return true;
            }

            bool crossesRow =
                (currentPoint.y > point.y) != (previousPoint.y > point.y);
            if (crossesRow &&
                point.x < (previousPoint.x - currentPoint.x) *
                (point.y - currentPoint.y) /
                (previousPoint.y - currentPoint.y) + currentPoint.x)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    float SegmentSqrDistance(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd)
    {
        if (SegmentsIntersect(firstStart, firstEnd, secondStart, secondEnd))
        {
            return 0f;
        }

        return Mathf.Min(
            PointSegmentSqrDistance(firstStart, secondStart, secondEnd),
            PointSegmentSqrDistance(firstEnd, secondStart, secondEnd),
            PointSegmentSqrDistance(secondStart, firstStart, firstEnd),
            PointSegmentSqrDistance(secondEnd, firstStart, firstEnd)
        );
    }

    float PointSegmentSqrDistance(
        Vector2 point,
        Vector2 segmentStart,
        Vector2 segmentEnd)
    {
        Vector2 segment = segmentEnd - segmentStart;
        float segmentLengthSqr = segment.sqrMagnitude;
        if (segmentLengthSqr < 0.000001f)
        {
            return (point - segmentStart).sqrMagnitude;
        }

        float t = Mathf.Clamp01(
            Vector2.Dot(point - segmentStart, segment) / segmentLengthSqr
        );
        return (point - (segmentStart + segment * t)).sqrMagnitude;
    }

    bool SegmentsIntersect(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd)
    {
        const float epsilon = 0.0001f;
        float firstStartSide =
            Cross(firstEnd - firstStart, secondStart - firstStart);
        float firstEndSide =
            Cross(firstEnd - firstStart, secondEnd - firstStart);
        float secondStartSide =
            Cross(secondEnd - secondStart, firstStart - secondStart);
        float secondEndSide =
            Cross(secondEnd - secondStart, firstEnd - secondStart);

        if (((firstStartSide > epsilon && firstEndSide < -epsilon) ||
             (firstStartSide < -epsilon && firstEndSide > epsilon)) &&
            ((secondStartSide > epsilon && secondEndSide < -epsilon) ||
             (secondStartSide < -epsilon && secondEndSide > epsilon)))
        {
            return true;
        }

        return (Mathf.Abs(firstStartSide) <= epsilon &&
                IsPointOnSegment(secondStart, firstStart, firstEnd)) ||
               (Mathf.Abs(firstEndSide) <= epsilon &&
                IsPointOnSegment(secondEnd, firstStart, firstEnd)) ||
               (Mathf.Abs(secondStartSide) <= epsilon &&
                IsPointOnSegment(firstStart, secondStart, secondEnd)) ||
               (Mathf.Abs(secondEndSide) <= epsilon &&
                IsPointOnSegment(firstEnd, secondStart, secondEnd));
    }

    bool IsPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
    {
        const float epsilon = 0.0001f;
        if (Mathf.Abs(Cross(segmentEnd - segmentStart, point - segmentStart)) >
            epsilon)
        {
            return false;
        }

        return Vector2.Dot(
                   point - segmentStart,
                   point - segmentEnd
               ) <= epsilon;
    }

    bool IsSamePoint(GeographicPoint first, GeographicPoint second)
    {
        const double epsilon = 0.00000001;
        return System.Math.Abs(first.latitude - second.latitude) <= epsilon &&
               System.Math.Abs(first.longitude - second.longitude) <= epsilon;
    }

    bool IsValid(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    float Cross(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }
}
