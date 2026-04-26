"""
Jupiter Touch — Director Server
Serves the presenter dashboard and relays control messages to the Quest.

Usage:
    python director_server.py [--port 8765] [--host 0.0.0.0]

Two WebSocket paths:
    /ws/director  — browser (presenter) connects here
    /ws/quest     — Unity app on Quest connects here

The server relays messages in both directions:
    director → quest  (scene.load, event.trigger, var.set, ping)
    quest → director  (ack, telemetry)
"""

import argparse
import asyncio
import json
import logging
import time
from pathlib import Path

from aiohttp import web, WSMsgType
import aiohttp_cors

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger("director")

STATIC_DIR = Path(__file__).parent / "static"

# Holds at most one active connection for each role.
_director_ws = None  # browser client
_quest_ws    = None  # Unity Quest client


async def _send_safe(ws, msg: dict):
    """Send a JSON message, ignore if socket is closing."""
    if ws is not None and not ws.closed:
        try:
            await ws.send_json(msg)
        except Exception as e:
            log.warning(f"Send failed: {e}")


async def handle_director_ws(request):
    """WebSocket handler for the browser director console."""
    global _director_ws
    ws = web.WebSocketResponse(heartbeat=10)
    await ws.prepare(request)
    _director_ws = ws
    log.info("Director connected")

    # Tell the director the current Quest connection state
    await _send_safe(ws, {
        "type": "server.status",
        "quest_connected": _quest_ws is not None and not _quest_ws.closed,
    })

    try:
        async for msg in ws:
            if msg.type == WSMsgType.TEXT:
                try:
                    data = json.loads(msg.data)
                except json.JSONDecodeError:
                    log.warning(f"Director sent invalid JSON: {msg.data!r}")
                    continue

                log.info(f"director → quest: {data}")
                await _send_safe(_quest_ws, data)

            elif msg.type in (WSMsgType.ERROR, WSMsgType.CLOSE):
                break
    finally:
        _director_ws = None
        log.info("Director disconnected")

    return ws


async def handle_quest_ws(request):
    """WebSocket handler for the Unity Quest app."""
    global _quest_ws
    ws = web.WebSocketResponse(heartbeat=10)
    await ws.prepare(request)
    _quest_ws = ws
    log.info("Quest connected")

    # Tell the director that the Quest came online
    await _send_safe(_director_ws, {"type": "quest.connected"})

    try:
        async for msg in ws:
            if msg.type == WSMsgType.TEXT:
                try:
                    data = json.loads(msg.data)
                except json.JSONDecodeError:
                    log.warning(f"Quest sent invalid JSON: {msg.data!r}")
                    continue

                log.info(f"quest → director: {data}")
                await _send_safe(_director_ws, data)

            elif msg.type in (WSMsgType.ERROR, WSMsgType.CLOSE):
                break
    finally:
        _quest_ws = None
        log.info("Quest disconnected")
        await _send_safe(_director_ws, {"type": "quest.disconnected"})

    return ws


async def handle_index(request):
    return web.FileResponse(STATIC_DIR / "index.html")


def build_app() -> web.Application:
    app = web.Application()

    # CORS — allow the ws-scrcpy iframe origin
    cors = aiohttp_cors.setup(app, defaults={
        "*": aiohttp_cors.ResourceOptions(
            allow_credentials=True,
            expose_headers="*",
            allow_headers="*",
        )
    })

    app.router.add_get("/", handle_index)
    app.router.add_get("/ws/director", handle_director_ws)
    app.router.add_get("/ws/quest",    handle_quest_ws)
    app.router.add_static("/static",   STATIC_DIR)

    return app


def main():
    parser = argparse.ArgumentParser(description="Jupiter Touch Director Server")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()

    app = build_app()
    log.info(f"Starting director server on http://{args.host}:{args.port}")
    log.info(f"Open http://localhost:{args.port} in Chrome")
    web.run_app(app, host=args.host, port=args.port, print=None)


if __name__ == "__main__":
    main()
