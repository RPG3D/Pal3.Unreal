using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Shader
    {
        public string name = "";
        public static Shader? Find(string n) => null;

        private static readonly object PropertyIdsLock = new();
        private static readonly Dictionary<string, int> PropertyIds = new(StringComparer.Ordinal);
        private static readonly List<string> PropertyNames = new();

        public static int PropertyToID(string name)
        {
            lock (PropertyIdsLock)
            {
                if (PropertyIds.TryGetValue(name, out int id)) return id;

                // Reserve 0 for the material wrapper's default texture slot.
                id = PropertyNames.Count + 1;
                PropertyIds.Add(name, id);
                PropertyNames.Add(name);
                return id;
            }
        }

        // UE material parameters are bound by name.
        public static string? GetPropertyName(int propertyId)
        {
            lock (PropertyIdsLock)
            {
                return propertyId > 0 && propertyId <= PropertyNames.Count
                    ? PropertyNames[propertyId - 1]
                    : null;
            }
        }
    }
}
