# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

This is a German A0–A1 learning MVP for Chinese-speaking users: six lessons, vocabulary and sentence readers, single-choice practice, server-side grading, guest progress, and generated audio. It uses JSON content and browser `localStorage`; there is no database, account system, or admin backend.

All projects target `net11.0`. The currently referenced SDK and Blazor packages are .NET 11 RC3. Run commands from the repository root.

## Common commands

### Restore, build, and test

```bash
dotnet restore LearnALanguage.BeafSoup.slnx
dotnet build LearnALanguage.BeafSoup.slnx --configuration Release --no-restore
dotnet test LearnALanguage.BeafSoup.slnx --configuration Release
```

Run one xUnit test:

```bash
dotnet test tests/BeafSoup.Tests/BeafSoup.Tests.csproj --filter "FullyQualifiedName=BeafSoup.Tests.CourseContentTests.InitialCourse_HasCompleteSixLessonPackage"
```

Run the browser acceptance suite against locally running API and Web servers (see `tests/browser/README.md`):

```bash
npm install --prefix tests/browser --ignore-scripts
node tests/browser/full-course.cjs
```

### Run the application

Start the API and standalone Blazor WebAssembly client in separate terminals:

```bash
dotnet run --project src/BeafSoup.Api --configuration Release --urls http://localhost:5049
dotnet run --project src/BeafSoup.Web --configuration Release --urls http://localhost:5287
```

Open `http://localhost:5287`. The client reads `ApiBaseUrl` from `src/BeafSoup.Web/wwwroot/appsettings.json`; its default is `http://localhost:5049/`.

### Generate audio

```bash
# Preview all jobs without credentials or network requests.
dotnet run --project src/BeafSoup.AudioGenerator -- --dry-run

# Azure credentials are read from the environment.
export AZURE_SPEECH_KEY="your-key"

# Verify one item before generating the full course.
dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --region eastus --id v-hallo

# Generate all missing or stale items (vocabulary, sentences, dialogue turns).
dotnet run --project src/BeafSoup.AudioGenerator -- --provider azure --region eastus
```

Azure is the default provider, with region `eastus` and voice `de-DE-KatjaNeural`. Dialogue turns override the voice, style and rate per speaker from the content file. Azure validates the style against the voice's `StyleList` and returns HTTP 400 for unsupported pairs, so probe a new character voice before a batch run. Use `--help` for the remaining options. The NVIDIA client is not a Riva protocol adapter.

### Database

SQLite is the default and needs no setup (`Data Source=artifacts/beafsoup.db`). Switch providers with configuration, not code:

```bash
export Database__Provider=postgresql
export ConnectionStrings__BeafSoup="Host=localhost;Port=5432;Database=beafsoup;Username=beafsoup;Password=beafsoup"
```

Schema creation happens at API startup. Keep new entities inside what both providers support (Guid keys, bounded strings, `DateTimeOffset`), and never add provider-specific column types or SQL.

## Architecture and important boundaries

- `BeafSoup.Content` owns the full course documents, JSON loading, validation, audio jobs, and fingerprints. This includes correct answers and explanations, so it is server-only.
- `BeafSoup.Contracts` contains the browser-safe DTOs shared by API and Web. `BeafSoup.Web` deliberately references only Contracts, not Content. `QuestionDto` must not expose `CorrectOptionId` or `Explanation`; the API returns those only in `GradeResultDto` after submission.
- `BeafSoup.Api` loads and validates the course and audio manifest once at startup, registers them as singletons, maps internal documents to localized DTOs, and performs grading. Restart the API after changing course JSON or regenerating the audio manifest.
- Course JSON stays outside `wwwroot`. Only `content/audio` is exposed statically, under the API's `/audio` route. `AudioUrlResolver` returns a URL only for a `ready` manifest entry whose source-text hash and file size still match.
- `BeafSoup.Data` owns EF Core persistence: guest profiles, attempts and completed lessons, with SQLite (default) and PostgreSQL behind one provider-agnostic model. Progress is authoritative on the server; the browser keeps a `localStorage` cache and syncs the full state, which the server merges idempotently by attempt id and lesson id.
- `BeafSoup.Web` is a standalone WebAssembly UI. `LearningApiClient` handles API calls, `GuestProgressStore` owns progress sync and offline fallback, `AudioPlaybackService` wraps the JS audio layer (`beafAudio.play` for one-shot playback, `beafAudio.playToEnd` for sequential dialogue playback), and `DialoguePlayer` implements the two dialogue modes: continuous playback and shadowing with a per-line pause.
- `BeafSoup.AudioGenerator` derives jobs from every vocabulary lemma, embedded sentence and dialogue turn. Dialogue jobs carry the speaker's voice, style and rate, so each entry has its own settings hash and changing a voice rebuilds only the affected lines. `AudioGenerationRunner` handles retry, fingerprints, resumable generation, atomic writes, and `content/audio/de-DE/manifest.json`.
- The xUnit suite covers content, DTO answer secrecy, every API route and grade path, dialogue and progress endpoints, persistence merge rules, audio validation, progress storage, and generation resume behavior. `tests/browser/full-course.cjs` adds browser end-to-end coverage of the full course and 60-question bank.

## Course content changes

The source course is `content/courses/de-DE/a1-foundations`; its README contains the complete schema and authoring rules. Key invariants enforced or relied upon across projects:

- Published IDs are stable, globally unique within their resource kind, and referenced by lessons, sentences, questions, dialogues, progress, and audio manifests.
- Concatenating `segments[].text` must reproduce each sentence's or dialogue turn's `text` exactly, including whitespace and punctuation.
- New lesson files must also be appended to `course.json`'s ordered `lessonIds`; new dialogues go into `dialogueIds`; increment `revision` for a published content revision.
- Dialogue speakers declare `voice`, optional `style` and `rate`; every speaker in one dialogue needs a distinct voice so the learner can tell them apart. Leave `pauseAfterMs` unset to use the character-based shadowing estimate in `DialogueTiming`.
- `JsonContentLoader` validates references, question shape, localization, and sentence/dialogue reconstruction whenever content is loaded.
- Current tests assert the exact six-lesson, 60-vocabulary, 30-sentence, 60-question, 3-dialogue, 24-turn package. Intentional content expansion requires updating those expectations.
