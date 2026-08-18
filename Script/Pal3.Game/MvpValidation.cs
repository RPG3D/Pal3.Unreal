// Smoke test: validates Pal3.Core + UnityEngine + Engine wire together in UE
using Engine.Core.Abstraction;
using Engine.Core.Implementation;
using UnityEngine;

namespace Pal3.Game
{
    public static class MvpValidation
    {
        public static void SmokeTest(IGameEntity entity)
        {
            entity.Name = "MvpTest";
            ITransform t = entity.Transform;
            t.Position = new Vector3(100f, 200f, 300f);
            var pos = t.Position;
            var mag = pos.magnitude;

            t.Rotation = Quaternion.Euler(0f, 90f, 0f);
            var euler = t.Rotation.eulerAngles;

            // Verify GameBox→Engine coordinate conversion
            var gbPos = new global::Pal3.Core.Primitives.GameBoxVector3(100f, 50f, 200f);
            Vector3 enginePos = new Vector3(-gbPos.X / 20f, gbPos.Y / 20f, gbPos.Z / 20f);

            Debug.Log($"[MVP] Entity: {entity.Name}, Pos: {pos}, Euler: {euler}");
            Debug.Log($"[MVP] GameBox(100,50,200) → Engine({enginePos.x}, {enginePos.y}, {enginePos.z})");
        }
    }
}
