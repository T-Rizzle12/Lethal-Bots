# Changelog

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