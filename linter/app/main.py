"""Lava Lint — a FastAPI microservice that reviews Lava templates."""

import json
import os
import urllib.request

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

from . import known
from .linter import lint


def refresh_known_filters() -> int:
    """Pulls the live filter list from the render API so the two services
    never drift. Falls back silently to the static list in known.py."""
    url = os.environ.get("RENDER_API_URL", "http://localhost:5133") + "/api/filters"
    try:
        with urllib.request.urlopen(url, timeout=3) as response:
            filters = json.load(response)
        names = {f["name"] for f in filters if "name" in f}
        if names:
            known.KNOWN_FILTERS.update(names)
        return len(names)
    except Exception:
        return 0


def refresh_capabilities() -> bool:
    """Learns tags, entity commands, and shortcodes from the engine's
    GET /api/capabilities, so this service never hand-maintains a copy.
    The static sets in known.py remain as an offline fallback."""
    url = os.environ.get("RENDER_API_URL", "http://localhost:5133") + "/api/capabilities"
    try:
        with urllib.request.urlopen(url, timeout=3) as response:
            caps = json.load(response)
        if caps.get("blockTags"):
            known.BLOCK_TAGS.clear()
            known.BLOCK_TAGS.update({k.lower(): v.lower() for k, v in caps["blockTags"].items()})
        if caps.get("simpleTags"):
            known.SIMPLE_TAGS.clear()
            known.SIMPLE_TAGS.update(t.lower() for t in caps["simpleTags"])
        if caps.get("entityCommands"):
            known.ENTITY_BLOCK_TAGS.clear()
            known.ENTITY_BLOCK_TAGS.update(t.lower() for t in caps["entityCommands"])
        if caps.get("blockShortcodes"):
            known.BLOCK_SHORTCODES.clear()
            known.BLOCK_SHORTCODES.update(s.lower() for s in caps["blockShortcodes"])
        if caps.get("inlineShortcodes"):
            known.INLINE_SHORTCODES.clear()
            known.INLINE_SHORTCODES.update(s.lower() for s in caps["inlineShortcodes"])
        return True
    except Exception:
        return False

app = FastAPI(
    title="Lava Lint",
    description="Static analysis for Lava templates: structure checks, "
    "unknown-filter detection, and did-you-mean suggestions.",
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


class LintRequest(BaseModel):
    template: str = ""
    engine: str = "local"  # "local" or "rock"


@app.on_event("startup")
def startup() -> None:
    refresh_known_filters()
    refresh_capabilities()


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "known_filters": len(known.KNOWN_FILTERS)}


@app.post("/lint")
def lint_template(request: LintRequest) -> dict:
    issues = lint(request.template, request.engine)
    return {
        "issues": [issue.to_dict() for issue in issues],
        "counts": {
            "error": sum(1 for i in issues if i.severity == "error"),
            "warning": sum(1 for i in issues if i.severity == "warning"),
            "info": sum(1 for i in issues if i.severity == "info"),
        },
    }
