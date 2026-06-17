import { getReleases } from '~~/server/utils/github'

// Full release history for the changelog page. Heavy HTML notes are included.
export default defineEventHandler(async () => {
  const releases = await getReleases()
  return releases.map((r, i) => ({
    version: r.version,
    tag: r.tag,
    name: r.name,
    date: r.date,
    prerelease: r.prerelease,
    latest: i === 0,
    htmlUrl: r.htmlUrl,
    notesHtml: r.notesHtml,
    notesMarkdown: r.notesMarkdown,
  }))
})
