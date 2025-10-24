using System;
using UnityEngine;

namespace LethalBots.Patches.ModPatches.ModelRplcmntAPI
{
    public class BodyReplacementAdapter : IBodyReplacementBase
    {
        public Component BodyReplacementBase => _component;

        public Type TypeReplacement => _srcType;

        public bool IsActive { get => _isActive; set { _srcType.GetProperty("IsActive")?.SetValue(_src, value); _isActive = value; } }

        public GameObject? DeadBody => _deadBody;

        public string SuitName { get => _suitName; set { _srcType.GetProperty("suitName")?.SetValue(_src, value); _suitName = value; } }

        private readonly Component _component;
        private readonly object _src;
        private readonly Type _srcType;

        private bool _isActive;
        private string _suitName;
        private GameObject? _deadBody;

        public BodyReplacementAdapter(Component component)
        {
            _component = component;
            _src = component;
            _srcType = component.GetType();

            _isActive = (bool)(_srcType.GetProperty("IsActive")?.GetValue(_src) ?? false);
            _suitName = _srcType.GetProperty("suitName")?.GetValue(_src).ToString() ?? string.Empty;
            _deadBody = _srcType.GetProperty("deadBody")?.GetValue(_src) as GameObject;
        }
    }
}
