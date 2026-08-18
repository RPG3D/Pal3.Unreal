using UnrealSharp.Attributes;
using UnrealSharp.Engine;
using Engine.Core.Abstraction;
using Engine.Core.Implementation;
using UnityEngine;

namespace Pal3.Game;

/// <summary>
/// PIE test: spawn this actor in any level, it runs MvpValidation on BeginPlay.
/// Place in level or set as DefaultPawnClass in GameMode.
/// </summary>
[UClass]
public partial class AApl3TestActor : AActor
{
    public override void BeginPlay()
    {
        base.BeginPlay();
        Debug.Log("[Apl3Test] BeginPlay — running SmokeTest");
        IGameEntity entity = UEGameEntityFactory.Wrap(this);
        MvpValidation.SmokeTest(entity);
    }
}
