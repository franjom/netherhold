# KWD/KLD writers

Base: `749b0213`; branch: `codex/kwd-writers`.

Implement the owner's five milestones in order. Each milestone ends with the
full gate in `docs/HANDOFF.md` and an independent read-only verifier applying
`.claude/agents/reviewer.md`. Preserve shipped images; never commit game data.

1. Container: retain all header words, including understated LEVEL sizes;
   add chunk/file writers and synthetic plus install-gated identity tests.
   Choose new headers from the corpus, with explicit uncertainty in an ADR.
2. Images: implement validated Split/Join for fixed and tagged records;
   preserve both creature strides and test GLOBALS only in its known path form.
3. Overlay: pair every reader with a writer using shared field layouts;
   identity and field-edit tests cover every type and tagged payload shape.
4. Editing: level file set, typed changes, tagged insertion/removal, map resize
   and repaint, consistent headers, and rejection of unsafe trigger changes.
5. Tool/docs: shared rewrite audit and CLI, complete ADR and question rows,
   README writers and goal 7 status; verify the entire branch independently.

Each logical commit builds Release without warnings, passes the affected
projects and `git diff --check`, and records `Milestone:` and `Next:`.
Record each milestone verdict in the next commit. After a clean final review,
merge into master and remove this task's worktree, as the owner authorized.
Do not push.
