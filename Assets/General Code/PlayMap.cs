using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Heatmap;

public class PlayMap : ScriptableObject
{
	public Matrix<TerrainType> Terrain { get; set; }
	public Diagnostics Stats { get; set; }

	public Matrix<GameObject> BaseLayer { get; set; } = new Matrix<GameObject>();
	public List<BuildingInstance> Buildings { get; set; } = new List<BuildingInstance>();
	public List<Position> AttackedCells { get; set; } = new List<Position>();

	public Size Size => Terrain.Size;

	/// <summary>
	/// if CanBePlaced is false, other values are not set.
	/// </summary>
	public BuildingPlacementPotentialStat CanBePlaced(BuildingConfig building, Position position, int rotation)
	{
		var invalidPosition = false;
		int? minDistanceToWater = null;
		int? minDistanceToEdge = null;

		var cellPairs = Terrain.MatchCellsOnlySubMatrix(position, building.Cells.Rotate90Clockwise(rotation));

		var distanceToClosestBuilding = DistanceToClosestBuilding(building.BuildingName, cellPairs.Where(z => z.MatrixCell.HasValue).Select(z => z.MatrixCell.Value.Position).ToArray());
		if (distanceToClosestBuilding.distance == 0)
		{
			invalidPosition = true;
		}
		else
		{
			foreach (var cellPair in cellPairs)
			{
				var terrain = cellPair.MatrixCell?.Value;
				var buildingCell = cellPair.SubMatrixCell.Value;

				if (!ConfigMatchesTerrain(buildingCell, terrain, !cellPair.MatrixCell.HasValue))
				{
					invalidPosition = true;
					break;
				}

				if (buildingCell.IsPartOfBuilding)
				{
					minDistanceToEdge = MinOrNull(minDistanceToEdge, cellPair.MatrixCell.Value.EdgeDistance);
					minDistanceToWater = MinOrNull(minDistanceToWater, terrain == TerrainType.Water ? 0 : DistanceToWater(cellPair.MatrixCell.Value.Position));
				}
			}
		}


		var result = new BuildingPlacementPotentialStat
		{
			CanBePlaced = !invalidPosition,
			DistanceToEdge = minDistanceToEdge.GetValueOrDefault(),
			DistanceToWater = minDistanceToWater.GetValueOrDefault(),
			DistanceToOtherBuilding = distanceToClosestBuilding.distance,
		};

		return result;
	}

	public int? MinOrNull(int? param1, int? param2)
	{
		if (!param1.HasValue)
			return param2;
		if (!param2.HasValue)
			return param1;
		return Math.Min(param1.Value, param2.Value);
	}

	public (BuildingInstance closestBuilding, int? distance) DistanceToClosestBuilding(string buildingToExclude, params Position[] positions)
	{
		if (!Buildings.Any(z => z.Metadata.BuildingName != buildingToExclude) || !positions.Any())
			return (null, null);

		return Buildings
			.Where(z => z.Metadata.BuildingName != buildingToExclude)
			.Select(z => (z, z.BuildingCells.Min(zz => positions.Min(zzz => zzz.DistanceTo(zz.MasterPosition)))))
			.OrderBy(z => z.Item2)
			.First();
	}

	public int DistanceToEdge(params Position[] positions)
	{
		return positions.Min(z => Terrain.GetEdgeCells().Min(zz => z.DistanceTo(zz.Position)));
	}

	public int DistanceToWater(params Position[] positions)
	{
		return positions.Min(z => Terrain.Where(z => z.Value == TerrainType.Water).Min(zz => z.DistanceTo(zz.Position)));
	}

	public static bool ConfigMatchesTerrain(BuildingConfigCell buildingCell, TerrainType? terrain, bool isOutOfBounds)
	{

		return ConfigMatchesTerrain(buildingCell, TerrainHeatmapCell.GetTerrainValue(terrain), isOutOfBounds) == 1f;
	}
	public static float ConfigMatchesTerrain(BuildingConfigCell buildingCell, float? waterProbability, bool isOutOfBounds)
	{
		if (isOutOfBounds)
			return (!buildingCell.IsPartOfBuilding
				&& buildingCell.LandPlacement != CellPlacementPossibility.Required
				&& buildingCell.WaterPlacement != CellPlacementPossibility.Required)
				? 1f : 0f;

		var validity = 1f;

		if (waterProbability.HasValue)
		{
			if (buildingCell.LandPlacement == CellPlacementPossibility.Impossible)
				validity = Mathf.Min(validity, waterProbability.Value);
			else if (buildingCell.LandPlacement == CellPlacementPossibility.Required)
				validity = Mathf.Min(validity, 1 - waterProbability.Value);

			if (buildingCell.WaterPlacement == CellPlacementPossibility.Impossible)
				validity = Mathf.Min(validity, 1 - waterProbability.Value);
			else if (buildingCell.WaterPlacement == CellPlacementPossibility.Required)
				validity = Mathf.Min(validity, waterProbability.Value);
		}

		return validity;
	}
}

public struct BuildingPlacementPotentialStat
{
	public bool CanBePlaced { get; set; }
	public int? DistanceToOtherBuilding { get; set; }
	public int DistanceToWater { get; set; }
	public int DistanceToEdge { get; set; }
}

public class BuildingInstance
{
	public BuildingConfig Metadata { get; set; }
	public Submatrix<BuildingInstanceCell> Cells { get; set; }
	public int Rotations { get; set; }

	public Bounds Bounds => Cells.Bounds;
	public Position Position => Cells.Position;
	public Size Size => Cells.Size;

	public MatrixCell<BuildingInstanceCell>[] BuildingCells => Cells.Where(z => z.Value.PartOfBuilding).ToArray();
	public bool IsDead => BuildingCells.All(z => z.Value.Status == BuildingCellStatus.Dead);

	public void ChangeCellStatus(BuildingInstanceCell cell, BuildingCellStatus status)
	{
		cell.Status = status;

		// If all cell statuses are destroyed, the building becomes dead.

		if (BuildingCells.All(z => z.Value.Status == BuildingCellStatus.Destroyed))
		{
			foreach (var buildingCell in BuildingCells)
			{
				buildingCell.Value.Status = BuildingCellStatus.Dead;
			}
		}
	}

	public BuildingInstance(BuildingConfig config, Position position, int rotation)
	{
		Metadata = config;
		Rotations = rotation;

		var map = config.Cells.Rotate90Clockwise(rotation);
		Cells = new Submatrix<BuildingInstanceCell>(new Bounds(position, map.Size));
		foreach (var cell in map.ToList())
		{
			Cells[cell.Position] = new BuildingInstanceCell(cell.Value.IsPartOfBuilding);
		}
	}

}

public class BuildingInstanceCell
{
	public bool PartOfBuilding { get; set; }
	public BuildingCellStatus Status { get; set; }

	public BuildingInstanceCell(bool partOfBuilding)
	{
		PartOfBuilding = partOfBuilding;
		Status = BuildingCellStatus.Intact;
	}
}

public enum BuildingCellStatus
{
	Protected,
	Intact,
	Destroyed,
	Dead,
}