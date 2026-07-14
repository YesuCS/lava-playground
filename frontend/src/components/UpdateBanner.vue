<script setup lang="ts">
import { onMounted, ref } from 'vue'

// These are baked in only for the desktop build (see desktop/build/build-all.sh).
// On the web build they're undefined and the banner stays inert.
const repo = import.meta.env.VITE_UPDATE_REPO
const builtCommit = import.meta.env.VITE_BUILD_COMMIT

const show = ref(false)
const repoUrl = ref('')

async function openInBrowser(url: string) {
  // Packaged app: open in the system browser via the Rust command.
  // Web build: fall back to a normal new tab.
  const tauri = (window as unknown as { __TAURI__?: { core?: { invoke: (c: string, a: unknown) => Promise<unknown> } } }).__TAURI__
  if (tauri?.core?.invoke) {
    try {
      await tauri.core.invoke('open_url', { url })
      return
    } catch {
      /* fall through */
    }
  }
  window.open(url, '_blank', 'noopener')
}

onMounted(async () => {
  if (!repo || !builtCommit) return
  try {
    // The .sha media type returns just the latest commit hash as plain text.
    const res = await fetch(`https://api.github.com/repos/${repo}/commits/main`, {
      headers: { Accept: 'application/vnd.github.sha' },
    })
    if (!res.ok) return
    const latest = (await res.text()).trim()
    if (latest && latest !== builtCommit) {
      repoUrl.value = `https://github.com/${repo}`
      show.value = true
    }
  } catch {
    // Offline or rate-limited — a failed update check should never disrupt use.
  }
})
</script>

<template>
  <div v-if="show" class="update-banner">
    <span>🔔 A newer version of Lava Playground is available.</span>
    <button @click="openInBrowser(repoUrl)">View on GitHub</button>
    <span class="hint">Pull the repo and re-run <code>desktop/build/build-all.sh</code> to update.</span>
    <button class="close" aria-label="Dismiss" @click="show = false">✕</button>
  </div>
</template>

<style scoped>
.update-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 16px;
  font-size: 13px;
  background: #3a2410;
  color: #ffd7a0;
  border-bottom: 1px solid #5a3a18;
}
.update-banner button {
  background: #ff6e14;
  color: #1a1a1a;
  border: none;
  padding: 4px 10px;
  border-radius: 5px;
  cursor: pointer;
  font-weight: 600;
}
.update-banner .close {
  background: transparent;
  color: #ffd7a0;
  margin-left: auto;
  font-weight: 400;
}
.update-banner .hint {
  opacity: 0.8;
}
.update-banner code {
  background: rgba(0, 0, 0, 0.3);
  padding: 1px 5px;
  border-radius: 3px;
}
</style>
