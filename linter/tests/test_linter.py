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


def test_remote_only_filter_is_info():
    issues = lint("{{ CurrentPerson | Attribute:'BaptismDate' }}")
    assert issues[0].code == "remote-only-filter"
    assert issues[0].severity == "info"


def test_remote_only_block_tag():
    source = "{% sql %}SELECT 1{% endsql %}"
    issues = lint(source)
    assert [i.code for i in issues] == ["remote-only-tag"]
    assert issues[0].severity == "info"


def test_remote_only_block_left_open():
    assert "unclosed-block" in codes("{% person where:'Id == 1' %}")


def test_case_when_structure_ok():
    assert lint("{% case x %}{% when 1 %}a{% else %}b{% endcase %}") == []


def test_when_outside_case():
    assert "orphan-branch" in codes("{% when 1 %}")


def test_raw_hides_content():
    assert lint("{% raw %}{{ | bogus }}{% wat %}{% endraw %}") == []


def test_case_filter_suggestions_include_remote():
    issues = lint("{{ p | PersonByIdd }}")
    assert issues[0].code == "unknown-filter"
    assert "PersonById" in issues[0].suggestion


def test_new_filters_are_known():
    assert lint("{{ Campuses | Sort:'Name','desc' | Take:2 | Sum:'Attendance' | FormatAsCurrency }}") == []


def test_assign_expression_filters_are_checked():
    assert "unknown-filter" in codes("{% assign x = 'a' | Upcaze %}")


def test_entity_command_is_clean():
    assert lint("{% person where:'Age > 18' %}{% for p in person %}{{ p.NickName }}{% endfor %}{% endperson %}") == []


def test_entity_command_left_open():
    assert "unclosed-block" in codes("{% campus %}")


def test_cycle_increment_are_known():
    assert lint("{% cycle 'a', 'b' %}{% increment i %}{% decrement j %}") == []


def test_tablerow_structure():
    assert lint("{% tablerow c in Campuses cols:2 %}{{ c.Name }}{% endtablerow %}") == []
    assert "unclosed-block" in codes("{% tablerow c in Campuses %}")


def test_shortcode_clean():
    assert lint("{[ alert type:'info' ]}Hi{[ endalert ]}{[ button text:'Go' ]}") == []


def test_unknown_shortcode_with_suggestion():
    issues = lint("{[ allert ]}x{[ endallert ]}")
    assert issues[0].code == "unknown-shortcode"
    assert "alert" in issues[0].suggestion


def test_unclosed_shortcode_block():
    assert "unclosed-shortcode-block" in codes("{[ panel title:'x' ]}body")


def test_unmatched_end_shortcode():
    assert "unmatched-end-shortcode" in codes("{[ endaccordion ]}")


def test_unclosed_shortcode_marker():
    assert "unclosed-shortcode" in codes("text {[ alert")


def test_shortcode_inside_raw_ignored():
    assert lint("{% raw %}{[ bogus ]}{% endraw %}") == []


def test_whitespace_control_dashes():
    assert lint("{%- if true -%}{{- 'x' | Upcase -}}{%- endif -%}") == []


def test_rock_mode_suppresses_remote_only_info():
    assert lint("{% sql %}SELECT 1{% endsql %}", engine="rock") == []
    assert lint("{{ CurrentPerson | Attribute:'BaptismDate' }}", engine="rock") == []


def test_rock_mode_downgrades_unknown_filter():
    issues = lint("{{ 'x' | SomeCustomFilter }}", engine="rock")
    assert issues[0].severity == "warning"
    assert issues[0].code == "unknown-filter"


def test_rock_mode_allows_custom_shortcodes():
    assert lint("{[ mychurchcard title:'x' ]}", engine="rock") == []


def test_rock_mode_still_catches_structure_errors():
    assert "unclosed-block" in [i.code for i in lint("{% if true %}oops", engine="rock")]
    assert "unclosed-output" in [i.code for i in lint("{{ Person.NickName", engine="rock")]
