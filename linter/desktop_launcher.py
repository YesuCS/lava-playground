"""Entry point for the frozen (PyInstaller) linter binary used by the desktop app.

Runs the FastAPI app on a fixed localhost port with an embedded uvicorn server.
Passing the app object directly (rather than an import string) keeps everything
in one process, which is what the single-file frozen binary needs.
"""

import os
import threading
import time

import uvicorn

from app.main import app


def _watch_host() -> None:
    """Exit when the desktop app that launched us goes away.

    PyInstaller one-file binaries run as a bootloader + child pair, so the
    parent app can't reliably kill the real server by killing the process it
    spawned. Instead we watch the host app's PID and exit ourselves when it's
    gone — this also covers force-quit and crashes, leaving no orphan holding
    the port.
    """
    host_pid = int(os.environ["LAVA_HOST_PID"])
    while True:
        try:
            os.kill(host_pid, 0)
        except OSError:
            os._exit(0)
        time.sleep(1.0)


if __name__ == "__main__":
    if os.environ.get("LAVA_HOST_PID"):
        threading.Thread(target=_watch_host, daemon=True).start()
    uvicorn.run(app, host="127.0.0.1", port=8000, log_level="warning")
