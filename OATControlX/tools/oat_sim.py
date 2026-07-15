#!/usr/bin/env python3
"""Minimal OAT mount simulator: LX200/Meade protocol over TCP for OATControlX testing."""
import socket
import threading

RESPONSES = {
    ":GVP#": "OpenAstroExplorer#",
    ":GVN#": "V1.13.6#",
    ":XGM#": "MKS Gen L V2.1,NEMA 17|400,NEMA 17|400,GYRO,HSAH,HSAV,AUTO_AZ_ALT#",
    ":XGMS#": "TU,16,64|TU,16,64#",
    ":XGR#": "420.0#",
    ":XGD#": "420.0#",
    ":XGS#": "1.0000#",
    ":XGHS#": "1234#",
    ":XGHD#": "-567#",
    ":XLGT#": "21.4#",
}

DIGIT_PREFIXES = (":MT", ":SHP", ":MHR", ":MHD", ":XFR")

state = {"tracking": True, "n": 0}


def gx():
    state["n"] += 1
    ra_s = 30 + state["n"] % 30
    trk = "T" if state["tracking"] else "-"
    return f"Idle,--{trk}---,12345,6789,1011,0830{ra_s:02d},+891213,50000#"


def handle(conn):
    buf = ""
    while True:
        try:
            data = conn.recv(256)
        except OSError:
            break
        if not data:
            break
        buf += data.decode("ascii", "ignore")
        while "#" in buf:
            i = buf.index("#")
            cmd, buf = buf[: i + 1], buf[i + 1 :]
            reply = None
            if cmd == ":GX#":
                reply = gx()
            elif cmd in RESPONSES:
                reply = RESPONSES[cmd]
            elif cmd.startswith(DIGIT_PREFIXES):
                if cmd.startswith(":MT"):
                    state["tracking"] = cmd == ":MT1#"
                reply = "1"
            # everything else (:M, :Q, :R, :XS, :MAZ, :MAL, :Qq) is blind
            print(f"  {cmd} -> {reply}")
            if reply:
                conn.sendall(reply.encode("ascii"))
    conn.close()


srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(("127.0.0.1", 4030))
srv.listen(2)
print("OAT simulator listening on 127.0.0.1:4030")
while True:
    c, addr = srv.accept()
    print(f"client connected: {addr}")
    threading.Thread(target=handle, args=(c,), daemon=True).start()
