<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { rockConnect, rockDisconnect, rockStatus, type RockStatus } from '../api'

const emit = defineEmits<{
  (e: 'status', status: RockStatus): void
  (e: 'close'): void
}>()

const baseUrl = ref(localStorage.getItem('rock.baseUrl') ?? '')
const authMode = ref<'apiKey' | 'login'>((localStorage.getItem('rock.authMode') as 'apiKey' | 'login') ?? 'apiKey')
const apiKey = ref('')
const username = ref(localStorage.getItem('rock.username') ?? '')
const password = ref('')
const busy = ref(false)
const error = ref('')
const status = ref<RockStatus>({ connected: false, baseUrl: null, authType: null })

onMounted(async () => {
  try {
    status.value = await rockStatus()
  } catch {
    /* API offline; the status dots already show it */
  }
})

async function connect() {
  busy.value = true
  error.value = ''
  try {
    status.value = await rockConnect({
      baseUrl: baseUrl.value,
      apiKey: authMode.value === 'apiKey' ? apiKey.value : undefined,
      username: authMode.value === 'login' ? username.value : undefined,
      password: authMode.value === 'login' ? password.value : undefined,
    })
    localStorage.setItem('rock.baseUrl', baseUrl.value)
    localStorage.setItem('rock.authMode', authMode.value)
    localStorage.setItem('rock.username', username.value)
    emit('status', status.value)
    emit('close')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}

async function disconnect() {
  status.value = await rockDisconnect()
  emit('status', status.value)
}
</script>

<template>
  <div class="modal-backdrop" @click.self="emit('close')">
    <div class="modal">
      <h3>Connect to your Rock server</h3>
      <p class="modal-note">
        Remote mode sends templates to your server's
        <code>POST /api/Lava/RenderTemplate</code> endpoint, so entity commands, CurrentPerson,
        attributes, and the full filter set all run for real. Credentials stay in this backend's
        memory only; nothing is persisted.
      </p>

      <div v-if="status.connected" class="connected-box">
        Connected to <strong>{{ status.baseUrl }}</strong> ({{ status.authType === 'apiKey' ? 'REST key' : 'login' }})
        <button class="check-button" @click="disconnect">Disconnect</button>
      </div>

      <label>Server URL</label>
      <input v-model="baseUrl" placeholder="https://rock.mychurch.org" />

      <div class="tab-buttons" style="margin: 0.5rem 0">
        <button :class="{ active: authMode === 'apiKey' }" @click="authMode = 'apiKey'">REST API key</button>
        <button :class="{ active: authMode === 'login' }" @click="authMode = 'login'">Username / password</button>
      </div>

      <template v-if="authMode === 'apiKey'">
        <label>REST key (Admin Tools → Security → REST Keys)</label>
        <input v-model="apiKey" type="password" placeholder="Authorization-Token value" />
      </template>
      <template v-else>
        <label>Username</label>
        <input v-model="username" />
        <label>Password</label>
        <input v-model="password" type="password" @keydown.enter="connect" />
      </template>

      <p v-if="error" class="error-text">{{ error }}</p>

      <div class="modal-actions">
        <button class="check-button" :disabled="busy" @click="connect">
          {{ busy ? 'Connecting…' : 'Connect' }}
        </button>
        <button class="check-button secondary" @click="emit('close')">Close</button>
      </div>
    </div>
  </div>
</template>
