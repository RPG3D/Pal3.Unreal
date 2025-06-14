using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnrealSharp.Attributes;
using UnrealSharp.Engine;

namespace ManagedSharpDemo
{

    /// <summary>
    /// open default map(/Game/ThirdPerson/Maps/ThirdPersonMap)
    /// switch game mode to ASharpThirdGameMode
    /// run PIE
    /// </summary>

    [UClass]
    public class ASharpThirdGameMode : AGameModeBase
    {
        public ASharpThirdGameMode()
        {
            // Set the default pawn class to our custom pawn
            DefaultPawnClass = typeof(ASharpThirdPersonCharacter);
        }    

    }
}
