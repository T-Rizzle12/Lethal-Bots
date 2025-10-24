using System;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.ModelRplcmntAPI
{
    public interface IBodyReplacementBase
    {
        Component BodyReplacementBase { get; }

        Type TypeReplacement { get; }
        bool IsActive { set; get; }
        GameObject? DeadBody { get; }
        string SuitName { set; get; }
    }
}
