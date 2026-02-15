using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace LethalBots.Patches.ModPatches.ShowCapacity
{
    public class ShowCapacityPatch
    {
        private static GameObject? capacityMeter;

        public static bool Update_PostFix_Prefix(bool __0, GrabbableObject __1, bool __2)
        {
            Update_ShowCapacityPatch(__0, __1, __2);
            return false;
        }

        private static void Update_ShowCapacityPatch(bool isHoldingObject, GrabbableObject currentlyHeldObjectServer, bool isCameraDisabled)
        {
            if (capacityMeter == null)
            {
                GameObject? gameObjectCapacityMeter = GameObject.Find("CapacityMeter");
                if (gameObjectCapacityMeter != null)
                {
                    capacityMeter = gameObjectCapacityMeter;
                    capacityMeter.SetActive(false);
                }
            }
            if (capacityMeter == null)
            {
                return;
            }

            GrabbableObject? grabbableObjectLocalPlayer = StartOfRound.Instance?.localPlayerController?.currentlyHeldObjectServer;
            if (grabbableObjectLocalPlayer == null)
            {
                capacityMeter.SetActive(false);
                return;
            }

            if (isCameraDisabled
                || !isHoldingObject
                || currentlyHeldObjectServer == null)
            {
                return;
            }

            if (grabbableObjectLocalPlayer != currentlyHeldObjectServer)
            {
                if (grabbableObjectLocalPlayer == null)
                {
                    capacityMeter.SetActive(false);
                }
                return;
            }

            SprayPaintItem? sprayPaintItem = currentlyHeldObjectServer.GetComponent<SprayPaintItem>();
            if (sprayPaintItem != null)
            {
                FieldInfo field = typeof(SprayPaintItem).GetField("sprayCanTank", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    float num = (float)field.GetValue(sprayPaintItem);
                    capacityMeter.GetComponent<Image>().fillAmount = num / 1.3f;
                }
                capacityMeter.SetActive(true);
                return;
            }

            TetraChemicalItem? tetraChemicalItem = currentlyHeldObjectServer.GetComponent<TetraChemicalItem>();
            if (tetraChemicalItem != null)
            {
                FieldInfo field2 = typeof(TetraChemicalItem).GetField("fuel", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field2 != null)
                {
                    float num2 = (float)field2.GetValue(tetraChemicalItem);
                    capacityMeter.GetComponent<Image>().fillAmount = num2 / 1.3f;
                }
                capacityMeter.SetActive(true);
                return;
            }

            capacityMeter.SetActive(false);
        }
    }
}
