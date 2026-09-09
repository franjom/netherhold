# DK2 v1.70 creature table (Creatures.kwd)

DK2's own data — CONFIRMED by definition. Layout and offsets:
`docs/research/kwd-creatures.md`. Reproduce the summary table with
`dk2tools kwd creatures` and any one block below with
`dk2tools kwd creature <name>`; both read the reference install directly, so a
disagreement between this page and the tool means the page is stale.

`speed`/`run`/`meleeRange`/`recharge` are fp32 (raw ÷ 4096) and the `raw` line
carries the unconverted integers. `angerSlap`/`angerInHand` are **signed**
(ADR-0050) — slapping angers, being picked up calms, and the Mistress inverts
both. `hungerRate`/`awake` = 32767 is the "never" sentinel, per need rather
than per creature (ADR-0048, `creature-needs.md`).

## Summary — all 46 creatures

```
 id name                hp  dmg  pay  fear threat  speed exp/lvl class    flags
  1 Imp                500   30    0     5      2   1.84    3000 Scout    worker,evil,water
  2 Prince Tristran   6000  400  750   800    320    0.7    3000 Fighter  water,body
  3 Bile Demon        6000  525  600   300    120    0.4    3000 Fighter  evil,water,portal,body
  4 Mistress          2000  480  600   267    107    0.7    3000 Fighter  evil,water,portal,body
  5 Warlock           1000  130  600    50     20    0.6    3000 Thinker  evil,water,portal,body
  6 Dark Elf          1000  140  600    50     20    0.7    3000 Fighter  evil,water,portal,body
  7 Goblin            1500  170  500    50     37    0.7    3000 Fighter  evil,water,portal,body
  8 Vampire           2500  666  750   267    107    0.7    3000 Thinker  evil,fearless,resurrect,needbody
  9 Skeleton          1500  280    0 30000     60    0.7    3000 Fighter  evil,water,fearless
 10 Troll             1000  130  500    50     20    0.6    3000 Artisan  evil,water,portal,body
 11 Salamander        1500  200  500   113     45    0.6    3000 Fighter  evil,water,lava,portal,body
 12 Firefly           1000  130  300    19      8   1.12    3000 Scout    evil,fly,water,lava,portal,body
 13 Knight            4000  400  750   533    213    0.5    3000 Fighter  water,body
 14 Dwarf             1000  130    0    50     20    0.9    3000 Scout    worker,water,body
 15 Giant             4000  450  500   400    160    0.5    3000 Fighter  water,lava,body
 16 Wizard            1500  170  750    75     30    0.6    3000 Thinker  water,body
 17 Elven Archer      1000  140  600    50     20    0.7    3000 Fighter  water,body
 18 Thief             1500  230  750   113     45    0.6    3000 Scout    water,body,needbody
 19 Monk              2500  150  300    75     30    0.6    3000 Thinker  water,body
 20 Fairy             1500  220  600   113     45    0.8    3000 Fighter  fly,water,lava,body
 21 King Reginald     6000  930 1000  1400    560    0.6    3000 Fighter  water,fearless,body
 22 Black Knight      4000  400  750   533    213    0.5    3000 Fighter  evil,water,portal,body
 23 Dark Angel        3000  840  750   700    280    0.7    3000 Fighter  evil,water,portal,body
 24 Rogue             1650  400  650   190     80    0.7    3000 Scout    evil,water,portal,body
 25 Guard             3000  400  600   300    120    0.5    3000 Fighter  water,body
 26 Prince Felix      6000  400  750   800    320    0.7    3000 Fighter  water,body
 27 Horny             6000  700    0  1400    560    0.8    3000 Fighter  evil,water,lava,fearless
 28 Stone Knight      4000  400    0   400    160    0.4    3000 Fighter  water,fearless
 29 Lord Of The Land  6000  400  750   800    320    0.7    3000 Fighter  water,body
 30 Royal Guard       5000  500  750   400    160    0.5    3000 Fighter  water,body
 31 Prince Balder     6000  400  750   800    320    0.7    3000 Fighter  water,body
 32 Unique Firefly    2000  130  450    19     15   1.12    2800 Scout    evil,fly,water,lava,unique,portal,body
 33 Unique Goblin     2000  220  750    93     59    0.7    2800 Fighter  evil,water,unique,portal,body
 34 Unique Warlock    1000  150  900    50     20    0.6    2800 Thinker  evil,water,unique,portal,body
 35 Unique Troll      1500  160  750    50     20    0.6    2800 Artisan  evil,water,unique,portal,body
 36 Unique Dark Elf   1000  210  900    50     20    0.7    2800 Fighter  evil,water,unique,portal,body
 37 Unique Skeleton   1500  340    0 30000     60    0.7    2800 Fighter  evil,water,fearless,unique,portal
 38 Unique Mistress   2250  700  900   267    107      1    2800 Fighter  evil,water,unique,portal,body
 39 Unique Salamander  2750  200  750   113     45    0.6    2800 Fighter  evil,water,lava,unique,portal,body
 40 Unique Rogue      2000  600  975   190     80    0.7    2800 Scout    evil,water,unique,portal,body
 41 Unique Bile Demon  6000  525  900   400    200    0.4    2800 Fighter  evil,water,unique,portal,body
 42 Unique Vampire    5000  667 1075   267    107    0.7    2800 Thinker  evil,unique,portal,resurrect,needbody
 43 Unique Black Knight  4000  600 1075   533    213    0.6    2800 Fighter  evil,water,unique,portal,body
 44 Unique Dark Angel  8000 1000 1075   700    280    0.7    2800 Fighter  evil,water,unique,portal,body
 45 Maiden Of The Nest  4500  450  750 30000    200    0.8    3000 Thinker  evil,water,fearless,portal,body
 46 Jack               240    0  600   267    107    1.9    3000 Artisan  evil,water,fearless
-- 46 creatures
```

`flags` names are this project's own decoding of the `0x0EFC` dword
(`DK2.Data.Model.CreatureFlags`); bits 11 and 29 are set on nearly every
creature and remain `Unk11` / `Unk29`.

## Per-creature detail (job tables, attractions, spells, flags)

Ten creatures, chosen because a rule or a test depends on each. The other 36
print the same shape from `dk2tools kwd creature <name>`.

```
Imp (id 1)  class=Scout style=NonFighter
  hp=500 dmg=30 pay=0 fear=5 threat=2
  speed=1.84 run=1.84 see=4 hear=4
  hungerRate=32767 hungerFill=0 awake=32767 sleep=0
  lairObject=0
  hpFromChicken=0 ownLandHeal/s=67 decompose=0
  meleeRange=0.35 recharge=1.2 attack=MeleeBlade
  maxGoldHeld=1400 initialGoldHeld=500
  exp/lvl=3000 exp/s=1200 train=30 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=1 angerNoFood=1 angerNoPay=1 angerNoWork=1
  angerSlap=1 angerInHand=0
  happy   : Explore(100%)
  unhappy : 
  angry   : 
  hates   : 
  altJobs : 
  attracts: 
  spells  : #37@lvl4, #36@lvl8
  resists : 
  flags   : 0xB000089F IsWorker, CanBePickedUp, CanBeSlapped, AlwaysFlee, CanWalkOnWater, IsEvil, Unk11, CameraRollsWhenTurning, Unk29, IsMale
  raw     : speed=7536 run=7536 shuffle=4096 height=2048 see=16384 hear=16384 meleeRange=1433 recharge=4915 guard=0 idle=0

Troll (id 10)  class=Artisan style=Blocker
  hp=1000 dmg=130 pay=500 fear=50 threat=20
  speed=0.6 run=1.08 see=4 hear=4
  hungerRate=200 hungerFill=3 awake=330 sleep=10
  lairObject=25
  hpFromChicken=83 ownLandHeal/s=0 decompose=200
  meleeRange=0.55 recharge=1.3 attack=MeleeBlunt
  maxGoldHeld=5000 initialGoldHeld=500
  exp/lvl=3000 exp/s=150 train=30 research=0 manufacture=60
  unhappyThreshold=50 angerNoLair=130 angerNoFood=110 angerNoPay=60 angerNoWork=40
  angerSlap=2000 angerInHand=-200
  happy   : Manufacture(100%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research
  altJobs : 
  attracts: room10(size>=0)
  spells  : 
  resists : 
  flags   : 0xA6098CD6 CanBePickedUp, CanBeSlapped, CanWalkOnWater, CanDisarmTraps, IsEvil, AvailableViaPortal, Unk11, GenerateDeadBody, CanBeHypnotized, CanBeElectrocuted, FreesFriendsOnJailBreak, RevealsAdjacentTraps, Unk29, IsMale
  raw     : speed=2457 run=4423 shuffle=1638 height=2039 see=16384 hear=16384 meleeRange=2252 recharge=5324 guard=0 idle=0

Warlock (id 5)  class=Thinker style=Support
  hp=1000 dmg=130 pay=600 fear=50 threat=20
  speed=0.6 run=1.08 see=4 hear=4
  hungerRate=200 hungerFill=2 awake=300 sleep=9
  lairObject=27
  hpFromChicken=125 ownLandHeal/s=0 decompose=200
  meleeRange=0.5 recharge=2.3 attack=MeleeBlunt
  maxGoldHeld=6000 initialGoldHeld=500
  exp/lvl=3000 exp/s=150 train=30 research=5 manufacture=0
  unhappyThreshold=50 angerNoLair=100 angerNoFood=100 angerNoPay=60 angerNoWork=70
  angerSlap=7000 angerInHand=-500
  happy   : Research(80%), Train(10%), Praying(10%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Manufacture
  altJobs : 
  attracts: room6(size>=0)
  spells  : #1@lvl1, #14@lvl2, #2@lvl8
  resists : 
  flags   : 0xA2098C96 CanBePickedUp, CanBeSlapped, CanWalkOnWater, IsEvil, AvailableViaPortal, Unk11, GenerateDeadBody, CanBeHypnotized, CanBeElectrocuted, FreesFriendsOnJailBreak, Unk29, IsMale
  raw     : speed=2457 run=4423 shuffle=1638 height=3686 see=16384 hear=16384 meleeRange=2048 recharge=9420 guard=0 idle=0

Goblin (id 7)  class=Fighter style=Flanker
  hp=1500 dmg=170 pay=500 fear=50 threat=37
  speed=0.7 run=1.26 see=4 hear=4
  hungerRate=180 hungerFill=1 awake=240 sleep=9
  lairObject=22
  hpFromChicken=375 ownLandHeal/s=0 decompose=200
  meleeRange=0.45 recharge=1.3 attack=MeleeBlade
  maxGoldHeld=5000 initialGoldHeld=500
  exp/lvl=3000 exp/s=200 train=40 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=100 angerNoFood=100 angerNoPay=60 angerNoWork=50
  angerSlap=3000 angerInHand=-250
  happy   : Train(100%)
  unhappy : Drink(100%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room2(size>=0)
  spells  : 
  resists : 
  flags   : 0xA2098C96 CanBePickedUp, CanBeSlapped, CanWalkOnWater, IsEvil, AvailableViaPortal, Unk11, GenerateDeadBody, CanBeHypnotized, CanBeElectrocuted, FreesFriendsOnJailBreak, Unk29, IsMale
  raw     : speed=2867 run=5160 shuffle=1638 height=2867 see=16384 hear=16384 meleeRange=1843 recharge=5324 guard=0 idle=0

Bile Demon (id 3)  class=Fighter style=Blocker
  hp=6000 dmg=525 pay=600 fear=300 threat=120
  speed=0.4 run=0.56 see=4 hear=4
  hungerRate=220 hungerFill=5 awake=270 sleep=9
  lairObject=18
  hpFromChicken=200 ownLandHeal/s=0 decompose=200
  meleeRange=0.8 recharge=3.5 attack=MeleeBody
  maxGoldHeld=6000 initialGoldHeld=500
  exp/lvl=3000 exp/s=100 train=20 research=0 manufacture=50
  unhappyThreshold=50 angerNoLair=150 angerNoFood=150 angerNoPay=60 angerNoWork=50
  angerSlap=3000 angerInHand=-300
  happy   : Train(40%), Manufacture(60%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research
  altJobs : 
  attracts: room10(size>=0), room4(size>=25)
  spells  : #5@lvl4, #6@lvl8
  resists : DarkElfGuidedBolt=100
  flags   : 0xA2098C96 CanBePickedUp, CanBeSlapped, CanWalkOnWater, IsEvil, AvailableViaPortal, Unk11, GenerateDeadBody, CanBeHypnotized, CanBeElectrocuted, FreesFriendsOnJailBreak, Unk29, IsMale
  raw     : speed=1638 run=2293 shuffle=819 height=2662 see=16384 hear=16384 meleeRange=3276 recharge=14336 guard=0 idle=0

Vampire (id 8)  class=Thinker style=Blitzer
  hp=2500 dmg=666 pay=750 fear=267 threat=107
  speed=0.7 run=1.26 see=4 hear=4
  hungerRate=32767 hungerFill=0 awake=330 sleep=9
  lairObject=26
  hpFromChicken=0 ownLandHeal/s=0 decompose=0
  meleeRange=0.5 recharge=2.5 attack=MeleeBlade
  maxGoldHeld=7500 initialGoldHeld=500
  exp/lvl=3000 exp/s=150 train=30 research=5 manufacture=0
  unhappyThreshold=50 angerNoLair=100 angerNoFood=1 angerNoPay=60 angerNoWork=60
  angerSlap=7000 angerInHand=-250
  happy   : Research(80%), Praying(20%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Rebel(100%)
  hates   : Manufacture
  altJobs : 
  attracts: room14(size>=0)
  spells  : #8@lvl4, #10@lvl8, #20@lvl10
  resists : 
  flags   : 0xB2BD0886 CanBePickedUp, CanBeSlapped, IsEvil, Unk11, CanBeHypnotized, IsFearless, CanBeElectrocuted, NeedBodyForFightIdle, NotTrainWhenIdle, CanBeResurrected, FreesFriendsOnJailBreak, CameraRollsWhenTurning, Unk29, IsMale
  raw     : speed=2867 run=5160 shuffle=1638 height=2637 see=16384 hear=16384 meleeRange=2048 recharge=10240 guard=0 idle=0

Mistress (id 4)  class=Fighter style=Blitzer
  hp=2000 dmg=480 pay=600 fear=267 threat=107
  speed=0.7 run=1.26 see=4 hear=4
  hungerRate=200 hungerFill=2 awake=300 sleep=9
  lairObject=23
  hpFromChicken=250 ownLandHeal/s=0 decompose=200
  meleeRange=0.45 recharge=1.8 attack=MeleeBlade
  maxGoldHeld=6000 initialGoldHeld=500
  exp/lvl=3000 exp/s=100 train=20 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=120 angerNoFood=110 angerNoPay=60 angerNoWork=50
  angerSlap=-5000 angerInHand=500
  happy   : Train(50%), Torture(50%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room12(size>=0)
  spells  : #4@lvl4, #3@lvl8, #11@lvl10
  resists : 
  flags   : 0x22098C96 CanBePickedUp, CanBeSlapped, CanWalkOnWater, IsEvil, AvailableViaPortal, Unk11, GenerateDeadBody, CanBeHypnotized, CanBeElectrocuted, FreesFriendsOnJailBreak, Unk29
  raw     : speed=2867 run=5160 shuffle=2048 height=3481 see=16384 hear=16384 meleeRange=1843 recharge=7372 guard=0 idle=0

Salamander (id 11)  class=Fighter style=Flanker
  hp=1500 dmg=200 pay=500 fear=113 threat=45
  speed=0.6 run=1.08 see=4 hear=4
  hungerRate=200 hungerFill=1 awake=270 sleep=10
  lairObject=24
  hpFromChicken=375 ownLandHeal/s=0 decompose=200
  meleeRange=0.6 recharge=1.3 attack=MeleeBody
  maxGoldHeld=5000 initialGoldHeld=500
  exp/lvl=3000 exp/s=200 train=40 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=90 angerNoFood=110 angerNoPay=60 angerNoWork=50
  angerSlap=3000 angerInHand=-300
  happy   : Train(100%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room7(size>=0)
  spells  : #39@lvl4, #26@lvl8
  resists : MeleeBlade=75, Invulnerable=50, WarlockHealCreature=50
  flags   : 0xA2098CB6 CanBePickedUp, CanBeSlapped, CanWalkOnWater, CanWalkOnLava, IsEvil, AvailableViaPortal, Unk11, GenerateDeadBody, CanBeHypnotized, CanBeElectrocuted, FreesFriendsOnJailBreak, Unk29, IsMale
  raw     : speed=2457 run=4423 shuffle=1638 height=2539 see=16384 hear=16384 meleeRange=2457 recharge=5324 guard=0 idle=0

Firefly (id 12)  class=Scout style=Flanker
  hp=1000 dmg=130 pay=300 fear=19 threat=8
  speed=1.12 run=1.12 see=4 hear=4
  hungerRate=200 hungerFill=2 awake=270 sleep=9
  lairObject=21
  hpFromChicken=0 ownLandHeal/s=0 decompose=200
  meleeRange=0.35 recharge=1 attack=MeleeBody
  maxGoldHeld=3000 initialGoldHeld=500
  exp/lvl=3000 exp/s=200 train=40 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=120 angerNoFood=1 angerNoPay=60 angerNoWork=50
  angerSlap=3000 angerInHand=-150
  happy   : Explore(100%)
  unhappy : Sulk(100%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room2(size>=0)
  spells  : #21@lvl8
  resists : 
  flags   : 0xB201ACB6 CanBePickedUp, CanBeSlapped, CanWalkOnWater, CanWalkOnLava, IsEvil, AvailableViaPortal, Unk11, CanFly, GenerateDeadBody, CanBeHypnotized, FreesFriendsOnJailBreak, CameraRollsWhenTurning, Unk29, IsMale
  raw     : speed=4587 run=4587 shuffle=3276 height=2457 see=16384 hear=16384 meleeRange=1433 recharge=4096 guard=0 idle=0

Skeleton (id 9)  class=Fighter style=Flanker
  hp=1500 dmg=280 pay=0 fear=30000 threat=60
  speed=0.7 run=0.9 see=4 hear=4
  hungerRate=32767 hungerFill=0 awake=32767 sleep=0
  lairObject=0
  hpFromChicken=0 ownLandHeal/s=0 decompose=0
  meleeRange=0.5 recharge=1.4 attack=MeleeBlade
  maxGoldHeld=0 initialGoldHeld=500
  exp/lvl=3000 exp/s=200 train=40 research=1 manufacture=0
  unhappyThreshold=50 angerNoLair=0 angerNoFood=0 angerNoPay=0 angerNoWork=0
  angerSlap=0 angerInHand=0
  happy   : Train(100%)
  unhappy : Guard(100%)
  angry   : Rebel(100%)
  hates   : Manufacture, Research
  altJobs : 
  attracts: room11(size>=0)
  spells  : 
  resists : 
  flags   : 0xA20D0896 CanBePickedUp, CanBeSlapped, CanWalkOnWater, IsEvil, Unk11, CanBeHypnotized, IsFearless, CanBeElectrocuted, FreesFriendsOnJailBreak, Unk29, IsMale
  raw     : speed=2867 run=3686 shuffle=2048 height=2867 see=16384 hear=16384 meleeRange=2048 recharge=5734 guard=0 idle=0

```

Room ids in `attracts` are ROOMS(120) ids — 2 Lair, 4 Hatchery, 6 Library,
7 Training Room, 10 Workshop, 11 Prison, 12 Torture Chamber, 14 Graveyard
(`DK2.Data.Model.RoomTypeId`; full list in `terrain-room-dump.md`). Note the
pairing with that record's `createdCreatureId`: the Skeleton is attracted to the
Prison and the Vampire to the Graveyard, the two rooms that create them. Spell
ids are CREATURE_SPELLS(160) indices and `resists` names are
`DK2.Data.Model.AttackType`.

Implementation: `DK2.Data.Kwd.KwdCreatures`, `DK2.Data.Model.CreatureDef`.
Tests: `DK2.Data.Tests.KwdCreaturesTests`,
`DK2.Behavior.Tests.CreatureNeedsOnRealDataTests`.
