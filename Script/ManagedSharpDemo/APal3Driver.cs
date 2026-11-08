// UE-side driver actor for the managed Pal3 runtime layer.
// Bridges UE's per-frame tick into the managed world:
//   ReceiveTick -> GameEntityScriptRuntime.TickAll (coroutines + script lifecycle + Time state)
// Spawned by AScriptGameMode.BeginPlay. Deliberately free of gameplay logic -
// everything managed lives in Pal3.Game (see Stubs/GameEntityScriptRuntime.cs).

using System;
using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using Engine.Core.Abstraction;
using Engine.Core.Implementation;
using Engine.Extensions;
using Pal3.Game;

namespace ManagedSharpDemo
{
    [UClass]
    public partial class APal3Driver : AActor
    {
        public APal3Driver()
        {
            // AActor ticks by default (CanEverTick/StartWithTickEnabled) - nothing to enable.
        }

        public override void BeginPlay()
        {
            base.BeginPlay();

            // Fresh PIE session: the managed runtime survives world teardown, so drop
            // every static left over from the previous run BEFORE touching anything.
            // Skipping this made the 2nd PIE run show the old dialogue line, skip the
            // full boot (static _fullBootStarted) and then crash on a stale coroutine.
            Pal3Boot.ResetForNewSession();
            JsonUiDemo.Hide();

            // Give the entity factory a world: from here on GameEntityFactory.Create
            // spawns real UE actors (before this it returned invisible stub entities).
            Engine.Core.Implementation.UEGameEntityFactory.RegisterWorldContext(this);

            // Managed-side monitoring: crash hooks + hang watchdog. UE covers the
            // native half (CrashReportClient / Saved/Crashes); Mono has no equivalent.
            Engine.Diagnostics.DebugOverlay.Install();
            Engine.Diagnostics.DebugOverlay.Info("[boot] driver BeginPlay");
            Engine.Diagnostics.ManagedDiagnostics.Install();
            Engine.Diagnostics.ManagedDiagnostics.StartWatchdog();

            // Diagnostics: with PAL3_DEBUG_CUBE=1 drop a huge checker cube at a known
            // spot. Distinguishes "camera sees nothing" from "procedural meshes missing".
            if (Environment.GetEnvironmentVariable("PAL3_DEBUG_CUBE") == "1")
            {
                UnrealSharp.Engine.UStaticMesh? cube = UnrealSharp.CoreUObject.UObject.StaticLoadObject<UnrealSharp.Engine.UStaticMesh>(
                    "/Engine/BasicShapes/Cube.Cube");
                UnrealSharp.Engine.UStaticMeshComponent smc =
                    AddComponentByClass<UnrealSharp.Engine.UStaticMeshComponent>(false, new UnrealSharp.CoreUObject.FTransform());
                smc.SetStaticMesh(cube);
                smc.RelativeScale3D = new UnrealSharp.CoreUObject.FVector(30, 30, 30);
                SetActorLocation(new UnrealSharp.CoreUObject.FVector(0, 0, 300));
                UnrealSharp.Log.UnrealLogger.Log("Pal3", cube != null ? "[DebugCube] spawned 30x cube" : "[DebugCube] cube asset missing");
            }
            UnityEngine.Debug.Log("[APal3Driver] BeginPlay - starting managed runtime smoke probe");
            Pal3Boot.RunSmokeProbe();
            Pal3Boot.RunFullBoot();
        }

        private bool _cameraBound;
        private bool _cameraWarned;

        /// <summary>
        /// Points the UE view at the actor the ported CameraManager drives (created by
        /// Pal3Boot) and hides the leftover demo pawn. Idempotent, called from the
        /// heartbeat until it succeeds.
        /// </summary>
        private void EnsurePal3Camera(UnrealSharp.Engine.APawn? pawn)
        {
            if (_cameraBound) return;

            UnrealSharp.Engine.AActor? cameraActor = Pal3Boot.CameraActor;
            UnrealSharp.Engine.APlayerController? pc =
                UnrealSharp.Engine.UGameplayStatics.GetPlayerController(0);

            if (cameraActor == null || pc == null)
            {
                if (!_cameraWarned)
                {
                    _cameraWarned = true;
                    UnrealSharp.Log.UnrealLogger.LogWarning("Pal3",
                        $"[camera] waiting: actor={(cameraActor == null ? "null" : "ok")} pc={(pc == null ? "null" : "ok")}");
                }
                return;
            }

            if (pawn != null)
            {
                pawn.ActorHiddenInGame = true;
            }

            pc.SetViewTargetWithBlend(cameraActor, 0.0f);
            _cameraBound = true;
            UnrealSharp.Log.UnrealLogger.Log("Pal3",
                $"[camera] view target -> Pal3Camera at {cameraActor.ActorLocation}");
        }

        private float _heartbeat;

        public override void Tick(float deltaTime)
        {
            base.Tick(deltaTime);

            // Minimal start menu: story begins on an explicit Enter press or an RC
            // "start game" request (UPal3DebugOverlay.StartGame → consumed here).
            if (Pal3Boot.WaitingForMenuStart)
            {
                if (!_bootMenuShown)
                {
                    _bootMenuShown = true;
                    Engine.Diagnostics.DebugOverlay.SetBootMenuVisible(true);
                }

                bool rcStart = Engine.Diagnostics.DebugOverlay.ConsumeStartGameRequest();
                var pc = UGameplayStatics.GetPlayerController(0);
                bool enterPressed = pc != null && pc.WasInputKeyJustPressed(UnrealSharp.InputCore.Keys.Enter);

                // Click hit-test on the HUD-drawn button.
                bool clicked = false;
                if (pc != null && pc.WasInputKeyJustPressed(UnrealSharp.InputCore.Keys.LeftMouseButton))
                {
                    pc.GetMousePosition(out float mx, out float my);
                    Engine.Diagnostics.DebugOverlay.GetBootMenuButtonRect(
                        out int bx, out int by, out int bw, out int bh);
                    if (bw > 0 && mx >= bx && mx <= bx + bw && my >= by && my <= by + bh)
                    {
                        clicked = true;
                    }
                }

                if (rcStart || enterPressed || clicked)
                {
                    Engine.Diagnostics.DebugOverlay.SetBootMenuVisible(false);
                    Pal3Boot.TryStartFromMenu();
                    _bootMenuShown = false;
                }
            }

            PollKeyboard();

            // RuntimeWidgetLayout demo (RC triggered): JSON -> UMG widget in viewport.
            if (Engine.Diagnostics.DebugOverlay.ConsumeJsonDemoRequest())
            {
                JsonUiDemo.Show();
                Engine.Diagnostics.DebugOverlay.Info("[JsonUi] demo widget shown");
            }
            string jsonClick = JsonUiDemo.PollClick();
            if (!string.IsNullOrEmpty(jsonClick))
            {
                Engine.Diagnostics.DebugOverlay.Info("[JsonUi] clicked: " + jsonClick);
            }

            Engine.Diagnostics.ManagedDiagnostics.Heartbeat();
            GameEntityScriptRuntime.TickAll(deltaTime);
            UnrealSharp.SharpDemo.UPal3AudioHelper.TickAudio();
            Pal3Boot.TickSmokeProbe(deltaTime);
            Pal3Boot.TickAutoDialogue(deltaTime);

            // Runtime probe: proves the ported simulation is actually progressing
            // (entities spawned, scripts/coroutines alive) without any rendering.
            _heartbeat += deltaTime;
            if (_heartbeat >= 2f)
            {
                _heartbeat = 0f;
                Engine.Core.Implementation.UEGameEntityFactory.TryGetWorldContext();
                UnrealSharp.Engine.APawn? pawn = UnrealSharp.Engine.UGameplayStatics.GetPlayerPawn(0);
                EnsurePal3Camera(pawn);
                string camInfo = "";
                if (Pal3Boot.CameraActor is { } cam && UnrealSharp.CoreUObject.UObjectExtensions.IsValid(cam))
                {
                    var fwd = cam.ActorForwardVector;
                    double cx = (Engine.Renderer.StaticMeshRenderer.MinX + Engine.Renderer.StaticMeshRenderer.MaxX) * 0.5;
                    double cy = (Engine.Renderer.StaticMeshRenderer.MinY + Engine.Renderer.StaticMeshRenderer.MaxY) * 0.5;
                    double cz = (Engine.Renderer.StaticMeshRenderer.MinZ + Engine.Renderer.StaticMeshRenderer.MaxZ) * 0.5;
                    var p = cam.ActorLocation;
                    double dx = cx - p.X, dy = cy - p.Y, dz = cz - p.Z;
                    double len = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    double dot = len > 0 ? (fwd.X * dx + fwd.Y * dy + fwd.Z * dz) / len : 0;
                    var r = cam.ActorRotation;
                    camInfo = $" cam=({p.X:F0},{p.Y:F0},{p.Z:F0}) fwd=({fwd.X:F2},{fwd.Y:F2},{fwd.Z:F2})"
                            + $" rot=(P{r.Pitch:F0},Y{r.Yaw:F0},R{r.Roll:F0}) towardCenter={dot:F2}";
                }

                string pawnInfo = pawn != null
                    ? $"pawn=({pawn.ActorLocation.X:F0},{pawn.ActorLocation.Y:F0},{pawn.ActorLocation.Z:F0})"
                    : "pawn=NULL";

                // Safety net: the level's collision may not catch the pawn on the first
                // frames (or the spawn point is inside geometry). Keep it in the scene
                // bounds so the camera never falls out of the world.
                if (pawn != null && Engine.Renderer.StaticMeshRenderer.UploadCount > 0
                    && pawn.ActorLocation.Z < Engine.Renderer.StaticMeshRenderer.MinZ - 500)
                {
                    double cx = (Engine.Renderer.StaticMeshRenderer.MinX + Engine.Renderer.StaticMeshRenderer.MaxX) * 0.5;
                    double cy = (Engine.Renderer.StaticMeshRenderer.MinY + Engine.Renderer.StaticMeshRenderer.MaxY) * 0.5;
                    double cz = Engine.Renderer.StaticMeshRenderer.MaxZ + 200;
                    pawn.SetActorLocation(new UnrealSharp.CoreUObject.FVector(cx, cy, cz));
                    UnrealSharp.Log.UnrealLogger.Log("Pal3", $"[Pal3Driver] pawn respawned into scene at ({cx:F0},{cy:F0},{cz:F0})");
                }
                UnrealSharp.Log.UnrealLogger.Log("Pal3",
                    $"[Pal3Driver] actors={Engine.Core.Implementation.UEGameEntityFactory.CreatedCount}"
                    + $" scripts={GameEntityScriptRuntime.ActiveScriptCount}"
                    + $" coroutines={Engine.Core.Implementation.CoroutineScheduler.ActiveCount}"
                    + $" meshes={Engine.Renderer.StaticMeshRenderer.UploadCount}"
                    + $" rcalls={Engine.Renderer.StaticMeshRenderer.RenderCalls}"
                    + $" tex={Engine.Diagnostics.DebugOverlay.AssetStats}"
                    + $" audio={UnrealSharp.SharpDemo.UPal3AudioHelper.AudioStats}"
                    + $" mstat={Pal3.Game.Data.UnityMaterial.SetTextureCalls}/{Pal3.Game.Data.UnityMaterial.BindFailures}"
                    + $" reupl={Engine.Renderer.StaticMeshRenderer.ReuploadCount}"
                    + $" verts={Engine.Renderer.StaticMeshRenderer.TotalVertices}"
                    + $" tris={Engine.Renderer.StaticMeshRenderer.TotalTriangles}"
                    + " " + pawnInfo
                    + $" bounds=({Engine.Renderer.StaticMeshRenderer.MinX:F0},{Engine.Renderer.StaticMeshRenderer.MinY:F0},{Engine.Renderer.StaticMeshRenderer.MinZ:F0})"
                    + $"..({Engine.Renderer.StaticMeshRenderer.MaxX:F0},{Engine.Renderer.StaticMeshRenderer.MaxY:F0},{Engine.Renderer.StaticMeshRenderer.MaxZ:F0})"
                    + camInfo
                    + ActorPositionsInfo()
                    + $" t={UnityEngine.Time.time:F1}s");
            }
        }

        // Keyboard bridge: UE raw keys → stub PlayerInputActions. The real
        // InputSystem backend doesn't exist yet; this makes the game playable
        // (WASD/arrows move the player, Space/Enter advance dialogue).
        private UnityEngine.Vector2 _lastMovement;
        private bool _bootMenuShown;

        private void PollKeyboard()
        {
            var pc = UGameplayStatics.GetPlayerController(0);
            if (pc == null) return;

            // F1: show/hide the diagnostics side panel (hidden by default so the
            // viewport shows the game; the dialogue bar is always drawn).
            if (pc.WasInputKeyJustPressed(UnrealSharp.InputCore.Keys.F1))
            {
                Engine.Diagnostics.DebugOverlay.SetOverlayVisible(
                    !Engine.Diagnostics.DebugOverlay.IsOverlayVisible);
            }

            var actions = Pal3Boot.InputActions;
            if (actions == null) return;

            if (pc.WasInputKeyJustPressed(UnrealSharp.InputCore.Keys.SpaceBar) ||
                pc.WasInputKeyJustPressed(UnrealSharp.InputCore.Keys.Enter))
            {
                actions.Cutscene.Continue.SimulatePerformed();
            }

            float x = 0f, y = 0f;
            if (pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.W) ||
                pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.Up)) y += 1f;
            if (pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.S) ||
                pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.Down)) y -= 1f;
            if (pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.D) ||
                pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.Right)) x += 1f;
            if (pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.A) ||
                pc.IsInputKeyDown(UnrealSharp.InputCore.Keys.Left)) x -= 1f;

            var movement = new UnityEngine.Vector2(x, y);
            if (movement != _lastMovement)
            {
                actions.Gameplay.Movement.InjectValue(movement);
                if (movement == UnityEngine.Vector2.zero)
                {
                    actions.Gameplay.Movement.SimulateCanceled();
                }
                else
                {
                    actions.Gameplay.Movement.SimulatePerformed();
                }
                _lastMovement = movement;
            }
        }

        // RC actor dumps can't see factory-spawned actors (TActorIterator from the
        // HTTP thread only sees level-placed actors), so verify script placements
        // (景天 tile (6,7) / 雪见 tile (16,12)) straight from the managed scene.
        private static string ActorPositionsInfo()
        {
            try
            {
                Pal3.Game.Scene.SceneManager? sceneManager =
                    Engine.Services.ServiceLocator.Instance.Get<Pal3.Game.Scene.SceneManager>();
                Pal3.Game.Scene.GameScene? scene = sceneManager.GetCurrentScene();
                if (scene == null) return " actors=<no-scene>";

                string Describe(int actorId, string label)
                {
                    try
                    {
                        var entity = scene.GetActorGameEntity(actorId);
                        if (entity.IsNullOrDisposed()) return $" {label}=<none>";
                        var pos = entity.Transform.Position;
                        var ue = Engine.Core.Implementation.UnityUeConvert.ToUePosition(pos);
                        string native = "";
                        if (entity.NativeObject is UnrealSharp.Engine.AActor actor)
                        {
                            var loc = actor.ActorLocation;
                            native = $" nat=({loc.X:F0},{loc.Y:F0},{loc.Z:F0}) hid={(actor.ActorHiddenInGame ? 1 : 0)}";
                        }
                        return $" {label}=({pos.x:F1},{pos.y:F1},{pos.z:F1}|ue{ue.X:F0},{ue.Y:F0},{ue.Z:F0}){native}";
                    }
                    catch (Exception)
                    {
                        return $" {label}=<inactive>";
                    }
                }

                // UE child actors of an entity (mesh sections are separate child actors);
                // (hid) marks ActorHiddenInGame — quickly answers "is the model there
                // but invisible?" vs "no mesh at all".
                string DescribeChildren(int actorId)
                {
                    try
                    {
                        var entity = scene.GetActorGameEntity(actorId);
                        if (entity?.NativeObject is not UnrealSharp.Engine.AActor actor) return " kids=?";
                        var sb = new System.Text.StringBuilder(" kids=[");
                        int n = 0;
                        foreach (UnrealSharp.Engine.AActor child in actor.Children)
                        {
                            if (n++ >= 8) { sb.Append("..."); break; }
                            sb.Append(child.Name.ToString());
                            if (child.ActorHiddenInGame) sb.Append("(hid)");
                            sb.Append(';');
                        }
                        return sb.Append(']').ToString();
                    }
                    catch (Exception e)
                    {
                        return " kidsErr=" + e.GetType().Name;
                    }
                }

                // PAL3 actor ids: the cutscene drives actor 11 (景天); 5/12/13 are the
                // scene NPCs. Debug labels stay id-based (the old 0/1 labels were wrong).
                return Describe(11, "a11") + DescribeChildren(11)
                     + Describe(1, "a1") + Describe(12, "a12") + Describe(13, "a13")
                     + Describe(5, "a5") + Describe(0, "a0");
            }
            catch (Exception e)
            {
                return $" actorPosErr={e.GetType().Name}";
            }
        }
    }
}
