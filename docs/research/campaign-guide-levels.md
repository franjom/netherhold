---
systems: [tooling-process]
---
# Campaign levels as the public text guides describe them

Guide-reading notes for the developer. Everything here is a guide's claim, not a verified game fact; numbers and mechanics
are collected again in `knowledge/guides.yaml` under `facts_to_verify`, and the advice is rules R21-R64 in
`knowledge/rules.yaml`.

Source keys (every claim below carries one):

- **H** = Jarrod Hampton, "The Dungeon Keeper 2 Mission Guide" (the-spoiler.com archive; the URL redirects to a mirror):
  https://the-spoiler.com/STRATEGY/Bullfrog/dungeon.keeper2.1/Page%202.htm  (mirror: https://strategygamers.com/walkthrough/dungeon-keepertm-2-2).
  Covers all 20 levels in one page. It numbers 14 and 15 both "Reap / Peachtree"; its text for 15 is the Fairy Fortress level (Fluttershine).
- **L** = ludo.guide "Dungeon Keeper 2" community guide, one page per level, base https://www.ludo.guide/guide/dungeon-keeper-2/ plus the slug
  named in each section. It is the only source here that separates the variants 6a/6b, 11a/b/c and 15a/15b.
- **B** = BellaOnline walkthrough (Lisa Shea), levels 1-10 only, https://computergames.bellaonline.com/gaming/w/dungeonkeeper2/dk2walkN.html (N = level).

Not readable: strategywiki.org (403), dungeonkeeper.fandom.com (403), pearlnightslair.neocities.org/shrines/guides (404, page gone), cheatcc.com (403).

Level-file mapping: the guides give a level name and a number; none of them knows the shipped file names. Level N = file `levelN` for the
single-variant levels (the order is the same in H and L). Variants: 6a/6b = L's "(a) Beseiged" / "(b) Rout" by letter only (H and B call the
level "Rout"; B says front = Besieged, back = Rout, so which shipped file is which is not settled by the guides; the owner's recorded 6a has a
Prison, Torture Chamber and no Portal, which both variants share). 11a/11b/11c = L's starting positions a/b/c. 15a/15b = L's "Crusade (a)" and
"Storm (b)"; H's level 15 text matches Storm (b).

---

## Level 1 - "Warcry", Smilesville (`level1`)
- Win: kill Lord Antonius. Sub: 5x3 Lair, 3x3 Hatchery (H, L `a-warcry-smilesville`).
- Introduces: Lair, Hatchery, Goblin (L a-warcry-smilesville).
- Threats: a few Dwarves break into the dungeon after the Lair and Hatchery stand; Antonius arrives alone when the last red block is mined (H).
- Plan: tag gold to 12,000; claim the Portal; build Lair and Hatchery as the narrator says; Goblins kill the Dwarves (experience); dig N then W through a thin soft-rock tunnel for the Increase Level special; open Antonius' door and swarm him with Goblins (H, L a-warcry-smilesville).
- A dropped creature is stunned and vulnerable for seconds; Goblins little (L, H).

## Level 2 - "Enchantments", Sing-Song (`level2`)
- Win: invade Lord Darius' Keep and kill him. Sub: Library, research all spells, train creatures (H, L `b-enchantments-sing-song`).
- Introduces: Library, Training Room, Warlock, Create Imp, Lightning Bolt (L b-enchantments-sing-song; B 2).
- Threats: waves of Dwarves from the north (B 2); a Dwarf horde when the Keep is breached, then Darius and bodyguards (H).
- Plan: Library and Training Room at least 4x4 (H); Library big enough for 5-6 researchers, walled Training Room, Goblins to level 4 first, then Warlocks (L); finish all research marked "?" before killing Darius, or it must be researched again next level (L); breach where the game marks the tiles, Goblins swarm, Warlock fireballs, Lightning Bolt to stun Darius (L); Imps dig east of the breach for the secret while the Dwarves fight (H).
- B: killing the Lord ends the level at once; the special was not found because the Lord died first.

## Level 3 - "Greed", Ramshackle (`level3`)
- Win: kill Lord Avaricious. He leaves his castle only when all the gold around his keep is mined (H, L `c-greed-ramshackle`).
- Introduces: Treasury, Workshop, Troll, Firefly (arrives as a scout), Sentry Trap, Wooden Door (L c-greed-ramshackle; B 3).
- Threats: Avaricious comes out with many Dwarves and other warriors once the gold is gone; cannons guard the secret (H); do not mine the gold near his keep before creatures are trained (L).
- Plan: Lair, Hatchery, Training, Workshop, Library, Treasury in that order; Wooden Door and Sentry Trap at the Portal entrance (H); two Trolls speed the Workshop (L); tag all the gold around the enemy area last, let the attackers walk into the Sentry traps, then loose the creatures plus Lightning Bolt (H, L); an Imp (fast) fetches the secret past the cannons (H).

## Level 4 - "Snipers", Shadygrove (`level4`)
- Win: cross the moat and kill Lord Ludwig in his fort. Sub: claim the Guard Rooms, lead a charge of grouped creatures (H, L `d-snipers-shadygrove`).
- Introduces: Guard Room, Dark Elf, Possession (L d-snipers-shadygrove; B 4).
- Threats: frequent early attacks; a lookout Dwarf that must be killed (sniped) without raising the alarm; Cannons in the moat; Hero Portals face the East and West Guard Rooms (L). 3 Warriors behind the first door (H).
- Plan: claim the Guard Rooms to draw Dark Elves; Doors and Sentry Traps round the Heart; train; two Increase Level specials by the Hero Portals saved until creatures are level 4; lock Imps in a room so they do not raise the alarm; possess a Dark Elf and snipe the lookout, snipe or possess a Troll to smash the Cannons; then group-possess a party and break in (L). H: possess a creature, group the others, attack the cannons, then Ludwig "isn't well defended". B: tunnel into the sides of the enemy area; a Hatchery by each guard post feeds the guards.
- Owner's own Level 4 play (docs/plans/2026-11-15-campaign-bot-owner-strategy.md, R14) agrees with L on the lookout and the possessed party.

## Level 5 - "Fear", Elmshadow (`level5`)
- Win: force the way into Lord Constantine's Keep and kill him. Sub: claim the Prison, make Skeletons (H, L `e-fear-elmshadow`).
- Introduces: Prison, Wooden Bridge, Skeleton (L e-fear-elmshadow; B 5). Secrets 6 incl. Horny Talisman part 1 and the Golf secret level key (H, L).
- Threats: patrolling Wizards just south; constant attacks from the north once the Prison is taken (H); Thieves, Dwarves (L); in front of the Keep 4 Fear Traps, many Warriors and Knights; when the Fear Traps die 2 Knights come out, then Knights, Mages and Dwarves pour from the Hero Gate (H).
- Plan: small first rooms (Lair, Hatchery, Training, Workshop, Library, Treasury), claim the Portal; dig south, Imps claim the Prison; Prison set to imprison so every fallen hero becomes a Skeleton; Doors and Sentry Traps at the north; Skeletons are the only creatures a Fear Trap cannot drive off, so they destroy it (H, B 5); Skeletons attack nearby cannons when they leave the dungeon, so clear the cannons first with a possessed Troll or Warlock (L); excavate beside the Fear Trap so Imp-claimed tiles let Skeletons attack it automatically, or possess one (L); then muster Trolls, Goblins, Dark Elves, Warlocks on Constantine, Lightning Bolt when he steps on your ground, lure the hero wave back onto your defences, every creature fighting (H, L).
- Matches the owner's R15, R16, R19.

## Level 6 - Sweetwater (variants `level6a`, `level6b`)
Common: no Portal, you start with a Prison and the Torture Chamber; the army grows from captured heroes and Skeletons (L f/g, H, B 6). Introduces: Torture Chamber, Heal, Barricade, Fear Trap (L f/g); L lists Dark Mistress as new for the (a) variant.
- **6a, "Beseiged" (L `f-beseiged-sweetwater-a`)**: win: break into Lord Iron Helm's castle (entrances to the south are too well guarded; go in from the rear guard rooms and prisons). Torture the first Thieves and Wizards for map information; dig far SW and E to find guard rooms and prisons holding neutral creatures; Hero Gate NE; convert about five Knights and five Guards (heal them so they survive the rack), add 6-7 Skeletons; Sentry traps and guards near the base; finish with Knights, Skeletons and Mistresses, Horny if the Talisman exists.
- **6b, "Rout" (L `g-rout-sweetwater-b`, H level 6, B 6)**: win: intercept and kill Iron Helm before he reaches the Hero Gate (a timer starts; at zero he flees). Torture the Wizard/Warlock in the Prison to learn the gate's place (far east of his stronghold, H says a fair way away). Hero Gate guarded by two cannons and two Fear Traps: Skeletons destroy them; Barricades and Sentry traps block the gate; claim land up to his side door; Thunderbolt him; he has level 3 Knights, so bring Horny; Barricades slow his escape (L g). H: Sentry traps after each claimed area, Lightning Bolt on heroes lured onto your ground, Imps drag prisoners off to become Skeletons, summon Horny only when the army is ready.
- B 6: Traps are the key; cross-linked traps; no Portal means protect every fighter.

## Level 7 - "Caverns", Emberglow (`level7`)
- Win: kill Lord Sigmund and his Giants. Sub: cross the Giants' caverns, mind the lava (H, L `h-caverns-emberglow`).
- Introduces: Salamander (joins; crosses lava), Giants as enemies (L h-caverns-emberglow). Secrets 6 incl. Horny Talisman part 2 and the Duck Shoot key (L, H).
- Threats: a first Giant attacks the Heart; more Giants later, a force when Sigmund "calls"; Sentry guns (6) on the right end of his fort; the fort sits in a lava pool with front and back doors (H, L).
- Plan: Goblins and Imps kill the first Giant (L); claim the abandoned dungeon; replace a Lair with a Prison and convert 5-6 Giants, plus Guards (L); Salamanders and Giants cross lava and batter doors, wooden bridges burn up so claim fast (H, L, B 7); attack the fort from the left, not the six-gun end (H); build bridges to both doors and drop troops front and back at once (L); bait enemies onto a bridge and sell it (H).

## Level 8 - "Aftermath", Snapdragon (`level8`)
- Win: take over Keeper Dante's abandoned dungeon and kill Lord Titus. Sub: claim all scattered rooms, beat the Guardian Wizards (H, L `i-aftermath-snapdragon`).
- Introduces: Rogue (via Casino), Stone Bridge, Casino, Gas Trap (L i-aftermath-snapdragon). Start: 3 Salamanders, 2 Mistresses, 1 Troll (L).
- Threats: claiming the Portal early sounds an Alarm and wakes nearby Wizards and Guards (L, H); Fear Trap and Gas Traps guard the Library/Workshop; Hero Gates; Giants come through the Casino; Titus has Wizards and a sphere of alertness (L, H).
- Plan: Lair and Hatchery first, two gold cubes only at the start (B); win a level or two before the Portal; Dark Elves snipe the Fear Trap, Gas Traps and Alarm (H); Mistresses may disable the Fear Trap, otherwise Skeletons after the Prison (L); contact the three neutral Mistresses in the Torture Chamber so they join (H, L); disable Hero Portals by claiming land around them (L); convert about six Giants and Guards (L); attack Titus from the left side, Imps claim up to the fort, then drop the army or Call to Arms; Horny if available (L, H).

## Level 9 - "Ambush", Silverstream (`level9`)
- Win: kill Lord Voss as he crosses the realm with the Gem. Timer 20 minutes (H, L `j-ambush-silverstream`, B 9).
- Introduces: Bile Demon (joins at once) (L j-ambush-silverstream).
- Threats: Voss arrives from the west at 0:00 with a force, does not stop to fight, breaks through rooms rather than going round, runs for the east exit if his escort dies (H, L).
- Plan: Training Room, Prison (Skeletons), Torture Chamber, Lairs; mine the gem seam; capture the east then west Guard Rooms from the side (bridge, Imps breach, drop creatures, convert 4-5 Guards) before 8:00 and 4:00 marks (H, L); fill them with doors, Sentry traps, Barricades, Fear traps; Bile Demons as a defence line (L); when Voss nears, drop all creatures there and Thunderbolt him each time he rises (H, L); Barricades and traps in the east guard room if he runs (L). B: trap him in the middle guard room and drop everyone there.

## Level 10 - "Smash", Woodsong (`level10`)
- Win: defeat Keeper Asmodeus, take the Gem. Timer 45 minutes (Lord Ronin's siege army attacks your own dungeon at the end) (H, L `k-smash-woodsong`); B: with the patch the clock is over 3 hours.
- Introduces: Tremor, Turncoat, Braced Door, Spike Trap (L k-smash-woodsong). Secrets 9 incl. Horny Talisman part 3 and the Maze key.
- Threats: attacks from several directions early (Hero Portals SE and W) (L); Ronin's armies east and west and on a middle bridge; two rooms east and west packed with enemies (H).
- Plan: build almost everything before claiming the northern Portal: Library, Workshop for 5 workers, Training Room 9 tiles, Prison, Torture Chamber, Lairs, Treasuries, a 5x5 Hatchery for Bile Demons (L); Braced Doors, Spike, Gas, Fear traps in invasion tunnels (L); interrogate prisoners for the siege's place (H); at about 15:00 left, ignore Ronin, bridge around him to Asmodeus (H, L); Tremor the walls, Imps dig in, Mistresses and Skeletons charge to scare enemy Imps, then Bile Demons, Giants, Warlocks on the Heart; suggested force: 4 Bile Demons, 4 Giants, 4 Guards, 6 Mistresses plus Elves and Warlocks at level 3-4 (L); Call to Arms at the Heart, Lightning Bolt the heroes who run home (H); leave the two packed rooms alone (H). B: Lair west, Training north, Treasury east, Hatchery south, little rooms first then expand; door and three cannons at each breach.

## Level 11 - "Carnage", Sparklydell (`level11a`, `level11b`, `level11c`)
- Win: kill every rival keeper (Kronos, Belial, Raksha, Morgana, Draco in L; H names Drako, Morgana, Raksha, Kronos) (H, L `l-carnage-sparklydell-a-b-and-c`).
- Introduces: nothing new (L). H says "newly granted traps" and Horny Talisman part 4; secrets are Trigger, Boulder, Steel Door.
- Threats: rival keepers, each with its own army and traps; Trigger traps round a Heart (H); Thunderbolt and capture/torture in the NE keep (L a).
- Plan, general: build, train, fortify, kill the weakest neighbour first, claim its Portal, move on; Call to Arms at each Heart; heal to level up; levels 8-10 make the last fight easy; Sight of Evil to scout each Heart (H, L).
  - **a**: nearby Portal; excavate SE to breach one dungeon; go anticlockwise; stiff opposition NE (L).
  - **b**: two weak keepers either side, one tricky one north; pre-dug land for Workshop, Prison, Torture; take East and West first, then the north (L).
  - **c**: start with 2 Mistresses and 3 Salamanders; dig NE to a lightly guarded Heart with Warlocks and Imps; Fear traps against rival Imps, Spike traps and heavy doors against incursions; North keeper last (L).

## Level 12 - "Scavenge", Goldenglade (`level12`)
- Win: finish Lord Bramble's remaining forces and take the Gem from Keeper Malachi. Sub: amass an undead army (H, L `m-scavenger-goldenglade`).
- Introduces: Graveyard and Vampires in use (H, L); no Portal on this level (L, H). Skeletons start trapped east and join when freed (L).
- Threats: small packs of Dwarves once the Graveyard is claimed; 3 Sentry traps looking into the camp; spike traps and cannons in Brambles' keep (H, L).
- Plan: Lair, Hatchery, Training, Library, Prison, Torture, Casino (to keep Vampires amused) (L); bodies go to the Graveyard if there is no Prison, and non-Skeleton prisoners go to the Graveyard to draw Vampires (H, L); army of 10 well-trained Skeletons (level 4+) and 5+ Vampires (level 4+) (H), or eight level-4 Vampires (L); a Vampire killed returns minus a level (L); storm Bramble's keep, sell its rooms; attack Malachi from the front door or the side; Skeletons first to frighten weak creatures, Vampires and Imps behind, Call to Arms at the Heart (H, L).

## Level 13 - "Conversion", Cherish (`level13`)
- Win: crush Keeper Malleus' Vampire horde and take the Gem. Sub: capture and convert the Monks of St. Cuthbert, use them against his Vampires (H, L `n-conversion-cherish`).
- Introduces: nothing new (L). Horny Talisman part 4; Bowling key (H, L).
- Threats: patrols (Wizard, two Warriors/Guards, Giants, a Monk) walk straight at your Heart; one room guarded by Sentry traps, Ice traps and four Elven Archers; two Hero Portals south of the Prison; when Malleus' dungeon is breached he releases the Vampire army, which comes in waves (H, L).
- Plan: Prison on imprisonment; Torture Chamber; convert 5-6 Monks (heal them through the process); house converted heroes apart; a 5x5 Hatchery, Workshop, Graveyard for 8+ Vampires (L); possess a Dark Elf and snipe the four Archers through the door, then everyone in with Call to Arms (H); funnel Malleus' Vampires by SELLING the wooden bridges, then Iron Doors, Spike, Freeze and Boulder traps in the thin tunnel (L); Monks stop Vampires (H, L); do not breach Malleus' dungeon early (H, L); then Skeletons to destroy the two Fear Traps before his Heart and the cannons around it, Call to Arms at the Heart, Lightning Bolt any Vampire that reaches yours (H, L).

## Level 14 - "Reap", Peachtree (`level14`)
- Win: summon Horny and kill Lord Tiberius. Sub: claim all Mana Vaults (H, L `o-reap-peachtree`). All four Talisman pieces; Horny costs 100,000 mana (H).
- Introduces: Mana Vaults, Magic Doors, Hero Gates (H, L).
- Threats: patrols from a Hero Gate (Wizard, Archer, 2 Giants, 2 Monks) (H); level-1 archers, Giants and Monks early (L); a Magic Door; cannons; damaging the lair door triggers a massive hero sortie (H).
- Plan: Lair, Hatchery, Training, Prison; Salamanders; a small Workshop to sell later for cash; do not dig east first, and leave the last gold tile west untouched until the army is trained (L); enter the Magic Door room from the side; claim the Guard Room, two Vaults, disable the Door; (H) Salamanders kill the Sentry traps over lava; attract the sortie then summon Horny on it; recharge, Call to Arms the creatures to the middle, summon Horny again to reach Tiberius (H, L). Enemies carry cash; imprisoned ones are easy for Imps to loot (L).

## Level 15 - Fluttershine (`level15a`, `level15b`)
- Win: storm the Fairy Fortress and kill Lord Volstag (H; L spells it Volstang). Introduces: Combat Pit, Black Knight (L p). No new traps/doors.
- **15a, "Crusade" (L `p-crusade-fluttershine-a`)**: you start with ten Black Knights in prisons; the nearest prison is heavily guarded, the others have patrols: dig beside them and break in after a patrol has passed; free them quickly (they sicken and anger); Lair, Hatchery, Training, Torture Chamber; claim the outer prisons and Combat Pit (6+ Black Knights and some Guards); dig north for a Mana Vault, south for Elite Troll Knud's Workshop; then a long bridge east into the fortress with two Hero Portals; Horny to clear the gate defenders, Imps disable the portals; or train Black Knights, Knights, Guards, Giants to level 8 and Skeletons to level 4.
- **15b, "Storm" (L `q-storm-fluttershine-b`, H level 15)**: big dungeon with Combat Pit, Prison, Torture, Casino, Graveyard; west entrance with a gem seam, sentry cannons to disable first; several portals; Fairies become Skeletons or give map information; either a direct assault with 20-30 level 6-8 Knights, Monks, Fairies plus Horny, or a bridge round the side to the back prisons, release the Skeletons, breach the SW corner to pick off guard rooms, then front and back at once with Horny. H: Fairies in packs hurt; heal; Horny if the heroes are too strong; Ice traps litter the Volstag rooms and kill creatures; race home only if the Heart is attacked while you are far away.

## Level 16 - "Creep", Stonekeep (`level16`)
- Win: kill the unsuspecting Lord Pureheart. Sub: build a dungeon inside his walls with Secret Doors (H, L `r-creep-stonekeep`).
- Introduces: Create Gold, Lightning Trap, Secret Door (L r-creep-stonekeep). Secrets 4.
- Threats: a hero patrol that wanders the level, Alarm traps, Hero Gates, a Lightning Trap on the Mana Vault, several heroes and Lightning Traps round Pureheart (H, L).
- Plan: keep Imps in a locked room and release one at a time; Sight of Evil to watch patrols; take the two Workshops, Secret Doors just beyond them, locked; claim the prisons (a Goblin, a Dark Mistress, a Black Knight, a Warlock); torture the Wizard for Create Gold; sell captured central rooms for cash and keep your rooms in a rim round the centre; disable Alarm traps before the patrols come; disable the four Hero Portals; H: Create Gold becomes the main income, summon Horny in the patrol's path; break the middle door, Call to Arms, Horny and the army on Pureheart (H, L).

## Level 17 - "Angelic", Moonshrine (`level17`)
- Win: use the Dark Angels to crush the rival keepers Harkan and Carrion (L: Harkon and Garrion). Sub: kill 20 heroes at the Dark Angels' Temple to win their favour, capture the Temple (H, L `s-angelic-moonshrine`).
- Introduces: Dark Angels (ten level 10 arrive after the 20 kills) (L, H).
- Threats: fortified hero areas, Magic Doors, Sentry traps; heroes rush when the Steel Door falls (H).
- Plan: big dungeon with Combat Pit; train Black Knights, Mistresses, Trolls, Salamanders to level 8; send the army (or Horny) to the guard posts and kill 20 heroes; kills in the Prison or Torture Chamber do not count (L); Lairs and a Hatchery ready for the Angels; claim then sell the bridges round the Temple so enemy Imps cannot retake it (L); possess one Angel and group-possess four to go through each keeper's dungeon to the Heart, Harkan first (H, L).
- CONTRADICTION: H builds a Prison and Torture Chamber on this level; L says build no Prison.

## Level 18 - "Brotherhood", Cherry Blossom (`level18`)
- Win: defeat Keeper Nemesis and his sons Faust and Fabian (L: three keepers). Introduces: Temple, Firestorm research (H, L `t-brotherhood-cherry-blossom`).
- Threats: Hero Gates (do not dig past the lava or one opens); heroes near the second Portal; keepers do not attack until their dungeon is breached (L); Nemesis' dungeon is heavily booby-trapped (H).
- Plan: Lairs, large Hatchery, Combat Pit, Graveyard, Temple, level 8 minions; three gem seams; 5-6 Vampires at level 8; no conversions, focus on Vampires and Skeletons (L); take the east then west dungeon: Call to Arms at the Heart and Tremor (L); sell most of what you capture and build a big Prison and Graveyard (H); Nemesis last: bridge to his wall, kill cannons and Barricades, post a Skeleton or Dark Elf on the squares, summon Horny (mana from claimed Vaults and the Temple) (L, H). Fabian is green, Faust yellow, Faust has the larger army (H).

## Level 19 - "Interception", Butterscotch (`level19`)
- Win: capture and convert the three Princes (Felix, Balder, Tristram); none may die before conversion or escape through a Hero Gate (H, L `r-interception-butterscotch`).
- Introduces: nothing new (L). Pre-dug dungeon. A neutral level-10 Black Knight sits in a Combat Pit west (H, L).
- Threats: Fairies, Giants, Archers at Portal 2; four Hero Gates; each Prince patrols; capturing one alerts the others and they flee (L).
- Plan: build rooms, train; get Reveal Map; leave bridges unfinished near enemy ground until needed (H); seal the gates by claiming land then a locked Secret Door (H) or sell a bridge to strand a Prince (L); about 20 level-8 monsters plus Vampires or Skeletons for the ambush (L); keep Horny away from the Princes (H); Imps stand ready to drag each fallen Prince to the Prison at once, then torture and heal him until he converts (H, L).

## Level 20 - "Regicide", Heartland (`level20`)
- Win: kill King Reginald and open the gateway; two Stone Knights/Lords guard it and only Horny can kill them (H, L `s-regicide-heartland`).
- Introduces: nothing new. Final Gem.
- Threats: three attack waves from routes in the north (H); four Hero Portals and a Hero Gate (L); a U-shaped wall with cannons and traps, two Hero Portals, then the castle with level 10 guards and Lightning Traps; after Reginald falls, about ten level-10 heroes, then continuous attacks until the Stone Lords die (L, H).
- Plan: Lair, Hatchery, Workshop, Prison, Torture, Library, Training; Thunderbolt and Heal early; neutral Salamanders SW (L); after the waves, build Secret Doors in the attack tunnels and lock them (H); disable the four portals by destroying their Treasury and Guard Room (L); Graveyard, Temple, Combat Pit; Create Gold; Black Knights by sacrificing a Mistress and a captured Guard in the Temple (L); Salamanders and Stone Bridges to kill the cannons on the lava, Tremor to break through (L, H); Horny first on the guards, then Call to Arms; summon again for Reginald; keep mana for the Stone Lords (H, L).

## Secret levels (key found in Levels 5, 7, 10, 13; Golf, Duck Shoot, Maze, Bowling) (H, L `10-secret-levels`)
Not campaign levels the bot plays; listed for completeness.
