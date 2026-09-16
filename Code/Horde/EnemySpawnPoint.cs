namespace DrunkenBarFight;

/// <summary>
/// Pure marker component - drop this on an empty GameObject, drag it wherever you want enemies to
/// spawn from, and HordeSpawner will pick a random one of these each time it spawns an enemy instead
/// of its default random-point-on-a-ring placement. No fields, no logic: the GameObject's own
/// WorldPosition (which you set by moving it in the editor) is the only thing that matters.
///
/// If no EnemySpawnPoint exists anywhere in the scene, HordeSpawner falls back to its original ring
/// spawn automatically - this is purely additive, not a required setup step.
/// </summary>
public class EnemySpawnPoint : Component
{
}
