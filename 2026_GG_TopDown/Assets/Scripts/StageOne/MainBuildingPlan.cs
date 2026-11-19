using UnityEngine;

internal static class MainBuildingPlan
{
    public static Vector2 World(Vector2Int cell) => new Vector2(cell.x, -cell.y);
    public static Vector2Int Cell(Vector2 world) =>
        new Vector2Int(Mathf.RoundToInt(world.x), Mathf.RoundToInt(-world.y));
}
