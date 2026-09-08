def count_tag(osm_data, key, value=None):
    count = 0

    for el in osm_data["elements"]:
        tags = el.get("tags", {})

        if key in tags:
            if value is None or tags[key] == value:
                count += 1

    return count

def compute_features(osm, elevation_grid, bbox):
    area = compute_area_km2(bbox)

    building = count_tag(osm, "building")
    water = count_tag(osm, "natural", "water")
    forest = count_tag(osm, "landuse", "forest")
    wood = count_tag(osm, "natural", "wood")

    elevations = [h for row in elevation_grid for h in row]

    if len(elevations) == 0:
        elevation_range = 0
    else:
        elevation_range = max(elevations) - min(elevations)

    return {
        "elevation_range": elevation_range,
        "building_density": building / max(area, 1),
        "water_present": water > 0,
        "vegetation_density": (forest + wood) / max(area, 1e-6)
    }
def extract_rivers(osm):
    rivers = []

    for el in osm["elements"]:
        tags = el.get("tags", {})

        if tags.get("waterway") in ["river", "stream"]:
            if "geometry" in el:
                coords = [(p["lat"], p["lon"]) for p in el["geometry"]]
                rivers.append(coords)

    return rivers
import math

def compute_area_km2(bbox):
    lat1, lat2 = bbox["south"], bbox["north"]
    lon1, lon2 = bbox["west"], bbox["east"]

    lat_dist = (lat2 - lat1) * 111000
    lon_dist = (lon2 - lon1) * 111000 * math.cos(math.radians(lat1))

    return abs(lat_dist * lon_dist)