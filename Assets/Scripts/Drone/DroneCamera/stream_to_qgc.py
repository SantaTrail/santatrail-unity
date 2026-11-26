import socket
import struct
import subprocess
import time
import signal

UDP_IP = "0.0.0.0"
UDP_PORT = 5005

WIDTH = 320
HEIGHT = 240
FRAME_SIZE = WIDTH * HEIGHT * 3

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind((UDP_IP, UDP_PORT))
sock.settimeout(0.5)

ffmpeg = subprocess.Popen([
    'ffmpeg',

    '-loglevel', 'error',
    '-nostats',

    '-fflags', 'nobuffer',
    '-flags', 'low_delay',

    '-f', 'rawvideo',
    '-pix_fmt', 'rgb24',
    '-s', f'{WIDTH}x{HEIGHT}',
    '-r', '20',
    '-i', 'pipe:0',

    '-an',

    # Unity sends RGB24 pixels with the image upside down for the QGC view.
    '-vf', 'vflip',

    '-c:v', 'libx264',
    '-preset', 'ultrafast',
    '-tune', 'zerolatency',

    '-pix_fmt', 'yuv420p',
    '-profile:v', 'baseline',
    '-level', '3.0',

    # 🔥 CRITICAL FIXES
    '-g', '20',
    '-keyint_min', '20',
    '-sc_threshold', '0',
    '-bf', '0',
    '-refs', '1',

    # 🔥 FORCE HEADERS
    '-x264opts', 'repeat-headers=1:aud=1',

    '-muxdelay', '0',
    '-muxpreload', '0',

    '-f', 'mpegts',
    'udp://127.0.0.1:5600?pkt_size=1316&fifo_size=1000000&overrun_nonfatal=1'
], stdin=subprocess.PIPE)

print("Listening for chunked frames...")

frames = {}
running = True
last_encoded_at = 0.0

def stop_bridge(_signal, _frame):
    global running
    running = False

signal.signal(signal.SIGTERM, stop_bridge)
signal.signal(signal.SIGINT, stop_bridge)

while running:
    try:
        data, _ = sock.recvfrom(65536)

        # HEADER (8 bytes)
        frame_id, total_chunks, chunk_idx = struct.unpack('IHH', data[:8])
        chunk_data = data[8:]

        if frame_id not in frames:
            frames[frame_id] = {
                "chunks": {},
                "total": total_chunks,
                "time": time.time()
            }

        frames[frame_id]["chunks"][chunk_idx] = chunk_data

        # ✅ If full frame received
        if len(frames[frame_id]["chunks"]) == total_chunks:
            chunks = frames[frame_id]["chunks"]

            full_data = b''.join(chunks[i] for i in range(total_chunks))

            if len(full_data) == FRAME_SIZE:
                # Unity already sends tightly packed RGB24; FFmpeg flips and
                # encodes it directly, so this bridge needs only Python stdlib.
                frame_time = time.monotonic()
                if frame_time - last_encoded_at >= 1.0 / 20.0:
                    ffmpeg.stdin.write(full_data)
                    last_encoded_at = frame_time
            else:
                print("Frame size mismatch")

            del frames[frame_id]

        # 🧹 Cleanup old frames (packet loss protection)
        now = time.time()
        for fid in list(frames.keys()):
            if now - frames[fid]["time"] > 1:
                del frames[fid]

    except socket.timeout:
        continue
    except Exception as e:
        print("Error:", e)

sock.close()
try:
    ffmpeg.stdin.close()
    ffmpeg.wait(timeout=3)
except Exception:
    ffmpeg.kill()
