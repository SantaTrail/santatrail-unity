import json
import time
from urllib import error, request

def query_osm(bbox):
    urls = [
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass-api.de/api/interpreter"
    ]

    query = f"""
    [out:json][timeout:12];
    (
      way["building"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
      way["landuse"="residential"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
      way["highway"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
      way["natural"="water"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
    );
    out body geom;
    """

    headers = {
        "User-Agent": "UnityTerrainBot/1.0",
        "Accept": "application/json"
    }

    payload = query.encode("utf-8")

    for url in urls:
        for attempt in range(2):
            try:
                print(
                    f"🌍 Trying OSM: {url} (attempt {attempt + 1}/2)"
                )

                req = request.Request(
                    url,
                    data=payload,
                    headers=headers,
                    method="POST"
                )

                with request.urlopen(req, timeout=8) as res:
                    data = json.loads(res.read().decode("utf-8"))
                    print(
                        f"✅ OSM success ({len(data.get('elements', []))} elements)"
                    )
                    return data

            except error.HTTPError as e:
                print(f"⚠️ Failed: {url} | HTTP {e.code}")
            except Exception as e:
                print(f"⚠️ Failed: {url} | {e}")

            if attempt < 1:
                time.sleep(1.0 + attempt)

    print("❌ ALL OSM FAILED → using empty data")
    return {"elements": []}
