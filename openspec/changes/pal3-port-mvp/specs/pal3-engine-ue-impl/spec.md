# pal3-engine-ue-impl

The Pal3 Engine abstraction interfaces (`IGameEntity`, `ITransform`, `IMaterial`, `ITexture2D`, `ISprite`, `ServiceLocator`, etc.) are implemented against UE via UnrealSharp, supporting entity creation, transform manipulation, component attachment, and service registration.

## Requirements

### Requirement: IGameEntity wraps AActor
The `UEGameEntity : IGameEntity` class SHALL wrap an `AActor` pointer (via UnrealSharp's generated `AActor` type) and provide the full `IGameEntity` interface: Name, IsStatic, Layer, Transform, SetParent, AddComponent, GetComponent, GetComponentInChildren, GetComponentsInChildren, GetOrAddComponent, FindChild.

#### Scenario: Create an entity and set its name
- **WHEN** `UEGameEntityFactory.Create("TestEntity")` is called
- **THEN** an `AActor` is spawned in the current world with the actor label "TestEntity"
- **AND** `entity.Name` returns "TestEntity"

#### Scenario: Add a component to an entity
- **WHEN** `entity.AddComponent<UStaticMeshComponent>()` is called
- **THEN** a `UStaticMeshComponent` is created and attached to the actor
- **AND** `entity.GetComponent<UStaticMeshComponent>()` returns the same component

#### Scenario: Set entity parent
- **WHEN** `child.SetParent(parent, worldPositionStays: true)` is called
- **THEN** the child actor is attached to the parent actor (via `USceneComponent::AttachToComponent`)
- **AND** world position is preserved if `worldPositionStays` is true

### Requirement: ITransform wraps USceneComponent
The `UETransform : ITransform` class SHALL wrap a `USceneComponent` (the actor's root component) and provide Position, Rotation, LocalPosition, LocalRotation, LocalScale, Forward, Right, Up, EulerAngles properties, and Translate, LookAt, RotateAround, GetPositionAndRotation, SetPositionAndRotation, GetLocalPositionAndRotation, SetLocalPositionAndRotation methods.

#### Scenario: Read and set world position
- **WHEN** `transform.Position = new Vector3(100f, 0f, 50f)` is set
- **THEN** `transform.Position` returns `Vector3(100f, 0f, 50f)`
- **AND** the underlying actor's world location is updated

#### Scenario: LookAt rotates entity toward a target
- **WHEN** `transform.LookAt(new Vector3(10f, 0f, 0f))` is called
- **THEN** the entity's forward vector points toward (10, 0, 0)

### Requirement: ServiceLocator is functional
The `ServiceLocator` singleton SHALL provide `Register<T>()`, `Get<T>()`, `Unregister<T>()`, and `UnregisterAll()` methods backed by a `Dictionary<Type, object>`, using UnrealSharp logging for registration/unregistration messages.

#### Scenario: Register and retrieve a service
- **WHEN** `ServiceLocator.Instance.Register<IMyService>(implementation)` is called
- **THEN** `ServiceLocator.Instance.Get<IMyService>()` returns the same implementation instance
- **AND** a log message confirms the registration

#### Scenario: Get unregistered service throws
- **WHEN** `ServiceLocator.Instance.Get<INotRegistered>()` is called without prior registration
- **THEN** an `InvalidOperationException` is thrown with a descriptive message

### Requirement: UnrealPrimitivesConvertor preserves coordinate conversion accuracy
The `UnrealPrimitivesConvertor` static class SHALL produce `UnityEngine.Vector3` / `Quaternion` outputs that match the original `UnityPrimitivesConvertor`'s converted values for all 22 conversion methods, using the same constants (`GameBoxUnitToUnityUnit = 20f`, `GameBoxMv3UnitToUnityUnit = 1270f`, `GameBoxTicksPerSecond = 4800`).

#### Scenario: GameBox→Engine position conversion
- **WHEN** `new GameBoxVector3(100f, 50f, 200f).ToEnginePosition()` is called
- **THEN** the result matches the expected engine-space position:
  - X = -100 / 20 = -5.0
  - Y = 50 / 20 = 2.5
  - Z = 200 / 20 = 10.0
- **AND** the X-axis is flipped (negative sign) as in the original

### Requirement: Engine implementations work without modifying the abstraction interfaces
All Engine interface implementations in `Pal3.Engine` SHALL work against the interfaces as originally defined in Pal3.Unity. If an interface references `UnityEngine.Vector3` or `UnityEngine.Quaternion`, the `UnityEngine` compat module satisfies the type — no interface changes are required.

#### Scenario: ITransform references resolve correctly
- **WHEN** Pal3.Game code accesses `transform.Position` (declared as `UnityEngine.Vector3` in `ITransform`)
- **THEN** the returned value is a `UnityEngine.Vector3` from our compat module
- **AND** it is backed by the entity's `USceneComponent` world location
