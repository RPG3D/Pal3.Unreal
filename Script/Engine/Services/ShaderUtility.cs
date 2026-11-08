// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Engine.Services
{
    using System.Collections.Generic;

    /// <summary>
    /// Maps shader parameter names to stable integer IDs, mirroring Unity's
    /// <c>Shader.PropertyToID</c>. The returned IDs are consumed by
    /// <see cref="Engine.Core.Abstraction.IMaterial.SetFloat(int, float)"/> and friends,
    /// which accept an int propertyId.
    /// </summary>
    public static class ShaderUtility
    {
        private static readonly Dictionary<string, int> PropertyIdCache = new();

        // Reverse lookup: UE material bindings are by parameter NAME, but the ported
        // game hands us only the Unity-style int id (SetTexture/SetFloat/SetColor).
        private static readonly Dictionary<int, string> PropertyNameById = new();

        /// <summary>Resolves an id produced by <see cref="GetPropertyIdByName"/> back to its name.</summary>
        public static string? GetPropertyNameById(int id)
        {
            lock (PropertyNameById)
            {
                return PropertyNameById.TryGetValue(id, out string? name) ? name : null;
            }
        }

        /// <summary>
        /// Gets a stable integer ID for the given shader parameter name.
        /// The same name always maps to the same ID within the process lifetime.
        /// </summary>
        /// <param name="name">The name of the shader parameter.</param>
        /// <returns>A stable integer ID for the parameter.</returns>
        public static int GetPropertyIdByName(string name)
        {
            if (!PropertyIdCache.TryGetValue(name, out int id))
            {
                // Deterministic hash so IDs are stable across runs and platforms.
                unchecked
                {
                    id = 17;
                    foreach (char c in name)
                    {
                        id = (id * 31) + c;
                    }
                }

                PropertyIdCache[name] = id;
                lock (PropertyNameById)
                {
                    PropertyNameById[id] = name;
                }
            }

            return id;
        }
    }
}
