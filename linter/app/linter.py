"""The Lava linter: pure functions, no framework code.

Walks a template, finds {{ output }} and {% tag %} markers, and reports
issues with line/column positions:

  errors    things the renderer will reject (unclosed markers, bad nesting)
  warnings  things that will probably not do what you meant
  info      style nudges (filter casing, empty bodies)
"""

from __future__ import annotations

import difflib
import re
from dataclasses import dataclass, field, asdict

from .known import KNOWN_FILTERS, BLOCK_TAGS, END_TAGS, BRANCH_TAGS, SIMPLE_TAGS, ALL_TAGS

_KNOWN_LOWER = {f.lower(): f for f in KNOWN_FILTERS}

MARKER_RE = re.compile(r"\{\{(.*?)\}\}|\{%(.*?)%\}", re.DOTALL)
OPEN_OUTPUT_RE = re.compile(r"\{\{")
OPEN_TAG_RE = re.compile(r"\{%")

# A filter name is the identifier that follows a pipe.
FILTER_NAME_RE = re.compile(r"\|\s*([A-Za-z_][A-Za-z0-9_]*)")

# Strings must be stripped before scanning for pipes, so a literal like
# 'a|b' does not look like a filter.
STRING_RE = re.compile(r"'[^']*'|\"[^\"]*\"")


@dataclass
class Issue:
    line: int
    col: int
    severity: str  # "error" | "warning" | "info"
    code: str
    message: str
    suggestion: str | None = None

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class _OpenBlock:
    tag: str
    line: int
    col: int
    saw_else: bool = False


def _position(source: str, index: int) -> tuple[int, int]:
    """1-based (line, col) of a character offset."""
    line = source.count("\n", 0, index) + 1
    last_newline = source.rfind("\n", 0, index)
    col = index - last_newline
    return line, col


def lint(source: str) -> list[Issue]:
    issues: list[Issue] = []
    issues.extend(_check_unclosed_markers(source))
    issues.extend(_check_structure(source))
    issues.sort(key=lambda i: (i.line, i.col))
    return issues


def _check_unclosed_markers(source: str) -> list[Issue]:
    """Finds {{ or {% openers that never close."""
    issues: list[Issue] = []
    matched_spans = [m.span() for m in MARKER_RE.finditer(source)]

    def inside_matched(index: int) -> bool:
        return any(start <= index < end for start, end in matched_spans)

    for opener_re, closer, code in (
        (OPEN_OUTPUT_RE, "}}", "unclosed-output"),
        (OPEN_TAG_RE, "%}", "unclosed-tag"),
    ):
        for m in opener_re.finditer(source):
            if not inside_matched(m.start()):
                line, col = _position(source, m.start())
                issues.append(Issue(
                    line, col, "error", code,
                    f'Found "{m.group()}" without a matching "{closer}".',
                ))
    return issues


def _check_structure(source: str) -> list[Issue]:
    issues: list[Issue] = []
    stack: list[_OpenBlock] = []
    in_comment_depth = 0

    for m in MARKER_RE.finditer(source):
        output_content, tag_content = m.group(1), m.group(2)
        line, col = _position(source, m.start())

        if in_comment_depth > 0:
            # Inside {% comment %} everything is ignored except comment nesting.
            if tag_content is not None:
                word = tag_content.strip().split(" ")[0].lower() if tag_content.strip() else ""
                if word == "comment":
                    in_comment_depth += 1
                elif word == "endcomment":
                    in_comment_depth -= 1
                    if in_comment_depth == 0 and stack and stack[-1].tag == "comment":
                        stack.pop()
            continue

        if output_content is not None:
            issues.extend(_check_output(output_content, line, col))
            continue

        content = tag_content.strip()
        if not content:
            issues.append(Issue(line, col, "error", "empty-tag", "Empty tag: {% %} has no content."))
            continue

        word = content.split(" ")[0].split("\t")[0]
        tag = word.lower()

        if tag not in ALL_TAGS:
            match = difflib.get_close_matches(tag, list(ALL_TAGS), n=1)
            issues.append(Issue(
                line, col, "error", "unknown-tag",
                f"Unknown tag {{% {word} %}}.",
                suggestion=f"Did you mean {{% {match[0]} %}}?" if match else None,
            ))
            continue

        if tag in BLOCK_TAGS:
            stack.append(_OpenBlock(tag, line, col))
            if tag == "comment":
                in_comment_depth = 1
            if tag == "capture" and len(content.split()) < 2:
                issues.append(Issue(
                    line, col, "error", "capture-name",
                    "{% capture %} requires a variable name.",
                ))
            continue

        if tag in BRANCH_TAGS:
            holder = next((b for b in reversed(stack) if b.tag in ("if", "unless")), None)
            if holder is None:
                issues.append(Issue(
                    line, col, "error", "orphan-branch",
                    f"{{% {tag} %}} outside of an {{% if %}} block.",
                ))
            elif tag == "else":
                if holder.saw_else:
                    issues.append(Issue(
                        line, col, "error", "duplicate-else",
                        "This {% if %} already has an {% else %}.",
                    ))
                holder.saw_else = True
            elif tag == "elsif" and holder.saw_else:
                issues.append(Issue(
                    line, col, "error", "elsif-after-else",
                    "{% elsif %} cannot appear after {% else %}.",
                ))
            continue

        if tag in END_TAGS:
            expected_opener = END_TAGS[tag]
            if not stack:
                issues.append(Issue(
                    line, col, "error", "unmatched-end",
                    f"{{% {tag} %}} has no matching {{% {expected_opener} %}}.",
                ))
            elif stack[-1].tag != expected_opener:
                open_block = stack[-1]
                issues.append(Issue(
                    line, col, "error", "mismatched-end",
                    f"Expected {{% {BLOCK_TAGS[open_block.tag]} %}} to close the "
                    f"{{% {open_block.tag} %}} on line {open_block.line}, "
                    f"but found {{% {tag} %}}.",
                ))
                stack.pop()
            else:
                stack.pop()
            continue

        if tag == "assign" and "=" not in content:
            issues.append(Issue(
                line, col, "error", "assign-equals",
                '{% assign %} is missing "=". Use: {% assign name = value %}.',
            ))

    for block in stack:
        issues.append(Issue(
            block.line, block.col, "error", "unclosed-block",
            f"{{% {block.tag} %}} is never closed. Add {{% {BLOCK_TAGS[block.tag]} %}}.",
        ))

    return issues


def _check_output(content: str, line: int, col: int) -> list[Issue]:
    issues: list[Issue] = []
    stripped = content.strip()

    if not stripped:
        issues.append(Issue(
            line, col, "error", "empty-output",
            'Empty output braces: "{{ }}" has nothing to render.',
        ))
        return issues

    scannable = STRING_RE.sub("''", content)
    for fm in FILTER_NAME_RE.finditer(scannable):
        name = fm.group(1)
        if name in KNOWN_FILTERS:
            continue
        canonical = _KNOWN_LOWER.get(name.lower())
        if canonical:
            issues.append(Issue(
                line, col, "info", "filter-casing",
                f'Filter "{name}" works, but Rock convention is PascalCase.',
                suggestion=f'Use "{canonical}".',
            ))
        else:
            match = difflib.get_close_matches(name, list(KNOWN_FILTERS), n=1, cutoff=0.55)
            issues.append(Issue(
                line, col, "error", "unknown-filter",
                f'Unknown filter "{name}".',
                suggestion=f'Did you mean "{match[0]}"?' if match else None,
            ))

    if "||" in scannable:
        issues.append(Issue(
            line, col, "warning", "double-pipe",
            'Double pipe "||" found; Lava chains filters with a single "|".',
        ))

    return issues
