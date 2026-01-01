# Changelog

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

## 1.0.5 - 2025-28-12
Changed EnableDebugLog default to true to help with debugging.

## 1.0.4 - 2025-28-12
Another day, another bug fix! Special thanks to everyone on the mod's discord who helped test this update.

- Added Kittenji-NavMeshInCompany version 1.0.3 to thunderstore.toml since this allows bots to spawn in while at the company to help sell stuff. If NavMeshInCompany is not installed, bots will not spawn at the company building.
- Updated bot spawn logic to wait for NetworkObject readiness before sending client RPC.
- Added a Harmony patch to prevent Dissonance voice chat from starting if the player is a bot, which fixes host's voice being heard from bot positions. (Reported on GitHub and Discord)

## 1.0.3 - 2025-25-12
- Added support for .wav and .mp3 bot voice files by updating file loading logic and determining AudioType by extension.
- Removed all audio fade-in and fade-out functionality, replacing it with direct Play/Stop calls. They stopped working after I changes bot voice volume to be controlled by the quick menu
- Cleaned up unused constants and improved path handling for custom voices.

## 1.0.2 - 2025-22-12
- Fixed an logic error where ListModelReplacement was never initialized. This caused the ModelReplacementApi support to not work! (Reported on GitHub)
- Update README files to remove any refrences to AI voices as they were copied over from when I forked Lethal Internship and do not reflect my interests for this mod.

## 1.0.1 - 2025-14-12
- Fixed a logic error in the ChillInShipState which caused the bot to not properly initalize the state while at the Company Building.

## 1.0.0 - 2025-06-22
- Initial release