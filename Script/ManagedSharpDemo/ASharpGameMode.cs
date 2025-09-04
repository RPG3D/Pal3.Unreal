using System;
using System.Collections.Generic;
using UnrealSharp.Attributes;
using UnrealSharp.Engine;
namespace ManagedSharpDemo
{
    [UClass]
    public class ASharpGameMode : AGameModeBase
    {
        public ASharpGameMode()
        {
            DefaultPawnClass = typeof(ASharpCharacter);
        }
    }
}
