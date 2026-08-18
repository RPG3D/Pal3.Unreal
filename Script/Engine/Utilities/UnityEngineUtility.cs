// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Engine.Utilities
{
    using UnityEngine;

    /// <summary>
    /// Device/collision helpers ported from UnityEngineUtility. Platform detection
    /// should be wired to UnrealSharp's platform APIs (UPlatformMisc / UDeviceProfile)
    /// when the compat layer lands; these defaults target desktop.
    /// </summary>
    public static class UnityEngineUtility
    {
        /// <summary>
        /// Whether the current device is a desktop device.
        /// </summary>
        public static bool IsDesktopDevice() => true;

        /// <summary>
        /// Whether the current device is a handheld (mobile) device.
        /// </summary>
        public static bool IsHandheldDevice() => false;

        /// <summary>
        /// Whether the current device is a legacy mobile device (low-end GPU/memory).
        /// </summary>
        public static bool IsLegacyMobileDevice() => false;

        /// <summary>
        /// Whether the given point is inside the given collider.
        /// </summary>
        /// <param name="collider">The collider to test against.</param>
        /// <param name="point">The world-space point to test.</param>
        /// <param name="tolerance">Optional extra tolerance added to the test.</param>
        /// <returns>True if the point is inside the collider.</returns>
        public static bool IsPointInsideCollider(Collider collider, Vector3 point, float tolerance = 0f)
        {
            // Collider bounds are not wired to UE yet; conservative default.
            return false;
        }
    }
}
