import { getLatestRelease, getLatestBeta } from '~~/server/utils/github'
import { FALLBACK_RELEASE, githubReleasesUrl, formatBytes } from '~~/server/utils/fallback'

// Lightweight "latest version" payload for the hero + download page. Checksums
// are resolved separately (/api/checksums) so this stays fast.
export default defineEventHandler(async () => {
  const latest = await getLatestRelease()
  const beta = await getLatestBeta()

  if (!latest) {
    return {
      source: 'fallback' as const,
      version: FALLBACK_RELEASE.version,
      codename: FALLBACK_RELEASE.codename,
      name: `Lodestone ${FALLBACK_RELEASE.version}`,
      date: FALLBACK_RELEASE.date,
      htmlUrl: githubReleasesUrl(),
      sizeText: '~8 MB',
      setup: null,
      portable: null,
      beta: { available: false, version: null as string | null, date: null as string | null },
    }
  }

  const mapAsset = (a: typeof latest.setup) =>
    a ? { name: a.name, size: a.size, sizeText: formatBytes(a.size), url: a.url } : null

  return {
    source: 'github' as const,
    version: latest.version,
    codename: null as string | null,
    name: latest.name,
    date: latest.date,
    htmlUrl: latest.htmlUrl,
    sizeText: formatBytes(latest.setup?.size),
    setup: mapAsset(latest.setup),
    portable: mapAsset(latest.portable),
    beta: {
      available: !!beta?.setup,
      version: beta?.version ?? null,
      date: beta?.date ?? null,
    },
  }
})
