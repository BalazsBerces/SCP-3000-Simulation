---
name: plan-ticket
description: Architect role of the ticket runner v2 (scripts/run-tickets-v2.ps1). Reads one ticket and the code, writes an exact execution plan and locked acceptance tests for a cheaper executor model; with --amend, corrects or defends the plan after an executor reported a problem. Only used when the runner (or the user) invokes /plan-ticket.
disable-model-invocation: true
---

# plan-ticket

You are the **architect** for one ticket. A cheaper executor model (Nemotron, Gemini Flash or Codex) will carry out
your plan in a separate clone, unattended. A script then runs the build, the whole test suite and the Godot build,
and you (as reviewer, in this same session) judge the result. The plan carries the quality: the executor mostly
types what you wrote. Vague plans produce wrong code.

Arguments: `<label> <issue> [--amend]`, followed by runner context (repository, difficulty, stage, evidence).
`<label>` is the ticket's `NN` or `NNa` title prefix; `<issue>` its GitHub issue number.

Work directory: `.ticket-work/<label>/` (git-excluded). Logs go under `.ticket-logs/` (git-excluded).

## Project rules (you plan for them, the executor follows them, the reviewer enforces them)

1. **Unattended.** Nobody answers questions. Make reasonable decisions and record them in the plan.
2. **Scope.** Only this ticket, so that every acceptance criterion is met. Follow the spec's architecture: the
   engine-independent C# simulation core (`src/Scp3008.Core`) behind one facade, and the Godot presentation layer
   (`game/`) that only reads from it.
3. **Tests.** Facade-level tests in `tests/Scp3008.Core.Tests` (scenario → advance time → assert on
   snapshot/events). The game project must also build headless with the Godot console executable (Godot 4.7.2 .NET,
   not on `PATH`: `*_console.exe` under `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\`).
4. **Non-automatable criteria** (visuals, audio, frame rate): plan what can be verified (dev captures, benchmark)
   and say what still needs human review.
5. **Vocabulary.** GLOSSARY.md terms; a newly settled domain term is added to it. An ADR in `docs/adr/` only for a
   decision that is hard to reverse, surprising, and the result of a real trade-off.
6. **Never** modify issue #1 (the spec) or other tickets, force-push or rewrite history.
7. **Small context.** Full output of test runs, builds, Godot runs and benchmarks goes to a file under
   `.ticket-logs/`; print only the summary and failures. Screenshots at most 1280 px wide, each looked at once.

## Normal run (no `--amend`)

1. Read the ticket with its comments (`gh issue view <issue> --comments`), the spec (`gh issue view 1`),
   GLOSSARY.md, every ADR in `docs/adr/`, and the code the ticket touches. Read enough to be exact; do not guess
   signatures, constants or file layouts.
2. Write `.ticket-work/<label>/PLAN.md` for an executor that is competent but literal and has not seen the ticket
   discussion. Start with a short summary of the goal and the acceptance criteria, then:
   - **file-by-file steps**, in order, each naming the file, the type/member, and what changes;
   - **exact numbers, formulas and signatures** (constants, ranges, units, new public API with full C# signatures);
   - **traps** you foresee (off-by-one, units, rotation order, sector bounds, determinism/seed use, the things a
     plausible-looking implementation gets wrong);
   - which **GLOSSARY.md / ADR** changes are needed (exact wording for new glossary entries), or "none";
   - which **tests** the executor should add besides your locked ones, and the `dotnet test --filter` expressions
     it should run (only the test classes it touches; never the whole suite, the script runs that);
   - **captures**: which acceptance criteria need a screenshot, and the exact dev capture command lines (Godot
     console exe, `--path game`, `--`, then `--seed=…`, `--poi=…`, `--capture=<absolute path under .ticket-work/<label>/>`,
     etc.; see `game/scripts/DevCapture.cs`), plus what the executor must check in each image. Say whether the
     benchmark must be run (default: no);
   - **must not touch**: files and areas outside the ticket, and the locked test files;
   - what to write in `REPORT.md` beyond the standard sections, if anything.
   Write the plan for the repository as it is at `HEAD`; the executor's clone starts there.
3. Write the **acceptance tests**: a small number (typically 2–6) of facade-level xUnit tests that pin the ticket's
   acceptance criteria, as new or changed files in `tests/Scp3008.Core.Tests/`. Assert behaviour through the facade,
   not implementation details, so a correct implementation that follows the plan passes. They may not compile yet
   because the new API does not exist; that is expected. Put them in their own test class(es), so a failing locked
   test can be told apart from the executor's tests.
4. Write `.ticket-work/<label>/locked.txt`: one repository path per line (forward slashes, relative to the
   repository root) of each acceptance test file you created or changed. The executor may not change these files.
5. Write `.ticket-work/<label>/captures.txt`: one repository path per line of each screenshot the executor must
   produce (paths under `.ticket-work/<label>/`). Leave the file empty when the ticket needs no captures.
6. **Cross-check** before finishing: re-read plan and tests against each other. Every number a test expects must
   follow from the plan's formulas and constants (an earlier plan had a test expecting the wrong elevation for a
   rolled piece). Check that the test file compiles against the API the plan defines (names, parameter order,
   types).

You do **not** edit production code, GLOSSARY.md, ADRs or any file other than the locked test files and the
files in `.ticket-work/<label>/`. The runner checks this and stops the ticket if anything else changed.

If the ticket cannot be planned (an unresolvable contradiction with the spec, or a blocker that is not closed in
reality), comment on the issue explaining why and end with `PLAN_BLOCKED: <reason>`.

End your final message with exactly one line: `PLAN_DONE` or `PLAN_BLOCKED: <reason>`.

## Amend run (`--amend`)

The runner resumes your planning session when the evidence points at the architect: the executor reported
`LOCKED_TEST_SUSPECT: <file>:<test> — <why>`, the only remaining gate failures are in locked tests, or the
reviewer answered `REJECT-PLAN`. The evidence follows the arguments.

1. Judge the evidence honestly against the ticket and the code. Rerun the relevant tests if that helps.
2. Either **correct** the plan and/or the locked tests (edit `PLAN.md`, the locked files and `locked.txt` in this
   repository; the runner copies them to the executor's clone and records the new hashes), and append an
   `## Amendment` section to `PLAN.md` that says exactly what changed and what the executor must do now;
3. or **confirm** them and explain precisely why the executor is wrong and what it must do instead.

Only the locked tests, `locked.txt`, `captures.txt` and `PLAN.md` may change; production code stays untouched.
There is only one amend per ticket, so make the answer complete.

End your final message with exactly one line: `AMEND_CHANGED: <one-line summary>` or
`AMEND_CONFIRMED: <one-line summary>`. Your final message is passed to the executor as its instructions, so
write it for the executor.
