using Godot;

public partial class TileGrid : Node3D
{
	[Export] public PackedScene TileScene;
	[Export] public int GridSize = 10;
	[Export] public float TileSpacing = 0.68f;
	[Export] public int FloorCount = 5;
	[Export] public float FloorGap = 3.5f;

	public void Activate()
	{
		foreach (var child in GetChildren())
			if (child is FallingTile tile)
				tile.Activate();
	}

	public override void _Ready()
	{
		if (TileScene is null)
		{
			return;
		}

		float offset = -(GridSize - 1) * TileSpacing / 2f;

		for (int floor = 0; floor < FloorCount; floor++)
		{
			float y = -floor * FloorGap;
			for (int row = 0; row < GridSize; row++)
			{
				for (int col = 0; col < GridSize; col++)
				{
					var tile = TileScene.Instantiate<Node3D>();
					tile.Position = new Vector3(
						offset + col * TileSpacing,
						y,
						offset + row * TileSpacing
					);
					AddChild(tile);
				}
			}
		}
	}
}
