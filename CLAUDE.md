# Shiny.Obd — Working Notes

Guidance for maintaining this repo. Code lives in `src/`, tests in `tests/`, the published Claude
Code skill in `skills/`, and the public documentation site in a **separate** repo at
`~/Desktop/dev/documentation` (rendered to https://shinylib.net/client/obd).

Shiny.Obd is an OBD-II vehicle communication library for .NET built on a command-object pattern with
generic return types. The core lives in `src/Shiny.Obd`: an `IObdCommand<T>` / `ObdCommand<T>` issues
ELM327/STN AT+PID commands and parses the hex response into a typed result; `IObdConnection` drives
an adapter over a pluggable `IObdTransport`; adapter auto-detection picks an `IObdAdapterProfile`
(`Elm327AdapterProfile`, `ObdLinkAdapterProfile`) via ATI probing; and `IObdDeviceScanner` /
`ObdDiscoveredDevice` surface adapters before connecting. `src/Shiny.Obd.Ble` is the BLE transport
(`BleObdTransport`, `BleObdDeviceScanner`) layered on **Shiny.BluetoothLE**, wired up through
`AddShinyObdBluetoothLE()`. The whole API is task-based — no Reactive Extensions in consuming code.

## After every new feature or fix

A change is not "done" until the four artifacts below are in sync. Do all of them in the same
change unless there's a reason not to.

1. **Code + tests** (`src/`, `tests/`)
   - New behavior generally lands as a command (`Shiny.Obd`) or a transport/scanner concern
     (`Shiny.Obd.Ble`). A new transport must satisfy `IObdTransport`; a new adapter quirk belongs in
     an `IObdAdapterProfile`, not scattered through the connection.
   - Run the suite before considering the change complete:
     `dotnet test tests/Shiny.Obd.Tests/Shiny.Obd.Tests.csproj` (the core libs build via `build.slnf`).

2. **Documentation site** (`~/Desktop/dev/documentation/src/content/docs/obd/`)
   - Update the relevant feature page (`index.mdx`, `commands.md`, `connection.md`, `transports.md`,
     `ble.md`).
   - Add a **release note** — see the release-note rules below.
   - Pages are `.md`/`.mdx`; release notes use the `<RN>` component
     (`import RN from '/src/components/ReleaseNote.astro'`), with `type="feature|enhancement|fix|breaking"`.

3. **Skill** (`skills/shiny-obd/SKILL.md`)
   - This is the source of the published `shiny-obd` Claude Code skill — the agent-facing
     "how to generate correct code" doc. It syncs to the `shiny-client` plugin in the
     [shinyorg/skills](https://github.com/shinyorg/skills) repo via `.github/workflows/sync-skills.yml`.
   - Keep `SKILL.md` aligned with the code. Update the `triggers:` keyword list near the top when a
     new public type / command / transport is introduced.
   - If the default or recommended pattern changes, the skill's default guidance must change too.

4. **readme.md** (repo root)
   - This file is packed into every NuGet package (`PackageReadmeFile` in `Directory.Build.props`).
     Update the feature list and any inline guidance when behavior changes.

## Release notes

Release notes live in the documentation repo at
`~/Desktop/dev/documentation/src/content/docs/obd/release-notes.mdx`.

**Which version does a note go against?** Use the `version` field in `version.json` (this repo uses
Nerdbank.GitVersioning) — **the raw version portion only** (strip any prerelease/build-metadata
suffix, e.g. `1.0.0-beta.{height}` → `1.0.0`).

**Heading style — match the existing file.** Sections are headed `## v<version>` with a `v` prefix
(`## v1.0.0 - TBD`). Newest version section stays at the top of the file.

**If the version isn't released yet (beta / prerelease, or work-in-progress for the next version):**
- If a `## v<version> - TBD` heading already exists, **add the note under that existing section**. If
  you're modifying a feature that hasn't shipped yet (already an entry under a `TBD` section), edit
  that existing entry in place rather than adding a duplicate.
- If no section exists for that version yet, **create a new `## v<version> - TBD` heading** at the top
  and add the note there.

**If the version is a final release**, the section is dated (`## v1.0.0 - June 28, 2026`); add the note
under the matching dated section (or promote the `TBD` section to a dated one when cutting the
release).

Each note is a single `<RN>` line. Use `type="breaking"` for breaking changes (it's its own note
type here, not a flag).

## Blog posts (only when explicitly requested)

Do **not** write blog posts automatically as part of a fix/feature. Write them **only when the user asks**. When asked to blog a feature, produce **two** posts — first the docs-site version, then adapt it for the personal blog.

### 1. Docs site — `~/Desktop/dev/documentation`

- File: `src/content/docs/blog/YYYY/MM/<slug>.mdx` (current year/month folders; create the month folder if needed).
- Frontmatter:
  ```yaml
  ---
  title: '...'
  description: '...'
  date: YYYY-MM-DD
  authors:
    - allanritchie
  tags:
    - Release        # or Feature, AI, etc.
  ---
  ```
- Body is MDX. Reuse components where relevant, e.g. `import NugetBadge from '/src/components/NugetBadge.astro';` then `<NugetBadge name="Shiny.Obd" />`.
- Voice: product/release-note tone — what shipped, breaking changes, code samples, how to use it. **No hero image** on this site.

### 2. Personal blog — `~/Desktop/dev/blog` (adapt the docs post)

- File: `src/content/blog/YYYY/MM/<slug>.mdx` (note: `content/blog`, not `content/docs/blog`).
- Frontmatter (different schema — see `src/content.config.ts`):
  ```yaml
  ---
  title: '...'
  description: '...'
  pubDate: 'Mon DD YYYY'                          # e.g. 'Jun 28 2026'
  heroImage: '../../../../assets/<slug>-hero.svg'
  tags: ['Shiny', '.NET']
  ---
  ```
- Voice: rework the docs post into a personal, first-person narrative ("Here's something that shouldn't be hard but is…", "So I built…") — story/motivation up front, not a dry changelog.
- **Hero image is required.** Create `src/assets/<slug>-hero.svg`:
  - SVG, `viewBox="0 0 1200 630"`, `width="1200" height="630"`.
  - Match the house style: dark navy/indigo gradient background (`#0f172a` → `#1e1b4b`), cyan/green/violet accent gradients, subtle glow filters, the feature name as the headline. Crib an existing one (e.g. `datasync-hero.svg`, `documentdb-orleans-hero.svg`) as a starting template.
