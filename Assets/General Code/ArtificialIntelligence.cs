using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public static class ArtificialIntelligence
{
	// IDK what to do with this for now

	public static void RunAITest(PlayMap map)
	{
		var seed = 0;
		var player = new FakePlayer(AIDifficulty.Normal, seed);
		player.Map = map;

		{
			var newConfig = ScriptableObject.CreateInstance<BuildingConfig>();
			newConfig.BuildingName = "Boat";
			newConfig.Description = "A small boat that can only be placed on water.";
			newConfig.CellsAsString = "102.102";
			player.MyBuildingConfigs.Add(newConfig);
		}
		{
			var newConfig = ScriptableObject.CreateInstance<BuildingConfig>();
			newConfig.BuildingName = "Bridge";
			newConfig.Description = "A bridge that goes over water.";
			newConfig.CellsAsString = "120.102.120";
			player.MyBuildingConfigs.Add(newConfig);
		}
		{
			var newConfig = ScriptableObject.CreateInstance<BuildingConfig>();
			newConfig.BuildingName = "Bunker";
			newConfig.Description = "A bunker that must be placed far away from water.";
			newConfig.CellsAsString = "020.020.020.020/020.120.120.020/020.120.120.020/020.020.020.020";
			player.MyBuildingConfigs.Add(newConfig);
		}
		{
			var newConfig = ScriptableObject.CreateInstance<BuildingConfig>();
			newConfig.BuildingName = "Laser lab";
			newConfig.Description = "A lab hosting a giant laser.";
			newConfig.CellsAsString = "120.120.120.120.120";
			player.MyBuildingConfigs.Add(newConfig);
		}

		player.PlaceBuildings();

		var randomOriginalState = Random.state;
		try
		{
			Random.InitState(seed);

			foreach (var waterCell in map.Terrain.ToList())
			{
				if (Random.Range(0, 3) == 0)
				{
					map.AttackedCells.Add(waterCell.Position);
				}
			}
		}
		finally
		{
			Random.state = randomOriginalState;
		}

		var heatmap = player.GetHeatmap(map, true);

		Diagnostics.LogAll();
	}
}

/// <summary>
/// The logic of this class is subject to change accordingly of the evolving mechanics of the game. Spots to verify are marked with "TOCHANGE"
/// </summary>
public class FakePlayer
{
	public int Seed { get; set; }
	public PlayMap Map { get; set; }
	public List<BuildingConfig> MyBuildingConfigs { get; set; } = new List<BuildingConfig>();

	// Private Behavior variables

	/// <summary>
	/// TOCHANGE Logic: All the possible placements are ordered ascending by distance to another building, by distance to edge, and distance to river (subsequently, not all at the same time)
	/// MinFactor: Removes the X% bottom results from the list
	/// MaxFactor: Removes the results that are not in the bottom X%
	/// Examples: Min 0 & Max 1 for all results. Min 1 & Max 1 to always choose the single largest result. Min 0.4 & Max 0.6 for somewhat average results.
	/// </summary>
	private float BuildingPlacement_MinBuildingDistanceFactor = StaticSettings.AI.BuildingPlacement_MinBuildingDistanceFactor; // Remove the bottom X% of the results ordered by distance to another building
	private float BuildingPlacement_MaxBuildingDistanceFactor = StaticSettings.AI.BuildingPlacement_MaxBuildingDistanceFactor; // Remove 
	private float BuildingPlacement_MinEdgeDistanceFactor = StaticSettings.AI.BuildingPlacement_MinEdgeDistanceFactor;
	private float BuildingPlacement_MaxEdgeDistanceFactor = StaticSettings.AI.BuildingPlacement_MaxEdgeDistanceFactor;
	private float BuildingPlacement_MinWaterDistanceFactor = StaticSettings.AI.BuildingPlacement_MinWaterDistanceFactor;
	private float BuildingPlacement_MaxWaterDistanceFactor = StaticSettings.AI.BuildingPlacement_MaxWaterDistanceFactor;

	private bool Vision_AlwaysGetBuildingIdentity = false; // Will always know which building has been hit.

	private bool? Heatmap_KnowRiverPath = null;  // Already knows the opponent's river path. If null, tries to guess.
	private bool Heatmap_EstimateBuildingImportance = true; // Adjust heatmap values according to the estimated importance of each building.

	private float Attack_ChooseHeatmapMaxFactor = 0.8f; // Attack cells whose heatmap value is equal to or above X% of the maximum heatmap value.


	public FakePlayer(AIDifficulty difficulty, int seed)
	{
		Seed = seed;

		switch (difficulty)
		{
			case AIDifficulty.Normal:
				// Keep default values
				break;
			case AIDifficulty.Easy:
				Heatmap_KnowRiverPath = false;
				Heatmap_EstimateBuildingImportance = false;
				Attack_ChooseHeatmapMaxFactor = 0.65f;
				break;
			case AIDifficulty.Hard:
				Vision_AlwaysGetBuildingIdentity = true;
				Heatmap_KnowRiverPath = true;
				Attack_ChooseHeatmapMaxFactor = 1f;
				break;
		}
	}

	public void PlaceBuildings()
	{
		if (!MyBuildingConfigs.Any())
			throw new AIBuildingPlacementException("Cannot place buildings, as no buildings to place were provided.");

		var stats = new Diagnostics("Placing buildings");
		var randomOriginalState = Random.state;
		try
		{
			Map.Buildings.Clear();
			stats.SetValue("Seed used", Seed);
			Random.InitState(Seed);

			foreach (var buildingConfig in MyBuildingConfigs)
			{
				var buildingStat = stats.StartSubstat(buildingConfig.BuildingName);
				var potentialPlacements = new List<(Position, int, BuildingPlacementPotentialStat)>();
				var positionConfig = buildingConfig.Cells;

				foreach (var rotation in buildingConfig.GetDifferentRotations())
				{
					var rotatedConfig = positionConfig.Rotate90Clockwise(rotation);

					foreach (var configPosition in Matrix.PositionsWhereBoundsIncludeSubmatrix(new Bounds(0,0,Map.Size), rotatedConfig.Size))
					{
						var stat = Map.CanBePlaced(buildingConfig, configPosition, rotation);
						if (stat.CanBePlaced)
							potentialPlacements.Add((configPosition, rotation, stat));
						buildingStat.AddToValue("Placements tested");
					}
				}

				if (!potentialPlacements.Any())
				{
					throw new AIBuildingPlacementException($"Tried to place the {buildingConfig.BuildingName} but there was no available spot.");
				}

				// TOCHANGE: Filter according to behavior settings (to change if there can be strategy associated to some buildings)

				potentialPlacements = FilterBetweenIndexFactors(potentialPlacements.OrderBy(z => z.Item3.DistanceToEdge), BuildingPlacement_MinEdgeDistanceFactor, BuildingPlacement_MaxEdgeDistanceFactor);
				potentialPlacements = FilterBetweenIndexFactors(potentialPlacements.OrderBy(z => z.Item3.DistanceToWater), BuildingPlacement_MinWaterDistanceFactor, BuildingPlacement_MaxWaterDistanceFactor);
				if (Map.Buildings.Any())
					potentialPlacements = FilterBetweenIndexFactors(potentialPlacements.OrderBy(z => z.Item3.DistanceToOtherBuilding.Value), BuildingPlacement_MinBuildingDistanceFactor, BuildingPlacement_MaxBuildingDistanceFactor);

				buildingStat.SetValue("Valid placements", potentialPlacements.Count());

				var chosenPlacement = potentialPlacements.ElementAt(Random.Range(0, potentialPlacements.Count()));

				Map.Buildings.Add(new BuildingInstance(buildingConfig, chosenPlacement.Item1, chosenPlacement.Item2));
				buildingStat.StopTimer();
			}

		}
		catch (Exception ex)
		{
			stats.LogNow(ex);
			throw;
		}
		finally
		{
			Random.state = randomOriginalState;
		}

		var placementsMap = Map.Terrain.ToConsoleString((cell) => Map.Buildings.SelectMany(z => z.BuildingCells).Any(z => z.MasterPosition == cell.Position) ? " O " : (cell.Value == TerrainType.Ground ? "  .  " : "     "), null);

		stats.AddComment("Preview of the building positions (% is a building cell) :\r\n" + placementsMap);
		stats.LogLater();
	}

	public Heatmap GetHeatmap(PlayMap map, bool isOpponentMap)
	{
		var heatmap = new Heatmap(map, isOpponentMap);
		heatmap.GetVision(Vision_AlwaysGetBuildingIdentity);

		if (Heatmap_KnowRiverPath == true)
		{
			heatmap.SetTerrainHeatmapToTruth();
		}
		else
		{
			var riverGuesser = new RiverGuesser();
			if (Heatmap_KnowRiverPath == false)
				riverGuesser.WavesToUse = 0;

			heatmap.GetTerrainHeatmap(riverGuesser);
		}
		// If at least one building cell has been found, and it's not dead, we focus the heatmap on the not-dead-yet cells.
		if (heatmap.Vision.Any(z => z.Value.HasBuilding == true && z.Value.BuildingStatus != BuildingCellStatus.Dead))
		{
			heatmap.GetBuildingHeatmap(true);
		}
		else
		{
			heatmap.GetBuildingHeatmap(false);
		}


		return heatmap;
	}

	private List<T> FilterBetweenIndexFactors<T>(IEnumerable<T> list, float minIndexFactor, float maxIndexFactor)
	{
		var minIndex = Mathf.FloorToInt(Mathf.Clamp(minIndexFactor * (list.Count() - 1), 0, list.Count() - 1));
		var maxIndex = Mathf.FloorToInt(Mathf.Clamp(maxIndexFactor * (list.Count() - 1), minIndex, list.Count() - 1));

		return list.Skip(minIndex).Take(maxIndex - minIndex + 1).ToList();
	}
}

public enum AIDifficulty
{
	Easy,
	Normal,
	Hard,
}

public class AIBuildingPlacementException : Exception
{
	public AIBuildingPlacementException(string message) : base(message)
	{
	}
}

public class Heatmap
{
	public Size Size => SourceMap.Size;
	public bool IsOpponentMap { get; private set; }

	public PlayMap SourceMap { get; private set; }
	/// <summary>
	/// Cells will be null if they are unknown.
	/// </summary>
	public Matrix<MapCellVisionInfo> Vision { get; private set; }
	public Matrix<TerrainHeatmapCell> Terrain { get; private set; }
	public Matrix<BuildingHeatmapCell> Buildings { get; private set; }

	public Heatmap(PlayMap map, bool isOpponentMap)
	{
		IsOpponentMap = isOpponentMap;
		SourceMap = map;
	}

	public void GetVision(bool cheatBuildingIdentity)
	{
		var stat = new Diagnostics("Vision");
		Vision = new Matrix<MapCellVisionInfo>(Size, (p) => new MapCellVisionInfo());

		// TOCHANGE: We have vision where hits have been made.
		var cellsWithVision = SourceMap.AttackedCells.Distinct().ToArray();
		// TOCHANGE: If the building is destroyed, we reveal all its cells.
		cellsWithVision = cellsWithVision.Union(SourceMap.Buildings.Where(z => z.IsDead).SelectMany(z => z.BuildingCells).Select(z => z.MasterPosition)).ToArray();


		foreach (var pos in cellsWithVision)
		{
			stat.AddToValue("Cells with vision");
			Vision[pos].Terrain = SourceMap.Terrain[pos];
			Vision[pos].HasBuilding = false; // Will be updated in the loop below
		}

		foreach (var building in SourceMap.Buildings)
		{
			// TOCHANGE: The only way to get the building's identity is to have destroyed it completely. Maybe there could be other ways to get its identity.
			var haveBuildingIdentity = (cheatBuildingIdentity && IsOpponentMap) || building.IsDead || false;
			if (haveBuildingIdentity)
				stat.AddComment("Got the identity of " + building.Metadata.BuildingName);

			// Apply changes on every vision cell where the building is placed
			foreach (var bc in building.BuildingCells.Where(z => cellsWithVision.Contains(z.MasterPosition)))
			{
				stat.AddToValue("Cells with vision on a building");
				Vision[bc.MasterPosition].HasBuilding = true;
				Vision[bc.MasterPosition].BuildingStatus = bc.Value.Status;

				if (haveBuildingIdentity)
					Vision[bc.MasterPosition].BuildingIdentity = building.Metadata;
			}
		}

		var visionString = Vision.ToConsoleString((cell) =>
		{
			if (cell.Value.BuildingStatus.HasValue || cell.Value.BuildingIdentity != null)
				return " % ";
			else if (cell.Value.Terrain.HasValue)
				return cell.Value.Terrain.Value == TerrainType.Ground ? " O " : "  .  ";
			else
				return "     ";
		}, null);

		stat.AddComment("Preview of the cells that have vision:\r\n" + visionString);
		stat.LogLater();
	}

	public void GetTerrainHeatmap(RiverGuesser riverGuesser)
	{
		Terrain = new Matrix<TerrainHeatmapCell>(Size, (p) => new TerrainHeatmapCell());

		foreach (var visionOnTerrain in Vision.Where(z => z.Value.Terrain.HasValue))
		{
			Terrain[visionOnTerrain.Position].SetObvious(visionOnTerrain.Value.Terrain.Value);
		}

		if (riverGuesser != null)
		{
			riverGuesser.ApplyGuess(Terrain);
		}
	}

	public void SetTerrainHeatmapToTruth()
	{
		Terrain = new Matrix<TerrainHeatmapCell>(Size, (p) => new TerrainHeatmapCell() { FoundProbability = true, Probability = TerrainHeatmapCell.GetTerrainValue(SourceMap.Terrain[p]).Value });
	}

	public void GetBuildingHeatmap(bool focusOnDestroyedCells)
	{
		var stats = new Diagnostics("Buildings heatmap");
		stats.AddComment("Focus on destroyed cells: " + focusOnDestroyedCells.ToString());
		Buildings = new Matrix<BuildingHeatmapCell>(Size, (p) => new BuildingHeatmapCell());

		if (Terrain == null)
			GetTerrainHeatmap(null);

		// For each building, add its probability everywhere
		foreach (var building in SourceMap.Buildings)
		{
			var buildingStat = stats.StartSubstat(building.Metadata.BuildingName);

			var buildingConfig = building.Metadata;
			// If there is at least one cell containing the building's identity, the only possible placements will have to include these cells.
			var touchedBuildingCells = Vision.Where(z => z.Value.BuildingIdentity != null && z.Value.BuildingIdentity.BuildingName == buildingConfig.BuildingName).Select(z => z.Position);
			var hasIdentityOnMap = touchedBuildingCells.Any();

			var searchingBounds = hasIdentityOnMap ? Matrix.GetBoundsFromPositions(touchedBuildingCells) : new Bounds(0, 0, SourceMap.Size);

			foreach (var rotation in buildingConfig.GetDifferentRotations())
			{
				var rotatedConfig = buildingConfig.Cells.Rotate90Clockwise(rotation);

				foreach (var configPosition in Matrix.PositionsWhereBoundsIncludeSubmatrix(searchingBounds, rotatedConfig.Size))
				{
					buildingStat.AddToValue("Placements tested");
					var matchedCells = Terrain.MatchCellsOnlySubMatrix(configPosition, rotatedConfig);

					var chanceOfInvalidPosition = 0f;
					bool touchedAttackedCell = false;

					foreach (var cellMatch in matchedCells)
					{
						var position = cellMatch.SubMatrixCell.Position + configPosition;

						// If we're looking at a cell inside the map's bounds, look for absolute truths (no building there, another building there, etc.)
						if (cellMatch.MatrixCell.HasValue)
						{
							var vision = Vision[position];

							if (cellMatch.SubMatrixCell.Value.IsPartOfBuilding)
							{
								// If it's placed on a cell that is known to have nothing
								if (vision.HasBuilding == false)
								{
									chanceOfInvalidPosition = 1f; break;
								}
								// If it's placed on a cell that is known to have a building
								if (vision.HasBuilding == true)
								{
									touchedAttackedCell = true;

									// If it's placed on a cell that has another building on it
									if (vision.BuildingIdentity != null && vision.BuildingIdentity.BuildingName != buildingConfig.BuildingName)
									{
										chanceOfInvalidPosition = 1f; break;
									}
								}
							}
							// if it's not a building cell
							else
							{
								// If the building doesn't fit on one of its identity cells
								if (hasIdentityOnMap && touchedBuildingCells.Any(z => z == position))
								{
									chanceOfInvalidPosition = 1f; break;
								}
							}
						}
						var terrainProbability = cellMatch.MatrixCell?.Value.FoundProbability == true ? cellMatch.MatrixCell.Value.Value.Probability : default(float?);
						var invalidTerrainRatio = 1 - PlayMap.ConfigMatchesTerrain(cellMatch.SubMatrixCell.Value, terrainProbability, !cellMatch.MatrixCell.HasValue);
						chanceOfInvalidPosition = Mathf.Max(chanceOfInvalidPosition, invalidTerrainRatio);

						if (chanceOfInvalidPosition == 1f)
							break;

					}

					if (chanceOfInvalidPosition != 1f && (!focusOnDestroyedCells || touchedAttackedCell))
					{
						buildingStat.AddToValue("Valid placements");
						foreach (var position in matchedCells.Where(z => z.SubMatrixCell.Value.IsPartOfBuilding).Select(z => z.MatrixCell.Value.Position))
						{
							Buildings[position].Add(buildingConfig.BuildingName, 1 - chanceOfInvalidPosition);
						}
					}
				}
			}

			buildingStat.AddComment("Preview of the heatmap for this building only:\r\n" + Buildings.ToConsoleString((cell) => Math.Round(cell.Value.GetTotal(buildingConfig.BuildingName) * 100, 0).ToString(), ";"));
			buildingStat.StopTimer();
		}

		stats.AddComment("Preview of the heatmap for all buildings:\r\n" + Buildings.ToConsoleString((cell) => Math.Round(cell.Value.GetTotal() * 100, 0).ToString(), ";"));
		stats.LogLater();
	}

	public static float CombineProbabilities(float prob1, float prob2)
	{
		return 1f - ((1f - prob1) * (1f - prob2));
	}

	public class MapCellVisionInfo
	{
		public TerrainType? Terrain { get; set; } = null;

		public bool? HasBuilding { get; set; } = null;
		public BuildingCellStatus? BuildingStatus { get; set; } = null;
		public BuildingConfig BuildingIdentity { get; set; } = null;
	}
	public class BuildingHeatmapCell
	{
		/// <summary>
		/// TOCHANGE: Key = Building name, Value = Score (number of possible building placements that use this cell)
		/// </summary>
		public Dictionary<string, float> Scores { get; set; } = new Dictionary<string, float>();



		/// <summary>
		/// Adds one more possible placement to this cell
		/// </summary>
		public void Add(string key, float value = 1)
		{
			if (value != 0)
			{
				if (!Scores.ContainsKey(key))
					Scores.Add(key, value);
				else
					Scores[key] += value;
			}
		}

		public float GetTotal(string key = null)
		{
			var values = Scores.Where(z => key != null ? z.Key == key : true).Select(z => z.Value);
			if (values.Any())
				return values.Sum();
			else
				return 0f;
		}

		public class BuildingHeatmapScore
		{
			public string BuildingName { get; set; }

		}
	}
	public class TerrainHeatmapCell
	{
		private const float GroundProbabilityValue = 0f;
		private const float WaterProbabilityValue = 1f;
		private const float DefaultProbability = GroundProbabilityValue;
		public static float? GetTerrainValue(TerrainType? terrain) { return terrain.HasValue ? (terrain == TerrainType.Water ? WaterProbabilityValue : GroundProbabilityValue) : default(float?); }

		/// <summary>
		/// From 0 (Ground) to 1 (Water)
		/// </summary>
		public float Probability { get; set; } = DefaultProbability;
		public bool FoundProbability { get; set; } = false;

		/// <summary>
		/// Returns true if values changed
		/// </summary>
		public bool SetObvious(TerrainType terrain)
		{
			var changed = !FoundProbability;
			var probabilityToSet = GetTerrainValue(terrain).Value;
			if (Probability != probabilityToSet)
				changed = true;

			FoundProbability = true;
			Probability = probabilityToSet;
			return changed;
		}
		/// <summary>
		/// Sets the probability to the one provided, only if it is higher than the current one.
		/// </summary>
		public void AddProbability(float probability, float certainty = 1f)
		{
			var valueToAdd = Mathf.Lerp(GroundProbabilityValue, WaterProbabilityValue, probability) * certainty;
			Probability = Mathf.Max(Probability, valueToAdd);
			FoundProbability = true;
		}
		/// <summary>
		/// Resets to default value.
		/// </summary>
		public void ResetProbability()
		{
			Probability = DefaultProbability;
			FoundProbability = false;
		}

		public bool IsWaterObvious => FoundProbability && Probability == WaterProbabilityValue;
		public bool IsGroundObvious => FoundProbability && Probability == GroundProbabilityValue;
		public bool IsTerrainObvious => IsWaterObvious || IsGroundObvious;
		public TerrainType? ObviousTerrain
		{
			get
			{
				if (IsWaterObvious)
					return TerrainType.Water;
				if (IsGroundObvious)
					return TerrainType.Ground;
				return null;
			}
		}
	}
}

