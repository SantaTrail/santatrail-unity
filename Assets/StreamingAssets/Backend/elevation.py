def generate_elevation_grid_fast(bbox, grid_size=25):
    import json
    import random
    from urllib import request

    south, north = bbox["south"], bbox["north"]
    west, east = bbox["west"], bbox["east"]

    lat_step = (north - south) / grid_size
    lon_step = (east - west) / grid_size

    locations = []

    for i in range(grid_size):
        for j in range(grid_size):
            lat = south + i * lat_step
            lon = west + j * lon_step

            locations.append({
                "latitude": lat,
                "longitude": lon
            })

    url = "https://api.open-elevation.com/api/v1/lookup"

    try:
        chunk_size = 100
        results = []

        for i in range(0, len(locations), chunk_size):
            chunk = locations[i:i+chunk_size]

            payload = json.dumps({"locations": chunk}).encode("utf-8")
            req = request.Request(
                url,
                data=payload,
                headers={"Content-Type": "application/json"},
                method="POST"
            )

            with request.urlopen(req, timeout=4) as res:
                if getattr(res, "status", 200) != 200:
                    raise Exception("Bad response")

                data = json.loads(res.read().decode("utf-8"))

            if "results" not in data:
                raise Exception("Bad response")

            results.extend(data["results"])

        grid = []
        idx = 0

        for i in range(grid_size):
            row = []
            for j in range(grid_size):
                row.append(results[idx]["elevation"])
                idx += 1
            grid.append(row)

        print("✅ Elevation API success")
        return grid

    except Exception as e:
        print("⚠️ Elevation failed → using fallback:", e)

        base = random.uniform(50, 300)

        grid = []
        for i in range(grid_size):
            row = []
            for j in range(grid_size):
                noise = random.uniform(-20, 20)
                row.append(base + noise)
            grid.append(row)

        return grid
