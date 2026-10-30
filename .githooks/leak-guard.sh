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
