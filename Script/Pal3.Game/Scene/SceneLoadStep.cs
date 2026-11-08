namespace Pal3.Game.Scene;

// Progress estimates use fixed bands for the major stages, with completed
// batches advancing the fraction within each band.
internal readonly record struct SceneLoadStep(string Phase, float Progress);
