<script setup lang="ts">
import { computed } from 'vue'
import { LESSONS, type Lesson, type LessonCheck } from '../lessons'

const props = defineProps<{
  currentId: string
  completed: Set<string>
  checkResults: { check: LessonCheck; passed: boolean }[] | null
}>()

const emit = defineEmits<{
  (e: 'select', lesson: Lesson): void
  (e: 'check'): void
}>()

const current = computed(() => LESSONS.find((l) => l.id === props.currentId) ?? LESSONS[0])
const allPassed = computed(() => props.checkResults?.every((r) => r.passed) ?? false)
const nextLesson = computed(() => {
  const index = LESSONS.findIndex((l) => l.id === props.currentId)
  return index >= 0 && index < LESSONS.length - 1 ? LESSONS[index + 1] : null
})
</script>

<template>
  <div class="learn-panel">
    <div class="lesson-list">
      <button
        v-for="lesson in LESSONS"
        :key="lesson.id"
        class="lesson-chip"
        :class="{ active: lesson.id === currentId, done: completed.has(lesson.id) }"
        @click="emit('select', lesson)"
      >
        {{ completed.has(lesson.id) ? '✓' : '' }} {{ lesson.title.split('.')[0] }}
      </button>
    </div>

    <div class="lesson-body">
      <h3>{{ current.title }}</h3>
      <p v-for="(paragraph, i) in current.teach.split('\n\n')" :key="i">{{ paragraph }}</p>

      <button class="check-button" @click="emit('check')">Check my Lava</button>

      <div v-if="checkResults" class="check-results">
        <div v-for="(result, i) in checkResults" :key="i" class="check-line" :class="{ pass: result.passed }">
          {{ result.passed ? '✓' : '✗' }} {{ result.check.hint }}
        </div>
        <div v-if="allPassed" class="lesson-done">
          🌋 Nailed it!
          <button v-if="nextLesson" class="check-button" @click="emit('select', nextLesson)">
            Next: {{ nextLesson.title }}
          </button>
          <span v-else>You finished the track; go write some real Lava.</span>
        </div>
      </div>
    </div>
  </div>
</template>
