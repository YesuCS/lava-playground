<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FilterInfo } from '../api'

const props = defineProps<{ filters: FilterInfo[] }>()
const emit = defineEmits<{ (e: 'insert', example: string): void }>()

const search = ref('')

const grouped = computed(() => {
  const term = search.value.toLowerCase()
  const visible = props.filters.filter(
    (f) => f.name.toLowerCase().includes(term) || f.description.toLowerCase().includes(term),
  )
  const groups = new Map<string, FilterInfo[]>()
  for (const filter of visible) {
    const list = groups.get(filter.category) ?? []
    list.push(filter)
    groups.set(filter.category, list)
  }
  return groups
})

function insertExample(filter: FilterInfo) {
  // Examples look like "{{ 'x' | Filter }} → result"; insert only the Lava part.
  const lava = filter.example.split('→')[0].trim()
  emit('insert', lava)
}
</script>

<template>
  <div>
    <input v-model="search" class="sidebar-search" placeholder="Search filters…" />
    <div v-for="[category, list] in grouped" :key="category" class="filter-group">
      <div class="filter-category">{{ category }}</div>
      <div
        v-for="filter in list"
        :key="filter.name"
        class="filter-row"
        :title="'Click to insert an example'"
        @click="insertExample(filter)"
      >
        <div class="name">{{ filter.name }}</div>
        <div class="desc">{{ filter.description }}</div>
        <div class="example">{{ filter.example }}</div>
      </div>
    </div>
  </div>
</template>
