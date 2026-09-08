import json
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
args = parser.parse_args()

SCHEMA_VERSION = 2
META_FILE_NAME = "level.meta.json"
GEOMETRY_FILE_NAME = "level.geometry.json"
LEGACY_OUTPUT_FILE_NAME = "output.json"

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

    return meta, geometry, result


if __name__ == "__main__":
    import os

    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    os.makedirs(BASE_DIR, exist_ok=True)

    plan_path = os.path.join(BASE_DIR, "auto.plan")
    output_path = os.path.join(BASE_DIR, LEGACY_OUTPUT_FILE_NAME)

    if os.path.exists(output_path):
        os.remove(output_path)

    meta_path = os.path.join(BASE_DIR, META_FILE_NAME)
    geometry_path = os.path.join(BASE_DIR, GEOMETRY_FILE_NAME)

    for path in (meta_path, geometry_path):
        if os.path.exists(path):
            os.remove(path)

    print("🛠 Generating SITL-aligned plan...")
    generate_sitl_plan(plan_path)

    if not os.path.exists(plan_path):
        print("❌ PLAN NOT CREATED")
        exit()

    print("✅ PLAN CREATED")

    meta, geometry, result = run(plan_path)

    with open(meta_path, "w") as f:
        json.dump(meta, f)

    with open(geometry_path, "w") as f:
        json.dump(geometry, f)

    with open(output_path, "w") as f:
        json.dump(result, f)

    print(f"📁 Writing output to: {output_path}")
    print(f"📁 Writing metadata to: {meta_path}")
    print(f"📁 Writing geometry to: {geometry_path}")

    print("✅ DONE")
