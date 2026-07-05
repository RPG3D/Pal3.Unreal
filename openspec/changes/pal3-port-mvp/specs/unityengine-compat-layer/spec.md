# unityengine-compat-layer

A `UnityEngine` namespace shim provides Unity-compatible APIs (Vector3, Quaternion, Debug, Time, Mathf, CoroutineCompatibility, etc.) backed by UnrealSharp / UE types, sufficient for Pal3's Engine interfaces and Game code to compile against.

## Requirements

### Requirement: Unity value types are provided as thin wrappers
The `UnityEngine` module SHALL provide `Vector3`, `Quaternion`, `Vector2`, `Color`, `Color32`, and `Matrix4x4` structs with the same public API surface as their Unity counterparts (fields, properties, operators, static methods). Each SHALL have implicit conversions to/from the corresponding UnrealSharp generated type (`FVector`, `FQuat`, `FVector2D`, `FLinearColor`, `FColor`, `FMatrix`).

#### Scenario: Vector3 ↔ FVector conversion
- **WHEN** `new UnityEngine.Vector3(1f, 2f, 3f)` is created
- **THEN** implicit cast to `FVector` yields `FVector(1.0, 2.0, 3.0)` (float→double promotion)
- **AND** implicit cast from `FVector(1.0, 2.0, 3.0)` to `UnityEngine.Vector3` yields `Vector3(1f, 2f, 3f)` (double→float truncation)

#### Scenario: Vector3 arithmetic operators work
- **WHEN** two `UnityEngine.Vector3` values are added, subtracted, multiplied by scalar, or divided by scalar
- **THEN** the result matches the equivalent Unity `Vector3` operation (float arithmetic)

#### Scenario: Quaternion ↔ FQuat conversion
- **WHEN** `UnityEngine.Quaternion.Euler(0f, 90f, 0f)` is called
- **THEN** it produces a quaternion representing a 90-degree Y-axis rotation
- **AND** implicit conversion to `FQuat` preserves the rotation

### Requirement: Static Unity utility classes are backed by UnrealSharp/UE
The `UnityEngine` module SHALL provide `Debug`, `Time`, `Mathf`, `PlayerPrefs`, and `Application` static classes whose public members map to corresponding UnrealSharp or UE C++ functionality.

#### Scenario: Debug.Log maps to UnrealSharp logging
- **WHEN** `UnityEngine.Debug.Log("test message")` is called at runtime
- **THEN** the message appears in the UE output log with appropriate log level

#### Scenario: Time.deltaTime returns frame delta
- **WHEN** `UnityEngine.Time.deltaTime` is read during a game tick
- **THEN** it returns the time in seconds since the last frame, matching `UWorld::GetDeltaSeconds()`

#### Scenario: Mathf functions delegate to System.MathF
- **WHEN** `UnityEngine.Mathf.Clamp(value, min, max)` is called
- **THEN** it returns the same result as `Math.Clamp(value, min, max)`
- **AND** `Mathf.Deg2Rad`, `Mathf.Rad2Deg`, `Mathf.Epsilon`, and `Mathf.Infinity` constants match Unity's values

### Requirement: CoroutineCompatibility provides async equivalents
The `UnityEngine.CoroutineCompatibility` static class SHALL provide `WaitForSeconds`, `WaitUntil`, and `NextFrame` methods that return `Task` and use `ConfigureWithUnrealContext()` to ensure continuation on the game thread.

#### Scenario: WaitForSeconds delays execution
- **WHEN** `await CoroutineCompatibility.WaitForSeconds(1f)` is called in an async method
- **THEN** execution resumes after approximately 1 second on the UE game thread

#### Scenario: WaitUntil polls a condition
- **WHEN** `await CoroutineCompatibility.WaitUntil(() => someFlag)` is called
- **THEN** execution resumes on the next frame after `someFlag` becomes true

#### Scenario: NextFrame yields one frame
- **WHEN** `await CoroutineCompatibility.NextFrame()` is called
- **THEN** execution resumes on the next game tick (equivalent to `yield return null`)

### Requirement: Only Pal3-used API surface is implemented
The `UnityEngine` module SHALL implement only the Unity API members that Pal3's Engine interfaces or Pal3.Game code actually reference. Unused Unity API members (e.g., `Physics.Raycast` signature variants Pal3 doesn't call) MAY be left unimplemented (compilation errors reveal gaps for incremental addition).

#### Scenario: Missing API surfaces as compile errors
- **WHEN** Pal3.Game code references a Unity API not yet implemented in `UnityEngine`
- **THEN** the C# compiler reports a clear missing-member error
- **AND** the error guides incremental addition of that API member to the `UnityEngine` module
