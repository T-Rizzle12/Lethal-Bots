# Changelog

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