---
name: triage
description: Triage a GitHub issue - claim it with the `triaging` label, understand it, reproduce it, find the cause, write a fix plan another agent can pick up, post it on the issue, label it `triaged` and release the claim. Skips issues another agent is triaging unless the user names one. Use when asked to triage, analyse or investigate an issue (`/triage 80`, "triage the next untriaged issue"). Never implements the fix.
---

# Triage an issue

The argument is an issue number, or `next` (pick the oldest open issue that has neither the `triaged` nor the `triaging` label; say which one you picked and why). Everything you learn goes **on the issue**, so that anyone, human or agent, can start the fix without re-deriving it. The `dev` skill refuses an issue that is not `triaged`.

## Claiming the issue

- `triaging` means another agent is on it right now. **Skip such issues by default**; take one only when the user names it explicitly (`/triage 80` on an issue labelled `triaging` is such a request; `next` never is).
- **As soon as you have picked an issue**, before any analysis: `gh issue edit N --add-label triaging`, so no other agent picks it up meanwhile.
- The label comes off again at the end (step 7), after `triaged` went on. If you abandon the triage, remove it too and say so on the issue.

## Steps

1. **Read everything.** `gh issue view N --comments`; follow every linked issue, PR and external page (WebFetch); read the files the issue names. Restate the problem in one paragraph before touching code.
2. **Find the code.** Map where the behaviour lives (file:line). Check CLAUDE.md for the area's rules and conventions.
3. **Reproduce it.** Prefer real evidence over reasoning: a throwaway unit test or probe, a real-browser run (Playwright), a request against a running server. Say plainly whether it is **confirmed**, **partly reproduced** or **not reproduced**, and what you ran. Throwaway probes live in the scratchpad or are deleted before you finish; **never commit them and never change source files** while triaging.
4. **Find the cause.** Explain the mechanism with file:line references and the evidence. If the issue's description turns out wrong or incomplete, say so and correct the title.
5. **Plan the fix.** Numbered steps, each with an acceptance criterion; the tests to add (unit / integration / real browser); docs to update; what is out of scope; related issues.
6. **Write the handoff comment** on the issue, in this order: the problem in one paragraph; verified facts (a table if there are several cases); code map; plan with acceptance criteria; tests; how to reproduce it (commands and scripts verbatim, in a `<details>` block when long); pitfalls you hit; related issues. State the commit of `main` it was checked against.
7. **Bookkeeping.**
   - `gh issue edit N --add-label triaged --remove-label triaging`: the handoff is posted, the issue is ready for `dev`, and the claim is released.
   - A duplicate: keep the one with the better discussion, close the other with `gh issue close M --duplicate-of N` plus the `duplicate` label, and carry anything the kept one lacks into its handoff.
   - A separate bug found on the way: file it (`gh issue create --label bug`) with what you know; do not fix it.
8. **Report** in chat: a short summary of the cause, the plan and the link to the comment. No code was changed; say so.

## Rules

- Only the tests you need for reproduction; never the full suite.
- Respect the machine's limits named in CLAUDE.md or memory (e.g. Vitest `--maxWorkers=4`, one `dotnet test` process at a time).
- Facts over guesses: a claim without a reproduction or a file:line reference is marked as such.
