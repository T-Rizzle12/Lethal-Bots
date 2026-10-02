using System;
using System.Reflection;
using UnityEngine;

namespace LethalBots.Utils
{
    /// <summary>
    /// Utilitary class for displaying debug log of infos of objects
    /// </summary>
    internal static class ComponentUtil
    {
        public static void ListAllComponents(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            Plugin.LogDebug(" ");
            Plugin.LogDebug("List of components :");
            Component[] components = gameObject.GetComponents(typeof(Component));
            foreach (Component component in components)
            {
                if(component == null) continue;
                Plugin.LogDebug(component.ToString());
            }

            Plugin.LogDebug("Child components :");
            components = gameObject.GetComponentsInChildren(typeof(Component));
            foreach (Component component in components)
            {
                if (component == null) continue;
                Plugin.LogDebug(component.ToString());
            }

            Plugin.LogDebug("Parent components :");
            components = gameObject.GetComponentsInParent(typeof(Component));
            foreach (Component component in components)
            {
                if (component == null) continue;
                Plugin.LogDebug(component.ToString());
            }
        }

        public static void ListAllColliders(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            Plugin.LogDebug(" ");
            Plugin.LogDebug("List of Colliders: ");
            Component[] components = gameObject.GetComponents(typeof(Collider));
            foreach (Component component in components)
            {
                if (component == null) continue;
                Plugin.LogDebug(component.ToString());
            }

            Plugin.LogDebug("Child Colliders: ");
            components = gameObject.GetComponentsInChildren(typeof(Collider));
            foreach (Component component in components)
            {
                if (component == null) continue;
                Plugin.LogDebug(component.ToString());
            }

            Plugin.LogDebug("Parent Colliders: ");
            components = gameObject.GetComponentsInParent(typeof(Collider));
            foreach (Component component in components)
            {
                if (component == null) continue;
                Plugin.LogDebug(component.ToString());
            }
        }

        public static void SetFieldValue(object obj, string fieldName, object value)
        {
            Type type = obj.GetType();
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field.SetValue(obj, value);
        }

        public static void SetPropertyValue(object obj, string propertyName, object value)
        {
            Type type = obj.GetType();
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(obj, value);
        }

        public static T GetCopyOf<T>(this Component comp, T other) where T : Component
        {
            Type type = comp.GetType();
#pragma warning disable CS8603 // Possible null reference return.
            if (type != other.GetType()) return null; // type mis-match
#pragma warning restore CS8603 // Possible null reference return.
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Default | BindingFlags.DeclaredOnly;
            PropertyInfo[] props = type.GetProperties(flags);
            foreach (var prop in props)
            {
                if (!prop.CanWrite || !prop.CanRead || prop.Name == "name") continue;
                try
                {
                    prop.SetValue(comp, prop.GetValue(other, null), null);
                }
                catch { }
            }
            var finfos = PropertiesAndFieldsUtils.GetAllFields(type);
            foreach (var finfo in finfos)
            {
                if (finfo.IsStatic) continue;
                finfo.SetValue(comp, finfo.GetValue(other));
            }
#pragma warning disable CS8603 // Possible null reference return.
            return comp as T;
#pragma warning restore CS8603 // Possible null reference return.
        }

        public static T AddCopyOfComponent<T>(this GameObject go, T toAdd) where T : Component
        {
            return go.AddComponent<T>().GetCopyOf(toAdd) as T;
        }

        public static T CopyComponent<T>(T original, GameObject destination) where T : Component
        {
            System.Type type = original.GetType();

            var dst = destination.GetComponent(type) as T;
            if (!dst) dst = destination.AddComponent(type) as T;

            var fields = PropertiesAndFieldsUtils.GetAllFields(type);
            foreach (var field in fields)
            {
                if (field.IsStatic) continue;
                field.SetValue(dst, field.GetValue(original));
            }

            var props = type.GetProperties();
            foreach (var prop in props)
            {
                if (!prop.CanWrite || !prop.CanWrite || prop.Name == "name") continue;
                prop.SetValue(dst, prop.GetValue(original, null), null);
            }

#pragma warning disable CS8603 // Possible null reference return.
            return dst as T;
#pragma warning restore CS8603 // Possible null reference return.
        }

        /// <summary>
        /// Finds a child transform with the specified name in the hierarchy of the given parent transform.
        /// </summary>
        /// <remarks>
        /// Only exists since the default Unity <see cref="Transform.Find(string)"/> method only searches for direct children, 
        /// not grandchildren or deeper descendants. <br/>
        /// WARNING: This method is recursive and may have performance implications if the hierarchy is deep or has many child objects. Use with caution. <br/>
        /// You should consider caching the result of this method if you need to access the same child multiple times.
        /// </remarks>
        /// <param name="parent"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Transform? FindChildWithName(this Transform parent, string name)
        {
            // Check if the parent itself has the name we are looking for
            for (int i = 0; i < parent.childCount; i++)
            {
                // Get the child transform at index i
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                // Recursively search in the child's children
                Transform? result = FindChildWithName(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            // If we reach here, it means we didn't find the child with the specified name in this branch of the hierarchy
            return null;
        }
    }
}
