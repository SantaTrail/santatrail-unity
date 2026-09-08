import json
import math

def load_plan(path):
    with open(path, "r") as f:
        content = f.read()
        print("FILE CONTENT:\n", content[:200])
        return json.loads(content)

def extract_waypoints(plan):
    items = plan["mission"]["items"]
    waypoints = []

    for item in items:
        params = item.get("params", [])

        if len(params) >= 7:
            lat = params[4]
            lon = params[5]
            alt = params[6]

            if lat and lon:
                waypoints.append({
                    "lat": lat,
                    "lon": lon,
                    "alt": alt
                })

    return waypoints

def create_bbox(waypoints, margin=0.001, margin_meters=None):
    lats = [w["lat"] for w in waypoints]
    lons = [w["lon"] for w in waypoints]

    if margin_meters is not None and margin_meters > 0:
        center_lat = sum(lats) / len(lats)
        center_lon = sum(lons) / len(lons)

        meters_per_degree_lat = 111320.0
        meters_per_degree_lon = meters_per_degree_lat * math.cos(
            center_lat * math.pi / 180.0
        )

        lat_margin = margin_meters / meters_per_degree_lat
        lon_margin = margin_meters / max(1e-9, meters_per_degree_lon)

        return {
            "south": min(lats) - lat_margin,
            "north": max(lats) + lat_margin,
            "west": min(lons) - lon_margin,
            "east": max(lons) + lon_margin
        }

    return {
        "south": min(lats) - margin,
        "north": max(lats) + margin,
        "west": min(lons) - margin,
        "east": max(lons) + margin
    }
