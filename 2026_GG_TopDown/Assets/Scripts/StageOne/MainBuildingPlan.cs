using UnityEngine;

internal sealed class MainBuildingPlan
{
    public const int Width = 40;
    public const int Height = 23;
    private readonly bool[,] walls = new bool[Width, Height];

    public MainBuildingPlan()
    {
        Horizontal(0, 39, 0);
        Horizontal(0, 39, 22);
        Vertical(0, 0, 22);
        Vertical(39, 0, 22);

        Horizontal(1, 38, 6, 7, 13, 23, 31);
        Vertical(8, 1, 5);
        Vertical(16, 1, 5);
        Vertical(27, 1, 5);

        Vertical(6, 7, 16, 9, 14);
        Horizontal(1, 5, 11);
        Horizontal(1, 16, 17, 8);
        Vertical(16, 18, 21, 19);

        Box(10, 9, 16, 14);
        Box(19, 9, 25, 14);

        Horizontal(30, 38, 9);
        Vertical(30, 10, 21, 11, 16);
        Horizontal(31, 38, 14);
        Horizontal(22, 29, 18, 25);
        Vertical(22, 19, 21);
    }

    public bool Walkable(Vector2Int cell) => cell.x >= 0 && cell.x < Width && cell.y >= 0
        && cell.y < Height && !walls[cell.x, cell.y];

    public static Vector2 World(Vector2Int cell) => new Vector2(cell.x, -cell.y);
    public static Vector2Int Cell(Vector2 world) => new Vector2Int(Mathf.RoundToInt(world.x), Mathf.RoundToInt(-world.y));

    private void Box(int left, int top, int right, int bottom)
    {
        for (int y = top; y <= bottom; y++) Horizontal(left, right, y);
    }

    private void Horizontal(int left, int right, int y, params int[] doors)
    {
        for (int x = left; x <= right; x++) walls[x, y] = System.Array.IndexOf(doors, x) < 0;
    }

    private void Vertical(int x, int top, int bottom, params int[] doors)
    {
        for (int y = top; y <= bottom; y++) walls[x, y] = System.Array.IndexOf(doors, y) < 0;
    }
}
