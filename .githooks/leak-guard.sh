# Sourced by commit-msg and pre-push. Not a hook itself.
#
# What must never reach this repo:
#   - a co-author trailer of any shape, whoever it names. GitHub turns one into a
#     contributor entry; AI help is credited as Assisted-by (AGENTS.md).
#   - the owner's work account: the string in a commit message or identity, the
#     domain in any added line of text.
#
# The patterns are spelled with a bracket so this file never contains the
# string it is guarding against.

LEAK_COAUTHOR_RE='co[-_ ]?authored[-_ ]?by'
LEAK_WORK_RE='[m]cs'
LEAK_WORK_DOMAIN_RE='[m]cs\.hr'

# An AI tool as a commit's author or committer. GitHub links the address to the
# tool's account and lists it as a contributor (three step-5 commits did, 2026-11-26).
# Commits use the repository's git user; AI help goes in the Assisted-by trailer.
# Matched against "name <email>" only, never the message, where Assisted-by names the tool.
LEAK_BOT_IDENT_RE='@(openai|anthropic)[.]com|(^|[^a-z])(codex|claude|chatgpt|copilot|gpt)([^a-z]|$)'
