import socket
import subprocess
import numpy as np
import cv2

UDP_IP = "0.0.0.0"
UDP_PORT = 5005

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind((UDP_IP, UDP_PORT))
sock.settimeout(5.0)

ffmpeg = subprocess.Popen([
    'ffmpeg',
    '-fflags', 'nobuffer',
    '-flags', 'low_delay',
    '-f', 'rawvideo',
    '-pix_fmt', 'bgr24',
    '-s', '320x240',
    '-r', '20',
    '-i', 'pipe:0',

    '-an',
    '-c:v', 'libx264',
    '-preset', 'ultrafast',
    '-tune', 'zerolatency',

    '-pix_fmt', 'yuv420p',
    '-profile:v', 'baseline',
    '-level', '3.0',

    '-g', '10',                 # ← better than 1 for stability
    '-keyint_min', '10',
    '-bf', '0',

    '-f', 'mpegts',
    'udp://127.0.0.1:5600?pkt_size=1316'
], stdin=subprocess.PIPE)

print("Listening on :5005, streaming MPEG-TS to QGC on :5600")
frame_count = 0

while True:
    try:
        data, _ = sock.recvfrom(65536)
        frame = np.frombuffer(data, dtype=np.uint8).reshape((240, 320, 3))
        frame = cv2.resize(frame, (320, 240))
        ffmpeg.stdin.write(frame.tobytes())
        ffmpeg.stdin.flush()
        frame_count += 1
        if frame_count % 20 == 0:
            print(f"Sent {frame_count} frames")
    except socket.timeout:
        print("Waiting for Unity...")
    except BrokenPipeError:
        print("FFmpeg pipe broke")
        break
    except Exception as e:
        print(f"Error: {e}")
