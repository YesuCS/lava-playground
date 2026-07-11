// The Learn-mode lesson track. Each lesson has a short teach block, a
// starter template, and goal checks that run against the student's
// template and its rendered output.

export interface LessonCheck {
  type: 'output-includes' | 'template-includes' | 'output-matches'
  value: string
  hint: string
}

export interface Lesson {
  id: string
  title: string
  teach: string
  starter: string
  checks: LessonCheck[]
}

const CURRENT_YEAR = String(new Date().getFullYear())

export const LESSONS: Lesson[] = [
  {
    id: 'output',
    title: '1. Output tags',
    teach:
      'Lava wraps expressions in double curly braces. Anything inside {{ }} is evaluated and printed. ' +
      'Dot notation walks into objects: the sample data has a Person with a NickName.\n\n' +
      'Goal: greet the sample person by their nickname using {{ Person.NickName }}.',
    starter: 'Hello, world!\n',
    checks: [
      { type: 'template-includes', value: 'Person.NickName', hint: 'Use {{ Person.NickName }} in your template.' },
      { type: 'output-includes', value: 'Sam', hint: 'The output should contain the nickname "Sam".' },
    ],
  },
  {
    id: 'filters',
    title: '2. Your first filter',
    teach:
      'Filters transform a value and are chained with the pipe character. ' +
      '{{ Person.NickName | Upcase }} shouts the name back at you.\n\n' +
      'Goal: render the nickname in all caps with the Upcase filter.',
    starter: '{{ Person.NickName }}\n',
    checks: [
      { type: 'template-includes', value: 'Upcase', hint: 'Add | Upcase after the value.' },
      { type: 'output-includes', value: 'SAM', hint: 'The output should contain "SAM".' },
    ],
  },
  {
    id: 'filter-args',
    title: '3. Filter arguments',
    teach:
      "Filters take arguments after a colon: {{ 'Houston' | Left:3 }} → Hou. Multiple arguments are " +
      "separated by commas: {{ 'pew pew' | Replace:'pew','wow' }}.\n\n" +
      "Goal: make the nickname possessive with the Possessive filter (no arguments), then append the word ' campus' " +
      "using Append:' campus'.",
    starter: '{{ Person.NickName }}\n',
    checks: [
      { type: 'template-includes', value: 'Possessive', hint: 'Chain | Possessive onto the value.' },
      { type: 'output-includes', value: "Sam's campus", hint: `The output should read "Sam's campus".` },
    ],
  },
  {
    id: 'chaining',
    title: '4. Chaining filters',
    teach:
      'Filters run left to right, each feeding the next. ' +
      "{{ 'sam houston' | TitleCase | Possessive }} first title-cases, then adds the apostrophe.\n\n" +
      'Goal: render exactly "Sam Houston\'s" starting from the lowercase string \'sam houston\'.',
    starter: "{{ 'sam houston' }}\n",
    checks: [
      { type: 'template-includes', value: 'TitleCase', hint: 'Use TitleCase to capitalize each word.' },
      { type: 'output-includes', value: "Sam Houston's", hint: 'Chain Possessive after TitleCase.' },
    ],
  },
  {
    id: 'numbers',
    title: '5. Number filters',
    teach:
      'Numbers have their own filters: Plus, Minus, Times, DividedBy, Format, FormatAsCurrency. ' +
      '{{ 6 | Times:7 }} → 42.\n\n' +
      'Goal: compute 6 times 7 in Lava.',
    starter: 'The answer is \n',
    checks: [
      { type: 'template-includes', value: 'Times', hint: 'Use the Times filter.' },
      { type: 'output-includes', value: '42', hint: 'The output should contain 42.' },
    ],
  },
  {
    id: 'dates',
    title: '6. Dates',
    teach:
      "The string 'Now' is a keyword meaning the current date and time. The Date filter formats with .NET " +
      "format strings: {{ 'Now' | Date:'dddd, MMMM d, yyyy' }}.\n\n" +
      "Goal: print the current year using 'Now' and Date:'yyyy'.",
    starter: "Today is {{ 'Now' }}\n",
    checks: [
      { type: 'template-includes', value: 'Date:', hint: "Use the Date filter with a format string, e.g. Date:'yyyy'." },
      { type: 'output-includes', value: CURRENT_YEAR, hint: 'The output should contain the current year.' },
    ],
  },
  {
    id: 'logic',
    title: '7. Conditionals',
    teach:
      'Tags use {% %} braces and control flow. {% if Person.Age > 18 %}adult{% else %}minor{% endif %} — ' +
      'with elsif for more branches, and operators ==, !=, >, <, and, or, contains.\n\n' +
      'Goal: use an if/else on Person.Age so the output says "adult".',
    starter: 'Sam is \n',
    checks: [
      { type: 'template-includes', value: '{% if', hint: 'Open a condition with {% if Person.Age > 18 %}.' },
      { type: 'template-includes', value: 'endif', hint: 'Close it with {% endif %}.' },
      { type: 'output-includes', value: 'adult', hint: 'The output should contain "adult".' },
    ],
  },
  {
    id: 'loops',
    title: '8. Loops',
    teach:
      'The for tag repeats its body for each item: {% for c in Campuses %}{{ c.Name }}{% endfor %}. ' +
      'Modifiers: reversed and limit:n.\n\n' +
      'Goal: loop over Campuses and print every campus name.',
    starter: 'Our campuses:\n',
    checks: [
      { type: 'template-includes', value: '{% for', hint: 'Use {% for c in Campuses %} ... {% endfor %}.' },
      { type: 'output-includes', value: 'The Loop', hint: 'Print each campus Name inside the loop.' },
      { type: 'output-includes', value: 'Sienna', hint: 'All four campuses should appear.' },
    ],
  },
  {
    id: 'forloop',
    title: '9. The forloop object',
    teach:
      'Inside a loop you get forloop.index (1-based), forloop.index0, forloop.first, forloop.last, and ' +
      'forloop.length. Great for numbering and separators.\n\n' +
      'Goal: number each campus using forloop.index.',
    starter: '{% for c in Campuses %}{{ c.Name }}\n{% endfor %}',
    checks: [
      { type: 'template-includes', value: 'forloop.index', hint: 'Print {{ forloop.index }} before the name.' },
      { type: 'output-matches', value: '1.*The Loop', hint: 'The Loop should be numbered 1.' },
    ],
  },
  {
    id: 'collections',
    title: '10. Collection filters',
    teach:
      "Arrays have LINQ-style filters: Where:'prop','value' keeps matching items, Select plucks a property, " +
      'Size counts. {{ Person.Groups | Where:\'Role\',\'Leader\' | Size }} counts groups Sam leads.\n\n' +
      'Goal: count how many groups Sam leads (the answer is 1).',
    starter: 'Sam leads {{ Person.Groups | Size }} groups\n',
    checks: [
      { type: 'template-includes', value: 'Where:', hint: "Filter with Where:'Role','Leader' before Size." },
      { type: 'output-matches', value: 'leads\\s+1', hint: 'The count should be 1 after filtering.' },
    ],
  },
  {
    id: 'sort',
    title: '11. Sort, Select, Join',
    teach:
      "Chain collection filters into little pipelines: Sort:'prop' orders (add 'desc' for descending), " +
      "Select:'prop' projects, Join:', ' glues into a sentence.\n\n" +
      'Goal: list campus names sorted by Attendance, biggest first. The Loop should come first.',
    starter: '{{ Campuses | Select:\'Name\' | Join:\', \' }}\n',
    checks: [
      { type: 'template-includes', value: 'Sort:', hint: "Add Sort:'Attendance','desc' before Select." },
      { type: 'output-matches', value: '^[^,]*The Loop', hint: 'The Loop (3200) should be the first name.' },
    ],
  },
  {
    id: 'entity-commands',
    title: '12. Entity commands',
    teach:
      "Entity commands query data: {% person where:'LastName == \"Houston\"' %} sets a person collection you " +
      "can loop over, with sort:'prop desc', limit:'n', id:'n', and iterator:'name' parameters. Operators: " +
      '==, !=, >, <, >=, <=, ^= (starts with), *= (contains), joined with && and ||. Locally these query the ' +
      'bundled sample database; on a real Rock server they query your actual data.\n\n' +
      'Goal: list everyone at campus 1 (CampusId == 1). You should see Sam and Liz.',
    starter: "{% person %}\n{% for p in person %}{{ p.NickName }} {% endfor %}\n{% endperson %}\n",
    checks: [
      { type: 'template-includes', value: 'where:', hint: "Add where:'CampusId == 1' to the person command." },
      { type: 'output-includes', value: 'Liz', hint: 'Liz should be in the results.' },
    ],
  },
  {
    id: 'shortcodes',
    title: '13. Shortcodes',
    teach:
      'Shortcodes are Lava macros with {[ ]} braces that emit formatted HTML: ' +
      "{[ alert type:'warning' ]}content{[ endalert ]}, {[ panel title:'x' ]}, {[ button text:'Go' link:'/x' ]}, " +
      'plus accordion and kpis with [[ item ]] child blocks.\n\n' +
      'Goal: wrap a message in an alert shortcode with type success.',
    starter: 'Great job!\n',
    checks: [
      { type: 'template-includes', value: '{[ alert', hint: 'Open with {[ alert type:\'success\' ]}.' },
      { type: 'template-includes', value: 'endalert', hint: 'Close with {[ endalert ]}.' },
      { type: 'output-includes', value: 'alert-success', hint: 'The rendered HTML should have the alert-success class.' },
    ],
  },
  {
    id: 'capstone',
    title: '14. Capstone',
    teach:
      'Put it together: assign stores a value ({% assign x = ... %}), capture stores rendered output, and ' +
      'everything you have learned composes.\n\n' +
      'Goal: build a template that uses an assign, a for loop, and an if — for example, a campus list that ' +
      'tags the original campus.',
    starter: '',
    checks: [
      { type: 'template-includes', value: '{% assign', hint: 'Use at least one {% assign %}.' },
      { type: 'template-includes', value: '{% for', hint: 'Use a {% for %} loop.' },
      { type: 'template-includes', value: '{% if', hint: 'Use an {% if %} somewhere.' },
      { type: 'output-matches', value: '.', hint: 'The template should render some output.' },
    ],
  },
]
