# DK2 v1.70 creature table (Creatures.kwd)

Extracted from the reference install by `dk2tools kwd creatures` on 2026-08-29.
DK2's own data — CONFIRMED by definition. Layout: docs/research/kwd-creatures.md

```
 id name                hp  dmg  pay  fear threat  speed exp/lvl class    flags
  1 Imp                500   30    0     5      2   1.84    3000 Scout    worker,evil
  2 Prince Tristran   6000  400  750   800    320    0.7    3000 Fighter  
  3 Bile Demon        6000  525  600   300    120    0.4    3000 Fighter  evil
  4 Mistress          2000  480  600   267    107    0.7    3000 Fighter  evil
  5 Warlock           1000  130  600    50     20    0.6    3000 Thinker  evil
  6 Dark Elf          1000  140  600    50     20    0.7    3000 Fighter  evil
  7 Goblin            1500  170  500    50     37    0.7    3000 Fighter  evil
  8 Vampire           2500  666  750   267    107    0.7    3000 Thinker  evil,fearless
  9 Skeleton          1500  280    0 30000     60    0.7    3000 Fighter  evil,fearless
 10 Troll             1000  130  500    50     20    0.6    3000 Artisan  evil
 11 Salamander        1500  200  500   113     45    0.6    3000 Fighter  evil
 12 Firefly           1000  130  300    19      8   1.12    3000 Scout    evil,fly
 13 Knight            4000  400  750   533    213    0.5    3000 Fighter  
 14 Dwarf             1000  130    0    50     20    0.9    3000 Scout    worker
 15 Giant             4000  450  500   400    160    0.5    3000 Fighter  
 16 Wizard            1500  170  750    75     30    0.6    3000 Thinker  
 17 Elven Archer      1000  140  600    50     20    0.7    3000 Fighter  
 18 Thief             1500  230  750   113     45    0.6    3000 Scout    
 19 Monk              2500  150  300    75     30    0.6    3000 Thinker  
 20 Fairy             1500  220  600   113     45    0.8    3000 Fighter  fly
 21 King Reginald     6000  930 1000  1400    560    0.6    3000 Fighter  fearless
 22 Black Knight      4000  400  750   533    213    0.5    3000 Fighter  evil
 23 Dark Angel        3000  840  750   700    280    0.7    3000 Fighter  evil
 24 Rogue             1650  400  650   190     80    0.7    3000 Scout    evil
 25 Guard             3000  400  600   300    120    0.5    3000 Fighter  
 26 Prince Felix      6000  400  750   800    320    0.7    3000 Fighter  
 27 Horny             6000  700    0  1400    560    0.8    3000 Fighter  evil,fearless
 28 Stone Knight      4000  400    0   400    160    0.4    3000 Fighter  fearless
 29 Lord Of The Land  6000  400  750   800    320    0.7    3000 Fighter  
 30 Royal Guard       5000  500  750   400    160    0.5    3000 Fighter  
 31 Prince Balder     6000  400  750   800    320    0.7    3000 Fighter  
 32 Unique Firefly    2000  130  450    19     15   1.12    2800 Scout    evil,fly,unique
 33 Unique Goblin     2000  220  750    93     59    0.7    2800 Fighter  evil,unique
 34 Unique Warlock    1000  150  900    50     20    0.6    2800 Thinker  evil,unique
 35 Unique Troll      1500  160  750    50     20    0.6    2800 Artisan  evil,unique
 36 Unique Dark Elf   1000  210  900    50     20    0.7    2800 Fighter  evil,unique
 37 Unique Skeleton   1500  340    0 30000     60    0.7    2800 Fighter  evil,fearless,unique
 38 Unique Mistress   2250  700  900   267    107      1    2800 Fighter  evil,unique
 39 Unique Salamander  2750  200  750   113     45    0.6    2800 Fighter  evil,unique
 40 Unique Rogue      2000  600  975   190     80    0.7    2800 Scout    evil,unique
 41 Unique Bile Demon  6000  525  900   400    200    0.4    2800 Fighter  evil,unique
 42 Unique Vampire    5000  667 1075   267    107    0.7    2800 Thinker  evil,unique
 43 Unique Black Knight  4000  600 1075   533    213    0.6    2800 Fighter  evil,unique
 44 Unique Dark Angel  8000 1000 1075   700    280    0.7    2800 Fighter  evil,unique
 45 Maiden Of The Nest  4500  450  750 30000    200    0.8    3000 Thinker  evil,fearless
 46 Jack               240    0  600   267    107    1.9    3000 Artisan  evil,fearless
-- 46 creatures
```

## Per-creature detail (job tables, attractions, spells)

```
Imp (id 1)  class=Scout style=NonFighter
  hp=500 dmg=30 pay=0 fear=5 threat=2
  speed=1.84 run=1.84 see=4 hear=4
  hungerRate=32767 hungerFill=0 awake=32767 sleep=0
  meleeRange=0.35 recharge=1.2 attack=MeleeBlade
  exp/lvl=3000 exp/s=1200 train=30 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=1 angerNoFood=1 angerNoPay=1 angerNoWork=1
  happy   : Explore(100%)
  unhappy : 
  angry   : 
  hates   : 
  altJobs : 
  attracts: 
  spells  : #37@lvl4, #36@lvl8
  resists : 

Troll (id 10)  class=Artisan style=Blocker
  hp=1000 dmg=130 pay=500 fear=50 threat=20
  speed=0.6 run=1.08 see=4 hear=4
  hungerRate=200 hungerFill=3 awake=330 sleep=10
  meleeRange=0.55 recharge=1.3 attack=MeleeBlunt
  exp/lvl=3000 exp/s=150 train=30 research=0 manufacture=60
  unhappyThreshold=50 angerNoLair=130 angerNoFood=110 angerNoPay=60 angerNoWork=40
  happy   : Manufacture(100%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research
  altJobs : 
  attracts: room10(size>=0)
  spells  : 
  resists : 

Warlock (id 5)  class=Thinker style=Support
  hp=1000 dmg=130 pay=600 fear=50 threat=20
  speed=0.6 run=1.08 see=4 hear=4
  hungerRate=200 hungerFill=2 awake=300 sleep=9
  meleeRange=0.5 recharge=2.3 attack=MeleeBlunt
  exp/lvl=3000 exp/s=150 train=30 research=5 manufacture=0
  unhappyThreshold=50 angerNoLair=100 angerNoFood=100 angerNoPay=60 angerNoWork=70
  happy   : Research(80%), Train(10%), Praying(10%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Manufacture
  altJobs : 
  attracts: room6(size>=0)
  spells  : #1@lvl1, #14@lvl2, #2@lvl8
  resists : 

Goblin (id 7)  class=Fighter style=Flanker
  hp=1500 dmg=170 pay=500 fear=50 threat=37
  speed=0.7 run=1.26 see=4 hear=4
  hungerRate=180 hungerFill=1 awake=240 sleep=9
  meleeRange=0.45 recharge=1.3 attack=MeleeBlade
  exp/lvl=3000 exp/s=200 train=40 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=100 angerNoFood=100 angerNoPay=60 angerNoWork=50
  happy   : Train(100%)
  unhappy : Drink(100%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room2(size>=0)
  spells  : 
  resists : 

Bile Demon (id 3)  class=Fighter style=Blocker
  hp=6000 dmg=525 pay=600 fear=300 threat=120
  speed=0.4 run=0.56 see=4 hear=4
  hungerRate=220 hungerFill=5 awake=270 sleep=9
  meleeRange=0.8 recharge=3.5 attack=MeleeBody
  exp/lvl=3000 exp/s=100 train=20 research=0 manufacture=50
  unhappyThreshold=50 angerNoLair=150 angerNoFood=150 angerNoPay=60 angerNoWork=50
  happy   : Train(40%), Manufacture(60%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research
  altJobs : 
  attracts: room10(size>=0), room4(size>=25)
  spells  : #5@lvl4, #6@lvl8
  resists : DarkElfGuidedBolt=100

Vampire (id 8)  class=Thinker style=Blitzer
  hp=2500 dmg=666 pay=750 fear=267 threat=107
  speed=0.7 run=1.26 see=4 hear=4
  hungerRate=32767 hungerFill=0 awake=330 sleep=9
  meleeRange=0.5 recharge=2.5 attack=MeleeBlade
  exp/lvl=3000 exp/s=150 train=30 research=5 manufacture=0
  unhappyThreshold=50 angerNoLair=100 angerNoFood=1 angerNoPay=60 angerNoWork=60
  happy   : Research(80%), Praying(20%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Rebel(100%)
  hates   : Manufacture
  altJobs : 
  attracts: room14(size>=0)
  spells  : #8@lvl4, #10@lvl8, #20@lvl10
  resists : 

Mistress (id 4)  class=Fighter style=Blitzer
  hp=2000 dmg=480 pay=600 fear=267 threat=107
  speed=0.7 run=1.26 see=4 hear=4
  hungerRate=200 hungerFill=2 awake=300 sleep=9
  meleeRange=0.45 recharge=1.8 attack=MeleeBlade
  exp/lvl=3000 exp/s=100 train=20 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=120 angerNoFood=110 angerNoPay=60 angerNoWork=50
  happy   : Train(50%), Torture(50%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room12(size>=0)
  spells  : #4@lvl4, #3@lvl8, #11@lvl10
  resists : 

Salamander (id 11)  class=Fighter style=Flanker
  hp=1500 dmg=200 pay=500 fear=113 threat=45
  speed=0.6 run=1.08 see=4 hear=4
  hungerRate=200 hungerFill=1 awake=270 sleep=10
  meleeRange=0.6 recharge=1.3 attack=MeleeBody
  exp/lvl=3000 exp/s=200 train=40 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=90 angerNoFood=110 angerNoPay=60 angerNoWork=50
  happy   : Train(100%)
  unhappy : Sulk(10%), Drink(90%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room7(size>=0)
  spells  : #39@lvl4, #26@lvl8
  resists : MeleeBlade=75, Invulnerable=50, WarlockHealCreature=50

Firefly (id 12)  class=Scout style=Flanker
  hp=1000 dmg=130 pay=300 fear=19 threat=8
  speed=1.12 run=1.12 see=4 hear=4
  hungerRate=200 hungerFill=2 awake=270 sleep=9
  meleeRange=0.35 recharge=1 attack=MeleeBody
  exp/lvl=3000 exp/s=200 train=40 research=0 manufacture=0
  unhappyThreshold=50 angerNoLair=120 angerNoFood=1 angerNoPay=60 angerNoWork=50
  happy   : Explore(100%)
  unhappy : Sulk(100%)
  angry   : Leave(50%), Rebel(50%)
  hates   : Research, Manufacture
  altJobs : 
  attracts: room2(size>=0)
  spells  : #21@lvl8
  resists : 

Skeleton (id 9)  class=Fighter style=Flanker
  hp=1500 dmg=280 pay=0 fear=30000 threat=60
  speed=0.7 run=0.9 see=4 hear=4
  hungerRate=32767 hungerFill=0 awake=32767 sleep=0
  meleeRange=0.5 recharge=1.4 attack=MeleeBlade
  exp/lvl=3000 exp/s=200 train=40 research=1 manufacture=0
  unhappyThreshold=50 angerNoLair=0 angerNoFood=0 angerNoPay=0 angerNoWork=0
  happy   : Train(100%)
  unhappy : Guard(100%)
  angry   : Rebel(100%)
  hates   : Manufacture, Research
  altJobs : 
  attracts: room11(size>=0)
  spells  : 
  resists : 

```
