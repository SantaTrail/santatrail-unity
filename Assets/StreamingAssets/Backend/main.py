import json
import math
import os
import random
import sys

# Windows embedded Python may use cp1252 for stdout. The backend logs contain
# emoji, so replace unsupported characters instead of aborting level generation.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)

from parser import load_plan, extract_waypoints, create_bbox
from osm import query_osm
from elevation import generate_elevation_grid_fast
from features import compute_features, extract_rivers
from classifier import classify

import argparse

parser = argparse.ArgumentParser()
parser.add_argument("--custom-location", type=str, default=None)
parser.add_argument("--radius-meters", type=float, default=500.0)
parser.add_argument("--output-path", type=str, default=None)
args = parser.parse_args()

SCHEMA_VERSION = 2
META_FILE_NAME = "level.meta.json"
GEOMETRY_FILE_NAME = "level.geometry.json"
LEGACY_OUTPUT_FILE_NAME = "output.json"


def write_json_atomic(path, value):
    """Publish complete JSON without exposing a partially written file."""
    temporary_path = f"{path}.tmp.{os.getpid()}"

    try:
        with open(temporary_path, "w", encoding="utf-8") as output_file:
            json.dump(value, output_file)
            output_file.flush()
            os.fsync(output_file.fileno())

        os.replace(temporary_path, path)
    finally:
        if os.path.exists(temporary_path):
            os.remove(temporary_path)


def can_reuse_building_cache(path, current_waypoints, radius_meters):
    """Only retain a valid, non-empty cache for the same launch location."""
    if not os.path.exists(path) or not current_waypoints:
        return False

    try:
        with open(path, "r", encoding="utf-8") as cache_file:
            cached = json.load(cache_file)

        cached_waypoints = cached.get("waypoints")
        cached_buildings = cached.get("buildings")
        cached_elevation = cached.get("elevation_data")

        if not cached_waypoints or not cached_buildings or not cached_elevation:
            return False

        cached_origin = cached_waypoints[0]
        current_origin = current_waypoints[0]
        current_latitude = float(current_origin["lat"])
        latitude_delta = (
            float(cached_origin["lat"]) - current_latitude
        ) * 111320.0
        longitude_delta = (
            float(cached_origin["lon"]) - float(current_origin["lon"])
        ) * 111320.0 * math.cos(math.radians(current_latitude))
        distance_meters = math.hypot(latitude_delta, longitude_delta)

        # A cache hundreds of metres away can cover a substantially different
        # set of buildings even when both points fit inside the query radius.
        location_tolerance_meters = min(
            25.0,
            max(5.0, float(radius_meters) * 0.05)
        )
        return distance_meters <= location_tolerance_meters
    except (OSError, TypeError, ValueError, KeyError, json.JSONDecodeError):
        return False

def get_random_location():
    import random
    import time

    random.seed(time.time())

    regions = [
        (27.9881, 86.9250),
        (35.3606, 138.7274),
        (46.8523, -121.7603),
        (45.8326, 6.8652),
        (35.6762, 139.6503),
    ]

    lat, lon = random.choice(regions)

    lat += random.uniform(-0.1, 0.1)
    lon += random.uniform(-0.1, 0.1)

    print(f"🌍 NEW LOCATION: {lat}, {lon}")

    return lat, lon

def generate_sitl_plan(path):
    if args.custom_location:
        lat, lon, alt, heading = map(float, args.custom_location.split(","))
    else:
        lat, lon, alt = 13.8455, 100.5688, 10

    plan = {
        "fileType": "Plan",
        "groundStation": "QGroundControl",
        "mission": {
            "items": [
                {
                    "params": [0, 0, 0, 0, lat, lon, alt]
                }
            ]
        },
        "version": 1
    }

    with open(path, "w") as f:
        json.dump(plan, f, indent=2)

    print(f"📍 Using location: {lat}, {lon}")
def generate_random_plan(path):
    center_lat, center_lon = get_random_location()

    num_points = random.randint(3, 6)

    items = []

    for i in range(num_points):
        lat = center_lat + random.uniform(-0.02, 0.02)
        lon = center_lon + random.uniform(-0.02, 0.02)
        alt = random.randint(80, 120)

        items.append({
            "params": [0,0,0,0, lat, lon, alt]
        })

    plan = {
        "fileType": "Plan",
        "groundStation": "QGroundControl",
        "mission": {
            "items": items
        },
        "version": 1
    }

    with open(path, "w") as f:
        json.dump(plan, f, indent=2)

    print(f"🌍 Generated random plan at {center_lat}, {center_lon}")

def extract_roads(osm):
    roads = []

    for el in osm["elements"]:
        tags = el.get("tags", {})

        if "highway" not in tags or "geometry" not in el:
            continue

        coords = []
        for p in el["geometry"]:
            coords.append({
                "lat": p["lat"],
                "lon": p["lon"]
            })

        if len(coords) > 1:
            roads.append({
                "highway": tags["highway"],
                "points": coords
            })

    return roads



def run(plan_path):
    plan = load_plan(plan_path)

    waypoints = extract_waypoints(plan)
    bbox = create_bbox(waypoints, margin_meters=args.radius_meters)

    osm_data = query_osm(bbox)
    osm_query_succeeded = osm_data is not None
    if osm_data is None:
        osm_data = {"elements": []}
    elevation_grid = generate_elevation_grid_fast(bbox, grid_size=25)

    features = compute_features(osm_data, elevation_grid, bbox)
    scene = classify(features)
    rivers = extract_rivers(osm_data)
    roads = extract_roads(osm_data)
    buildings = []

    for el in osm_data["elements"]:
        if el["type"] == "way" and "building" in el.get("tags", {}):
            if "geometry" in el:
                coords = []

                for p in el["geometry"]:
                    coords.append({
                        "lat": p["lat"],
                        "lon": p["lon"]
                    })

                if len(coords) > 2:
                    buildings.append(coords)
    flat = [h for row in elevation_grid for h in row]
    road_count = len(roads)
    
    print("🏢 Buildings extracted:", len(buildings))
    print("🛣 Roads extracted:", road_count)

    buildings_wrapped = []

    for b in buildings:
        buildings_wrapped.append({
            "points": b
        })

    geometry = {
        "schema_version": SCHEMA_VERSION,
        "elevation_data": flat,
        "grid_size": len(elevation_grid),
        "rivers": rivers,
        "roads": roads,
        "buildings": buildings_wrapped,
    }

    meta = {
        "schema_version": SCHEMA_VERSION,
        "scene": scene,
        "bbox": bbox,
        "features": features,
        "generation_mode": {
            "terrain": "arcgis",
            "buildings": "osm",
            "manual_level": False,
        },
        "counts": {
            "building_count": len(buildings),
            "river_count": len(rivers),
            "road_count": road_count,
        },
        "waypoints": waypoints,
        "geometry_file": GEOMETRY_FILE_NAME,
    }

    result = dict(meta)
    result.update(geometry)

    return meta, geometry, result, osm_query_succeeded


if __name__ == "__main__":
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    os.makedirs(BASE_DIR, exist_ok=True)

    plan_path = os.path.join(BASE_DIR, "auto.plan")
    output_path = (
        os.path.abspath(args.output_path)
        if args.output_path
        else os.path.join(BASE_DIR, LEGACY_OUTPUT_FILE_NAME)
    )
    output_directory = os.path.dirname(output_path)
    os.makedirs(output_directory, exist_ok=True)

    if args.output_path:
        output_stem = os.path.splitext(os.path.basename(output_path))[0]
        meta_path = os.path.join(
            output_directory,
            f"{output_stem}.meta.json"
        )
        geometry_path = os.path.join(
            output_directory,
            f"{output_stem}.geometry.json"
        )
    else:
        meta_path = os.path.join(BASE_DIR, META_FILE_NAME)
        geometry_path = os.path.join(BASE_DIR, GEOMETRY_FILE_NAME)

    print("🛠 Generating SITL-aligned plan...")
    generate_sitl_plan(plan_path)

    if not os.path.exists(plan_path):
        print("❌ PLAN NOT CREATED")
        exit()

    print("✅ PLAN CREATED")

    meta, geometry, result, osm_query_succeeded = run(plan_path)

    if (
        (not osm_query_succeeded or not result.get("buildings"))
        and can_reuse_building_cache(
            output_path,
            result["waypoints"],
            args.radius_meters
        )
    ):
        print(
            "✅ OSM refresh returned no usable building footprints; "
            "preserving the last successful building cache"
        )
        sys.exit(0)

    # Metadata and geometry are committed first. output.json is the readiness
    # marker observed by Unity, so publish it last and only as an atomic rename.
    write_json_atomic(meta_path, meta)
    write_json_atomic(geometry_path, geometry)
    write_json_atomic(output_path, result)

    print(f"📁 Writing output to: {output_path}")
    print(f"📁 Writing metadata to: {meta_path}")
    print(f"📁 Writing geometry to: {geometry_path}")

    print("✅ DONE")
