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


@app.on_event("startup")
def startup() -> None:
    refresh_known_filters()


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "known_filters": len(known.KNOWN_FILTERS)}


@app.post("/lint")
def lint_template(request: LintRequest) -> dict:
    issues = lint(request.template)
    return {
        "issues": [issue.to_dict() for issue in issues],
        "counts": {
            "error": sum(1 for i in issues if i.severity == "error"),
            "warning": sum(1 for i in issues if i.severity == "warning"),
            "info": sum(1 for i in issues if i.severity == "info"),
        },
    }
