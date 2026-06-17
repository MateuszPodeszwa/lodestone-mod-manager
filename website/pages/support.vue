<script setup lang="ts">
const app = useAppConfig()
const help = app.helpPage

useSeoMeta({
  title: 'Help & support',
  description: 'Get help with Lodestone — FAQs, priority support for patrons, and how to report a problem.',
})

const openFaq = ref<number | null>(0)
const toggle = (i: number) => (openFaq.value = openFaq.value === i ? null : i)
</script>

<template>
  <div>
    <header class="relative overflow-hidden px-6 pb-12 pt-16 text-center sm:px-10" style="background: radial-gradient(115% 130% at 50% 0%, #2a2030 0%, #1a1c21 52%, #141519 100%)">
      <div class="grid-backdrop pointer-events-none absolute inset-0" style="-webkit-mask-image: radial-gradient(circle at 50% 0%, #000 0%, transparent 65%); mask-image: radial-gradient(circle at 50% 0%, #000 0%, transparent 65%)" />
      <div class="pointer-events-none absolute -right-[8%] top-[6%] h-[420px] w-[420px] animate-floatY rounded-full" style="background: radial-gradient(circle, rgba(226, 113, 154, 0.14), transparent 64%)" />
      <div class="relative mx-auto max-w-[720px]">
        <div class="font-pixel text-[13px] font-semibold uppercase tracking-[2.4px] text-pink">{{ help.eyebrow }}</div>
        <h1 class="mt-3 font-pixel font-bold leading-none tracking-[0.6px] text-[#f5f5f7]" style="font-size: clamp(34px, 5vw, 56px)">{{ help.title }}</h1>
        <p class="mx-auto mt-4 max-w-[520px] text-[17px] leading-relaxed text-muted">{{ help.subtitle }}</p>
      </div>
    </header>

    <main class="mx-auto max-w-[980px] px-6 pb-20 pt-12 sm:px-10">
      <!-- channels -->
      <div class="grid gap-[18px] md:grid-cols-3">
        <Reveal class="relative overflow-hidden rounded-2xl border border-pink/30 p-6" style="background: linear-gradient(135deg, rgba(226,113,154,0.1), rgba(226,113,154,0.03))">
          <div class="flex h-11 w-11 items-center justify-center rounded-xl bg-pink/15"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#e2719a" stroke-width="1.9" stroke-linejoin="round" stroke-linecap="round"><path d="M12 20s-7-4.7-7-10a4 4 0 0 1 7-2.5A4 4 0 0 1 19 10c0 5.3-7 10-7 10Z" /></svg></div>
          <div class="mt-3.5 text-lg font-bold text-[#f3f3f5]">Priority support</div>
          <div class="mt-1.5 text-sm leading-relaxed text-muted">Patrons get faster, direct help. Message me through Patreon and I'll jump on it.</div>
          <a :href="app.links.patreon" target="_blank" rel="noopener" class="mt-4 inline-block text-sm font-semibold text-pink no-underline hover:underline">Message on Patreon →</a>
        </Reveal>

        <Reveal :delay="0.06" class="rounded-2xl border border-white/[0.08] bg-[rgba(28,28,32,0.7)] p-6">
          <div class="flex h-11 w-11 items-center justify-center rounded-xl bg-white/[0.06]"><svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" class="text-soft"><path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.1.68-.22.68-.49l-.01-1.9c-2.78.62-3.37-1.21-3.37-1.21-.46-1.18-1.11-1.5-1.11-1.5-.91-.64.07-.62.07-.62 1 .07 1.53 1.06 1.53 1.06.9 1.56 2.36 1.11 2.94.85.09-.67.35-1.11.63-1.37-2.22-.26-4.55-1.14-4.55-5.06 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.3.1-2.72 0 0 .84-.27 2.75 1.05a9.4 9.4 0 0 1 5 0c1.91-1.32 2.75-1.05 2.75-1.05.55 1.42.2 2.46.1 2.72.64.72 1.03 1.63 1.03 2.75 0 3.93-2.34 4.79-4.57 5.05.36.32.68.94.68 1.9l-.01 2.82c0 .27.18.6.69.49A10.02 10.02 0 0 0 22 12.25C22 6.58 17.52 2 12 2Z" /></svg></div>
          <div class="mt-3.5 text-lg font-bold text-[#f3f3f5]">Report a problem</div>
          <div class="mt-1.5 text-sm leading-relaxed text-muted">Found a bug or have an idea? Open a tracked issue on GitHub — it's the fastest way to get it fixed.</div>
          <NuxtLink to="/report" class="mt-4 inline-block text-sm font-semibold text-brand no-underline hover:underline">Report a bug →</NuxtLink>
        </Reveal>

        <Reveal :delay="0.12" class="rounded-2xl border border-white/[0.08] bg-[rgba(28,28,32,0.7)] p-6">
          <div class="flex h-11 w-11 items-center justify-center rounded-xl bg-white/[0.06]"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#5a91c2" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8 12h.01M12 12h.01M16 12h.01M21 12a8 8 0 0 1-11.6 7.1L3 21l1.9-6.4A8 8 0 1 1 21 12Z" /></svg></div>
          <div class="mt-3.5 text-lg font-bold text-[#f3f3f5]">Community chat</div>
          <div class="mt-1.5 text-sm leading-relaxed text-muted">A Discord server is in the works. For now, the best way to reach me is through Patreon.</div>
          <div class="mt-4"><DiscordLink variant="text" /></div>
        </Reveal>
      </div>

      <!-- FAQ -->
      <Reveal class="mt-12">
        <h2 class="font-pixel text-2xl font-bold text-[#f3f3f5]">Frequently asked</h2>
        <div class="mt-5 flex flex-col gap-2.5">
          <div v-for="(f, i) in help.faq" :key="i" class="overflow-hidden rounded-xl border border-white/[0.08] bg-[rgba(28,28,32,0.6)]">
            <button class="flex w-full items-center justify-between gap-4 px-5 py-4 text-left" @click="toggle(i)">
              <span class="text-[15px] font-semibold text-[#f0f0f2]">{{ f.q }}</span>
              <svg class="flex-none text-faint transition-transform duration-300" :class="{ 'rotate-180': openFaq === i }" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6" /></svg>
            </button>
            <Transition name="faq">
              <div v-if="openFaq === i" class="px-5 pb-4 text-sm leading-relaxed text-muted">{{ f.a }}</div>
            </Transition>
          </div>
        </div>
      </Reveal>

      <!-- CTA -->
      <Reveal class="mt-10 flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-brand/20 bg-brand/[0.06] px-6 py-6">
        <div>
          <div class="text-lg font-bold text-[#f3f3f5]">Still stuck?</div>
          <div class="mt-1 text-sm text-[#b6c9ba]">Open an issue with your logs and steps — I read every one.</div>
        </div>
        <NuxtLink to="/report" class="btn-primary mc-clip px-5 py-3 text-[15px]">Report a bug</NuxtLink>
      </Reveal>
    </main>
  </div>
</template>

<style scoped>
.faq-enter-active,
.faq-leave-active {
  transition: opacity 0.25s ease;
}
.faq-enter-from,
.faq-leave-to {
  opacity: 0;
}
</style>
