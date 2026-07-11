<script setup lang="ts">
import type { LintIssue } from '../api'

defineProps<{ issues: LintIssue[]; ready: boolean }>()
</script>

<template>
  <div class="lint-panel">
    <div class="pane-header">
      Lava Lint
      <span class="meta" v-if="issues.length">{{ issues.length }} issue{{ issues.length === 1 ? '' : 's' }}</span>
    </div>
    <div v-if="!ready" class="lint-clean" style="color: var(--text-dim)">Linter warming up…</div>
    <div v-else-if="issues.length === 0" class="lint-clean">✓ No issues found</div>
    <div v-else>
      <div v-for="(issue, index) in issues" :key="index" class="lint-item">
        <span class="loc">{{ issue.line }}:{{ issue.col }}</span>
        <span class="sev" :class="issue.severity">{{ issue.severity }}</span>
        <span>
          {{ issue.message }}
          <span v-if="issue.suggestion" class="suggestion">{{ issue.suggestion }}</span>
        </span>
      </div>
    </div>
  </div>
</template>
