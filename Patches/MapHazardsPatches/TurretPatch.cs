using GameNetcodeStuff;
using HarmonyLib;
using LethalBots.AI;
using LethalBots.Managers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace LethalBots.Patches.MapHazardsPatches
{
    /// <summary>
    /// Patch for the <c>Turret</c>
    /// </summary>
    [HarmonyPatch(typeof(Turret))]
    public class TurretPatch
    {
        static MethodInfo DamagePlayersInLOSMethod = SymbolExtensions.GetMethodInfo(() => TurretPatch.DamagePlayersInLOS(new Turret()));

        /// <summary>
        /// Patch for making the turret able to detect bot and kill them by using another methode
        /// </summary>
        /// <param name="instructions"></param>
        /// <param name="generator"></param>
        /// <returns></returns>
        [HarmonyPatch("Update")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var startIndex = -1;
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo checkForPlayersInLineOfSightMethod = AccessTools.Method(typeof(Turret), "CheckForPlayersInLineOfSight");
            MethodInfo killPlayerMethod = AccessTools.Method(typeof(PlayerControllerB), "KillPlayer");

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 40; i++)
            {
                if (codes[i].IsLdarg(0) //306
                    && codes[i + 3].Calls(checkForPlayersInLineOfSightMethod) //309
                    && codes[i + 40].Calls(killPlayerMethod)) //345
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                for (var i = startIndex; i < startIndex + 4; i++)
                {
                    codes[i].opcode = OpCodes.Nop;
                    codes[i].operand = null;
                }

                codes[startIndex].opcode = OpCodes.Ldarg_0;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = DamagePlayersInLOSMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapHazardsPatches.TurretPatch.Update_Transpiler use other method for shooting player/bot in TurretMode.Firing");
            }

            // ----------------------------------------------------------------------
            for (var i = 0; i < codes.Count - 40; i++)
            {
                if (codes[i].IsLdarg(0) //490
                    && codes[i + 3].Calls(checkForPlayersInLineOfSightMethod) //493
                    && codes[i + 40].Calls(killPlayerMethod)) //529
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex > -1)
            {
                for (var i = startIndex; i < startIndex + 4; i++)
                {
                    codes[i].opcode = OpCodes.Nop;
                    codes[i].operand = null;
                }

                codes[startIndex].opcode = OpCodes.Ldarg_0;
                codes[startIndex].operand = null;
                codes[startIndex + 1].opcode = OpCodes.Call;
                codes[startIndex + 1].operand = DamagePlayersInLOSMethod;
                startIndex = -1;
            }
            else
            {
                Plugin.LogError($"LethalBot.Patches.MapHazardsPatches.TurretPatch.Update_Transpiler use other method for shooting player/bot in TurretMode.berzerk");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// Method injected in code, for checking bot and damage/kill them
        /// </summary>
        /// <param name="turret"></param>
        private static PlayerControllerB? DamagePlayersInLOS(Turret turret)
        {
            PlayerControllerB player = turret.CheckForPlayersInLineOfSight(3f, false);
            if (player == null)
            {
                return null;
            }

            LethalBotAI? lethalBotAI = LethalBotManager.Instance.GetLethalBotAIIfLocalIsOwner(player);
            if (lethalBotAI == null)
            {
                // Player not bot
                return player;
            }

            // bot
            if (player.health > 50)
            {
                player.DamagePlayer(50, hasDamageSFX: true, callRPC: true, CauseOfDeath.Gunshots);
            }
            else
            {
                Plugin.LogDebug($"SyncKillLethalBot from turret for LOCAL client #{lethalBotAI.NetworkManager.LocalClientId}, lethalBot object: Bot #{lethalBotAI.BotId}");
                player.KillPlayer(turret.aimPoint.forward * 40f, spawnBody: true, CauseOfDeath.Gunshots);
            }

            return null;
        }
    }
}
