# Changelog

## 7.0.3 2026-5-3
I have no idea how long this has been a bug, but I fixed the Lethal Bot Prefab hash sometimes not being consistent between clients.

## 7.0.2 2026-5-1
Just a minor patch to publicize some ship navmesh fields to help modders add support for their custom ship sizes!

## 7.0.1 2026-4-30
Hello again, I just have some minor bug fixes for some issues that were found after the 7.0.0 Update.

Change Log:
- Fixed bots in the Panik state not grabbing nearby weapons if said weapon wasn't on the ship
- Fixed Safe Path considering underwater nodes as a good fallback spot
- Fixed a potential desync between the bot's player controller and its NavMeshAgent
- Changed bots to use PlayerControllerB.PlayFootstepSound which fixed the scrap jiggle audio to not playing
- Made a minor optimization to LethalBotManager.HostPlayerScript
- Fixed some issues with the Quicksand NavMeshAttribute causing some enemies to fail to path over quicksand.
- Fixed a bug with late joining players where a bot could be selected to die in SpawnExplosion causing the local player to not take any kind of damage from said explosion.
- Forced all EnemyAI objects to include my quicksand NavMeshAttribute in their areaMask
- Fixed some logic errors in KillPlayerClientRpc_Postfix
- Changed how player damage is handled for bots
  - Lethal Bots no longer blocks the orignal damage player function from running for bots.
  - A transpiler was used to make the orignal DamagePlayer function return early if its called on a bot
  - This allows mod added Postfixes for DamagePlayer to properly run for bots

## 7.0.0 2026-4-28
It time for another update. This more focused around reworking some of the bot's AI itself. Now then lets get right onto it!

## Selling Item Blacklist
Ever wanted to stop the bot from selling certain items. Well guess what, your wish has been granted. The sell item blacklist is here! This was a HIGHLY requested feature so I'm glad to finally get this out of the way!
- You can now blacklist specific grabbable objects from being sold by using the `/blacklistitem` chat command. You can remove an item from the blacklist using the `/unblacklistitem` chat command. These commands only apply to the object you are currently holding.
- The sell blacklist gives feedback on if the item was added or removed from the blacklist
- The save manager now handles the saving of blacklisted items. Items you blacklist will persist for that save file.
- The host will get a warning if my mod fails to reblacklist an item when the save file is loaded

## Restock Rework and More Company Cosmetics
Yep, the restock system has been reworked to give you more control over when items are purchased. I also gave bots support for More Company Cosmetics, "including modded," so you can deck out your bots! This closes #61 
- You can now specify multiple items that are required before the bot will buy an item. You can also specify how much of each required item is needed in the crew's possession.
- You can now override the EcoLimit for each item to stock as well.
- Bots can now be assigned more company cosmetics that they will automatically equip. For Example: builtin.tophat,builtin.gunholster,builtin.watch would give the bot the built in tophat, gun holster, and wrist watch. cosmetics. You must separate each cosmetic with a comma.
- Updated default ConfigIdentities.json file with the new identity parameter `moreCompanyCosmetics`

## Better Water and Quicksand detection
I finally figured out how to add custom NavMesh attributes at runtime. This means I can control how the bots path better without affecting the base game enemies. I have updated my code to apply a custom `Quicksand` attribute which makes my bots dislike pathing over them. This works even if the bot isn't using Safe Path.
- Add Quicksand NavMesh attribute
- Apparently, QuicksandTriggers have more than 1 collider. Which caused some cases where the bots would fail to detect water and quicksand at times.
- Bots now consider paths through quicksand to be 50 times as expensive
- My mod now adds NavMesh modifier volumes to all QuicksandTrigger objects to modify the NavMesh around them.
- The NavMesh will be updated, not rebaked, when Quicksand is detected to update the NavMesh Attributes.

## Bug fixes and Miscellaneous changes
To end this update, there are some minor changes and more bugs fixes.
- Changed the loadout config file to be inline with the other files. YOU MUST MOVE YOUR LOADOUTS TO THE NEW FILE!
- Improved the LethalBotThreat constructor
- Remove RagdollInternBody.cs, it was unusued by Lethal Bots
- Updated LethalBotInfection to override the == and != operators
- Also fixed a logic error with bots and selling dead bodies
- Fixed bots losing their voice if they die and the human player didn't die that round.
- Fixed DunGenTileTracker not being consitent with the base game at times
- Removed some redundant comments

## 6.1.3 2026-4-24
Sigh, I only discovered this bug because I died because of it.........Bots were unable to cure players if the mod Usual Scrap was not installed. 
This was caused by a logic error that caused my mod to not check if it was installed or not in the HealPlayerState.

Bots are now able to cure players again without spaming the console with TypeLoadExceptions

## 6.1.2 2026-4-24
Hello, 6.1.1 had an issue where certian mods could cause the bots player controller to be disabled.
I have now restored the part of the code that enables the player controllers when a bot spawns which should fix the issue.

## 6.1.1 2026-4-24
Hello, I'm just making some bug fixes for issues that were reported.

Change Log:
- Updated suit code to also check if the suit Interactable Trigger is spawned just in case it doesn't properly set the appropriate flags
- Fixed a rare bug where a player who joins late could tell the terminal bot to follow them which would break the terminal for other players. Kicking the bot was on the only way to fix this before this patch.
- Fixed bot names being changed to Unnamed if a player joins the lobby while there are active bots
- Added another safeguard for the bots held item not being set for player who join in late.
- Fixed player names not being reset when a bot disconnects in a LAN lobby
- Fixed bots controlled by disconnecting players not changing their ownership back to the host
- Fixed bots in the BrainDeadState not changing to another AI state if the ship was in orbit and they were revived
- Adjusted LethalBotAI.LeaveTerminal to allow the server to force the bot off of the terminal
- Singleton_OnClientDisconnectCallback now properly updates the real player count after a human player disconnects
- Made some minor optimizations

## 6.1.0 2026-4-23
There was reported a bug with the bots causing infinite loading loops when trying to change moons if you had DawnLib installed. This has been fixed along with a few other bugs with some other mods as well. I also added #36 since it was no longer blocked by a pending update to Usual Scrap.

## New Features
As I stated earlier, I gave the bots the knowledge on how to use some of Usual Scraps tools. There are more items in the mod, but I will work on that later.
- Bots now know how to use the Usual Scrap Defibrillator to revive dead players. This closes #36.
- Bots now know how to use the Usual Scrap Bandages to heal themself
- Bots now know how to use the Usual Scrap Medkit to heal themself and other players
- Bots can now grab required tools to revive dead players, just like HealPlayerState, this is only done while on the ship
- Bots will now charge their chosen medical and revival tools as needed

## Bug Fixes
Now for the part some of you have been waiting for, the bug fixes!
- Fixed DawnLib breaking when bots are in orbit and you choose to route to a new moon. Fixes #87 and Fixes #84 as well.
- Fixed LethalMin Support
- Also updated LethalMin dll and mod version number
- Fixed ReviveCompany support
- Fixed some issues where the mission controller bot would fail to teleport back dead bodies if a supported revive mod was installed
- Fixed a field having the wrong protection level in HealPlayerState
- Fixed bots not updating their LethalBotAI's postion on other clients
- Fixed the ship's NavMesh not being generated for late joining clients while the ship is in orbit
- Fixed Ghost Girl being too aggressive if she chose to haunt a bot
- Fixed a logic error in LethalBotAI.CanEnemyBeKilled that caused bots not to rescue players being dragged by the kidnapper fox
- Updated LethalPhones dll file

## Miscellaneous Changes
There are some other changes that are not new features or bugs that were fixed.
- Bots now consider paths through water to be three times as expensive. Was twice as expensive.
- Updated NpcController.LethalBotAIController to use the internal field reference variable
- Changed some of the OrderToLookAtPosition calls' look at times
- Decreased the max chase range for bots in the FightEnemyState
- Removed old and unused Revive Company patch. The bots get revived just fine without it.
- Publicied ReviveMethod and HealMethod, I have no idea why they were private before.
- Added a helper function LethalBotAI.GetItemAtSlot to help me reduce duplicated code
- Added a helper function LethalBotAI.IsItemKey
- Removed the dead body hacks left over from Lethal Internship. The base game now handles it. 
- Added a helper function LethalBotManager.GetShipNavPrefab, this was made to help modders replace the default ship navmesh with their own custom one

## 6.0.0 2026-4-19
Hello again, this update adds a long requested feature of letting the bots join you while the ship is in orbit. This fixes SO MANY BUGS. You see, when I had the bots "leave" the lobby, some mods thought the bots were still active in the game and could cause the game to enter an infinite loop of waiting for the bot to send the ready RPC. Now that the bots don't automatically "leave," I fixed up my bots to send that ready RPC. This should fix that infinite loading loop now that the game thinks all players have loaded. Now then, onto the patch notes! 

## Bots in Orbit
Well, here it is. The bots can now walk around and do stuff with players while in orbit. Its basic for now, but more can be added later.
- My mod will now generate a NavMesh on the ship for the bots to use while in orbit. The NavMesh will automatically clean itself up when you land on a moon. NOTE: This only works for the default ship size, it will be up to other modders to add support for Lethal Bots! Nothing will break, but bots will be unable to path to certain parts of the ship.
- Added /addbots chat command for host spawning while in orbit
- Added a new config option AllowBotsInOrbit, to enable and disable the new bots stay on the ship after round ends code
- My mod will now generate a txt file with all items and unlockable object's internal names. You can find in in the Config folder named GameItems.txt
- If bots are allowed in orbit, they get fired with the human players as well.............
- The Mission Controller bot will now automatically route to The Company Building if there are 0 days left in the profit quota. This can be disabled using the AutoRouteToCompany config option
- Rewrote most of ChillAtShip state to be cleaner and support being in orbit
- Changed other states to understand when the bot is on the ship in orbit

## Lethal Bot Interaction  
I'm working on a new interaction system for the bots, this new system has the bot actually hold their +use key when interacting with stuff. For now this is only used for the main entrance and fire exits, but will be expanded to other systems as well.
- Introduced LethalBotInteraction to help mimic interactions with objects in the game that have to be held. Example: Using the main entrance.
- Updated AI states to use the new interaction system. For now only used for the main entrance!

## Multiplayer Improvements
Yep, the multiplayer grind never ends it seems, I fixed more bugs the bots had and the allowing bots in orbit makes the bots much more stable than they were before.
- Improved bot synchronization code when a new player joins the game
- Fixed the game allowing players to join the lobby when the lobby has no open player controllers, "before it wouldn't consider 'connected' bots."
- Fixed late joining players sometimes being assigned a controller already in use by a bot
- Fixed the real human player count not being networked to late joining players
- Updated bot pull lever code to use more reflection to be more consistent with the base game code
- Fixed bots being able to equip suits that the crew didn't own
- Improved bot suit change RPC code
- Fixed the bot carry weight not being networked to players who join late

## AI Improvements
During testing of this update, I noticed some issues the bot had during testing with some of the new enemies added and with the Kidnapper Fox. I went ahead and made some adjustments to the AI to help them respond better.
- Improved PanikState logic: bots can now attempt to grab nearby weapons if the bot could potentially save themself with it
- Finally fixed bot movement code, bots movement should no longer be affected by framerate.
- - Improved weedkiller healing logic for bots. Bots in the ship can now automatically pickup weedkiller if they don't have it in their inventory to heal a player.
- Added generic method LethalBotAI.FindItemOnShip method, removed duplicate code
- Improved NavMeshAgent enable/disable and teleport logic. Bots also consider paths through water to be twice as expensive. NOTE: This only works if the map sets the water NavMesh attribute.
- Refactored bot death handling for better mod compatibility. The base game now handles more of the death logic.
- Removed some redundant code in LethalBotAI

## Bugfixes and Optimizations
There were a few bugs I found and was informed about.
- Made a minor optimization to ProgressPlayerInfections_Postfix
- Hopefully fixed ship entry/exit bugs (e.g., bots falling out during landing)
- Fixed PumaAI transpiler and AudioReverbTrigger issues
- Removed PriorityMessageQueue class and replaced it with a helper class named PriorityQueue. This accepts all classes rather than just strings
- Updated PatchesUtils with more fieldrefs
- Made more optimizations in the terminal code
- Cleaned up code, removed unused patches, improved logging

## 5.0.1 - 2026-4-8
Hello, a minor bug fix patch here. It came to my attention that trying to have more than 3 bots would REALLY lag the game. The cause was a logic error in HealPlayerState that would cause the bots to spam FindObjectOfType calls every time CanHealPlayerWithWeedKiller was called.

Here was how bad the logic error was:
For context, this was called for every bot for every player in the game other than the bot itself.

For three bots, that's about 3 Bots * 4 Players each. That's around 12 FindObjectOfType calls. Which isn't too bad........

As for 7 bots, that's about 7 Bots * 8 Players each. That's over 56 FindObjectOfType CALLS.........

And for the 15 bots, that's about 15 Bots * 16 Players each. That's OVER 240 FindObjectOfType..... :sob: 

Mind you the issue gets worse with adding more human players as well.........

Anyway I also made some other optimizations and bug fixes.

Change Log:
- Fixed a logic error in FightEnemyState which caused bots to sometimes believe they cannot hit an enemy when they actually could. (Yes, bots can now properly fight Thumpers again! :confetti_ball:)
- Fixed a logic error in HealPlayerState that would cause the bots to spam FindObjectOfType calls every time CanHealPlayerWithWeedKiller was called
- Fixed AIState.useNoiseMakerCooldown not being shared between states which could causes some infinite loops
- Fixed all bots being teleported by an inverse teleporter even if they were not standing near it
- Fixed a bug created by the V80 update which caused bots to not drop their held items when teleported back to the ship
- DunGenTileTracker now keeps track of the LethalBotAI its attached to
- Added an UpdateLimiter to DunGenTileTracker and had its update rate set the the LethalBotAI's AIIntervalTime so about every 0.3 seconds
- Added a new function to UpdateLimiter, SetUpdateInterval. This allows me to change updateInterval without having access to the constructor
- Added some comments to UpdateLimiter

## 5.0.0 - 2026-4-7
ITS V81 TIME!!!!!!!<br/>
Hello again, V81 came out and as such the bots needed to be updated to work with the newest version of Lethal Company. I have also taken the liberty to teach the bots how to counter one of the new enemies, but with a catch. Now onto the patch notes!

## Main Bot Fixes
Now, here are some general AI fixes for the bots:
- Added Harmony reverse patch for ShipTeleporter.GetInverseTelePosition to mimic base game inverse teleporter logic
- Bots now understand how the new equipment slot works
- Updated some of the inventory code to be more consistent with how the base game does it
- Fixed a minor logic error with bots assigned to transfer loot that had a loadout assigned
- Added a constraint for the new reserved equipment slot
- Added a new reverse patch for DropHeldItem
- Improved drunkness effects and ship bounds logic for bots
- Updated item drop syncing to be more in line with how the base game handles it
- Refactored UpdateLimiter and DeadBodyInfoMonitor for reuse and robustness

## Bot Infection & Healing System
The new infection system with one of the new enemies had to be addressed in this update. I needed to have the bots understand how to counter it since it would be really bad if they didn't. I will note that the bots were taught to NOT understand the too far gone mechanic........for now.......
- Added infection support for bots
- Introduced LethalBotInfection for networked infection state tracking
- Bots can now be infected, show symptoms, and have the infection burst from them
- Introduced HealInfectionLevel network variable to control when bots attempt to cure the infection with weed killer (randomly selected from 0.3-0.7 inclusive)
- Added SprayPaintItemPatch to allow bots to use spray items
- Added bot healing state: bots can cure the infection with Weed Killer
- Added HealPlayerState and healing logic for bots
- Bots now detect and heal infected players using Weed Killer
- The infection must reach the bot's chosen infection threshold (HealInfectionLevel); bots only heal when appropriate and wait until their target's health is stable before using weed killer

## AI and Logic Improvements
To end off, we have some AI logic improvements and some general bug fixes.
- Updated the default loadout file to include a new weed killer loadout
- Updated the default restock file to have the bots automatically purchase weed killer
- Updated the default bot restock economy limit to 550 credits. This is enough to make it to Rend.
- Changed how CanEnemyBeKilled works (will improve system later)
- Bots will now attempt to rescue players from the Kidnapper Fox if they are armed
- Bots are now afraid of the new enemies
- Improved enemy AI patches to use new utilities and reduce code duplication
- Fixed multiple other patches and updated documentation
- Fixed some logic errors with the update limiter system used in my patches

## 4.0.0 - 2026-3-30
Since V80 just came out recently, I think its fine for me to release this update now rather than later.
**NOTE: THIS UPDATE IS FOR V73, IT MAY OR MAY NOT WORK IN V80**

Now then, this update adds a new group system that allows you to give bots predefined groups in the ConfigIdentities file. Bots will automatically join their assigned groups when they first enter the searching for scrap state.
This update also fixes some bugs that were found as well.

## Group System
This is it, the Group System. There isn't much to talk about since the system is mostly the "concept" of a group. What I mean by that is that there is no fancy UI showing who is and isn't in your group. This system can definently be improved as time goes on, but for now it works.
- GroupManager handles the creation, removal, and networking of all player and bot groups
- Changed LethalBotAI.SyncAssignTargetAndSetMovingTo to work with bots as well.
- Updated bot identity file to accept internal group ids

## AI State Improvements and Bug Fixes
I made some fixes to some AI states that were reported and as well as found during testing of this update.
- Refactored bot "look at" logic, again, to now properly utilize NetworkObjects for targeting, and updated several AI states to use the new system.
- Hopefully fixed a logic error in FightEnemyState where bots had a chance to stand too far away from the enemy they are trying to fight
- Fixed bots in the MissionControlState attempting to grab their selected weapon and walkie-talkie if they have no room for items. 
- Bots upon entering PanikState will now flee backwards while waiting for their retreat node to be chosen
- Made some optimizations to GetCloseToPlayerState
- Changed AIStates to use new CurrentEnemy property
- Fixed a logic error in MissionControl state that caused them to never consider Baboon Hawks as threats
- Fixed Mission Controller bots grabbing their loadout just to immediately drop it.
- Bots will now remind players of their poor decision of not purchasing a teleporter if they request a teleport and the crew doesn't own said teleporter
- Bots that are following other players will now drop their held scrap, if there is a HUMAN player or bot transferring loot, at entrances. 
- Fixed more bugs with bots picking up and dropping items sometimes breaking item tooltips
- Fixed a rare bug where an EnemyAI.eye could be invalid, but not null which would cause the bots to break and spam the console with logic errors
- Fixed bots sometimes failing to grab items from the dropship.     
- Bots can now randomly use NoiseMaker items

## Chat, Signal Translator and Voice Commands improvements
The entire chat command and voice command system has been reworked to be much better than it was before. Most of the changes are behind the hood, but make it 10 times easier to add and remove chat, signal translator, and voice commands.
- Added two new managers ChatCommandManager and SignalTranslatorCommandsManager, ChatCommandManager also handles voice commands.
- Refactored the chat command system to a much better system that has a manager that handles registering commands.
All AIStates that use chat commands have been updated.
- LethalBotManager now handles registering chat commands.
- Chat commands will now automatically register themselves as voice commands
- Voice command registration is now handled in LethalBotManager
- Replaced PySpeech with SpeechRecognitionAPI for voice command recognition
-  Fix a sanitization issue with file paths with the bot voice chat system

Oh, and there are new chat commands as well!
- Added some new chat commands:
1. i will transfer loot, this tells bots that you will be transferring loot! NOTE: All it does is add you to the LootTransferPlayers list. This causes the drop loot outside of entrances code to run!
2. create group, this creates a new group with you as the leader!   
3. leave group, this causes you to leave the current group you are in.
4. join group, this lets you join a group. You must look at the bot of the group you want to join.
5. use key, this tells every bot that is following you to unlock the door you are looking at. NOTE: You must be standing within use range for this to work!

## General and Mod Support Fixes
- Voice recognition API is now an optional mod requirement
- Added LethalBotNetworkSerializer.SerializeNullable support for NetworkBehaviourReference
- Added a new Postfix in EnemyAIPatch to catch enemies that don't properly register themselves to SpawnedEnemies when spawned.
- Made an experimental change to see what happens if I allow the bot to pathfind and move while in midair
- Added a new config option, DisableNameBillBoards. This disables the bot's name billboards from rendering.
- Added partial support for the GeneralImprovements mod, fixing scannable player's feature not working properly with bots.
- Fixed a logic error with Bunk Bed Revives. Bots no longer get infinite revives.
- Fixed bots not hanging up their phones before leaving the server. 

## Other changes
- Updated outdated README.md files........there is still more to fix, but I think its good enough for now

## 3.0.1 - 2026-3-4
Just some minor bug fixes!
- Fixed some logic errors in CollectPurchasedItemsState
- Split some of the dropship landing checks into helper functions
- Fixed bots failing properly terminate the CollectPurchasedItemsState in some rare circumstances
- Adjusted some MissionControlState logic for CollectPurchasedItemsState
- Bots no longer consider items with no value as scrap, even if the itemProperties tells us so
- Fixed StartOfRound.SyncAlreadyHeldObjectsClientRpc not setting bot's HeldItem
- Fixed PlayerControllerB.TeleportPlayer not properly setting teleportingThisFrame for bots
- Made a proper transpiler for GrabbableObject.DiscardItem, GrabbableObject.DiscardItemOnClient, and GrabbableObject.EquipItem which fixes the hacky prefix rpc patches.......
- Made some optimizations

## 3.0.0 - 2026-2-27
Alright, its time for the next update. This time improving how bots use the terminal and allowing them to buy stuff on it. There is a config file that allows you to specify what items to buy and how much to keep stocked on the ship. The config system **HAS BEEN CHANGED**, instead of keeping track of two files Default and User. A general Identity and Stock config file will be created. This allows you to make edits directly without my mod overriding it anymore. If you want to reset said config files, its a simple as deleting them. My mod will recreate them automatically. This doesn't apply to the Loadout system since it loads both Default and User config files and the Default loadouts can be overridden in the User config file as desired. Now, onto the patch notes!

## Bot Level Persistency
After you get fired, all bot identities would be reset back to default. This ends up causing bots to loose their levels. They will no longer do this by default. Config options have been added to you to reenable this or choose when to reset all identities.
- Added new config option ResetIdentitiesWhenFired, when set to true, the save's identity files will be reset for all clients after you get fired by the company. By default, this is false.
- Added a new config option ResetIdentities, when set to true, this is force reset the save's identity files upon loading a save file. After force resetting the save's identity files, it will automatically set itself back to false. By default, this is false.  

## Bot Terminal rework
The entire system for how the bots use terminals has been rewritten. Bots can now actually go through the terminal node trees when on the terminal.
- TerminalManager now actually has a purpose other than caching the terminal instance. It holds all of my modified versions of the terminal code.
- Mission Control State has been updated to reflect this new system!
- Improved LethalBotAI.EnterTerminal
  
## Inverse Teleport Improvements
Bots now use more base game code when being inverse teleported. This should make mods to selectively drop items when being teleported work with them now.
- TeleportedBotDropItems config option has been removed since the base game and other mods will handle that now
  
## Bot Restock System
As stated earlier, you can configure bots on what gear to buy and how much to keep in stock on the ship! Bots also consider sale prices as well.
- Added new config option RestockEcoLimit, How much money should the bot leave in reserve when restocking the ship. This is useful if you want the bot to keep some spare cash on hand.
- Added CollectPurchasedItemsState, this makes the bot wait nearby the drop ship landing zone until it arrives, which then the bot will open it and transfer the purchased items back to the ship. For now, bots will only do this at the company building.
- The Mission Controller bot and bots chilling at the ship will automatically swap to the CollectPurchasedItemsState as needed
 
## Ghost Girl Support
You may have not known, but Ghost Girl would break with bots. She wouldn't run any of her logic due to the way she was programmed. This has been fixed and she now properly stalks and attacks bots.
- Finally fixed Ghost Girl breaking if targeting a bot
- Bots being haunted by Ghost Girl can now see her when being chased or watched
 
## General Improvements
- Rewrote the config system to only use one file for each type, rather than pairs of two.
- Bots that are grabbing their loadout now consider reserved item slots
- Bots in the FetchingObjectSlot now consider reserved item slots when checking if they still have inventory space
- Made some minor adjustments to JustLostPlayerState
- Bots in the MissionControlState will now grab their loadout
- Made some adjustments to MissionControlState in general. Bot's logic when monitoring players has been slightly improved.
- Added a new overload to LethalBotAI.HasSpaceInInventory that considers if the bot has space for the given GrabbableObject
- My mod will now list all items and unlockables in the game along with their id in the console. This makes it easier to find custom item names and suit ids!
- Improved how FightEnemyState detects enemy colliders
- Improved PanikState by adding a new function CounterEnemy, which will make it easier to make bots counter certain enemies.
- Updated Constraints files
- Made some minor optimizations
  
## Bug fixes
- Fixed a logic error where clients' custom loadouts and bot identities could be overridden by other players if you joined their game. The issue would fix itself if you restart the game.
- LethalBotManager.BeamOutLethalBots is called on all clients, but ran its code on all bots. This could create some strange logic issues. Its code will now only run on bots owned by the local player. Which should fix duplicated logic calls.
- Changed how bots open doors to be more consistent with how the base game does it for players.
- Fixed a logic error in FightEnemyState.TryPlayCurrentStateVoiceAudio where bots would rarely say combat voice lines.
- Fixed some potential infinite loops with the Loot Transfer system
- Bots will only teleport players being threatened by Coil Heads if said player has not moved for over 10 seconds.
- Fixed a bug where Nutcrackers would hate some bots more than others........        
- Fixed Old Birds dealing double footstep damage......with this mod installed.......
- Fixed bots not gaining fear if a Coil Head stops moving near them.
- Fixed bot crouching and uncrouching, causing Nutcrackers to agro onto the local player...........

## 2.3.1 - 2026-2-18
Sigh, this might be one of the buggiest updates I have ever released........
- Fixed a logic error in LostInFacilityState that caused searchForExit to never start

## 2.3.0 - 2026-2-18
Just a simple bug fix update along with some code refactoring.
- Moved some repeated code into helper classes
- Fixed bots only selling to quota when the config is set to sell everything on the ship
- Fixed a logic error in a sanity check for reviveUsingZaprillator
- The new search routine system is a bit more lax on how close the bot must be before it can consider a node checked

## 2.2.1 - 2026-2-18
Emergency Patch!

Fixed a bug where the mod would fail to load if Lethal Phones was not installed. This was due to a reflection error that has been fixed on my part.

## 2.2.0 - 2026-2-17
Its that time again, another update! It was voted upon in the mod's discord for what I should work on next and creating a custom search routine was selected. This update also includes some improvements to the bot's AI in regards to some of the base game items as well as fixing some of the issues found since the last update. Ok, onto the change log!

## New Features
### Dynamic Item Usage
- Bots can now intelligently swap between items depending on the situation instead of only using the currently held item.

### Improved LookAtTarget System
- Bots can now track targets using ``NetworkObjectReference``s
- Bots now use dot-product based “sighted in” logic for more accurate aiming.

### Light Detection System
- Bots can now check the light level of the area around them
- Bots have learned how to use flashlights and will turn them on and off based on the bot's perceived light level

## New Config Options
### Kill Everything Config
- Added ``ShouldKillEverything`` config option to allow bots to attack all enemies regardless of normal killability. Closes #44 

### Allow Talking While Dead
- New config option ``AllowTalkingWhileDead`` lets bots play random voice lines when dead. Closes #43 
- Do note that they will play Chilling for now. A dedicated dead voice state will be added later.

## Lethal Phones Support
Yeah, you heard correctly, bots now support Lethal Phones! They can pickup and accept calls from players and even enemies. Currently, bots will **NOT** call other player, but the code to do so does exist. So, this can be improved upon later. This closes #45 
- My mod will handle ownership, initialization, and cleanup for bots.
- Bots can answer calls
- Mission Controller bots will become the switchboard operator automatically

## AI Improvements
### Major Search Routine refactor
Credit goes to https://github.com/Gummar for this new system! Fixes #16 
- Overhauled Search Logic: Bots no longer rely on the vanilla ``EnemyAI.AISearchRoutine`` or its related functions. Search logic is now handled within the custom ``LethalBotSearchRoutine``, which is optimized for better performance.
- Much Better Persistent Search Memory: Bots can now pause and resume searches without forgetting explored areas. This prevents them from revisiting the same locations repeatedly.
- Dynamic Search Center: Search paths are now calculated based on the bot's current position or destination, rather than a fixed point chosen when the search began.
  - Note: If a bot loses sight of a player, it will use the player's last known position as the search center.
- Unrestricted Searching: Removed distance limitations on search behavior.

### Combat improvements
Combat is in a pretty good place right now, but there were a few things that needed to change and be fixed.
- Bots are much better at picking fallback and attack distances based on weapon type.
- Bots should hopefully be better at checking where they are aiming before attacking.

### State logic fixes and improvements
- More improvements to bot's elevator usage
- Improved RescueAndReviveState's aiming logic
- Bots will no longer sell dead bodies if there are better scrap items that could be sold at the moment
- Improved bot fear ranges for the Giant Sapsucker.

## Voice & Audio
- Added additional voice lines from Lethal Internship. Closes #49 
- Fixed voice transmission over Lethal Phones.
- Fixed footstep volume syncing across clients.

## Bug Fixes
And now the part you have been waiting for, the bug fixes!
- Fixed bots failing to swap items in certain situations.
- Fixed bots sometimes getting stuck in loops with elevators or terminals.
- Fixed revived bots always having full health.
- Fixed ship parenting issues when bots join or revive.
- Fixed ownership RPC error in HealthRegen.
- Fixed NullReferenceExceptions in door logic. Fixes #50 
- Fixed multiple logic errors in pathing and AI states.
- Fixed bots incorrectly updating last known player position while using elevators.
- Fixed the "request teleport" command targeting wrong player if there is an active signal booster.
- Fixed various AI state edge cases.
- Tentative fix for ShowCapacity creating issues with bots. Fixes #41

## Performance, Optimizations, & Cleanup
- Reduced index calls in some functions
- Replaced some Lists with HashSets
- Removed obsolete damage sync code, we let the base game handle that now.
- Removed redundant patches and unused logic
- Many other logic simplification and cleanup

## 2.1.0 - 2026-1-28
It was brought to my attention that multiplayer use with the bots had some issues. I have been working with the community on the both GitHub and Discord to find and fix these desync issues that were happening. I have also added some requested configuration options and much needed AI adjustments. Anyway, on to patch notes!

## Animation & Networking
- Fixed multiple animation networking issues that caused bots to slide or fail to animate on non-owning clients.
  - Corrected handling of animation slot 0 and animation hash layers.
  - Resolved issues where IsMoving returned false due to non-networked movement data.
- The animation culling system, to be honest, is a waste of CPU resources. Unlike Lethal Internship the bots only take up open player slots. This means if your PC can handle 16 animated players, then your PC can handle 16 bots. (NOTE: Not 1:1, but you should hopefully get what I mean) The animation culling system also created many animation desync issues with other players, so I have just decided to retire it.
- Removed CutAnimations, it was causing more issues than optimzations it gave.
- Fixed emote-related errors, including BetterEmotes compatibility and preventing emote spam when idle.
- Fixed a base game item desync bug
There is a bug in the base game when first opening a lobby that clients that join don't update the in-ship status of items. This causes bots to pickup items in the ship because they think said item is "outside" of the ship.

## Mission Control State Improvements
- Fixed multiple logic errors:
  - Bots now correctly teleport players who request it
  - Added AllowMissionControlTeleport config option, if AllowMissionControlTeleport is set to false, bots will no longer teleport players or bots they consider in danger. There is ONE exception to this, if the player is in an animation with an enemy, for example Forest Giants eating the player!
  - Mission Controller bots no longer teleport armed players fighting an enemy unless critically injured.
- Added new mission control-related config and chat command:
  - StartShipChatCommandProtection: If set to false, non-host clients can tell the Mission Controller bot to start the ship. By default this is true, making it so only the host can tell the bot to start the ship. Regardless if this setting is true or false, bots will allow non-host clients to use this chat command if the host is dead!
  - AutoMissionControl: if true, bots will be allowed to assume the mission control state if the current mission controller is not set or dead. If false, bots will not be allowed to assume the mission control position.
  - Added new chat command "i will man the ship", this tells all bots that the player who said this will be the mission controller. THIS WORKS FOR HUMAN PLAYERS! Good for if you want AutoMissionControl true, but a human player wants the role!
- Bots in the ChillAtShip state will no longer auto-follow the player assigned as Mission Controller.

## General AI, Movement, & Pathfinding Improvements
- Fixed several AI logic and pathfinding issues:
  - Improved safe path handling and stuck detection.
  - Prevented NavMeshAgents from being disabled during safe path usage.
  -  Changed the bot movement system to be more the one players use. This also fixes a bug where bots would move slower or even spin in place with low FPS!
- Added some new helpers and improvements:
  - Added IsHeadAimingOnTarget helper to LookAtTarget class
  - Added RegisterCustomThreats to LethalBotManager This makes it easier for modders to add custom fear ranges and functions for bots.
  - Added AIState.IsSafePathRunning, allows me to check if the safePathCoroutine is running on a state
  - The static version of LethalBotAI.IsValidPathToTarget can now return path distance. Don't know why I forgot to add that in the first place......
  - Made the instance version of LethalBotAI.IsValidPathToTarget's logs a bit better and changed its code to be more consitent with how the base game does path checks.

## Return To Ship State improvements
- Fixed entrance detection and return-to-ship logic, bots should no longer only pick the middle of the ship when returning.
- Bots will now return to the ship if the ~~company~~ is attacking when selling.
- Added another config option ReturnToShipTime, determines what time should bots automatically return to the ship. This has to be a normalized value. TIP: 10:00 PM is about 0.9f and the time the ship auto leaves at is 0.996f!

## Item Interaction & Inventory
- Fixed player controller ownership being on the wrong client and desync issues when bots pick up or drop items.
- Bots now ignore unspawned network objects when searching for items.
- Fixed a logic error in FetchingObjectState. 
When checking if an object could be picked up bots would check their current position rather than their camera position like the base game. This caused the bots to fail to pickup high up items that were reachable.
- Fixed blacklist logic to prevent bots from grabbing dangerous or forbidden items (e.g., Giant Sapsucker eggs while parent is alive).
- Added config option GrabDockedApparatus: If set to false, bots will no longer be allowed to pull the apparatus. Bots will still be able to pick them up if a human player decides to pull the apparatus themselves.
 - Bots will no longer grab extension ladders unless they returning to the ship at the end of the day and they are not actively deployed.

## Reserved Item Slots && Hotbar Plus Support
- Bots will no longer reset their inventory size back to the default value of 4. They will now allow other mods to adjust their inventory size!
- Bots have a basic understanding of how Reserved Item Slots works. When they pickup items they will automatedly check if they can put it into its respective reserved item slot. Bots are also able to use items in reserved item slots.
- Special thanks to https://github.com/cmooref17 for adjusting parts of their mod I needed to make this happen.

## Fight Enemy State && Company Building fixes
- Fixed a rare issue where bots could forget how to use their weapon.
- Fixed a rare bug where bots fail to stop using their held item, this would make the bot unable to swap to its chosen weapon!
- Fixed bots being unabled to use the zap gun in some cases.
- Bots are hopefully better at aiming weapons they are attempting to use
FightEnemyState had a rare issue where bots would fail to stop using their held item. This has been fixed.
- Bots can now trigger AnimatedObjectFloatSetters and OutOfBoundsTriggers. These triggers are commonly used to kill players!
- Fixed the company killing the local player when colliding with bots.

## Miscellaneous
- Added SwitchToItemSlot prefix for bots and cleaned up unused PlayerControllerB patches.
- Removed outdated comments and unused code.
- Multiple minor backend optimizations and stability fixes.
- Fixed non-host clients attempting to update player counts at the end of the round. A warning would be logged, but nothing would happen due to my failsafe code!
- Put CountAliveAndDisableLethalBots in a try catch statement to make sure bots are cleared even if an error occurs!
- Fixed bot voice chat not respecting the default volume set in the Identity file
- Updated NpcControler.ForceTurnTowardsTarget to be more in line with how PlayerControllerB.ForceTurnTowardsTarget is.

## 2.0.0 - 2026-1-16
Hello and welcome the the first **MAJOR** patch of Lethal Bots! Please do note that you **MUST** update your custom identity config files. If not, many errors may occur! Ok, now onto the changes!

## Bot Loadouts
You have heard correctly, bots can now have loadouts! No longer will have to give each bot an item when moving out to loot. Just set the bot's loadout in a custom identity config and tell them to "gear up!" Bots that are set to search for scrap will also automatically grab their loadout!
- Added new LoadoutManager which manages all available loadouts
- Bots identity files have been updated to have a new member loadoutName. This is the name of the loadout the bot wants to use can be set to Empty to tell the game the bot has no loadout.
- Bots that are grabbing their loadout will not grab conductive items if the current planet weather is stormy
- Added new state GrabLoadoutState, bots in this state will find and grab all items that are in their loadout on the ship.
- Added new chat command, "gear up." Bots that are following the player will automatically swap to the GrabLoadoutState.
- Added a bunch of important networking functions and classes to sync the host's loadouts for bots to other clients
- LethalBotNetworkSerializer has a new function SerializeStringArray. This was made to help sync the loadouts between clients.
- Bots will not automatically drop items that are in their loadout while at the ship. They will drop duplicate loadout items they may have.
- Multiple inventory functions have been updated to consider loadout items
- Added new function to LethalBotAI HasDuplicateLoadoutItems. This checks if the bot has more than one of the same items for their particular loadout
- Added IsGrabbableObjectInLoadout to LethalBotAI and LethalBotLoadout classes. This checks if the given grabbable object is in the bots current loadout.
- SearchingForScrapState automatically makes bots grab their loadout.

## New Default AI States
As requested in #27, bots can now be assigned a default AI state. It gets kind of annoying to make all of the bots lead the way at round start and oops you send the wrong bot to lead the way when you wanted them on the terminal. Well look no further, you can now use a custom Identity file and set their default state.
1. Dynamic: This is the default, if the bot finds a human player to follow when spawning, the bot will do just that. Other than that the bot will choose which AI state to enter dynamically!
2. FollowPlayer: Self explanatory, makes the bot follow the nearest human player
3. SearchForScrap: Makes the bot search for scrap upon spawning
4. ShipDuty: Makes the bot man the ship upon spawning

- Added EnumDefaultAIState which is used by bots to check which state to enter upon spawning, there are four options, Dynamic, FollowPlayer, SearchForScrap, ShipDuty. Closes #27 
- GetDesiredAIState has been updated to respect the new bot config option defaultAIState. Bots can now be assigned a default AI state that they swap to upon spawn.
- Fixed a logic error in GetDesiredAIState where dead players were considered valid follow targets
- Updated default ConfigIdentity file with the new defaultAIState member

## Aiming system overhaul
The aiming system used to be **TERRIBLE**, no joke it sucked a lot. There was no way to set look at priorities which made it hard to make the bot look where it was running and look at the coil head heading Mack 10 at them.
- The aiming system bots use has been reworked. Bots can how have priorities set for look targets and can even define how long to look at said look at target.
- All AIStates have been updated to use this new system.
- NpcController has been updated to use this new system and expose some helper methods for use as well.
- Bots that are returning to ship will now look around randomly

## Bug Fixes
Of course, we can't have an update without **CRUSHING** some bugs! Most of these I found while testing.
- Fixed IsHoldingRangedWeapon and HasRangedWeapon not checking if the bot has ammo for said ranged weapon
- Fixed a logic error in SellScrapState where there was a rare chance the bot would sell an item that was marked not to be sold.
- Fixed a logic error in ChillWithPlayerState that would cause bots following the player to not use the inverse teleporter if they were holding any items. (This would cause bots with loadouts to not use the inverse teleporter)
- Potential fix for Lethal Company VR mod breaking when bots die. I don't have a VR headset so I can't really test if it was fixed. Should fix #24

## Misc. Changes 
And the changes I don't really know where to put anywhere else, so uh, here you go!
- Changed ChillAtShipState to be more like ChillWithPlayerState when checking for items in their inventory to charge
- Bots that are in the SearchingForScrapState and GetCloseToPlayerState will wait for the ship to land before doing anything
- Cleaned up some code
- Bots that are returning to the ship will now sprint if they are exposed
- Made a minor optimization to the loading of this mod's asset bundle.

## 1.2.1 - 2026-1-12
Hotfix for the 1.2.0 update. I added some experimental retreat code, but it made the bots too afraid to run away at times.
- Reverted some of the new parts of the experimental retreat code
- Made bots immediatly reconsider their target safe path postion if they swap states
- Fixed a minor logic error in the fallback code in BrainDeadState

## 1.2.0 - 2026-1-9
As you may or may not be aware. Bots have support for some revive mods. Before this update, bots could only be revived and could not revive other players, now they can! There are also some other improvemnets such as better fallback choices when paniking.

## Revival and Mod Compatibility
- Bots, using supported revive mods, can now revive dead players and bots! (Suggestion from GitHub)
- Improved Bunkbed Revive navigation and RPC calls.
- Added static CanRevivePlayer function for unified revival checks.
- Fixed bots revived via Revive Company being fully healed when revived. They will now take ReviveToHeath config into consideration now.
- Fixed bots not updating the Spectator UI if they were revived

## Panik / Fleeing Improvements
- Rewrote a good chunk of the Panik system. Bots now assess if the path to the node they are thinking about fleeing to will get too close to the enemy they are fleeing from when picking a safe node to fall back to.
- Fixed a logic error in PanikState that would cause bots to forget that they were running away from jester if another enemy became a more important threat.
- Fixed bots using facility entrances to flee from outside enemies. This could cause the bot to enter an infinite loop of fleeing the same two inside and outside enemies!
- Made bots always test node visibility in PanikState
- Cleaned up code and removed redundant comments in PanikState
- Made PathIsIntersectedByLineOfSight more consistent with the checks used in the safe path system

## Mission Control & other AI State Improvements
- Improved combat fallback movement with navmesh raycasting, improved edge case handling, and fixed the spectating UI not updating when a bot was revived.
- Removed the hacky method used by Mission Control bots for collecting bodies. They will now actually pickup the body shortly after teleporting them.
- Added a helper function to MissionControlState, GetOffTerminal, its designed to get the bot to leave the terminal and stop all active coroutines in the state. Helps reduce duplicated code!
- After spawning bots will now pick their stating AI state based on the current situation. This is good if you have player revive mods since they will return to searching for scrap if another bot revives them.
- Added some comments to FightEnemyState
- Gave MissionControlState a new constructor

## Grabbable Object & Enum Changes
- Added a new enum EnumGrabbableObjectCall which helps IsGrabbableObjectBlackListed determine which items are actually blacklisted.
- Also changed IsGrabbableObjectGrabbable to accept the new EnumGrabbableObjectCall
- Made FetchingObjectState accept EnumGrabbableObjectCall instead of a bool for checking if the bot is selling or reviving a player.

## Networking, Spawning, & Sync Fixes
- LethalBotManager can now return all active bot instances.
- Readded some of ModelReplacementAPIPatch, its needed to fix my mod's support with late join mods!
- SpawnLethalBotParamsNetworkSerializable.SpawnPosition can now be null. When spawning bots, if SpawnPosition is null, the ship will be used instead!
- Fixed bots sometimes spawning outside of the ship on round start. This was caused by my mod now waiting for the bot's network object to be ready, which could cause the ship to move too far from the original spawn position.
- Fixed SetStartingRevivesReviveCompany only being called on the server causing the number of remaining revives to be desynced

## Misc.
- Cleaned up obsolete code and updated plugin initialization.

## 1.1.1 - 2026-1-4
Hotfix for the 1.1.0 update. 
- Fixed a potential race condition in safe path system.
- Fixed a logic error where bots checking if an entrance was safe would consider other bots as enemies. (This is since bots are EnemyAI's internally!)

## 1.1.0 - 2026-1-4
Its time for the first "real" update that isn't just bug fixes. You can now give bots another role! You can now assign, multiple bots to focus on transferring loot between the facility entrances to the ship. Other bots should recognize this and will leave loot they find outside of the building entrances. There are also some bug fixes included as well!

## New Features & Gameplay Changes
- Added TransferLootState for bots to handle transferring loot from facility entrances to the ship, with entrance selection and wait logic.
- Added new chat and voice command "transfer loot"
- Bots now drop scrap for loot transfer players near entrances, instead of always returning to the ship if a bot was assigned to transfer loot.
- Bots that are alone no longer wait to press the elevator button
- Improve enemy combat logic to consider stunned state. Enemies that bots would only fight with ranged weapons, will now be fought with melee weapons while said enemy is stunned.

## AI & State Refactors
- Refactored looking around coroutine into the base AIState for reuse across states.
- Updated ReturnToShipState to support early exit when outside, allowing it to be used to move the bot outside if they were inside the facility.
- Refactor FindClosestEntrance to accept a list of entrances to avoid
- OnExitState now has a new parameter which is the state the bot is changing to
- Refactor Bot item selection with virtual helper methods
- Added virtual FindObject/FindBetterObject in AIState for item selection and comparison.
- Refactor state logic to override these methods, replacing redundant helpers.

## Chat, Voice, & Interaction Improvements
- Added new helper function IsBotBeingAddressed, this helper was provided by https://github.com/iSeeEthan
- Bots will now respond in the chat upon "hearing" or "seeing" a chat command
- Added new config option AllowBotsToChat and improved SendChatMessage
- AllowBotsToChat controls if bots can reply to chat or voice commands by sending a chat message. NOTE: This doesn't affect the bots ability to declare jesters!
- SendChatMessage has been updated to no longer cut off words in long messages if possible. The bot will try to send the rest of the word on another line.

## Loot & Item Handling Changes
- Refactored IsGrabbableObjectGrabbable to allow bots set to transfer loot, to grab items near entrances. It now also prevent bots from picking up items near entrances if a bot is set to transfer loot.
- Add IsItemScrap utility and update scrap checks.

## Bug Fixes & Behavior Adjustments
- PanikState will no longer call the base class for OnPlayerChatMessageRecevied since it seemed a bit weird for a bot that is panicking to be listening to a player
- Fixed a logic error in PanikState that caused bots to return to the ship even if they were following a player
- Potentially fixed another logic error in PanikState:
Changed node consideration code to use the safety score instead of a bunch of if else statements. This should hopefully fix an issue where bots only prioritized paths further from the enemy instead of trying to break line of sight as well.

## Config Changes
- Changed GrabManeaterBaby config name:
This was done since the name may have been misleading for users. Its new name actually describes what it does! Bots will pickup and calm down the baby Maneater if its crying!

## 1.0.7 - 2026-1-3
Waiter, Waiter, more bugs fixed please! Back with another bug fix update. Hopefully, this should be the last of them for a while, other than the "Incompatible with Better Emotes" bug, which I'm still working on.

- Bots now cache the transform they chose when returning to the ship. They will now update their target ship postion every few seconds. Fixes #18 on GitHub.
- Fixed logic error in stuck detection causing the off the navmesh teleport check to never run.
- Updated TeleportAgentAIAndBody to also call agent.Warp when teleporting the bot.
- Added null checks for NpcController in patches to prevent errors on bot spawn.
- Disabled and removed most of my model replacement patches. They were from Lethal Internship and were only creating more issues than they were worth. Besides, the base game and original mod handles model replacements just fine. Fixes #19 on GitHub.
- Minor change to have my mod cleanup model replacements when the bots "disconnect."
- Fixed LethalBotManager.ShipDoor being a private property
- Fixed a potential null reference exception in UpdateOwnershipOfBotInventoryServer

## 1.0.6 - 2026-1-1
I don't know when, but apparently a bug got introduced that caused bots to not properly clean up their model replacements when they disconnected from the server. This update fixes that bug. Oh, and added a few other things too:

- Bots will change their suit back to the default suit when they "disconnect." This mimics base game logic and fixes a memory leak with model replacements.
- Bots will now have a joined ship message the first time they spawn. To mimic how the base game does it.
- Fixed BodyReplacementBasePatch not calling OnDeath function and not calling the avatar update function on dead bodies
- Fixed my mod sometimes failing to clean up old dead bodies with model replacements
- Added a few helper functions with safely removing model replacements
- Fixed bots not properly cleaning up their model replacements when they left the server. (This bug didn't happen if the host closes the lobby since ModelReplacementAPI will cleanup the models automatically)
- Fixed a logic error where searchForScrap would wrongfully clear its already searched nodes prematurely
- Gave bots support for using the Zap Gun
- Added a new constraint called TIMER_CHILL_AT_SHIP_AT_COMPANY

## 1.0.5 - 2025-12-28
Changed EnableDebugLog default to true to help with debugging.

## 1.0.4 - 2025-12-28
Another day, another bug fix! Special thanks to everyone on the mod's discord who helped test this update.

- Added Kittenji-NavMeshInCompany version 1.0.3 to thunderstore.toml since this allows bots to spawn in while at the company to help sell stuff. If NavMeshInCompany is not installed, bots will not spawn at the company building.
- Updated bot spawn logic to wait for NetworkObject readiness before sending client RPC.
- Added a Harmony patch to prevent Dissonance voice chat from starting if the player is a bot, which fixes host's voice being heard from bot positions. (Reported on GitHub and Discord)

## 1.0.3 - 2025-12-25
- Added support for .wav and .mp3 bot voice files by updating file loading logic and determining AudioType by extension.
- Removed all audio fade-in and fade-out functionality, replacing it with direct Play/Stop calls. They stopped working after I changes bot voice volume to be controlled by the quick menu
- Cleaned up unused constants and improved path handling for custom voices.

## 1.0.2 - 2025-12-22
- Fixed an logic error where ListModelReplacement was never initialized. This caused the ModelReplacementApi support to not work! (Reported on GitHub)
- Update README files to remove any refrences to AI voices as they were copied over from when I forked Lethal Internship and do not reflect my interests for this mod.

## 1.0.1 - 2025-12-14
- Fixed a logic error in the ChillInShipState which caused the bot to not properly initalize the state while at the Company Building.

## 1.0.0 - 2025-06-22
- Initial release