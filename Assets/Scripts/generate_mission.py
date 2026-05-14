import json

def generate_empty_mission():
    # Default training location
    home_lat = 13.7563
    home_lon = 100.5018
    home_alt = 20

    mission = {
        "fileType": "Plan",
        "groundStation": "QGroundControl",
        "version": 1,

        "geoFence": {
            "circles": [],
            "polygons": [],
            "version": 1
        },

        "rallyPoints": {
            "points": [],
            "version": 1
        },

        "mission": {
            "cruiseSpeed": 15,
            "hoverSpeed": 5,
            "firmwareType": 12,
            "vehicleType": 2,
            "version": 2,

            "plannedHomePosition": [home_lat, home_lon, home_alt],

            "items": []
        }
    }

    with open("trainer_template.plan", "w") as f:
        json.dump(mission, f, indent=4)

    print("Empty training mission created!")

generate_empty_mission()