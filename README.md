# Lethal-Bots
A Lethal Company Mod that adds bot players to the game. Based off of the code of the mod Lethal Internship.<br/>
Everyone must have the mod in order for it to work!<br/>

You can download the mod here: https://thunderstore.io/c/lethal-company/p/TRizzle/LethalBots/

I also have a discord server as well for any questions or support: https://discord.gg/TVqJst8yHf

## Preramble:<br/>

Playing solo in Lethal Company is hard, as the game is inherently designed around a team of 4 players.<br/><br/>
Now, there are certain mechanics that automatically adjust for single player, but you still run into multiple disadvantages.<br/><br/>
Loosing all your scrap if you die, enemies like the butler and coil head really screwing you over, turrets, landimes, big doors, and many other issues that are a lot easier to deal with multiple players.<br/><br/>
After I discovered the mod Lethal Interns, it inspired me in a way. I realized I could use my previous modding experience with player bots and use it to finally create player-like bots for Lethal Company.<br/><br/>
And that is how this mod came to be, and you would not believe how long it took me to get here. There are many older verison of this mod, lost to time.<br/><br/>
In the end, while they may never be as smart as a real player, its still better than playing alone. <br/><br/>
Oh, and before I forget, this mod is 100% MULTIPLAYER friendly, so feel free to add them to assist your Duo or Trio.

</br>![bots_example](https://github.com/T-Rizzle12/Lethal-Bots/blob/master/Assets/Images/bots_example.png?raw=true)</br>

## Bots and how they spawn

These passionate workers spawn in after you start landing on a moon.
The number of ~~bots that spawn~~ players that join depend on how many player slots are available and the maximum number of ~~bots~~ players set in the config<br/>
<br/>**They return to the ship**
<br/>If they lose the player they are following and it starts getting late out,
<br/>they will automatically return to the ship.
<br/>
<br/>**They always follow you unless told otherwise**
<br/>Or at least try to follow you, or another player.
<br/>The moons are not very welcoming and your ~~bots~~ new co-workers may have (some) difficulties to navigate smoothly in those tricky areas due to how the navmesh is made.
<br/>
<br/>**They can loot on their own**
<br/>You are able to command your ~~Bots~~ new co-workers to enter the facility and they will search for scrap and will only return to the ship when full or after a while!
<br/>
<br/>**They continue the round after you die**
<br/>In the event of your demise, ~~bots~~ your new co-workers are capable of continuing to collect scrap without you,
<br/>You can vote to leave early which will tell ~~bots~~ your new co-workers to return to the ship
<br/>They will automatically return to the ship if not following a player after the sun sets
<br/>
<br/>

## How to use the mod
- Bots will take up player slots that are open, if you have a full server this addon won't do anything.
- Bots leave after the ship returns to orbit, don't worry they will come back next time you land! </br>
This is done so other players can join if you have some kind of late join mod! </br>
- Bots automatically revive after returning to orbit. </br>
- You can revive bots and bots can revive you with other mods like 'Revive company', 'Bunkbed revives' and 'Zaprillator'</br>
- Bots can sell scrap at the company. You can configure if they sell everything, or only to quota </br>

While they are spawned, while looking at them: </br>
- You can command them to follow you with as well as tell them to loot on their own with [E]</br>
- Make them drop their held item with [G], they will automatically swap to the next item in their inventory</br>
- Change the suit of the bot by the one you're wearing with \[X]</br>
- All input are configurable.</br>

## Configuration files
Lots of settings for the bot's AI can be configured, so go check them !</br>
Mod is compatible with InputUtils so you can change your inputs !</br>
There's also a config file for the identities of the bots (a name, preferred suit, a voice)</br>
![folder_config](https://github.com/T-Rizzle12/Lethal-Bots/blob/master/Assets/Images/folder_configs.png?raw=true)</br>
Identities can be found in a new config file at Your_profile_folder\BepInEx\config\LethalBots\ConfigIdentitiesDefault.json</br>
If you want to make your own file, name it 'ConfigIdentitiesUser.json' and the default one will be ignored. Details can be found in the default json.</br>
![folder_config](https://github.com/T-Rizzle12/Lethal-Bots/blob/master/Assets/Images/folder_config_user.png?raw=true)</br>
To link the voice folder to the bot, simply change the "VoiceFolder" property in the identity you want.

## Fully voiced bots
T-Rizzle: I may change the voice lines along with using TTS in the future, but since the original mod was under an MIT license, I will keep the original voice lines for now.
</br>A big thanks to **Mathew Kelly** and his incredible voice acting, there's more than 700 (!!) voice lines for those little guys.</br>
Chilling with you, following, founding loot, panicking, you name it, there's a voice line for every state of mind !</br>
You may know him as **Dragon-V0942** from [FurAffinity](https://www.furaffinity.net/user/dragon-v0942), and you can find some of his voice acting works on youtube [(Voice acting example)](https://www.youtube.com/watch?v=5LGVz-ONeKE).
</br>
</br>

## Have fun with cosmetic mods !
This mod is compatible with ModelReplacementAPI and all of its users (tooManySuits, MoreSuits, ThiccCompany, etc...).
It is also compatible with the emotes mod, emote in front of bot and they will copy your dance moves !

</br>![Lethal-Bots-Suits](https://github.com/T-Rizzle12/Lethal-Bots/blob/master/Assets/Images/bot_suits.gif?raw=true)</br>

## Chat and Voice commands!
The bots have a few chat and voice commands that you can use to tell them to do certain things.
Please note that these commands are not case sensitive, so you can use any combination of upper and lower case letters.
Also, the bot only checks for the keywords anywhere in the message, for example, you can use "Jester is going to pop!" and the bot will still respond to it.
</br>**jester** - The bot will check to see if there is an active jester, if there is, they will try to escape the facility immediately.
</br>**start the ship** - The bot will check if the host sent the command or if the host is dead, if so, the bot will start the ship.
</br>**hop off the terminal** - The bot currently on the terminal will hop off for a few seconds, allowing you to use it.
</br>**request monitoring** - The bot who is currently on the terminal will monitor you rather than cycling through the players.
</br>**request teleport** - The bot who is currently on the terminal will teleport you back to the ship.
</br>**clear monitoring** - The bot who is currently on the terminal will stop monitoring you and return to the default behavior of cycling through players.
</br>**man the ship** - Makes the bot you are looking at go to the ship terminal and start manning it.
</br>**transmit (desired message)** - The bot who is currently on the terminal will send the given message on the signal translator.
</br>**transfer loot** - The bot will cycle between facility entrances and transfer any loot they find to the ship.
</br>**gear up** - Bots that are following the player will automatically swap to the GrabLoadoutState and grab their set loadout.
</br>Please note that you must be in chat range for the bot to hear you. If you are too far away, the bot will not respond to your command.
If both you and the bot have a walkie-talkie, you can use the command in the chat and the bot will respond to it.
</br>Please note that these are also voice commands, but they require you to only say the word unlike how they work as said in the chat.

Note: The bot will also respond to commands on the signal translator, but there is a seperate list of commands for that.
</br>**return** - The bot will return to the ship immediately.
</br>**jester** - The bot will check to see if there is an active jester, if there is, they will try to escape the facility immediately.

## How the bots work
The bots take one of the player objects in the game and I attach an EnemyAI to it for the pathfinding code.
</br>As a result, the game considers the bots as a human player for most intents and purposes, although there are some hacks added since the game runs most of its player logic on the client/local player only.
</br>The bot uses states to run its AI with certain conditions that determine when the bot changes its states.

</br>BrainDead:
</br>The Brain Dead state is when the bot dies in some way shape or form. In this state the bot can vote to make the ship leave early.
</br>There are two conditions in which the bot will vote to leave early.
1. All human players are dead and its late outside
2. The ship becomes compromised, aka an enemy is on the ship

NOTE: All players, "humans and bots," must be on the ship before the bot will vote!

</br>SearchingForPlayer
</br>If the bot loses the human player they were following,
</br>they will wander around looking for a human player before searching for scrap on their own.

</br>GetCloseToPlayer
</br>The bot moves closer to the player they are following, they check horizantal and vertical distances when checking if they are close enough.

</br>JustLostPlayer
</br>The bot just lost sight of the player they were following, they will check the last position they saw you at.
</br>If your last known position is near an entrance, the bot will use it!

</br>ChillWithPlayer
</br>The bot is waiting nearby the player they are following. They will mimic emotes the player is using.
</br>If they have any loot in their inventory and they are on the ship, they will drop it off!

</br>FetchingObject
</br>The bot is moving to pickup an object. If they are holding a two handed item, they will set it down to pick up the object.

</br>PlayerInCruiser
</br>The bot will hop into the back of the cruiser the player they are following is in and ride!

</br>Panik
</br>The bot will flee from a nearby enemy. If the bot is near an entrance, it will use it to escape!
</br>NOTE: If the bot escapes the enemy, has scrap, and is not following a player. They will return to drop off scrap early!
</br>If the bot has a weapon and they believe the enemy can be killed, they will swap to the FightEnemyState

</br>ReturnToShip
</br>The bot will leave the facility and return to the ship.

</br>ChillAtShip
</br>The bot is at the ship and will empty their inventory. They will perform a random emote.
</br>If there is no nearby human player and it's not late out, the bot will go back into the facility and look for more loot after a bit!
</br>The bot will start the ship if all human players are dead, all players are on the ship, and one of the following conditions are true:
1. It's late out
2. All human players voted to leave early
3. The ship becomes compromised

</br>SearchingForScrap
</br>The bot is searching the facility for scrap. If one of the following conditions are true, the bot will return to the ship:
1. The bot has at least one piece of scrap and hasn't found any other scrap recently
2. The bot's inventory is full
3. It's late out
4. All human players voted to leave early

</br>UseInverseTeleport
</br>If the inverse teleporter is active and the bot is on the ship, the bots will move to be teleported by it.
</br>It doesn't matter if the bot successfully teleported or not, they will switch to the SearchingForScrap state after, regardless!

</br>UseKeyOnLockedDoor
</br>The bot will attempt to use a key they have in their inventory on a locked door.
</br>The bot picks the closest side of the door
</br>If both sides of the door are accessible, they will only open it if there is a distance greater than 10 m between the two sides!

</br>MissionControl
</br>The bot will use the ship terminal and monitor the ship's crew while executing commands on the terminal as well as teleporting players as needed.
</br>Here is what the bot can do while on the terminal!
- Use commands to open heavy doors, turn off turrets, disable landmines, and disable ceiling traps // NOTE: For some reason, they don't seem to be able to disable ceiling traps, I'm looking into this
- Open heavy doors for the player they are currently monitoring
- Teleport bodies of dead players
- Teleport players who they considered in grave danger
- Use the signal translator to send messages to the crew about enemies and the current time
- Use the walkie-talkie to keep players' sanity up and receive chat messages from the crew

</br>TransferLoot
</br>The bot will transfer loot from the facility entrances to the ship

</br>CollectScrapToSell
</br>The bot will collect scrap on the ship with the intent of selling it. There are a few items the bots will refuse to sell:
1. Zed Dogs
</br>Please note that this does affect any item that inherits from the following classes!
1. Gift Boxes
2. Shotguns
3. Knives
4. Shovels

</br>SellScrap
</br>The bot goes over to the company desk to sell, ringing the bell and waiting if the desk is out of space.

</br>ChargeHeldItem
</br>The bot will charge its held item using the charging coil
</br>This is called when the bot is in the mission control state, and after returning to the ship

</br>FightEnemy
</br>The bot will fight its current targeted enemy
</br>The bot will pick the shotgun for most enemies, except for snare fleas, where they prefer the shovel or knife!
</br>Here are enemies the bot will always fight as long as they have a weapon
1. Snare Fleas
2. Masked
3. Thumpers
4. Hoarding Bugs
</br>Here are the enemies the bot will fight if they have a shotgun
1. Nutcrackers
2. Brackens
3. Bunker Spiders
4. Baboon Hawks (Only when in close proximity)

</br>UseTZPInhalant
</br>The bot will use the TZPInhalant in its inventory to get a speed boost!
</br>The return to ship state calls this when the bot is carrying heavy loot!

</br>LostInFacility
</br>The bot is lost in the facility and will try to find a way out.
</br>They will wander around the facility, calling out for help.

## On a more serious note
This mod is very alpha and I still need some polishing.
<br/>I'm just happy to share this project with you, in hope that you find it fun and enjoyable.
<br/>T-Rizzle: I have made multiple changes to this from the original addon. While Lethal Interns intend to have the, well you know, interns assist the player, I found it not to my liking, but I saw the potential it had in general.<br/> 
These bots are to be more player like and this includes advantages such as counting as real players and the downsides like limited stamina.
- Although they will never be the same a players, they are great for small groups or playing with weak or no internet.
- Or you could go crazy and use MoreCompany and run around with an army of them.
- I recommend about 23 to 31 of them. After that it really starts to get laggy and there is only so much I can do to optimize them.

## Credits
- [**Szumi57**](https://github.com/Szumi57) - Original idea and coding of the original mod, Lethal Intership.
- [**T-Rizzle**](https://github.com/T-Rizzle12) - Major code refactor, bug fixes, and new features.
- **Mathew Kelly** - Voice acting for the bots, over 700 voice lines!

## Things to add
- More Orders, currently you can tell them to loot and follow you. There are a decent amount of chat commands at least?
- I hope to make them able to do more on the ship terminal in the future. Like purchasing items and who knows what else!
- ~~Add some kind of voice recognition software, so bots could respond to simple voice commands. (DEVNOTE: Looks like there are some librarys on thunderstore that could help me with this endeavor!)~~ DONE! :)
- As stated earlier, add TTS to bots so they can be better at relaying information to players. (DEVNOTE: TTS would be perfect for helping the bots declare enemies on the terminal!)

## Bugs to fix
- The game will lag during the inital landing sequence after the bots spawn. I have no idea what causes this, but it fixes itself after the ship lands. This only seems to happen with about 21 or more bots. If you play with the default lobby size of 4 players, you won't see this issue. (DEVUPATE: Found out it might have something to do with collsion, have a few ideas on how to aleviate it!)
- Sand spider web trap not working for bots. Transpiler or Postfix should work, just need to find out what would be the best option.
- Snare Fleas ignore bots that walk under them. The cause is that they only check for the Local Client walks under them, a transpiler or postfix can fix this!
- ~~Nutcracker ignoring bots. The cause of this is known, but I still don't know a good way to fix it yet!~~ Fixed!
- Ghost Girl spawns but does nothing if it picks a bot. Plans to fix this are currently in the works! Lets just say its a lot of transpiler work.......
- Compatibility with modded maps, for environmental hazards damages. The cause is due to most of the player code only running on the local player, I think I can get some kill triggers to work using transpilers/postfixes.
- Bots don't use ladders, I know this is a major issue, but fixing it is harder than it seems. For now they use the same movement as the masked!
- Bots can hear you speak if you have push to talk set. The cause of this is how PySpeech works, since it runs a separate application that listens to your microphone. I may be able to fix this......
