<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import LavaEditor from './components/LavaEditor.vue'
import LintPanel from './components/LintPanel.vue'
import FilterReference from './components/FilterReference.vue'
import LearnPanel from './components/LearnPanel.vue'
import RockConnectPanel from './components/RockConnectPanel.vue'
import { LESSONS, type Lesson, type LessonCheck } from './lessons'
import {
  debounce,
  fetchFilters,
  fetchSampleContext,
  lintTemplate,
  renderTemplate,
  rockStatus,
  type Engine,
  type FilterInfo,
  type LintIssue,
  type RockStatus,
} from './api'

const DEFAULT_TEMPLATE = `{% comment %} Welcome to the Lava Playground! {% endcomment %}
{% assign campusCount = Campuses | Size %}

<h2>{{ GlobalAttributes.OrganizationName }}</h2>
<p>
  Hi {{ Person.NickName }}! You lead
  {{ Person.Groups | Where:'Role','Leader' | Size }}
  {{ 'group' | PluralizeForQuantity:1 }} at
  {{ Person.Campus.Name }}.
</p>

<h3>Our {{ campusCount | NumberToWords }} campuses</h3>
<ul>
{% for campus in Campuses %}
  <li>
    {{ forloop.index | NumberToOrdinal }}:
    <strong>{{ campus.Name }}</strong> ({{ campus.City }})
    {% if campus.IsOriginal %} — where it all started{% endif %}
  </li>
{% endfor %}
</ul>

{[ kpis ]}
[[ item value:"{{ Campuses | Sum:'Attendance' | Format:'#,##0' }}" label:'Weekend attendance' ]][[ enditem ]]
[[ item value:"{{ Person.Groups | Size }}" label:"{{ Person.NickName | Possessive }} groups" ]][[ enditem ]]
{[ endkpis ]}

{% person where:'ConnectionStatus == "Member" && CampusId == 1' iterator:'members' %}
<p>Members at The Loop (sample data):
{% for m in members %}{{ m.NickName }}{% unless forloop.last %}, {% endunless %}{% endfor %}</p>
{% endperson %}

<p>Rendered {{ 'Now' | Date:'dddd, MMMM d, yyyy' }} in the year {{ 'Now' | Date:'yyyy' | NumberToRomanNumerals }}.</p>
`

const template = ref(DEFAULT_TEMPLATE)
const output = ref('')
const renderError = ref<string | null>(null)
const elapsedMs = ref(0)
const issues = ref<LintIssue[]>([])
const filters = ref<FilterInfo[]>([])
const contextJson = ref('')
const apiUp = ref(false)
const linterUp = ref(false)
const previewTab = ref<'rendered' | 'raw'>('rendered')
const sidebarTab = ref<'filters' | 'context'>('filters')

// App mode: free-form playground or the guided lesson track.
const appMode = ref<'play' | 'learn'>('play')

// Render engine: the local C# engine, or the user's real Rock server.
const engine = ref<Engine>('local')
const rock = ref<RockStatus>({ connected: false, baseUrl: null, authType: null })
const showRockPanel = ref(false)

// Learn mode state.
const currentLessonId = ref(localStorage.getItem('learn.current') ?? LESSONS[0].id)
const completed = ref(new Set<string>(JSON.parse(localStorage.getItem('learn.completed') ?? '[]')))
const checkResults = ref<{ check: LessonCheck; passed: boolean }[] | null>(null)
const playgroundTemplate = ref(DEFAULT_TEMPLATE)

const engineLabel = computed(() =>
  engine.value === 'rock' && rock.value.connected ? `rock: ${rock.value.baseUrl}` : 'local engine')

async function runRender(source: string) {
  try {
    const result = await renderTemplate(source, engine.value)
    apiUp.value = true
    renderError.value = result.error
    if (result.error === null) {
      output.value = result.output ?? ''
      elapsedMs.value = result.elapsedMs
    }
  } catch {
    apiUp.value = false
  }
}

async function runLint(source: string) {
  try {
    const result = await lintTemplate(source)
    linterUp.value = true
    issues.value = result.issues
  } catch {
    linterUp.value = false
  }
}

const refresh = debounce((source: string) => {
  void runRender(source)
  void runLint(source)
}, 300)

function onTemplateChange(value: string) {
  template.value = value
  refresh(value)
}

function insertExample(example: string) {
  onTemplateChange(template.value + '\n' + example + '\n')
}

function setEngine(next: Engine) {
  if (next === 'rock' && !rock.value.connected) {
    showRockPanel.value = true
    return
  }
  engine.value = next
  localStorage.setItem('engine', next)
  void runRender(template.value)
}

function onRockStatus(status: RockStatus) {
  rock.value = status
  if (status.connected) {
    engine.value = 'rock'
  } else if (engine.value === 'rock') {
    engine.value = 'local'
  }
  void runRender(template.value)
}

function setMode(mode: 'play' | 'learn') {
  if (appMode.value === mode) return
  if (mode === 'learn') {
    playgroundTemplate.value = template.value
    openLesson(LESSONS.find((l) => l.id === currentLessonId.value) ?? LESSONS[0])
  } else {
    onTemplateChange(playgroundTemplate.value)
  }
  appMode.value = mode
}

function openLesson(lesson: Lesson) {
  currentLessonId.value = lesson.id
  localStorage.setItem('learn.current', lesson.id)
  checkResults.value = null
  onTemplateChange(lesson.starter)
}

async function checkLesson() {
  const lesson = LESSONS.find((l) => l.id === currentLessonId.value)
  if (!lesson) return
  // Always check against the local engine so lessons behave the same everywhere.
  const result = await renderTemplate(template.value, 'local')
  const rendered = result.error === null ? (result.output ?? '') : ''
  checkResults.value = lesson.checks.map((check) => ({
    check,
    passed:
      check.type === 'template-includes'
        ? template.value.includes(check.value)
        : check.type === 'output-includes'
          ? rendered.includes(check.value)
          : new RegExp(check.value, 's').test(rendered),
  }))
  if (checkResults.value.every((r) => r.passed)) {
    completed.value.add(lesson.id)
    completed.value = new Set(completed.value)
    localStorage.setItem('learn.completed', JSON.stringify([...completed.value]))
  }
}

onMounted(async () => {
  void runRender(template.value)
  void runLint(template.value)
  try {
    rock.value = await rockStatus()
    // Remote-first: when a Rock connection is live (e.g. auto-connected via
    // ROCK_BASE_URL env vars), default to it unless the user chose local.
    if (rock.value.connected && localStorage.getItem('engine') !== 'local') {
      engine.value = 'rock'
      void runRender(template.value)
    }
    filters.value = await fetchFilters()
    contextJson.value = JSON.stringify(await fetchSampleContext(), null, 2)
  } catch {
    // Status dots already reflect availability.
  }
})
</script>

<template>
  <header class="topbar">
    <h1>🌋 Lava Playground</h1>
    <div class="tab-buttons">
      <button :class="{ active: appMode === 'play' }" @click="setMode('play')">Playground</button>
      <button :class="{ active: appMode === 'learn' }" @click="setMode('learn')">Learn</button>
    </div>
    <div class="tab-buttons">
      <button :class="{ active: engine === 'local' }" @click="setEngine('local')">Local</button>
      <button :class="{ active: engine === 'rock' }" @click="setEngine('rock')">
        {{ rock.connected ? 'Rock server' : 'Rock server…' }}
      </button>
      <button v-if="rock.connected" title="Connection settings" @click="showRockPanel = true">⚙</button>
    </div>
    <span class="status">
      <span class="dot" :class="apiUp ? 'ok' : 'err'"></span>{{ engineLabel }}
      &nbsp;
      <span class="dot" :class="linterUp ? 'ok' : 'err'"></span>lint (python)
    </span>
  </header>

  <div class="layout" :class="{ 'learn-layout': appMode === 'learn' }">
    <LearnPanel
      v-if="appMode === 'learn'"
      class="pane sidebar"
      :current-id="currentLessonId"
      :completed="completed"
      :check-results="checkResults"
      @select="openLesson"
      @check="checkLesson"
    />

    <section class="pane">
      <div class="pane-header">Template</div>
      <LavaEditor :model-value="template" @update:model-value="onTemplateChange" />
    </section>

    <section class="pane">
      <div class="pane-header">
        Preview
        <div class="tab-buttons">
          <button :class="{ active: previewTab === 'rendered' }" @click="previewTab = 'rendered'">Rendered</button>
          <button :class="{ active: previewTab === 'raw' }" @click="previewTab = 'raw'">Raw</button>
        </div>
        <span class="meta" v-if="!renderError">{{ elapsedMs.toFixed(1) }} ms</span>
      </div>
      <div v-if="renderError" class="preview error-view">{{ renderError }}</div>
      <!-- eslint-disable-next-line vue/no-v-html — rendering the user's own template output is the point -->
      <div v-else-if="previewTab === 'rendered'" class="preview" v-html="output"></div>
      <div v-else class="preview raw">{{ output }}</div>
    </section>

    <aside v-if="appMode === 'play'" class="pane sidebar">
      <div class="pane-header">
        <div class="tab-buttons">
          <button :class="{ active: sidebarTab === 'filters' }" @click="sidebarTab = 'filters'">Filters</button>
          <button :class="{ active: sidebarTab === 'context' }" @click="sidebarTab = 'context'">Sample Data</button>
        </div>
      </div>
      <FilterReference v-if="sidebarTab === 'filters'" :filters="filters" @insert="insertExample" />
      <div v-else class="context-json">{{ contextJson }}</div>
    </aside>
  </div>

  <LintPanel :issues="issues" :ready="linterUp" />

  <RockConnectPanel v-if="showRockPanel" @status="onRockStatus" @close="showRockPanel = false" />
</template>
