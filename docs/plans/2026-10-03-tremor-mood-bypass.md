# Tremor idle mood bypass correction

Scope: preserve the existing dispatcher for creatures without Fleeing. For
Fleeing keepers in raw idle, run the modeled mood prelude before clearing the
flag. An angry selector failure continues through the unhappy selector.

1. Add failing acceptance/refusal regressions, including Rebel retaining raw
   idle and Leave accepting without a reachable portal.
2. Share built job starters with an explicit acceptance result. Keep legacy
   unbuilt angry-table behavior outside this focused path.
3. On accepted mood selection, preserve Fleeing; otherwise clear it, run
   fight return, request state89, and end the handler turn.
4. Reset a transient dispatcher marker at tick start. The later anger pass
   applies anger but cannot repeat this turn's mood selection.
5. Update the canonical function records and record unmapped binary gates.
6. Run the full gate, compare all42 scenarios against Stage2, commit the fix,
   and obtain fresh independent verification before Stage3.
