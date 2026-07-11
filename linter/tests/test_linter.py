from app.linter import lint


def codes(source: str) -> list[str]:
    return [issue.code for issue in lint(source)]


def test_clean_template_has_no_issues():
    source = (
        "{% assign name = Person.NickName | Upcase %}"
        "{% if Person.Age > 18 %}Hi {{ name }}!{% else %}Hey kid{% endif %}"
        "{% for g in Person.Groups %}{{ g.Name | Possessive }}{% endfor %}"
    )
    assert lint(source) == []


def test_empty_template():
    assert lint("") == []


def test_unclosed_output():
    assert "unclosed-output" in codes("Hello {{ Person.NickName")


def test_unclosed_tag_marker():
    assert "unclosed-tag" in codes("Hello {% if true")


def test_empty_output():
    assert "empty-output" in codes("{{ }}")


def test_unknown_filter_with_suggestion():
    issues = lint("{{ 'x' | Upcaze }}")
    assert issues[0].code == "unknown-filter"
    assert "Upcase" in issues[0].suggestion


def test_filter_casing_is_info_not_error():
    issues = lint("{{ 'x' | upcase }}")
    assert issues[0].code == "filter-casing"
    assert issues[0].severity == "info"
    assert "Upcase" in issues[0].suggestion


def test_pipe_inside_string_is_ignored():
    assert lint("{{ 'a|b' | Upcase }}") == []


def test_unclosed_if_block():
    issues = lint("{% if true %}oops")
    assert [i.code for i in issues] == ["unclosed-block"]


def test_mismatched_end_tag():
    assert "mismatched-end" in codes("{% if true %}{% endfor %}")


def test_unmatched_end_tag():
    assert "unmatched-end" in codes("{% endif %}")


def test_orphan_else():
    assert "orphan-branch" in codes("{% else %}")


def test_duplicate_else():
    assert "duplicate-else" in codes(
        "{% if true %}a{% else %}b{% else %}c{% endif %}"
    )


def test_elsif_after_else():
    assert "elsif-after-else" in codes(
        "{% if true %}a{% else %}b{% elsif false %}c{% endif %}"
    )


def test_unknown_tag_with_suggestion():
    issues = lint("{% asign x = 1 %}")
    assert issues[0].code == "unknown-tag"
    assert "assign" in issues[0].suggestion


def test_assign_missing_equals():
    assert "assign-equals" in codes("{% assign x 5 %}")


def test_capture_requires_name():
    assert "capture-name" in codes("{% capture %}x{% endcapture %}")


def test_comment_hides_bad_content():
    assert lint("{% comment %}{{ | bogus }}{% wat %}{% endcomment %}") == []


def test_nested_blocks_ok():
    source = (
        "{% for c in Campuses %}"
        "{% if c.IsOriginal %}{{ c.Name | Upcase }}{% endif %}"
        "{% endfor %}"
    )
    assert lint(source) == []


def test_line_and_column_positions():
    issues = lint("line one\nline two {{ 'x' | Nope }}")
    assert issues[0].line == 2
    assert issues[0].col == 10


def test_double_pipe_warning():
    assert "double-pipe" in codes("{{ 'x' || Upcase }}")
