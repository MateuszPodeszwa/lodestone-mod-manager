<script setup lang="ts">
const app = useAppConfig()
const toast = useToast()
const { data: versions } = await useFetch('/api/releases/versions')

useSeoMeta({
  title: 'Report a bug',
  description: "Found something off in Lodestone? Tell us what happened and we'll open a tracked issue on GitHub.",
})

const issuesUrl = computed(() => `${app.links.github.replace(/\/+$/, '')}/issues`)

type Kind = 'bug' | 'feature' | 'question'
const form = reactive({
  type: 'bug' as Kind,
  title: '',
  desc: '',
  steps: '',
  logs: '',
  version: '',
  os: 'Windows 11',
  searched: false,
})
watchEffect(() => {
  if (!form.version && versions.value?.length) {
    const latest = versions.value.find((v) => v.latest) ?? versions.value[0]
    if (latest) form.version = `${latest.version} (latest)`
  }
})

const submitted = ref(false)
const lastUrl = ref('')

function buildUrl() {
  const labels = form.type === 'bug' ? 'bug' : form.type === 'feature' ? 'enhancement' : 'question'
  const body = [
    '### What happened?',
    form.desc.trim() || '_(not provided)_',
    '',
    '### Steps to reproduce',
    form.steps.trim() || '_(not provided)_',
    '',
    '### Environment',
    `- Lodestone: ${form.version || 'unknown'}`,
    `- OS: ${form.os}`,
    '',
    form.logs.trim() ? '### Logs\n```\n' + form.logs.trim() + '\n```' : '',
  ].join('\n')
  const base = app.links.github.replace(/\/+$/, '')
  return `${base}/issues/new?title=${encodeURIComponent(form.title.trim())}&labels=${encodeURIComponent(labels)}&body=${encodeURIComponent(body)}`
}

function submit() {
  if (!form.title.trim()) {
    toast.error('Add a title', 'A short summary is required to open an issue')
    return
  }
  const url = buildUrl()
  lastUrl.value = url
  if (import.meta.client) window.open(url, '_blank')
  submitted.value = true
  toast.success('Opening GitHub', 'Your report is pre-filled — review and submit')
}
function reset() {
  Object.assign(form, { type: 'bug', title: '', desc: '', steps: '', logs: '', searched: false })
  submitted.value = false
  lastUrl.value = ''
}

const seg = (active: boolean) =>
  active
    ? 'px-3.5 py-2 rounded-[7px] cursor-pointer text-[13.5px] font-semibold text-[#10130f] bg-brand border-none'
    : 'px-3.5 py-2 rounded-[7px] cursor-pointer text-[13.5px] font-medium text-[#c7c7cc] bg-transparent border-none'
const inputCls =
  'w-full mt-1.5 rounded-[9px] border border-white/10 bg-surface-3 px-3.5 py-2.5 text-sm text-[#ededf0] outline-none focus:border-brand'

const tips = [
  "Make sure you're on the latest version.",
  'Search open issues to avoid duplicates.',
  'Include exact steps — it really helps.',
]
</script>

<template>
  <div>
    <header class="relative overflow-hidden px-6 pb-9 pt-14 sm:px-10" style="background: radial-gradient(115% 130% at 50% 0%, #26352c 0%, #1a1c21 50%, #141519 100%)">
      <div class="grid-backdrop pointer-events-none absolute inset-0" style="-webkit-mask-image: radial-gradient(circle at 50% 0%, #000 0%, transparent 65%); mask-image: radial-gradient(circle at 50% 0%, #000 0%, transparent 65%)" />
      <div class="relative mx-auto max-w-[1000px]">
        <div class="eyebrow">Report a bug</div>
        <h1 class="mt-3 font-pixel font-bold leading-[1.02] tracking-[0.5px] text-[#f5f5f7]" style="font-size: clamp(32px, 4.6vw, 52px)">Found something off?</h1>
        <p class="mt-3.5 max-w-[560px] text-[16.5px] leading-relaxed text-muted">Tell us what happened and we'll open a tracked issue on GitHub. The more detail, the faster the fix.</p>
      </div>
    </header>

    <main class="mx-auto grid max-w-[1000px] items-start gap-7 px-6 pb-20 pt-10 sm:px-10 lg:grid-cols-[1fr_300px]">
      <!-- form / submitted -->
      <div v-if="!submitted" class="rounded-2xl border border-white/[0.08] bg-[rgba(28,28,32,0.8)] p-7">
        <div class="mb-2.5 text-[13px] font-semibold text-[#dcdce0]">What kind of report is this?</div>
        <div class="inline-flex gap-1 rounded-[10px] border border-white/[0.07] bg-[#1f1f23] p-1">
          <button :class="seg(form.type === 'bug')" @click="form.type = 'bug'">🐛 Bug</button>
          <button :class="seg(form.type === 'feature')" @click="form.type = 'feature'">✨ Feature</button>
          <button :class="seg(form.type === 'question')" @click="form.type = 'question'">❔ Question</button>
        </div>

        <div class="mt-5">
          <label class="text-[13px] font-semibold text-[#dcdce0]">Title <span class="text-[#e2503f]">*</span></label>
          <input v-model="form.title" :class="inputCls" placeholder="Short summary, e.g. “Crash when dropping a .zip on Home”" />
        </div>
        <div class="mt-4">
          <label class="text-[13px] font-semibold text-[#dcdce0]">What happened?</label>
          <textarea v-model="form.desc" rows="4" :class="inputCls + ' resize-y leading-relaxed'" placeholder="Describe the problem. What did you expect, and what happened instead?" />
        </div>
        <div class="mt-4">
          <label class="text-[13px] font-semibold text-[#dcdce0]">Steps to reproduce</label>
          <textarea v-model="form.steps" rows="3" :class="inputCls + ' resize-y font-mono leading-relaxed'" placeholder="1. Open Lodestone&#10;2. Drag a .zip onto the window&#10;3. …" />
        </div>
        <div class="mt-4 grid gap-3.5 sm:grid-cols-2">
          <div>
            <label class="text-[13px] font-semibold text-[#dcdce0]">Lodestone version</label>
            <select v-model="form.version" :class="inputCls + ' cursor-pointer'">
              <option v-for="v in versions" :key="v.tag" :value="v.latest ? `${v.version} (latest)` : v.version">
                {{ v.version }}{{ v.latest ? ' (latest)' : '' }}{{ v.prerelease ? ' (pre-release)' : '' }}
              </option>
              <option value="Other">Other</option>
            </select>
          </div>
          <div>
            <label class="text-[13px] font-semibold text-[#dcdce0]">Operating system</label>
            <select v-model="form.os" :class="inputCls + ' cursor-pointer'">
              <option>Windows 11</option>
              <option>Windows 10</option>
              <option>macOS</option>
              <option>Linux</option>
            </select>
          </div>
        </div>
        <div class="mt-4">
          <label class="text-[13px] font-semibold text-[#dcdce0]">Logs <span class="font-normal text-faint">(optional)</span></label>
          <textarea v-model="form.logs" rows="3" :class="inputCls + ' resize-y font-mono text-[13px] leading-relaxed'" placeholder="Paste anything from Settings → Open logs that looks relevant." />
        </div>

        <label class="mt-4 flex cursor-pointer items-start gap-2.5">
          <input v-model="form.searched" type="checkbox" class="mt-0.5 h-5 w-5 flex-none accent-brand" />
          <span class="text-[13.5px] leading-snug text-[#a6a6ae]">I searched <a :href="issuesUrl" target="_blank" rel="noopener" class="text-brand no-underline hover:underline">existing issues</a> and didn't find this one.</span>
        </label>

        <div class="mt-6 flex flex-wrap items-center gap-3 border-t border-white/[0.07] pt-[22px]">
          <button class="btn-primary mc-clip px-5 py-3 text-[15px]" @click="submit">
            <svg width="17" height="17" viewBox="0 0 24 24" fill="#10221a"><path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.1.68-.22.68-.49l-.01-1.9c-2.78.62-3.37-1.21-3.37-1.21-.46-1.18-1.11-1.5-1.11-1.5-.91-.64.07-.62.07-.62 1 .07 1.53 1.06 1.53 1.06.9 1.56 2.36 1.11 2.94.85.09-.67.35-1.11.63-1.37-2.22-.26-4.55-1.14-4.55-5.06 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.3.1-2.72 0 0 .84-.27 2.75 1.05a9.4 9.4 0 0 1 5 0c1.91-1.32 2.75-1.05 2.75-1.05.55 1.42.2 2.46.1 2.72.64.72 1.03 1.63 1.03 2.75 0 3.93-2.34 4.79-4.57 5.05.36.32.68.94.68 1.9l-.01 2.82c0 .27.18.6.69.49A10.02 10.02 0 0 0 22 12.25C22 6.58 17.52 2 12 2Z" /></svg>
            Open issue on GitHub
          </button>
          <span class="text-[12.5px] text-faint">Opens GitHub with everything pre-filled — review before posting.</span>
        </div>
      </div>

      <div v-else class="rounded-2xl border border-white/[0.08] bg-[rgba(28,28,32,0.8)] p-10 text-center">
        <div class="mx-auto flex h-[60px] w-[60px] items-center justify-center rounded-full bg-brand/15">
          <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="#5ac26d" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 4 4 10-10" /></svg>
        </div>
        <div class="mt-4 font-pixel text-2xl font-bold text-[#f3f3f5]">Thanks for the report!</div>
        <div class="mx-auto mt-2.5 max-w-[380px] text-[14.5px] leading-relaxed text-muted">A GitHub tab should have opened with your report pre-filled. Hit <span class="font-semibold text-[#f0f0f2]">Submit new issue</span> there to post it. If nothing opened, use the button below.</div>
        <div class="mt-6 flex flex-wrap justify-center gap-2.5">
          <a :href="lastUrl" target="_blank" rel="noopener" class="btn-primary rounded-[9px] px-5 py-3 text-sm">Open GitHub again</a>
          <button class="rounded-[9px] border border-white/[0.12] bg-transparent px-5 py-3 text-sm font-semibold text-soft hover:bg-white/[0.06]" @click="reset">Report another</button>
        </div>
      </div>

      <!-- sidebar -->
      <aside class="flex flex-col gap-4">
        <div class="rounded-2xl border border-white/[0.07] bg-[rgba(28,28,32,0.6)] p-5">
          <div class="font-pixel text-[13px] font-semibold uppercase tracking-wide text-faint">Before you report</div>
          <div class="mt-3.5 flex flex-col gap-2.5">
            <div v-for="tip in tips" :key="tip" class="flex items-start gap-2.5">
              <svg class="mt-0.5 flex-none" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#5ac26d" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 4 4 10-10" /></svg>
              <span class="text-[13px] leading-snug text-muted">{{ tip }}</span>
            </div>
          </div>
        </div>
        <div class="rounded-2xl border border-white/[0.07] bg-[rgba(28,28,32,0.6)] p-5">
          <div class="font-pixel text-[13px] font-semibold uppercase tracking-wide text-faint">Links</div>
          <div class="mt-3.5 flex flex-col gap-3">
            <a :href="issuesUrl" target="_blank" rel="noopener" class="flex items-center gap-2.5 text-[13.5px] text-[#b9b9bf] no-underline hover:text-brand">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.1.68-.22.68-.49l-.01-1.9c-2.78.62-3.37-1.21-3.37-1.21-.46-1.18-1.11-1.5-1.11-1.5-.91-.64.07-.62.07-.62 1 .07 1.53 1.06 1.53 1.06.9 1.56 2.36 1.11 2.94.85.09-.67.35-1.11.63-1.37-2.22-.26-4.55-1.14-4.55-5.06 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.3.1-2.72 0 0 .84-.27 2.75 1.05a9.4 9.4 0 0 1 5 0c1.91-1.32 2.75-1.05 2.75-1.05.55 1.42.2 2.46.1 2.72.64.72 1.03 1.63 1.03 2.75 0 3.93-2.34 4.79-4.57 5.05.36.32.68.94.68 1.9l-.01 2.82c0 .27.18.6.69.49A10.02 10.02 0 0 0 22 12.25C22 6.58 17.52 2 12 2Z" /></svg>Browse open issues
            </a>
            <DiscordLink variant="text" />
            <NuxtLink to="/changelog" class="flex items-center gap-2.5 text-[13.5px] text-[#b9b9bf] no-underline hover:text-brand">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8 6h11M8 12h11M8 18h11M3.5 6h.01M3.5 12h.01M3.5 18h.01" /></svg>Maybe it's fixed — changelog
            </NuxtLink>
          </div>
        </div>
      </aside>
    </main>
  </div>
</template>
