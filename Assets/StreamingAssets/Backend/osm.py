import json
import time
from urllib import error, request

def query_osm(bbox):
    """Return OSM data, or None when every endpoint failed.

    An empty elements list can be a valid response for an area with no mapped
    features.  None is reserved for a transport/server failure so callers can
    preserve a previously successful building cache instead of replacing it
    with an empty result.
    """
    urls = [
        "https://overpass.kumi.systems/api/interpreter",
        "https://overpass-api.de/api/interpreter"
    ]

    query = f"""
    [out:json][timeout:15];
    (
      way["building"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
      way["landuse"="residential"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
      way["highway"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
      way["natural"="water"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
    );
    out body geom;
    """

    building_query = f"""
    [out:json][timeout:15];
    way["building"]({bbox['south']},{bbox['west']},{bbox['north']},{bbox['east']});
    out body geom;
    """

    headers = {
        "User-Agent": "UnityTerrainBot/1.0",
        "Accept": "application/json"
    }

    def request_overpass(url, query_text, attempt_label):
        payload = query_text.encode("utf-8")
        req = request.Request(
            url,
            data=payload,
            headers=headers,
            method="POST"
        )

        print(f"🌍 Trying OSM: {url} ({attempt_label})")
        with request.urlopen(req, timeout=20) as res:
            return json.loads(res.read().decode("utf-8"))

    def has_building_footprints(data):
        return any(
            element.get("type") == "way"
            and "building" in element.get("tags", {})
            and len(element.get("geometry", [])) >= 3
            for element in data.get("elements", [])
        )

    best_result = None

    for url in urls:
        for attempt in range(2):
            try:
                data = request_overpass(
                    url,
                    query,
                    f"full map attempt {attempt + 1}/2"
                )
                print(f"✅ OSM success ({len(data.get('elements', []))} elements)")
                if has_building_footprints(data):
                    return data
                if data.get("elements"):
                    best_result = data
                    print("⚠️ Full OSM response had no building outlines; trying a focused building query")

            except error.HTTPError as e:
                print(f"⚠️ Failed: {url} | HTTP {e.code}")
            except Exception as e:
                print(f"⚠️ Failed: {url} | {e}")

            if attempt < 1:
                time.sleep(1.0 + attempt)

    # A combined buildings/roads/water query can time out on a busy Overpass
    # server even when local building ways are available. Retry just the
    # footprints so Unity can place houses on their real map polygons.
    for url in urls:
        try:
            data = request_overpass(url, building_query, "focused building query")
            if has_building_footprints(data):
                print(
                    "✅ OSM building fallback succeeded "
                    f"({len(data.get('elements', []))} elements)"
                )
                return data
            print(f"⚠️ No building outlines returned by {url}")
        except error.HTTPError as e:
            print(f"⚠️ Building query failed: {url} | HTTP {e.code}")
        except Exception as e:
            print(f"⚠️ Building query failed: {url} | {e}")

    if best_result is not None:
        return best_result

    print("❌ ALL OSM FAILED → preserving cached map data when available")
    return None
