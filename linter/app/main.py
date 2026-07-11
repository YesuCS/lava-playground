"""Lava Lint — a FastAPI microservice that reviews Lava templates."""

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

from .linter import lint

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


@app.get("/health")
def health() -> dict:
    return {"status": "ok"}


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
