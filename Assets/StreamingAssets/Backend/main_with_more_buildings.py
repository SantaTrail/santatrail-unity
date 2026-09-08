import runpy
import sys
from pathlib import Path


if __name__ == "__main__":
    backend_directory = Path(__file__).resolve().parent
    if str(backend_directory) not in sys.path:
        sys.path.insert(0, str(backend_directory))

    runpy.run_path(
        str(backend_directory / "main.py"),
        run_name="__main__"
    )
