<script setup lang="ts">
import { onMounted, ref } from 'vue'
import LavaEditor from './components/LavaEditor.vue'
import LintPanel from './components/LintPanel.vue'
import FilterReference from './components/FilterReference.vue'
import {
  debounce,
  fetchFilters,
  fetchSampleContext,
  lintTemplate,
  renderTemplate,
  type FilterInfo,
  type LintIssue,
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

async function runRender(source: string) {
  try {
    const result = await renderTemplate(source)
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

onMounted(async () => {
  void runRender(template.value)
  void runLint(template.value)
  try {
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
    <span class="tagline">write Lava, watch it flow</span>
    <span class="status">
      <span class="dot" :class="apiUp ? 'ok' : 'err'"></span>render api (C#)
      &nbsp;
      <span class="dot" :class="linterUp ? 'ok' : 'err'"></span>lint api (python)
    </span>
  </header>

  <div class="layout">
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
      <!-- eslint-disable-next-line vue/no-v-html — rendering user's own template output is the point -->
      <div v-else-if="previewTab === 'rendered'" class="preview" v-html="output"></div>
      <div v-else class="preview raw">{{ output }}</div>
    </section>

    <aside class="pane sidebar">
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
</template>
