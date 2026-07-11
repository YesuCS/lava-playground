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

var entityJson = """
{
  "person": [
    { "Id": 1, "NickName": "Sam", "LastName": "Houston", "Age": 33, "CampusId": 1 },
    { "Id": 2, "NickName": "Liz", "LastName": "Austin", "Age": 28, "CampusId": 1 },
    { "Id": 3, "NickName": "Deb", "LastName": "Crockett", "Age": 45, "CampusId": 2 }
  ],
  "campus": [
    { "Id": 1, "Name": "The Loop", "IsActive": true },
    { "Id": 2, "Name": "Cypress", "IsActive": false }
  ]
}
""";
var entityData = JsonObjectConverter.ToDictionary(JsonDocument.Parse(entityJson).RootElement)
    .ToDictionary(
        kv => kv.Key,
        kv => (kv.Value as List<object?>) ?? new List<object?>(),
        StringComparer.OrdinalIgnoreCase);

void Check(string name, string template, string expected)
{
    try
    {
        var output = LavaTemplate.Parse(template).Render(new RenderContext(baseContext, filters, entityData));
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

void CheckContains(string name, string template, string expectedFragment)
{
    try
    {
        var output = LavaTemplate.Parse(template).Render(new RenderContext(baseContext, filters, entityData));
        if (output.Contains(expectedFragment))
        {
            passed++;
        }
        else
        {
            failed++;
            failures.Add($"{name}: expected output to contain \"{expectedFragment}\" but got \"{output}\"");
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
        LavaTemplate.Parse(template).Render(new RenderContext(baseContext, filters, entityData));
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

// ---------------- New text filters ----------------
Check("TruncateWords", "{{ 'the quick brown fox jumps' | TruncateWords:3 }}", "the quick brown…");
Check("ReplaceFirst", "{{ 'pew pew' | ReplaceFirst:'pew','wow' }}", "wow pew");
Check("ReplaceLast", "{{ 'pew pew' | ReplaceLast:'pew','wow' }}", "pew wow");
Check("RemoveFirst", "{{ 'a-b-c' | RemoveFirst:'-' }}", "ab-c");
Check("Strip", "{{ '  hi  ' | Strip }}", "hi");
Check("StripHtml", "{{ '<b>Big</b> news' | StripHtml }}", "Big news");
Check("NewlineToBr", "{% capture t %}a\nb{% endcapture %}{{ t | NewlineToBr }}", "a<br/>b");
Check("Singularize", "{{ 'ministries' | Singularize }}", "ministry");
Check("Singularize people", "{{ 'people' | Singularize }}", "person");
Check("ToQuantity", "{{ 'phone' | ToQuantity:3 }}", "3 phones");
Check("EscapeOnce", "{{ '&lt;b&gt; & x' | EscapeOnce }}", "&lt;b&gt; &amp; x");
Check("UrlEncode", "{{ 'a b&c' | UrlEncode }}", "a%20b%26c");
Check("UrlDecode", "{{ 'a%20b' | UrlDecode }}", "a b");
Check("ObfuscateEmail", "{{ 'sam@example.com' | ObfuscateEmail }}", "sxxxxx@example.com");
Check("Linkify", "{{ 'see https://rockrms.com now' | Linkify }}",
    "see <a href=\"https://rockrms.com\">https://rockrms.com</a> now");
Check("FromMarkdown", "{{ '**Big** news' | FromMarkdown }}", "<p><strong>Big</strong> news</p>");
Check("ReadTime short", "{{ 'a few words here' | ReadTime }}", "1 min");
Check("RegExMatch", "{{ '555-1234' | RegExMatch:'\\d\\d\\d-\\d\\d\\d\\d' }}", "true");
Check("RegExMatchValue", "{{ 'Room 214 is open' | RegExMatchValue:'\\d+' }}", "214");
Check("RegExMatchValues", "{{ 'a1 b2 c3' | RegExMatchValues:'\\d' | Join:',' }}", "1,2,3");
Check("RegExReplace", "{{ 'a1b2' | RegExReplace:'\\d','-' }}", "a-b-");

// ---------------- New numeric filters ----------------
Check("Abs", "{{ -17 | Abs }}", "17");
Check("Round to places", "{{ 3.14159 | Round:2 }}", "3.14");
Check("Round whole", "{{ 4.5 | Round }}", "5");
Check("AtLeast", "{{ 3 | AtLeast:5 }}", "5");
Check("AtMost", "{{ 9 | AtMost:5 }}", "5");
Check("DividedBy precision", "{{ 12.434 | DividedBy:6,2 }}", "2.07");
Check("FormatAsCurrency", "{{ 1234.5 | FormatAsCurrency }}", "$1,234.50");

// ---------------- New date filters ----------------
Check("DateAdd days", "{{ '2026-07-11' | DateAdd:3,'d' | Date:'M/d/yyyy' }}", "7/14/2026");
Check("DateAdd months", "{{ '2026-07-11' | DateAdd:2,'M' | Date:'M/d/yyyy' }}", "9/11/2026");
Check("DateDiff days", "{{ '2026-01-01' | DateDiff:'2026-01-31','d' }}", "30");
Check("DateDiff months", "{{ '2026-01-15' | DateDiff:'2026-04-01','M' }}", "3");
Check("DaysInMonth", "{{ '2026-02-14' | DaysInMonth }}", "28");
Check("SundayDate", "{{ '2026-07-11' | SundayDate | Date:'M/d/yyyy' }}", "7/12/2026");
Check("ToMidnight", "{{ '2026-07-11 14:30' | ToMidnight | Date:'h:mm tt' }}", "12:00 AM");
Check("HumanizeTimeSpan", "{{ '2026-07-01' | HumanizeTimeSpan:'2026-08-02',2 }}", "4 weeks, 4 days");
Check("AsDateTime chain", "{{ '2026-12-25' | AsDateTime | Date:'MMM d' }}", "Dec 25");

// ---------------- New collection filters ----------------
Check("Select dotted path", "{{ Person.Groups | Select:'Name' | First }}", "Young Pros");
Check("Sort desc", "{{ Campuses | Sort:'Attendance','desc' | Map:'Name' | First }}", "The Loop");
Check("Index", "{% assign g = Person.Groups | Index:1 %}{{ g.Name }}", "Check-In Team");
Check("Take", "{{ Campuses | Take:2 | Map:'Name' | Join:',' }}", "The Loop,Cypress");
Check("Skip", "{{ Campuses | Skip:2 | Map:'Name' | Join:',' }}", "Downtown");
Check("Sum property", "{{ Campuses | Sum:'Attendance' }}", "4950");
Check("Sum plain", "{{ '1,2,3' | Split:',' | Sum }}", "6");
Check("Compact", "{{ 'a,,b' | Split:',' | Compact | Size }}", "2");
Check("Concat", "{% assign both = Campuses | Concat:Person.Groups %}{{ both | Size }}", "6");
Check("Contains true", "{{ 'a,b' | Split:',' | Contains:'b' }}", "true");
Check("Distinct by prop", "{{ Campuses | Distinct:'Name' | Size }}", "3");
Check("RemoveFromArray", "{{ 'a,b,a' | Split:',' | RemoveFromArray:'a' | Join:',' }}", "b");
Check("AddToArray from empty", "{% assign ids = '' %}{% assign ids = ids | AddToArray:5 %}{{ ids | Size }}", "1");
Check("GroupBy + iterate",
    "{% assign byRole = Person.Groups | GroupBy:'Role' %}{% for entry in byRole %}{% assign kv = entry | PropertyToKeyValue %}{{ kv.Key }}={{ kv.Value | Size }};{% endfor %}",
    "Leader=1;Member=2;");
Check("AddToDictionary + AllKeys",
    "{% assign d = '' | AddToDictionary:'a',1 %}{% assign d = d | AddToDictionary:'b',2 %}{{ d | AllKeysFromDictionary | Join:',' }}",
    "a,b");

// ---------------- Type coercion ----------------
Check("AsBoolean", "{{ 'Yes' | AsBoolean }}", "true");
Check("ToJSON + FromJSON roundtrip",
    "{% capture j %}{\"Name\":\"Rock\"}{% endcapture %}{% assign o = j | FromJSON %}{{ o.Name }}", "Rock");
Check("ToString", "{{ 42 | ToString | Size }}", "2");

// ---------------- Color filters ----------------
Check("Darken", "{{ '#ffffff' | Darken:'50%' }}", "#808080");
Check("Lighten black", "{{ '#000000' | Lighten:'50%' }}", "#808080");
Check("Grayscale", "{{ '#ff0000' | Grayscale }}", "#808080");
Check("FadeOut", "{{ '#ff0000' | FadeOut:'50%' }}", "rgba(255, 0, 0, 0.5)");
Check("Mix", "{{ '#000000' | Mix:'#ffffff','50%' }}", "#808080");
Check("Tint", "{{ '#000000' | Tint:'100%' }}", "#ffffff");
Check("Shade", "{{ '#ffffff' | Shade:'100%' }}", "#000000");
Check("named color", "{{ 'navy' | Lighten:'0%' }}", "#000080");
Check("rgb() input", "{{ 'rgb(255, 0, 0)' | Grayscale }}", "#808080");

// ---------------- case / raw tags ----------------
Check("case match", "{% case Person.NickName %}{% when 'Bob' %}b{% when 'Sam' %}s{% else %}x{% endcase %}", "s");
Check("case else", "{% case 99 %}{% when 1 %}a{% else %}z{% endcase %}", "z");
Check("case multi-value when", "{% case 2 %}{% when 1 or 2 %}hit{% endcase %}", "hit");
Check("raw", "{% raw %}{{ not.rendered }}{% endraw %}", "{{ not.rendered }}");
CheckError("missing endcase", "{% case 1 %}{% when 1 %}a", "endcase");
CheckError("missing endraw", "{% raw %}oops", "endraw");

// ---------------- Dictionary iteration ----------------
Check("for over dictionary",
    "{% assign d = '' | AddToDictionary:'x',1 %}{% for entry in d %}{{ entry.Key }}:{{ entry.Value }}{% endfor %}",
    "x:1");

// ---------------- Entity commands ----------------
Check("entity command basic",
    "{% person %}{{ person | Size }}{% endperson %}", "3");
Check("entity where equality",
    "{% person where:'LastName == \"Houston\"' %}{% for p in person %}{{ p.NickName }}{% endfor %}{% endperson %}", "Sam");
Check("entity where and",
    "{% person where:'CampusId == 1 && Age > 30' %}{{ person | Size }}{% endperson %}", "1");
Check("entity where or",
    "{% person where:'Age > 40 || NickName == \"Liz\"' %}{{ person | Size }}{% endperson %}", "2");
Check("entity where starts-with",
    "{% person where:'LastName ^= \"Hou\"' %}{{ person | Size }}{% endperson %}", "1");
Check("entity where contains",
    "{% person where:'LastName *= \"usti\"' %}{% for p in person %}{{ p.NickName }}{% endfor %}{% endperson %}", "Liz");
Check("entity sort desc + limit",
    "{% person sort:'Age desc' limit:'1' %}{% for p in person %}{{ p.NickName }}{% endfor %}{% endperson %}", "Deb");
Check("entity id returns single",
    "{% person id:'2' %}{{ person.NickName }}{% endperson %}", "Liz");
Check("entity iterator rename",
    "{% person where:'Age < 30' iterator:'youngins' %}{{ youngins | Size }}{% endperson %}", "1");
Check("second entity type",
    "{% campus where:'IsActive == true' %}{% for c in campus %}{{ c.Name }}{% endfor %}{% endcampus %}", "The Loop");
Check("entity variable scoped to block",
    "{% campus limit:'1' %}{% endcampus %}[{{ campus }}]", "[]");
CheckError("unknown entity data", "{% definedvalue %}x{% enddefinedvalue %}", "No sample data");
CheckError("bad where clause", "{% person where:'LastName ~ 5' %}{% endperson %}", "operator");

// ---------------- Shortcodes ----------------
Check("alert shortcode",
    "{[ alert type:'warning' ]}Watch out{[ endalert ]}",
    "<div class=\"alert alert-warning\">Watch out</div>");
Check("alert renders inner lava",
    "{[ alert ]}Hi {{ Person.NickName }}{[ endalert ]}",
    "<div class=\"alert alert-info\">Hi Sam</div>");
Check("panel with title",
    "{[ panel title:'Notes' ]}Body{[ endpanel ]}",
    "<div class=\"panel panel-default\"><div class=\"panel-heading\"><h3 class=\"panel-title\">Notes</h3></div><div class=\"panel-body\">Body</div></div>");
Check("button inline",
    "{[ button text:'Go' link:'/next' type:'success' ]}",
    "<a href=\"/next\" class=\"btn btn-success\">Go</a>");
Check("button lava in params",
    "{[ button text:'{{ Person.NickName }}' ]}",
    "<a href=\"#\" class=\"btn btn-primary\">Sam</a>");
Check("youtube embed contains id",
    "{% assign html = '' %}{[ youtube id:'abc123' ]}",
    "<div class=\"embed-responsive embed-responsive-16by9\"><iframe width=\"100%\" src=\"https://www.youtube.com/embed/abc123\" frameborder=\"0\" allowfullscreen></iframe></div>");
CheckError("unknown shortcode", "{[ sparkle ]}", "Unknown shortcode");
CheckError("unclosed shortcode marker", "{[ alert", "Unclosed shortcode");
CheckError("missing end shortcode", "{[ alert ]}oops", "endalert");

var accordion = "{[ accordion ]}[[ item title:'One' ]]First[[ enditem ]][[ item title:'Two' ]]Second[[ enditem ]]{[ endaccordion ]}";
CheckContains("accordion has both items", accordion, "One");
CheckContains("accordion second item", accordion, "Second");
CheckContains("kpis renders values",
    "{[ kpis ]}[[ item value:'42' label:'Groups' ]][[ enditem ]]{[ endkpis ]}", "42");

// ---------------- cycle / increment / decrement / tablerow ----------------
Check("cycle alternates",
    "{% for c in Campuses %}{% cycle 'odd', 'even' %}{% endfor %}", "oddevenodd");
Check("named cycle groups are independent",
    "{% cycle 'g1': 'a', 'b' %}{% cycle 'g2': 'a', 'b' %}{% cycle 'g1': 'a', 'b' %}", "aab");
Check("increment", "{% increment i %}{% increment i %}{% increment i %}", "012");
Check("decrement", "{% decrement d %}{% decrement d %}", "-1-2");
Check("tablerow",
    "{% tablerow c in Campuses cols:2 %}{{ c.Name }}{% endtablerow %}",
    "<tr class=\"row1\"><td class=\"col1\">The Loop</td><td class=\"col2\">Cypress</td></tr><tr class=\"row2\"><td class=\"col1\">Downtown</td></tr>");

// ---------------- WithFallback + comment semantics ----------------
Check("WithFallback with value", "{{ Person.NickName | WithFallback:'Hi ',' friend' }}", "Hi Sam");
Check("WithFallback empty", "{{ Person.MiddleName | WithFallback:'Hi ','friend' }}", "friend");
Check("comment content is not parsed", "a{% comment %}{{ | totally }} {% bogus %}{% endcomment %}b", "ab");

// ---------------- Report ----------------
Console.WriteLine($"\n{passed} passed, {failed} failed");
foreach (var failure in failures)
{
    Console.WriteLine($"  FAIL {failure}");
}
return failed == 0 ? 0 : 1;
