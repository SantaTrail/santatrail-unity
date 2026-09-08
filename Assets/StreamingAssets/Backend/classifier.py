def classify(f):
    if f["building_density"] > 5000:
        return "urban"

    if f["elevation_range"] > 150 and f["vegetation_density"] > 50:
        return "mountain_forest"

    if f["water_present"] and f["elevation_range"] < 50:
        return "river_plain"

    if f["vegetation_density"] > 50:
        return "forest"

    if f["elevation_range"] > 120:
        return "mountain"

    return "rural"