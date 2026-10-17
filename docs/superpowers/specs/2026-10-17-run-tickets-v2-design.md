# Ticket runner v2: Opus plans and reviews, cheaper models implement

Date: 2026-10-17. Status: design, awaiting approval.

This document is self-contained: it describes the whole workflow and every rule the runner and its two skills
need. It does not rely on any other runner script.

## Why

Running a whole ticket in one headless Opus session (read, plan, type the code, loop on builds and tests,
review, commit) spends most of its tokens on the mechanical middle: typing code, re-running builds, fixing compile
errors, where every turn re-reads the whole context. Burning Opus on that repetitive work makes no sense.

This runner keeps Opus only where judgement matters: **architect** (understand the ticket and the code, write an
exact execution plan and the acceptance tests) and **reviewer** (judge the result, decide, commit). The middle goes
to cheaper executors. The goal is to spend far less Opus usage **without losing quality**.

## Goals, non-goals, anti-goals

Goals:

- Opus architect → executor → automatic gate → Opus reviewer, for every ready ticket, unattended.
- Executors **alternate** between Nemotron (free) and Gemini Flash, ticket by ticket, with a fallback chain.
- The project rules below hold for every ticket.
- Measurable: per ticket, which executor did it, how many rounds, minutes and tokens.

Non-goals:

- Running several tickets in parallel.
- Changing any existing runner script in `scripts/`; this runner is a new, separate script.
- Making the workflow project-independent (a later step; this version targets this repository).

Anti-goal (failure even if everything "works"):

- **A wrong solution reaching main.** A ticket that closes with a bad implementation is worse than a ticket
  that stops blocked. Every safeguard below exists for this.

## Evidence: the 08f bake-off (2026-10-16)

Seven executors implemented ticket 08f (Armchair henge → Stonehenge, a spatial 3D ticket) from the same Opus plan,
each in an isolated clone of the parent commit. A hidden acceptance test (structure + physical soundness:
nothing sunk in the floor, every lintel resting on two uprights), which the reviewed original solution passes, was
run afterwards.

| Executor | Time | Own tests | Hidden test | Notes |
|---|---|---|---|---|
| Codex (gpt-6.1-sol) | 37 min | 400/400 | pass | cleanest, smallest diff |
| Gemini 3.8 Flash High (`agy`) | 61 min | 400/400 | pass | 44 M cache-read tokens, mostly polling a 12-minute test run |
| Nemotron 3.5 Super VL free (opencode) | 57 min | 400/400 | pass | exit code 1 at the end but delivered everything |
| GLM-5.3 Flash free | timeout 75 | 400/400 | pass | no report or captures |
| DeepSeek 4.1 Flash free | timeout 75 | test host hangs | – | wrong code |
| Nemotron Super VL (NVIDIA key) | 9 min | fails | fails | `output-limit` error |
| Nemotron 3 Ultra free | 20 min | – | – | provider `400` error |

Lessons that shape this design:

1. **The plan carried the quality.** The four good results look almost identical: the plan gave exact numbers,
   formulas and a pre-warned trap. The executor mostly typed it.
2. **Executors' own tests are not enough.** A locked, Opus-written acceptance test is the cheap guard.
3. **Executors waste tokens waiting on long test runs.** The script, not the executor, runs the full suite.
4. **Free providers fail at random** (`400`, `output-limit`). Every executor needs a fallback behind it.
5. The Gemini CLI is retired for individuals; Gemini runs through Antigravity's `agy` CLI.

## Project rules (apply to every ticket)

These are the rules the architect plans for, the executor follows and the reviewer enforces.

1. **Unattended.** Nobody answers questions. Every role makes reasonable decisions itself and records them
   (executor: in its report; reviewer: in the closing comment).
2. **Scope.** Implement only this ticket, so that every acceptance criterion is met. Follow the spec's
   architecture: the engine-independent C# simulation core (`src/Scp3008.Core`) behind one facade, and the Godot
   presentation layer (`game/`) that only reads from it.
3. **Tests.** Facade-level tests in `tests/Scp3008.Core.Tests` (scenario → advance time → assert on
   snapshot/events). The game project must also build headless with the Godot console executable. Godot 4.7.2
   .NET is not on `PATH`: find `*_console.exe` under
   `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\`.
4. **Non-automatable criteria** (visuals, audio, frame rate): verify what can be verified (run the game with the
   dev capture arguments and look at the screenshot, run the benchmark for fps), and list what still needs human
   review.
5. **Vocabulary.** Use the GLOSSARY.md terms; add a newly settled domain term to it. Write an ADR in `docs/adr/`
   only for a decision that is hard to reverse, surprising, and the result of a real trade-off.
6. **Stage review.** Build stages are GitHub milestones. The ticket that completes a stage (see "Picking the
   ticket") gets a review of the whole stage instead of only itself (see step 4).
7. **Never** modify issue #1 (the spec) or other tickets, force-push or rewrite history.
8. **Small context.** Full output of test runs, builds, Godot runs and benchmarks goes to a file under
   `.ticket-logs/`; only the summary and failures are printed. Screenshots at most 1280 px wide, each looked at
   once.

## The pipeline, per ticket

```
pick ticket ─► 1 PLAN (Opus) ─► 2 EXECUTE (Nemotron | Gemini, alternating) ─► 3 GATE (script)
                    ▲                   ▲                                         │ red: ≤2 fix rounds,
                    │                   └──────────── failure summary ────────────┤ then fallback chain
                    └──── 3a AMEND (plan or locked test wrong, once) ─────────────┘
                                                                                   │ green
                                               5 COMMIT ◄─ 4 REVIEW (Opus) ◄───────┘
```

### 0. Picking the ticket

- Candidates: open issues labelled `ready-for-agent` whose title starts with `NN: ` or `NNa: ` (two digits, an
  optional lower-case letter for a ticket inserted after `NN`). Order by number, then by the letter, so `07a` runs
  after `07` and before `08`. An open issue that has a milestone or an `easy`/`hard` label but no such title prefix
  is reported as a warning and skipped.
- The first candidate whose blockers (GitHub issue dependencies, `repos/<repo>/issues/<n>/dependencies/blocked_by`)
  are all closed is the next ticket. If candidates exist but all are blocked, the run stops with a message.
  No candidates: the run ends successfully.
- **Stage end.** The ticket is a stage end when its milestone is still open and it is the milestone's only open
  issue. A closed milestone counts as already reviewed: tickets added to it later get no stage review.
- **Stage start** (for the stage review): the lowest `NN[a]` label among all issues (open and closed) of the
  milestone.
- **Difficulty.** The `easy` / `hard` label (or none) is passed to the architect as information.

### 1. Plan — Opus, skill `plan-ticket`

The script runs `claude -p "/plan-ticket <label> <issue>"` with Opus, full permissions, stream-JSON output, the
runner settings file (see "Claude sessions") and effort `-Effort` (stage ends: `-StageEffort`). The architect:

- reads the ticket with its comments (`gh issue view <n> --comments`), the spec (issue #1), GLOSSARY.md, every ADR
  in `docs/adr/`, and the code it needs;
- writes `.ticket-work/<label>/PLAN.md`: file-by-file steps, exact numbers, formulas and signatures, the traps it
  foresees, which GLOSSARY/ADR changes are needed, which acceptance criteria need captures and the exact dev
  capture command lines (`--poi=…`, `--capture=…`), and what the executor must not touch;
- writes the **acceptance tests**: a small number of facade-level tests that pin the ticket's acceptance criteria,
  as new or changed files in `tests/Scp3008.Core.Tests/`. They will not compile until the executor implements the
  new API; that is expected;
- writes `.ticket-work/<label>/locked.txt`: the repository paths of the acceptance test files;
- before finishing, re-reads plan and tests against each other: every number a test expects must follow from the
  plan (the 08f plan first had a test expecting the wrong elevation for a rolled piece).

The architect does not edit production code. The script records the planning session id for steps 3a and 4.

### 2. Execute — alternating Nemotron and Gemini

**Rotation.** Tickets alternate: one ticket Nemotron, the next Gemini, and so on. The last *primary* executor is
persisted in `.ticket-logs/v2-rotation.json`, so a restarted run continues the alternation (`-StartWith`
overrides it). A fallback does not change the rotation: if ticket N was Nemotron's turn and fell back to Gemini,
ticket N+1 is still Gemini's turn.

| Executor | Command (run in the executor workspace) |
|---|---|
| Nemotron | `opencode run --standalone --auto --format json -m opencode/nemotron-3.5-super-vl-free "<prompt>"` |
| Gemini | `~/.gemini/bin/agy.exe -p "<prompt>" --dangerously-skip-permissions --model gemini-3.8-flash-high --output-format stream-json` |
| Codex (fallback) | `codex exec --dangerously-bypass-approvals-and-sandbox --json "<prompt>"` |

Model ids are parameters (`-NemotronModel`, `-GeminiModel`, `-CodexModel`), defaults as above.

**Executor workspace (isolation).** Executors never run in the main repository. One persistent clone,
`<repo>/../SCP_3000_SIMULATION-exec`, without an `origin` remote, so nothing can be pushed or committed to main by
accident. Before each executor attempt the script resets it to the main repository's `HEAD` (fetch from the local
path, `git reset --hard`, `git clean -fdx` except `game/.godot/`, so the Godot import cache survives) and copies in
the plan, the locked tests and `locked.txt`. After a green gate the script carries the result back to the main
repository as a patch (`git diff --binary HEAD` in the clone, `git apply` in the main tree), excluding
`.ticket-work/` and Godot `*.import` reimport noise.

**Executor prompt** (one template for all executors):

- unattended; read `.ticket-work/<label>/PLAN.md` and carry it out; follow the project rules (copied into the
  prompt);
- the Godot console executable path (the script resolves it);
- never run `git commit`, `git push` or any `gh` command;
- **never modify the files listed in `locked.txt`**; if one looks wrong, write
  `LOCKED_TEST_SUSPECT: <file>:<test> — <why>` in the report and stop;
- run only the tests of the test classes it touches (`dotnet test --filter`), never the whole suite (the script
  does that);
- take the captures the plan lists and look at them;
- do not run the benchmark unless the plan says so;
- write `.ticket-work/<label>/REPORT.md`: changes per file, test results, captures and what they show, decisions
  the plan left open, anything not done;
- last line exactly `TICKET_DONE` or `TICKET_BLOCKED: <reason>`.

Time limit per attempt: `-ExecutorMinutes` (default 75); on timeout the whole process tree is killed.

### 3. Gate — the script, no model

In the executor workspace, in order, stopping at the first failure:

1. The files in `locked.txt` are byte-identical to the architect's version (SHA-256).
2. No commits were made in the clone (`HEAD` unchanged).
3. `dotnet build` of the solution: 0 errors.
4. `dotnet test` of the whole core test project (timeout `-TestMinutes`, default 25; a hang counts as a failure).
5. Headless Godot build of the game project.
6. `REPORT.md` exists; the captures the plan lists exist.

Full output goes to `.ticket-logs/<label>/gate-<n>.log`; a short failure summary (failing test names and
messages, compile errors, at most ~60 lines) is kept for the next round.

**Red gate → fix rounds.** The failure summary goes back to the **same** executor, continuing its session where
the CLI supports it (opencode `--session`, agy `--conversation`; otherwise a fresh run on the current workspace
with the summary), at most `-FixRounds` (default 2) times.

**Fallback chain.** When the primary executor fails (provider error, timeout, `TICKET_BLOCKED`, or still red after
its fix rounds), the workspace is reset and the next executor starts from the plan:
primary (Nemotron or Gemini) → the other one → Codex. Provider errors (HTTP 4xx/5xx, `output-limit`) get one
retry after 2 minutes before counting as a failure. A quota/limit error marks that executor exhausted for the rest
of the run; it is skipped in the rotation and the chain.

**Blocked.** If the chain is used up: the script comments on the issue (what was tried, the last gate summary,
where the logs are), leaves the issue open, saves the clone's state as a patch in `.ticket-logs/<label>/`, and
stops the run with a non-zero exit code.

### 3a. Amend — when the plan or a locked test is wrong

A wrong locked test can never go green, and a fault in the plan does not get better by sending it to more
executors. So faults that point at the architect go back to the architect:

- The executor's report contains `LOCKED_TEST_SUSPECT: …`, **or** after the primary executor's fix rounds the only
  remaining gate failures are in locked tests.
- The script then does **not** start the fallback chain. It resumes the planning session with
  `/plan-ticket <label> <issue> --amend` and the evidence (report excerpt, failing locked tests). The architect
  either corrects the plan and/or the locked tests (only the architect may change locked files; the script
  re-records their hashes), or confirms them and explains why the executor is wrong.
- Then the same executor continues with the amended plan or the architect's answer, as one more fix round.
- **One amend per ticket.** A second suspect or locked-test-only failure stops the ticket as blocked.

### 4. Review — Opus, skill `review-ticket`

Only green results reach review. The script applies the patch to the main working tree (uncommitted) and runs
`claude -p "/review-ticket <label> <issue>" --resume <planning session id>` (Opus, same settings and effort as the
plan), so the reviewer already has the ticket, the code and the plan in context. If the planning session cannot be
resumed (for example after a usage-limit wait), a fresh session is fine; the skill reads what it needs.

The reviewer reads the diff, `REPORT.md`, the gate log summary and the captures, and checks every acceptance
criterion and the project rules.

**Stage end.** For a stage-end ticket the reviewer instead reviews the whole stage: it runs `/spec-review` on the
diff from the parent of the stage-start ticket's commit (found with `git log --grep '^<first>: '`) up to and
including the uncommitted changes, fixes the real findings, reruns the tests, and includes the fixes in this
ticket's commit. Otherwise `/spec-review` is skipped.

Verdict, last line of the reviewer's final message:

- `APPROVE` — commit (step 5).
- `FIX` — small, clear problems: the reviewer fixes them itself in the main tree, reruns the affected tests and the
  build, then commits.
- `REJECT: <notes>` — real problems in the implementation. The script resets the main tree to `HEAD` (only the
  ticket's files, never the baseline files, see step 5), sends the notes to the executor that did the work as one
  more round in its workspace, gates again and reviews again. **One** reject round; a second reject stops the
  ticket as blocked.
- `REJECT-PLAN: <notes>` — the fault is in the plan or the locked tests: goes to step 3a (counts as the one amend).

### 5. Commit

**Baseline.** Before each ticket the script records the main tree's `git status --porcelain` (ignoring
`.claude/`). These baseline files are never staged, reset or committed; the review prompt lists them as
off-limits.

On approval the reviewer:

- stages only this ticket's changes (never baseline files, never `.claude/`);
- commits to `main` with the message `<title>`, a blank line, `Closes #<n>`, a blank line, and the co-author lines
  of the executor model that did the work and of Opus;
- pushes to `origin main`;
- makes sure the issue is closed;
- posts a short closing comment: what was built, the test results, what needs human visual review, and which
  executor did it.

Then the script checks: the issue is closed, the tree has no changes beyond the baseline, and `git rev-list --count
origin/main..main` is 0 (after `git fetch`). Otherwise it stops with the reviewer's last message and the log path.
For a stage-end ticket it closes the milestone (marking the stage reviewed) and, unless `-NoStagePause`, asks
"Continue with the next stage? (Y/n)".

## Claude sessions (plan and review)

- `claude -p "<prompt>" --dangerously-skip-permissions --output-format stream-json --verbose --model opus
  --effort <level> --settings scripts/ticket-settings-v2.json`. The prompt is passed as an argument (piped stdin
  does not expand a slash command); Windows PowerShell 5.1 does not escape embedded double quotes in native
  arguments, so double quotes in the prompt are replaced by single quotes.
- `scripts/ticket-settings-v2.json` turns off what the runs never use: `"disableClaudeAiConnectors": true` and
  every plugin disabled (`superpowers`, `context-mode`, `agent-tree`). It affects only these processes.
- `CLAUDE_CODE_AUTO_COMPACT_WINDOW` is set to `-CompactWindow` (default `155000`, a plain number of tokens) for the
  child processes. Sessions compact about 35–40 k tokens below this window, so 155000 compacts at about 120 k.
- Every stream line is appended to the ticket's log. The console shows the first line of each text message and the
  name of each tool call, and at the end turns, minutes, the API-price estimate, token totals over all models
  (`modelUsage`) and the subscription utilisation (5-hour and weekly, first vs last `rate_limit_event`).
- **Usage limit.** A `rate_limit_event` with status `rejected` carries `resetsAt`. The script waits until two
  minutes after it and resumes the same session (`--resume <id>`) with "The usage limit interrupted you. Continue
  from where you stopped, following the same rules." At most `-MaxLimitResumes` (default 3) times per session, and
  not if the reset is more than `-MaxLimitWaitHours` (default 8) away; then the ticket stops.

## Limits and failures of executors

- Gemini Pro or Codex Plus quota exhausted: that executor is marked exhausted for the run.
- Nemotron free unavailable for the whole run: Gemini takes every ticket; if both are out, Codex.
- Every executor out: the run stops with a clear message.

## Files and logs

- `.ticket-logs/` and `.ticket-work/` are added to `.git/info/exclude` (local only) when missing.
- Per ticket: `.ticket-logs/<label>/` holds the plan and review stream logs, each executor attempt's log, gate
  logs, and the blocked patch if any. `.ticket-work/<label>/` holds `PLAN.md`, `locked.txt`, `REPORT.md` and
  captures; it is copied to and from the executor workspace.
- `.ticket-logs/v2-runs.csv`, one row per ticket: ticket, primary executor, executor that finished, attempts, fix
  rounds, amends, rejects, minutes per phase, executor tokens (from the opencode/agy/codex JSON), Opus tokens and
  limit utilisation before/after, verdict.

## Parameters

`-Repo` (default `BalazsBerces/SCP-3000-Simulation`), `-Max` (tickets to run, 0 = all), `-NoStagePause`,
`-Effort` (default `medium`), `-StageEffort` (default `high`), `-CompactWindow`, `-MaxLimitResumes`,
`-MaxLimitWaitHours`, `-ExecutorMinutes`, `-TestMinutes`, `-FixRounds`, `-NemotronModel`, `-GeminiModel`,
`-CodexModel`, `-StartWith Nemotron|Gemini`, and `-DryRun` (pick the ticket, print the executor choice and the
prompts, run nothing).

## Testing the runner itself

1. **Stub harness.** Fake `claude`, `opencode`, `agy`, `codex` and `gh` commands (scripts earlier on `PATH`
   for the test run) that replay scripted outcomes: green, red then green, provider error, timeout, locked-test
   suspect, quota exhausted, APPROVE / FIX / REJECT / REJECT-PLAN, usage-limit rejection. They drive the state
   machine (rotation, fix rounds, amend, fallback chain, blocking, limit wait) against a throwaway local clone
   with a bare local "origin", so no GitHub issue or real push is touched.
2. **`-DryRun`** on the real repository: ticket choice, executor choice, prompts.
3. **First real run with `-Max 1`** on an `easy` ticket, watched by the user, before any unattended run.

## Decided by the user

- Opus is architect and reviewer; cheaper models implement.
- Executors alternate between Nemotron (free) and Gemini Flash.
- Quality comes first: a wrong solution must not land.
- A script drives the workflow; the Opus roles are skills.
- This document is self-contained; a separate workflow repository comes later.

## Calls made by the designer (check these)

- Codex Plus is the last fallback, not part of the rotation.
- The architect writes locked acceptance tests; the executor may not change them.
- A wrong plan or locked test goes back to the architect (one amend per ticket), not down the fallback chain.
- Executors work in a separate persistent clone without a remote; results come back as a patch.
- The script, not the executor, runs the full test suite and the Godot build.
- Two fix rounds per executor, one reject round per ticket.
- The commit stages only this ticket's changes; files dirty before the run are never committed.
- The two skills live in the project: `.claude/skills/plan-ticket/` and `.claude/skills/review-ticket/`, with
  `.claude/skills/` tracked in git (the rest of `.claude/` stays untracked).
- Usage-limit defaults: 3 resumes per session, wait at most 8 hours.
- The runner is tested with stub CLIs against a throwaway clone first, then one watched real ticket.

## Left to the builder

- Exact resume flags and JSON parsing for opencode and agy sessions and token usage.
- How the patch excludes noise files; how the clone is reset quickly.
- Script name and structure (one file or a module next to it).
- Wording of the two skills, within the responsibilities above.
