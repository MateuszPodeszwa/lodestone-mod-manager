import { getReleases } from '~~/server/utils/github'
import { FALLBACK_RELEASE } from '~~/server/utils/fallback'

// Lightweight endpoint returning the version list for the report-a-bug dropdown.
// No per-release diffs are fetched, so this stays fast.
export default defineEventHandler(async () => {
  try {
    const releases = await getReleases()
    if (!releases.length) {
      return [{ version: FALLBACK_RELEASE.version, tag: `v${FALLBACK_RELEASE.version}`, latest: true, prerelease: false }]
    }
    const latestIndex = releases.findIndex((r) => !r.prerelease)
    const resolvedLatestIndex = latestIndex === -1 ? 0 : latestIndex
    return releases.map((r, i) => ({
      version: r.version,
      tag: r.tag,
      latest: i === resolvedLatestIndex,
      prerelease: r.prerelease,
    }))
  } catch {
    return [{ version: FALLBACK_RELEASE.version, tag: `v${FALLBACK_RELEASE.version}`, latest: true, prerelease: false }]
  }
})
