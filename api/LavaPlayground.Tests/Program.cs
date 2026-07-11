using System.Text.Json;
using LavaPlayground.Api.Lava;

// ---------------------------------------------------------------------------
// A zero-dependency test harness (NuGet-free by design).
// Run with: dotnet run --project api/LavaPlayground.Tests
// Exits non-zero if any test fails, so it works as a CI gate.
// ---------------------------------------------------------------------------

var passed = 0;
var failed = 0;
var failures = new List<string>();

var filters = LavaFilterRegistry.CreateDefault();

var contextJson = """
{
  "Person": {
    "NickName": "Sam",
    "LastName": "Houston",
    "Age": 33,
    "MiddleName": "",
    "IsBaptized": true,
    "Campus": { "Name": "The Loop" },
    "Groups": [
      { "Name": "Young Pros", "Role": "Leader", "Members": 14 },
      { "Name": "Check-In Team", "Role": "Member", "Members": 8 },
      { "Name": "Choir", "Role": "Member", "Members": 120 }
    ]
  },
  "Campuses": [
    { "Name": "The Loop", "Attendance": 3200 },
    { "Name": "Cypress", "Attendance": 1100 },
    { "Name": "Downtown", "Attendance": 650 }
  ]
}
""";

var baseContext = JsonObjectConverter.ToDictionary(JsonDocument.Parse(contextJson).RootElement);

void Check(string name, string template, string expected)
{
    try
    {
        var output = LavaTemplate.Parse(template).Render(new RenderContext(baseContext, filters));
        if (output == expected)
        {
            passed++;
        }
        else
        {
            failed++;
            failures.Add($"{name}: expected \"{expected}\" but got \"{output}\"");
        }
    }
    catch (Exception ex)
    {
        failed++;
        failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
    }
}

void CheckError(string name, string template, string expectedFragment)
{
    try
    {
        LavaTemplate.Parse(template).Render(new RenderContext(baseContext, filters));
        failed++;
        failures.Add($"{name}: expected a LavaException containing \"{expectedFragment}\" but nothing was thrown");
    }
    catch (LavaException ex) when (ex.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase))
    {
        passed++;
    }
    catch (Exception ex)
    {
        failed++;
        failures.Add($"{name}: expected message containing \"{expectedFragment}\" but got: {ex.Message}");
    }
}

// ---------------- Output and variable paths ----------------
Check("plain text", "Hello!", "Hello!");
Check("simple variable", "{{ Person.NickName }}", "Sam");
Check("nested path", "{{ Person.Campus.Name }}", "The Loop");
Check("array index", "{{ Person.Groups[1].Name }}", "Check-In Team");
Check("missing variable renders empty", "[{{ Person.Nope.Nada }}]", "[]");
Check("case-insensitive paths", "{{ person.nickname }}", "Sam");
Check("string literal", "{{ 'hi' }}", "hi");
Check("number literal", "{{ 42 }}", "42");

// ---------------- String filters ----------------
Check("Upcase", "{{ Person.NickName | Upcase }}", "SAM");
Check("Downcase", "{{ 'LOUD' | Downcase }}", "loud");
Check("Capitalize", "{{ 'houston' | Capitalize }}", "Houston");
Check("TitleCase", "{{ 'the loop campus' | TitleCase }}", "The Loop Campus");
Check("Humanize camelCase", "{{ 'FirstTimeGuest' | Humanize }}", "First time guest");
Check("Humanize snake_case", "{{ 'first_time_guest' | Humanize }}", "First time guest");
Check("Possessive", "{{ 'Yesu' | Possessive }}", "Yesu's");
Check("Possessive ending in s", "{{ 'James' | Possessive }}", "James'");
Check("Pluralize regular", "{{ 'group' | Pluralize }}", "groups");
Check("Pluralize person", "{{ 'person' | Pluralize }}", "people");
Check("Pluralize y-rule", "{{ 'ministry' | Pluralize }}", "ministries");
Check("Pluralize ch-rule", "{{ 'church' | Pluralize }}", "churches");
Check("PluralizeForQuantity one", "{{ 'kid' | PluralizeForQuantity:1 }}", "kid");
Check("PluralizeForQuantity many", "{{ 'kid' | PluralizeForQuantity:3 }}", "kids");
Check("Truncate", "{{ 'Hallelujah' | Truncate:4 }}", "Hall…");
Check("Replace", "{{ 'pew pew' | Replace:'pew','wow' }}", "wow wow");
Check("Append and Prepend", "{{ 'First' | Prepend:'Houston ' | Append:'!' }}", "Houston First!");
Check("Right", "{{ 'Houston' | Right:3 }}", "ton");
Check("Left", "{{ 'Houston' | Left:3 }}", "Hou");
Check("ToCssClass", "{{ 'The Loop Campus' | ToCssClass }}", "the-loop-campus");
Check("HtmlEncode", "{{ '<b>hi</b>' | HtmlEncode }}", "&lt;b&gt;hi&lt;/b&gt;");
Check("filter chaining", "{{ 'sam houston' | TitleCase | Possessive }}", "Sam Houston's");

// ---------------- Number filters ----------------
Check("Plus", "{{ 3 | Plus:4 }}", "7");
Check("Minus", "{{ 10 | Minus:4 }}", "6");
Check("Times", "{{ 6 | Times:7 }}", "42");
Check("DividedBy", "{{ 84 | DividedBy:2 }}", "42");
Check("Modulo", "{{ 7 | Modulo:3 }}", "1");
Check("Floor", "{{ 4.7 | Floor }}", "4");
Check("Ceiling", "{{ 4.2 | Ceiling }}", "5");
Check("Format number", "{{ 1234.5 | Format:'#,##0.00' }}", "1,234.50");
Check("NumberToOrdinal 3", "{{ 3 | NumberToOrdinal }}", "3rd");
Check("NumberToOrdinal 11", "{{ 11 | NumberToOrdinal }}", "11th");
Check("NumberToOrdinal 22", "{{ 22 | NumberToOrdinal }}", "22nd");
Check("NumberToOrdinalWords", "{{ 3 | NumberToOrdinalWords }}", "third");
Check("NumberToOrdinalWords 21", "{{ 21 | NumberToOrdinalWords }}", "twenty-first");
Check("NumberToWords", "{{ 3200 | NumberToWords }}", "three thousand two hundred");
Check("NumberToRomanNumerals", "{{ 2026 | NumberToRomanNumerals }}", "MMXXVI");
Check("AsInteger", "{{ '42.9' | AsInteger }}", "42");
Check("filter arg from variable", "{{ Person.Age | Plus:Person.Age }}", "66");
CheckError("divide by zero", "{{ 1 | DividedBy:0 }}", "divide by zero");

// ---------------- Date filters ----------------
Check("Date format", "{{ '2026-12-25' | Date:'MMMM d, yyyy' }}", "December 25, 2026");
Check("Date passthrough for non-dates", "{{ 'not a date' | Date:'yyyy' }}", "not a date");

// ---------------- Array filters ----------------
Check("Size of array", "{{ Person.Groups | Size }}", "3");
Check("Size of string", "{{ 'Lava' | Size }}", "4");
Check("First then property access", "{% assign g = Person.Groups | First %}{{ g.Name }}", "Young Pros");
Check("Map + Join", "{{ Campuses | Map:'Name' | Join:', ' }}", "The Loop, Cypress, Downtown");
Check("Sort by property", "{{ Campuses | Sort:'Attendance' | Map:'Name' | Join:',' }}", "Downtown,Cypress,The Loop");
Check("Reverse", "{{ Campuses | Reverse | Map:'Name' | First }}", "Downtown");
Check("Uniq", "{{ 'a,b,a,c' | Split:',' | Uniq | Join:',' }}", "a,b,c");
Check("Where", "{{ Person.Groups | Where:'Role','Leader' | Map:'Name' | Join:',' }}", "Young Pros");
Check("Split + Size", "{{ 'a,b,c' | Split:',' | Size }}", "3");

// ---------------- Logic filters ----------------
Check("Default on empty string", "{{ Person.MiddleName | Default:'(none)' }}", "(none)");
Check("Default not applied", "{{ Person.NickName | Default:'friend' }}", "Sam");

// ---------------- Tags: assign / capture / comment ----------------
Check("assign literal", "{% assign x = 5 %}{{ x }}", "5");
Check("assign with filters", "{% assign name = Person.NickName | Upcase %}{{ name }}", "SAM");
Check("capture", "{% capture greeting %}Hi {{ Person.NickName }}!{% endcapture %}{{ greeting }}", "Hi Sam!");
Check("comment", "a{% comment %} hidden {{ bad }} {% endcomment %}b", "ab");

// ---------------- Tags: if / unless ----------------
Check("if true", "{% if Person.IsBaptized %}yes{% endif %}", "yes");
Check("if false -> else", "{% if Person.Age > 40 %}old{% else %}young{% endif %}", "young");
Check("elsif", "{% if Person.Age > 40 %}a{% elsif Person.Age > 30 %}b{% else %}c{% endif %}", "b");
Check("if equality", "{% if Person.NickName == 'Sam' %}match{% endif %}", "match");
Check("if not-equal", "{% if Person.NickName != 'Bob' %}nope{% endif %}", "nope");
Check("and binds tighter than or", "{% if false and false or true %}t{% endif %}", "t");
Check("contains on string", "{% if 'Houston' contains 'ust' %}yes{% endif %}", "yes");
Check("contains on array", "{% if Campuses contains 'x' %}y{% else %}n{% endif %}", "n");
Check("unless", "{% unless Person.Age > 40 %}young{% endunless %}", "young");
Check("numeric string comparison", "{% if '10' > 9 %}ten{% endif %}", "ten");

// ---------------- Tags: for ----------------
Check("for loop", "{% for g in Person.Groups %}{{ g.Name }};{% endfor %}",
    "Young Pros;Check-In Team;Choir;");
Check("forloop.index", "{% for g in Person.Groups %}{{ forloop.index }}{% endfor %}", "123");
Check("forloop.first/last",
    "{% for g in Person.Groups %}{% if forloop.first %}[{% endif %}{{ g.Members }}{% if forloop.last %}]{% else %},{% endif %}{% endfor %}",
    "[14,8,120]");
Check("for reversed", "{% for c in Campuses reversed %}{{ c.Name }};{% endfor %}",
    "Downtown;Cypress;The Loop;");
Check("for limit", "{% for c in Campuses limit:2 %}{{ c.Name }};{% endfor %}", "The Loop;Cypress;");
Check("nested loops",
    "{% for c in Campuses limit:1 %}{% for g in Person.Groups limit:2 %}{{ c.Name }}/{{ g.Name }};{% endfor %}{% endfor %}",
    "The Loop/Young Pros;The Loop/Check-In Team;");
Check("loop variable scoped", "{% for g in Person.Groups limit:1 %}{% endfor %}[{{ g }}]", "[]");

// ---------------- Errors ----------------
CheckError("unknown filter", "{{ 'x' | Frobnicate }}", "Unknown filter");
CheckError("unknown tag", "{% frob %}", "Unknown tag");
CheckError("missing endif", "{% if true %}oops", "endif");
CheckError("missing endfor", "{% for c in Campuses %}oops", "endfor");
CheckError("unclosed output", "hello {{ Person.NickName", "Unclosed output");
CheckError("unclosed tag", "hello {% if true", "Unclosed tag");
CheckError("stray endif", "{% endif %}", "no matching opening tag");
CheckError("unterminated string", "{{ 'oops }}", "Unterminated string");
CheckError("empty output", "{{ }}", "Empty output");

// ---------------- Report ----------------
Console.WriteLine($"\n{passed} passed, {failed} failed");
foreach (var failure in failures)
{
    Console.WriteLine($"  FAIL {failure}");
}
return failed == 0 ? 0 : 1;
