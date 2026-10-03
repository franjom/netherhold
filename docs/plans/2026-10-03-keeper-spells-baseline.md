# Keeper spells: stage 0 baseline

Measured 2026-10-03 at `d3abf248e6d81d906f75a10b2f301a44bf3da78d`.
Decision and staged predictions: ADR-0347. HashVersion/FormatVersion: 169.
Command: `dotnet run --project src/DK2.Harness -c Release --no-build -- run scenarios --out build/keeper-spells-baseline`.
`DK2_DIR=E:/GogGames/Dungeon Keeper 2`. All 42 runs completed; exit 0.
Series are retained under that ignored output directory. SHA-256 below hashes
the complete original `hashes.tsv` bytes, including tick zero and CRLF endings.
Final tick/hash is also listed so a changed series has a useful endpoint.
Level 1/2 seeded replay drivers and packets are absent from this worktree;
no per-seed comparison was run. These scenario runs are modern-only measurements.

| Scenario id | Final tick | Final state hash | SHA-256 of hash series |
|---|---:|---|---|
| creature-roots-walk-each-turn-001 | 4 | c5fdc6c0c97068d1 | 8ef112fd96b03325c23c5b62c74b11663bef15ba4b53fdf190d25ad02a2398d8 |
| creatures-a-goblin-collapses-and-dies-001 | 2500 | 3c09614e7c4a1651 | e94bec404169be94d9dd512d43f2c3a60cc05ba03d4965b837698ff4bed673ba |
| creatures-a-goblin-eats-a-chicken-001 | 4200 | aa4b922e1a67c976 | f9b98840bdfcb0391252c94fccd19ef8c047bd8024fd859f07a21e4b7691b308 |
| creatures-a-goblin-gets-hungry-001 | 5400 | af506b36af0ce0ef | 0ab7935e15050a08fb2fe6c35fb2ba2dc76f7fd4e19f09bff87219cd4a070e62 |
| creatures-a-goblin-with-no-lair-walks-out-001 | 6700 | 489ca61c4d5a2d68 | 9ec02ccbbacf4626bb4078d2e2fbe7a6cdf7066d795ab4d548b6c78707b4d81a |
| creatures-a-keeper-drops-a-chicken-in-a-prison-001 | 2400 | 10995b4d929bcd4d | 5380a5d448fd1473db25d1db940394f07b8968e87c94f9201955f40774f375c9 |
| creatures-a-keeper-drops-a-warlock-and-an-imp-on-open-ground-001 | 120 | 13b7de175fe28435 | 6f57172b61bef4d9a19faf778ecea1d1f335bcbfc33aaadbcdbeb1e050296cd8 |
| creatures-a-keeper-meets-its-wage-bill-001 | 18200 | 57b777dbc60bae2d | 3969440ec00e592cfcdeb436a1c0faf97178b0c3c966619ce8e9bef7ca340daa |
| creatures-a-portal-fills-a-dungeon-001 | 2600 | 9b4338d9e9f79100 | 35d1290e0c96c34433b4b6ddb9de5eecfe023931dcd70fc0b0465f94725d0883 |
| creatures-a-vampire-with-no-portal-turns-good-001 | 1500 | 5c76fdf9d10ae18a | ebda421f0122fc7f9850aadef19a9ffa867e3b526f5cc8e504beae3e02fa87ac |
| creatures-an-imp-buries-a-corpse-001 | 2000 | e976d520dce10432 | d94ad77c2cd54d3947228ad2253a49ae9102ac5f26b7b62e7bc5c2bf592d202c |
| creatures-an-imp-claims-a-portal-001 | 900 | d30775788f07704c | 828163b2939343c99d2d8c67b9925b879a5fd40fb9b163fff8d44945c1c68dab |
| creatures-an-imp-drags-an-enemy-to-prison-001 | 4000 | 155aafa5ce064d60 | e49478f8e284725017fae5e5b15dd0d8f85af1d6581e023852bb996bf7b0bb3e |
| creatures-five-corpses-make-a-vampire-001 | 500 | 2fa23b7004a0caad | be9dc95be31e72401c17b509fc91c8535e1308af7547cddc833c18ed6e319162 |
| creatures-the-keeper-picks-up-an-imp-and-slaps-it-001 | 400 | 634e1ff8cf650403 | f5cc7b8da004bc4110e4d8c8df2636d6b4e3695dbeaabfec4235b3d8bf07866f |
| creatures-two-keepers-find-work-for-a-goblin-001 | 12000 | 454d7ed3f34a1579 | 562f024cd0bc364a3b62c24566df1c00dd0b41ff6ec71ded1c1452737e9e48d7 |
| fog-a-creature-reveals-part-of-the-world-001 | 1 | ca77f06d2d96f68b | ada0f0deea8e63864560b963ec15c8c0190e54de3be31bec521e29d8f69f5ca8 |
| imps-carry-gold-home-001 | 250 | 4fd56f6e984a11ff | a72b3d6c8aeaa90906b629800b4485979d3e3039385f1eee772ee2a72f9b8b4c |
| imps-dig-what-is-marked-001 | 200 | 9ee2458fc0994c98 | 183220ba4ca9733d86c0e103642bcdedbd26780d8e16e984a0fd4f2600a9d012 |
| imps-fetch-dropped-gold-001 | 1800 | 8d620e7055fe14e2 | ef1fc972afda65b500def96cd555a1ff2052316752783bd87f4eb30344e86fb2 |
| imps-never-run-out-001 | 700 | 104c8eb8507ae9bb | e00a03d358781f7e217f4d01ac1b9142ab6c83153904dba89377eda1e03d9ae9 |
| keepers-level10-asmodeus-idle-001 | 2408 | df274625816dafd4 | f16be43bf3f069f0a0447a42e2f5ed05ee38c51fb3a6034b2f52ead804e48233 |
| keepers-level11a-drako-attack-001 | 200 | ffc5098d00fe5367 | 6a6cf52027dab37ebaee4ed8f0fce42fa8783e1ba8d62ba9231ee1b0079bcd08 |
| keepers-level17-harkan-call-to-arms-001 | 140 | f346fe3c75aec23f | bcf11c9aca112ff864f8b09688eb4a5d7b5d9692d5d1fc2064edd8971ff1a3c7 |
| keepers-level17-harkan-training-001 | 40 | ae33fe3c4382c560 | e884a03e84ac97471499432f94df7600e921d99bc7810d2014a776ea06bf7ae8 |
| keepers-level17-harkan-treasury-full-001 | 3000 | 4d866499aaf978f5 | c9b5910ab0b7a4db76274fa92f485486c53ac7e72b8033c15d90216cd06b872e |
| keepers-level17-harkan-unassisted-001 | 1200 | 3310e8cc5d7c9e34 | e8a7b44aca446a1ea1ee0546e9e7f0932e234eac882c212d3157a78f6bf7ddb7 |
| keepers-level17-player5-retreat-001 | 130 | deab079858a289c2 | 9c6aefb472837d4cd201db85ed39943b80581c80d277b42cea3fe13490084252 |
| keepers-level18-nemesis-attack-001 | 400 | 2ebbe6e7202c3afe | f66217c55f1c0e5b383b83bad9edd1bea83d694c2f731a57e872de787b74b87d |
| keepers-level18-nemesis-heal-001 | 120 | 61d1113b168dc891 | 6240cc38d4a29c03af679807791cd8c8b2643610364af1a01d2289f8f8e1cc02 |
| keepers-level18-nemesis-inferno-chicken-001 | 60 | 56eed799ae7dd42d | 841ce702073d33787a8cd34f82d000c1d3b9313cc4b9389aa5da8a379b2d9a1f |
| mana-land-pays-a-keeper-001 | 100 | 1b002d9f14c9a9f8 | b4ae2e4382df80e8337933e9abb183a117a388094ffa0a5dc042f94fb39f3b39 |
| rooms-a-hatchery-lays-and-hatches-001 | 3000 | 58755f72350001be | 1beba01f7b26ceb33cec5bc4ce548b30e0db62b69e9c71e3fba591e37c3edd40 |
| rooms-build-a-treasury-001 | 20 | f01e82b3ce9c404f | 3a2b7478213860115c9f12d1ecd24da882ab443aa773d8312b995b7d5d78e3d4 |
| rooms-sell-a-room-back-001 | 12 | d1c893a52145baf1 | 5f8f7a46675e5e1053f6d0e4740bb8bac36058f8e4836ea80f6a3699bb4c7917 |
| smoke-campaign-level1-001 | 60 | 84bfd755bad8bf20 | 92215bb727d8eafc989c918c675db43f0aab0ff9f4bdbe677f894af04d91a5e2 |
| smoke-campaign-level1-things-001 | 60 | e7f40b9ee203c360 | c89cf140bbf0f2d677bf99c1977483b8a62f430677f0f2c68c7bbb4584cacdc1 |
| smoke-determinism-flat-001 | 600 | 68b04785cd9acd3f | fc4213cf8b17b5a67fe58f84e7fbca3920e6a34f718df99a796c57dcdf27ba09 |
| spells-call-to-arms-001 | 560 | 7c005bc3435f950d | 78d54e3c109984fbe0056d4a093947f29b48648f1b85bdb698499ba132196ac4 |
| spells-call-to-arms-level8-001 | 240 | 94c9301f3df1eafa | b4bc9b09c9951c686f42f351a6ed698d32180e97fe6d89976d7ac1d4b67f10b6 |
| tiles-dig-and-claim-001 | 80 | de18c0c20247d66e | 7c048d4e42ee15e2371d7e4c352620e934ed44389c8f3fdb99e850316e200d81 |
| tiles-mine-gold-001 | 24 | 293023f1666ded35 | 24e38aad2bc0abdcdd6c409f63e03ffa8d9ea86f17972ff70e6c87229ff2a243 |
