// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Engine.Core.Abstraction
{
    /// <summary>
    /// Extension helpers for <see cref="IManagedObject"/>.
    /// </summary>
    public static class IManagedObjectExtensions
    {
        /// <summary>
        /// Whether the managed object is null or its underlying native object has been disposed.
        /// Mirrors UnityEngine.Object's null/implicit-destroyed checks.
        /// </summary>
        public static bool IsNullOrDisposed(this IManagedObject obj)
            => obj == null || obj.IsNativeObjectDisposed;
    }
}
