"""The Lava linter: pure functions, no framework code.

Walks a template, finds {{ output }} and {% tag %} markers, and reports
issues with line/column positions:

  errors    things the local renderer will reject (unclosed markers, bad nesting)
  warnings  things that will probably not do what you meant
  info      style nudges, plus "this needs a real Rock server" notices
"""

from __future__ import annotations

import difflib
import re
from dataclasses import dataclass, asdict

from . import known
from .known import (
    BLOCK_SHORTCODES,
    BLOCK_TAGS,
    BRANCH_TAGS,
    END_TAGS,
    ENTITY_BLOCK_TAGS,
    INLINE_SHORTCODES,
    REMOTE_ONLY_BLOCK_TAGS,
    REMOTE_ONLY_FILTERS,
    SIMPLE_TAGS,
)

ALL_LOCAL_TAGS = set(BLOCK_TAGS) | set(END_TAGS) | BRANCH_TAGS | SIMPLE_TAGS | ENTITY_BLOCK_TAGS
ALL_SHORTCODES = BLOCK_SHORTCODES | INLINE_SHORTCODES

MARKER_RE = re.compile(r"\{\{(.*?)\}\}|\{%(.*?)%\}|\{\[(.*?)\]\}", re.DOTALL)
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
    remote_only: bool = False


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
        (re.compile(r"\{\["), "]}", "unclosed-shortcode"),
    ):
        for m in opener_re.finditer(source):
            if not inside_matched(m.start()):
                line, col = _position(source, m.start())
                issues.append(Issue(
                    line, col, "error", code,
                    f'Found "{m.group()}" without a matching "{closer}".',
                ))
    return issues


def _branch_holder_tags(branch: str) -> tuple[str, ...]:
    """Which block tags a branch tag may live inside."""
    if branch == "when":
        return ("case",)
    if branch == "elsif":
        return ("if", "unless")
    return ("if", "unless", "case")  # else


def _check_structure(source: str) -> list[Issue]:
    issues: list[Issue] = []
    stack: list[_OpenBlock] = []
    shortcode_stack: list[_OpenBlock] = []
    literal_depth = 0        # inside {% comment %} or {% raw %}
    literal_tag = ""         # which literal block we're inside

    for m in MARKER_RE.finditer(source):
        output_content, tag_content, shortcode_content = m.group(1), m.group(2), m.group(3)
        line, col = _position(source, m.start())

        if literal_depth > 0:
            # Inside comment/raw everything is ignored except block nesting.
            if tag_content is not None:
                word = tag_content.strip().split(" ")[0].lower() if tag_content.strip() else ""
                if word == literal_tag:
                    literal_depth += 1
                elif word == f"end{literal_tag}":
                    literal_depth -= 1
                    if literal_depth == 0 and stack and stack[-1].tag == literal_tag:
                        stack.pop()
            continue

        if output_content is not None:
            issues.extend(_check_output(output_content, line, col))
            continue

        if shortcode_content is not None:
            issues.extend(_check_shortcode(shortcode_content.strip(), line, col, shortcode_stack))
            continue

        content = tag_content.strip()
        if not content:
            issues.append(Issue(line, col, "error", "empty-tag", "Empty tag: {% %} has no content."))
            continue

        word = content.split(" ")[0].split("\t")[0]
        tag = word.lower()

        if tag in BLOCK_TAGS:
            stack.append(_OpenBlock(tag, line, col))
            if tag in ("comment", "raw"):
                literal_depth = 1
                literal_tag = tag
            if tag == "capture" and len(content.split()) < 2:
                issues.append(Issue(
                    line, col, "error", "capture-name",
                    "{% capture %} requires a variable name.",
                ))
            continue

        if tag in ENTITY_BLOCK_TAGS:
            stack.append(_OpenBlock(tag, line, col))
            continue

        if tag in REMOTE_ONLY_BLOCK_TAGS:
            stack.append(_OpenBlock(tag, line, col, remote_only=True))
            issues.append(Issue(
                line, col, "info", "remote-only-tag",
                f"{{% {tag} %}} is a Rock server command; it renders only in "
                f"remote mode, on an endpoint where the command is enabled.",
            ))
            continue

        if tag in BRANCH_TAGS:
            valid_holders = _branch_holder_tags(tag)
            holder = next((b for b in reversed(stack) if b.tag in valid_holders), None)
            if holder is None:
                issues.append(Issue(
                    line, col, "error", "orphan-branch",
                    f"{{% {tag} %}} outside of "
                    + (" an {% if %} block." if tag != "when" else " a {% case %} block."),
                ))
            elif tag == "else":
                if holder.saw_else:
                    issues.append(Issue(
                        line, col, "error", "duplicate-else",
                        f"This {{% {holder.tag} %}} already has an {{% else %}}.",
                    ))
                holder.saw_else = True
            elif tag == "elsif" and holder.saw_else:
                issues.append(Issue(
                    line, col, "error", "elsif-after-else",
                    "{% elsif %} cannot appear after {% else %}.",
                ))
            continue

        expected_opener = END_TAGS.get(tag)
        if expected_opener is None and tag.startswith("end") and tag[3:] in (REMOTE_ONLY_BLOCK_TAGS | ENTITY_BLOCK_TAGS):
            expected_opener = tag[3:]
        if expected_opener is not None:
            if not stack:
                issues.append(Issue(
                    line, col, "error", "unmatched-end",
                    f"{{% {tag} %}} has no matching {{% {expected_opener} %}}.",
                ))
            elif stack[-1].tag != expected_opener:
                open_block = stack[-1]
                open_closer = BLOCK_TAGS.get(open_block.tag, f"end{open_block.tag}")
                issues.append(Issue(
                    line, col, "error", "mismatched-end",
                    f"Expected {{% {open_closer} %}} to close the "
                    f"{{% {open_block.tag} %}} on line {open_block.line}, "
                    f"but found {{% {tag} %}}.",
                ))
                stack.pop()
            else:
                stack.pop()
            continue

        if tag in SIMPLE_TAGS and tag != "assign":
            continue

        if tag == "assign":
            if "=" not in content:
                issues.append(Issue(
                    line, col, "error", "assign-equals",
                    '{% assign %} is missing "=". Use: {% assign name = value %}.',
                ))
            else:
                issues.extend(_check_output(content.split("=", 1)[1], line, col))
            continue

        match = difflib.get_close_matches(tag, list(ALL_LOCAL_TAGS | REMOTE_ONLY_BLOCK_TAGS), n=1)
        issues.append(Issue(
            line, col, "error", "unknown-tag",
            f"Unknown tag {{% {word} %}}.",
            suggestion=f"Did you mean {{% {match[0]} %}}?" if match else None,
        ))

    for block in stack:
        closer = BLOCK_TAGS.get(block.tag, f"end{block.tag}")
        issues.append(Issue(
            block.line, block.col, "error", "unclosed-block",
            f"{{% {block.tag} %}} is never closed. Add {{% {closer} %}}.",
        ))

    for block in shortcode_stack:
        issues.append(Issue(
            block.line, block.col, "error", "unclosed-shortcode-block",
            f"{{[ {block.tag} ]}} is never closed. Add {{[ end{block.tag} ]}}.",
        ))

    return issues


def _check_shortcode(content: str, line: int, col: int, stack: list[_OpenBlock]) -> list[Issue]:
    if not content:
        return [Issue(line, col, "error", "empty-shortcode", "Empty shortcode: {[ ]} has no name.")]

    name = content.split(" ")[0].lower()

    if name.startswith("end"):
        opener = name[3:]
        if stack and stack[-1].tag == opener:
            stack.pop()
            return []
        if opener in ALL_SHORTCODES:
            return [Issue(
                line, col, "error", "unmatched-end-shortcode",
                f"{{[ {name} ]}} has no matching {{[ {opener} ]}}.",
            )]
        return [Issue(line, col, "error", "unknown-shortcode", f"Unknown shortcode {{[ {name} ]}}.")]

    if name in BLOCK_SHORTCODES:
        stack.append(_OpenBlock(name, line, col))
        return []
    if name in INLINE_SHORTCODES:
        return []

    match = difflib.get_close_matches(name, list(ALL_SHORTCODES), n=1)
    return [Issue(
        line, col, "error", "unknown-shortcode",
        f"Unknown shortcode {{[ {name} ]}}.",
        suggestion=f"Did you mean {{[ {match[0]} ]}}?" if match else None,
    )]


def _check_output(content: str, line: int, col: int) -> list[Issue]:
    issues: list[Issue] = []
    stripped = content.strip()

    if not stripped:
        issues.append(Issue(
            line, col, "error", "empty-output",
            'Empty output braces: "{{ }}" has nothing to render.',
        ))
        return issues

    known_filters = known.KNOWN_FILTERS
    known_lower = {f.lower(): f for f in known_filters}
    remote_lower = {f.lower(): f for f in REMOTE_ONLY_FILTERS}

    scannable = STRING_RE.sub("''", content)
    for fm in FILTER_NAME_RE.finditer(scannable):
        name = fm.group(1)
        if name in known_filters:
            continue
        if (remote_canonical := remote_lower.get(name.lower())) is not None:
            issues.append(Issue(
                line, col, "info", "remote-only-filter",
                f'"{remote_canonical}" is a Rock entity filter; it renders only '
                f"in remote mode, on a connected Rock server.",
            ))
            continue
        canonical = known_lower.get(name.lower())
        if canonical:
            issues.append(Issue(
                line, col, "info", "filter-casing",
                f'Filter "{name}" works, but Rock convention is PascalCase.',
                suggestion=f'Use "{canonical}".',
            ))
        else:
            match = difflib.get_close_matches(
                name, list(known_filters | REMOTE_ONLY_FILTERS), n=1, cutoff=0.55)
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
