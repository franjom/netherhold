# SubagentStart hook: reminds every subagent (Explore and Plan included, which skip CLAUDE.md)
# to read AGENTS.md and to use context-mode for large output. Prints the hook's JSON reply.
$context = @'
Project rules for this subagent (Dungeon Keeper 2 remake):
1. Read AGENTS.md in the repository root before you start, and follow it. Explore and Plan agents do not get CLAUDE.md, so this is your pointer to it.
2. Use context-mode for anything over about 20 lines of output: builds, test runs, kb check, the docs gate, DK2.Tools dumps, Ghidra listings, logs, git log or diff, searches across the tree. Run it through mcp__plugin_context-mode_context-mode__ctx_execute and print only the answer. Plain Bash, PowerShell, Grep and Read are for small, known-size output and for edits.
'@
$reply = @{
    hookSpecificOutput = @{
        hookEventName     = 'SubagentStart'
        additionalContext = $context
    }
}
$reply | ConvertTo-Json -Compress -Depth 4
