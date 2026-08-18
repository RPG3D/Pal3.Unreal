// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

// The original Unity port relied on UnityEngine's auto-generated global usings.
// Ported Pal3.Game files reference UnityEngine types (Transform, GameObject, ...)
// without per-file `using UnityEngine;`, so restore that scope here.
global using UnityEngine;

// The converter extensions (ToUnityPosition etc.) live in Engine.Extensions. Most ported
// files already import it explicitly; make it global so every file sees the aliases.
global using Engine.Extensions;
