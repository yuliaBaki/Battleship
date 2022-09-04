using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Heatmap;

public class RiverGuesser
{
	public int WavesToUse { get; set; } = 4;
	public float Wave4_PathCertaintyThreshold { get; set; } = 0.005f;

	private int MinimumDistanceFromEdge { get; set; }
	private int MinimumDistanceBetweenEntranceAndExit { get; set; }
	private Matrix<TerrainHeatmapCell> Heatmap { get; set; }
	private RiverGuesserStat Stats { get; set; }

	public void ApplyGuess(Matrix<TerrainHeatmapCell> heatmap)
	{
		Stats = new RiverGuesserStat("River Guesser");
		try
		{
			Stats.SetValue("Configured number of waves to use", WavesToUse);
			Heatmap = heatmap;
			var riverGenerator = new RiverGenerator(Heatmap.Size, 0);
			MinimumDistanceFromEdge = riverGenerator.MinimumDistanceFromEdge;
			MinimumDistanceBetweenEntranceAndExit = riverGenerator.MinimumDistanceBetweenEntranceAndExit;

			bool changeMade;
			do
			{
				changeMade = false;

				if (MinimumDistanceFromEdge > 0)
				{
					while (WavesToUse >= 1 && ManageEdge())
						changeMade = true;
				}

				while (!changeMade && WavesToUse >= 2 && SpreadObviousness())
					changeMade = true;

				if (changeMade)
					continue;

				if (WavesToUse >= 3)
					changeMade |= SetGroundWhereTooFarFromWater();

				if (changeMade)
					continue;

				if (WavesToUse >= 4)
				{
					changeMade |= SpreadPartialCertainties();
				}
			}
			while (changeMade);

			ApplyOverallProbabilitiesOnBlank();
			PrintHeatmapInStats();
		}
		catch (Exception ex)
		{
			Stats.LogNow(ex);
			throw;
		}

		Stats.LogLater();
	}


	/// <summary>
	/// Wave 1 : Uses the RiverGenerator's parameters to determine cells on the edge of the map
	/// </summary>
	private bool ManageEdge()
	{
		var wave = Stats.StartWave("Wave 1");
		bool madeAChange = false;

		var fatEdgeCells = Heatmap.Where(z => z.EdgeDistance < MinimumDistanceFromEdge).ToList();
		var edgeCells = Heatmap.GetEdgeCells().ToList();
		var cornerCells = edgeCells.Where(z => z.IsOnCorner).ToList();

		// Remove corners from the possibilities
		foreach (var cornerCell in cornerCells)
		{
			foreach (var nearCornerCell in GetAllFatEdgeNeighborsFromCorner(cornerCell, MinimumDistanceFromEdge).Where(z => !z.Value.IsGroundObvious))
			{
				if (nearCornerCell.Value.SetObvious(TerrainType.Ground))
				{
					madeAChange = true;
					wave.FoundCell(TerrainType.Ground);
				}
			}
		}

		// For all cells with known terrain inside the fat edge, apply the same value to its perpendicular neighbors.
		foreach (var cellInFatEdge in fatEdgeCells.Where(z => z.Value.IsTerrainObvious))
		{
			// If a water cell is inside the fat edge, its range extends 1 cell outside of the fat edge.
			var distanceFromEdgeToApplySameValueAsThisCell = cellInFatEdge.Value.IsWaterObvious ? MinimumDistanceFromEdge + 1 : MinimumDistanceFromEdge;
			foreach (var neighbor in GetAllFatEdgeNeighbors(cellInFatEdge, distanceFromEdgeToApplySameValueAsThisCell).Where(z => z.Value.ObviousTerrain != cellInFatEdge.Value.ObviousTerrain))
			{
				if (neighbor.Value.SetObvious(cellInFatEdge.Value.ObviousTerrain.Value))
				{
					madeAChange = true;
					wave.FoundCell(cellInFatEdge.Value.ObviousTerrain.Value);
				}
			}
		}

		// Are there water cells on the edge? Those are entrance/exit cells. Most of the other ones are certainly ground cells.

		var endCells = edgeCells.Where(z => z.Value.IsWaterObvious);
		if (endCells.Count() == 0)
		{
			// Guessed nothing
		}
		else if (endCells.Count() == 1) // Found 1 of 2 ends. Can eliminate all fat edge cells that are too close to that end
		{
			foreach (var edgeCell in edgeCells.Where(z => z.Position != endCells.Single().Position && z.Position.DistanceTo(endCells.Single().Position) < MinimumDistanceBetweenEntranceAndExit))
			{
				foreach (var neighbor in GetAllFatEdgeNeighbors(edgeCell, MinimumDistanceFromEdge).Where(z => !z.Value.IsGroundObvious))
				{
					if (neighbor.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
			}
		}
		else if (endCells.Count() == 2) // Found both ends. Eliminate all other fat edge cells.
		{
			foreach (var edgeCell in edgeCells.Where(z => !endCells.Any(zz => zz.Position == z.Position)))
			{
				foreach (var neighbor in GetAllFatEdgeNeighbors(edgeCell, MinimumDistanceFromEdge).Where(z => !z.Value.IsGroundObvious))
				{
					madeAChange |= neighbor.Value.SetObvious(TerrainType.Ground);
					if (neighbor.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
			}
		}

		wave.StopTimer();
		return madeAChange;
	}

	/// <summary>
	/// Wave 2 : Random bits of logic that can 100% determine if a cell is ground or water.
	/// </summary>
	private bool SpreadObviousness()
	{
		var wave = Stats.StartWave("Wave 2");
		var madeAChange = false;

		foreach (var cell in Heatmap.ToList())
		{
			var immediateNeighbors = cell.GetImmediateNeighbors(OutOfBoundsRule.Ignore);

			if (cell.Value.IsTerrainObvious)
			{
				// Logics for certain water cells
				if (cell.Value.IsWaterObvious)
				{
					float goalWaterNeighbors = cell.IsOnEdge && MinimumDistanceFromEdge > 0 ? 1 : 2;

					// Wave 2.1: If goal water neighbors count is reached, other neighbors are ground.
					if (immediateNeighbors.Count(z => z.Value.IsWaterObvious) == goalWaterNeighbors)
					{
						foreach (var neighbor in immediateNeighbors.Where(z => !z.Value.IsTerrainObvious))
						{
							if (neighbor.Value.SetObvious(TerrainType.Ground))
							{
								madeAChange = true;
								wave.FoundCell(TerrainType.Ground);
							}
						}
					}
					// Wave 2.2: If a water cell is surrounded by 2 ground cells, the other neighbors are water.
					else if (immediateNeighbors.Count(z => z.Value.IsGroundObvious) == 2)
					{
						foreach (var neighbor in immediateNeighbors.Where(z => !z.Value.IsTerrainObvious))
						{
							if (neighbor.Value.SetObvious(TerrainType.Water))
							{
								madeAChange = true;
								wave.FoundCell(TerrainType.Water);
							}
						}
					}
				}
			}
			else
			{
				// Wave 2.3: If an uncertain cell is surrounded by 3+ ground cells, it is also a ground cell.
				if (immediateNeighbors.Count(z => z.Value.IsGroundObvious) >= 3)
				{
					if (cell.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
				// Wave 2.4: If an uncertain cell has 3 diagonal water neighbors, it is a ground cell.
				else if (cell.GetDiagonalNeighbors(OutOfBoundsRule.Ignore).Count(z => z.Value.IsWaterObvious) >= 3)
				{
					if (cell.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
				// Wave 2.5: If an uncertain cell is "cornered" water-water-water, it is a ground cell.
				else if (cell.GetDiagonalNeighborsWhereAllSharedImmediates(z => z.Value.IsWaterObvious, OutOfBoundsRule.Ignore).Any(z => z.Value.IsWaterObvious))
				{
					if (cell.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
				// Wave 2.6: If an uncertain cell is "cornered" ground-x-ground and has a water cell in the oposite corner, it is a ground cell.
				else if (cell.GetDiagonalNeighborsWhereAllSharedImmediates(z => z.Value.IsGroundObvious, OutOfBoundsRule.Ignore).Any(z => cell.GetOppositeNeighbor(z, OutOfBoundsRule.Ignore)?.Value.IsWaterObvious == true))
				{
					if (cell.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
				// Wave 2.7: If an uncertain cell is "cornered" water-ground-water, it is a water cell.
				else if (cell.GetDiagonalNeighborsWhereAllSharedImmediates(z => z.Value.IsWaterObvious, OutOfBoundsRule.Ignore).Any(z => z.Value.IsGroundObvious))
				{
					if (cell.Value.SetObvious(TerrainType.Water))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Water);
					}
				}
				// Wave 2.8: If an uncertain cell is "cornered" ground-water-ground, it is a ground cell.
				else if (cell.GetDiagonalNeighborsWhereAllSharedImmediates(z => z.Value.IsGroundObvious, OutOfBoundsRule.Ignore).Any(z => z.Value.IsWaterObvious))
				{
					if (cell.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
				else
				{
					// Wave 2.9: If an uncertain cell is next to another uncertain cell and both together are surrounded by ground in a way that makes a U-turn impossible, both are ground cells.
					foreach (var neighbor in immediateNeighbors.Where(z => !z.Value.IsTerrainObvious))
					{
						var totalNeighbors = immediateNeighbors.Union(neighbor.GetImmediateNeighbors(OutOfBoundsRule.Ignore)).Except(new[] { cell, neighbor });
						var uncertainTotalNeighbors = totalNeighbors.Where(z => !z.Value.IsTerrainObvious);
						if (uncertainTotalNeighbors.Count() == 2 && totalNeighbors.Count(z => z.Value.IsGroundObvious) == 4)
						{
							if (uncertainTotalNeighbors.First().GetImmediateNeighbors(OutOfBoundsRule.Ignore).Contains(uncertainTotalNeighbors.Last()))
							{
								if (cell.Value.SetObvious(TerrainType.Ground))
								{
									madeAChange = true;
									wave.FoundCell(TerrainType.Ground);
								}
								if (neighbor.Value.SetObvious(TerrainType.Ground))
								{
									madeAChange = true;
									wave.FoundCell(TerrainType.Ground);
								}
							}
						}
					}
				}
			}
		}

		wave.StopTimer();
		return madeAChange;
	}

	/// <summary>
	/// Wave 3 : Tries to connect unknown cells to water cells, and sets terrain to Grounds where it fails to make a connection.
	/// </summary>
	private bool SetGroundWhereTooFarFromWater()
	{
		var wave = Stats.StartWave("Wave 3");
		var madeAChange = false;
		var remainingCellsToFind = FindNumberOfRemainingWaterCells().max;

		var connectableCells = new List<(MatrixCell<TerrainHeatmapCell> cell, int remainingWaterNeighborsToFind)>();
		foreach (var cell in Heatmap.Where(z => z.Value.IsWaterObvious || (z.IsOnEdge && !z.Value.IsTerrainObvious)))
		{
			var remainingWaterNeighborsToFind = (cell.IsOnEdge ? 1 : 2) - cell.GetImmediateNeighbors(OutOfBoundsRule.Ignore).Count(z => z.Value.IsWaterObvious);
			if (remainingWaterNeighborsToFind > 0) // Can't connect to cells that are already fully connected
				connectableCells.Add((cell, remainingWaterNeighborsToFind));
		}

		if (connectableCells.Any())
		{
			foreach (var unknownCell in Heatmap.Where(z => !z.Value.IsTerrainObvious).ToList())
			{
				var connectableCellsInRange = new List<(MatrixCell<TerrainHeatmapCell> cell, int remainingWaterNeighborsToFind, int distance)>();

				foreach (var (cell, remainingWaterNeighborsToFind) in connectableCells)
				{
					var distance = cell.Position.DistanceTo(unknownCell.Position);
					if (distance > remainingCellsToFind)
						continue; // Can't connect to cells that are further than the number of remaining cells to find

					if (remainingWaterNeighborsToFind == 1 && distance > 1 && cell.GetDiagonalNeighborsWhereAllSharedImmediates(z => !z.Value.IsWaterObvious, OutOfBoundsRule.Ignore).Any(z => z.Value.IsWaterObvious))
						continue; // Can't connect to cells that must be linked to their diagonal neighbor (unless the distance <= 1)

					connectableCellsInRange.Add((cell, remainingWaterNeighborsToFind, distance));
				}

				connectableCellsInRange = connectableCellsInRange.OrderBy(z => z.distance).ToList();

				if (!connectableCellsInRange.Any()) // If the unknown cell can't connect to any connectable cell
				{
					if (unknownCell.Value.SetObvious(TerrainType.Ground))
					{
						madeAChange = true;
						wave.FoundCell(TerrainType.Ground);
					}
				}
				else if (connectableCellsInRange.Count >= 2)
				{
					var foundValidPair = false;
					// Find just ONE possible pair and the cell could be a valid water cell.
					for (var i1 = 0; i1 < connectableCellsInRange.Count(); i1++)
					{
						var cell1 = connectableCellsInRange.ElementAt(i1);
						for (var i2 = i1 + 1; i2 < connectableCellsInRange.Count(); i2++)
						{
							var cell2 = connectableCellsInRange.ElementAt(i2);

							var totalDistance = cell1.distance + cell2.distance - 1;

							if (totalDistance > remainingCellsToFind)
								break; // Can't connect to cell pairs where the total distance to the 2 cells is larger than the number of remaining cells to find. Break because the list is ordered by distance

							if (cell1.cell.GetImmediateNeighbors(OutOfBoundsRule.Ignore).Contains(cell2.cell))
								continue; // Can't connect to a pair of two adjacent cells, because they are already connected to each other

							var otherPotentialCells = connectableCells.Where(z => z.cell.Position != cell1.cell.Position && z.cell.Position != cell2.cell.Position);
							var minimumCellsToLeaveToOtherConnections = otherPotentialCells.Count(z => z.remainingWaterNeighborsToFind == 2)
								+ Mathf.CeilToInt(
									(otherPotentialCells.Count(z => z.remainingWaterNeighborsToFind == 1)
										+ new[] { cell1, cell2 }.Count(z => z.remainingWaterNeighborsToFind == 2)
									) / 2f)
								;

							if (remainingCellsToFind - totalDistance < minimumCellsToLeaveToOtherConnections)
								continue; // Can't connect to cell pairs doesn't let the minimum space needed to connect the other cells

							// We dit it! We found a possible pair for this cell! It is valid and won't be marked as Ground. Let's exit the double-loop.
							foundValidPair = true;
							break;
						}
						if (foundValidPair)
							break;
					}
					if (!foundValidPair)
					{
						if (unknownCell.Value.SetObvious(TerrainType.Ground))
						{
							madeAChange = true;
							wave.FoundCell(TerrainType.Ground);
						}
					}
				}
			}
		}

		wave.StopTimer();
		return madeAChange;
	}

	/// <summary>
	/// Wave 4  : Adjusts unknown cell's probabilities according to nearby water cells
	/// </summary>
	private bool SpreadPartialCertainties()
	{
		var wave = Stats.StartWave("Wave 4");
		wave.SetValue("Path certainty threshold", Wave4_PathCertaintyThreshold);

		var madeAChange = false;
		var remainingCellsToFind = FindNumberOfRemainingWaterCells().max;

		foreach (var unknownCell in Heatmap.Where(z => !z.Value.IsTerrainObvious && z.Value.FoundProbability))
		{
			unknownCell.Value.ResetProbability();
		}

		var waterCellsToConnect = Heatmap.Where(z => z.Value.IsWaterObvious)
			.Select(z => (z, (z.IsOnEdge ? 1 : 2) - z.GetImmediateNeighbors(OutOfBoundsRule.Ignore).Count(zz => zz.Value.IsWaterObvious)))
			.Where(z => z.Item2 != 0)
			.ToList();

		var potentialPathExits = Heatmap.GetEdgeCells().Where(z => !z.Value.IsTerrainObvious).ToList();

		var pathResultsCollection = new List<TestPathResults>();

		foreach (var (waterCell, waterNeighborsToFind) in waterCellsToConnect)
		{
			var connectionStat = wave.StartSubstat(waterCell.Position.ToString());
			var pathResults = new TestPathResults(waterCell.Position, waterNeighborsToFind);

			var failSafeCounter = 0;
			while (pathResults.PathsToTest.Any() && failSafeCounter < remainingCellsToFind)
			{
				connectionStat.AddToValue("Maximum path distance");
				foreach (var path in pathResults.PathsToTest.ToList())
				{
					var currentCell = Heatmap.GetCell(path.Path.Last()).Value;
					IEnumerable<MatrixCell<TerrainHeatmapCell>> nextNeighbors = null;

					if (path.Path.Count == remainingCellsToFind) // Normally we would count +1, but the starting cell is already included. TOCHANGE if we decide to start from edges too
						pathResults.InvalidatePath(path, null);
					else if (potentialPathExits.Count > 0 && path.SkippedExits.Count == potentialPathExits.Count) // If blocked all the exits
						pathResults.InvalidatePath(path, null);
					else if (path.Certainty < Wave4_PathCertaintyThreshold) // If the path is too uncertain to keep going (saving time)
						pathResults.ValidatePath(path, null);
					else if (currentCell.IsOnEdge && path.Path.Count > 1) // If the path's last cell is on edge (and we're not at the beginning)
						pathResults.ValidatePath(path, currentCell.Position);
					else
					{
						// Won't go on ground or on previous path cells
						nextNeighbors = currentCell.GetImmediateNeighbors(OutOfBoundsRule.Ignore).Where(z => !path.Path.Contains(z.Position));

						// Wont't go to an obvious cell unless it's connectable
						if (nextNeighbors.Any())
							nextNeighbors = nextNeighbors.Where(z => !z.Value.IsTerrainObvious || waterCellsToConnect.Any(zz => zz.z == z));

						// If inside the edge, cannot go sideways
						if (nextNeighbors.Any() && currentCell.EdgeDistance < MinimumDistanceFromEdge)
							nextNeighbors = nextNeighbors.Where(z => z.EdgeDistance != currentCell.EdgeDistance);

						// Won't go near an already guessed cell, to prevent islands
						if (nextNeighbors.Any())
						{
							var cellsCantGetNearTo = path.Path.Except(path.Path.TakeLast(2));
							nextNeighbors = nextNeighbors.Where(z => !z.GetSurroundingNeighbors(OutOfBoundsRule.Ignore).Any(zz => cellsCantGetNearTo.Contains(zz.Position)));
						}

						// Invalid if can't continue
						if (!nextNeighbors.Any())
							pathResults.InvalidatePath(path, currentCell.Position);
						else
						{
							// Validate if got next to a connectable water cell
							var availablePathEnds = nextNeighbors.Where(z => !z.IsOnEdge && waterCellsToConnect.Select(zz => zz.z).Contains(z));
							if (availablePathEnds.Any())  // if the path got next to a water cell
								pathResults.ValidatePath(path, availablePathEnds.First().Position);
						}
					}

					if (!path.IsValid.HasValue) // If path continues
					{
						if (nextNeighbors.Count() == 1) // If only one choice, continue the path
						{
							path.Path.Add(nextNeighbors.Single().Position);
						}
						else // If many choices, split the path
						{
							var newPaths = new List<TestPath>();
							var closePotentialExits = potentialPathExits.Where(z => z.Position.DistanceTo(currentCell.Position) <= MinimumDistanceFromEdge + 1).Select(z => (z.Position, z.Position.DistanceTo(currentCell.Position)));

							foreach (var neighbor in nextNeighbors)
							{
								var skippedExits = closePotentialExits.Where(z => z.Position.DistanceTo(neighbor.Position) > z.Item2).Select(z => z.Position);
								newPaths.Add(path.Branch(neighbor.Position, skippedExits.ToArray()));
							}
							pathResults.ReplacePath(path, newPaths.ToArray());
						}
					}

				}

				failSafeCounter++;
			}

			pathResultsCollection.Add(pathResults);

			connectionStat.SetValue("Valid paths", pathResults.ValidPaths.Count);
			connectionStat.SetValue("Invalid paths", pathResults.InvalidPaths.Count);
			connectionStat.StopTimer();
		}

		foreach (var pathResults in pathResultsCollection.Where(z => z.IsValid))
		{
			foreach (var pathCell in pathResults.GetPathCellTotals())
			{
				Heatmap[pathCell.Key].AddProbability(pathCell.Value / pathResults.WaterNeighborsMissing);
			}
		}

		if (Wave4_PathCertaintyThreshold == 0f)
		{
			foreach (var untouchedCell in Heatmap.Where(z => !z.Value.FoundProbability))
			{
				if (untouchedCell.Value.SetObvious(TerrainType.Ground))
				{
					madeAChange = true;
					wave.FoundCell(TerrainType.Ground);
				}
			}
		}

		wave.StopTimer();
		return madeAChange;
	}

	/// <summary>
	/// This method calculates the general probability of water, and applies it only on cells without a probability.
	/// </summary>
	private void ApplyOverallProbabilitiesOnBlank()
	{
		var wave = Stats.StartWave("Final wave");
		if (Heatmap.All(z => z.Value.FoundProbability))
			return;

		var (min, max) = FindNumberOfRemainingWaterCells();

		if (MinimumDistanceFromEdge > 0)
		{
			var edgeCells = Heatmap.Where(z => z.EdgeDistance < MinimumDistanceFromEdge).ToList();
			var uncertainEdgeCells = edgeCells.Where(z => !z.Value.IsTerrainObvious);
			int missingWaterCells = (2 * MinimumDistanceFromEdge) - edgeCells.Count(z => z.Value.IsWaterObvious);
			float edgeWaterProbability = (float)missingWaterCells / uncertainEdgeCells.Count();

			foreach (var fatEdgeCell in uncertainEdgeCells.Where(z => !z.Value.FoundProbability))
			{
				fatEdgeCell.Value.AddProbability(edgeWaterProbability);
			}

			min = Mathf.Clamp(min - missingWaterCells, 0, min);
			max = Mathf.Clamp(max - missingWaterCells, 0, max);
		}

		var uncertainGeneralCells = Heatmap.Where(z => !z.Value.IsTerrainObvious && z.EdgeDistance >= MinimumDistanceFromEdge);
		var generalProbability = (min + max) / 2f / uncertainGeneralCells.Count();

		foreach (var blankCell in uncertainGeneralCells.Where(z => !z.Value.FoundProbability))
		{
			blankCell.Value.AddProbability(generalProbability);
		}

		wave.StopTimer();
	}

	private (int min, int max) FindNumberOfRemainingWaterCells()
	{
		var max = Mathf.FloorToInt(StaticSettings.Maps.River_TargetWaterCellsToPlace_RatioOfMinimumDistance * MinimumDistanceBetweenEntranceAndExit);
		var min = Mathf.FloorToInt(StaticSettings.Maps.River_MinimumWaterCellsToPlace_RatioOfTarget * max);

		if (MinimumDistanceFromEdge > 0)
		{
			var maximumDistanceBetweenEnds = 0;
			var riverEnds = Heatmap.GetEdgeCells().Where(z => z.Value.IsWaterObvious).ToArray();

			switch (riverEnds.Length)
			{
				case 2:
					maximumDistanceBetweenEnds = riverEnds[0].Position.DistanceTo(riverEnds[1].Position);
					break;
				case 1:
					maximumDistanceBetweenEnds = Heatmap.GetEdgeCells().Where(z => !z.Value.IsTerrainObvious).Max(z => z.Position.DistanceTo(riverEnds[0].Position));
					break;
				case 0:
					var unknownEdgeCells = Heatmap.GetEdgeCells().Where(z => !z.Value.IsTerrainObvious).ToArray();
					maximumDistanceBetweenEnds = unknownEdgeCells.Max(z => unknownEdgeCells.Max(zz => z.Position.DistanceTo(zz.Position)));
					break;
			}

			max += maximumDistanceBetweenEnds - MinimumDistanceBetweenEntranceAndExit;
		}

		var foundWaterCells = Mathf.RoundToInt(Heatmap.Count(z => z.Value.IsWaterObvious));
		min = Mathf.Clamp(min - foundWaterCells, 0, min);
		max = Mathf.Clamp(max - foundWaterCells, 0, max);
		Stats.SetMissingWaterCells(min, max);

		return (min, max);
	}

	private MatrixCell<T>[] GetAllFatEdgeNeighbors<T>(MatrixCell<T> cell, int fatEdgeWidth)
	{
		if (cell.EdgeDistance >= fatEdgeWidth)
			return new MatrixCell<T>[0];

		var result = new List<MatrixCell<T>>() { cell };

		bool foundANewEdgeNeighbor;
		do
		{
			foundANewEdgeNeighbor = false;
			foreach (var icell in result.ToList())
			{
				var fatNeighbors = icell.GetImmediateNeighbors(OutOfBoundsRule.Ignore).Where(z => z.EdgeDistance < fatEdgeWidth && !result.Any(zz => zz.EdgeDistance == z.EdgeDistance));
				foreach (var neighbor in fatNeighbors)
				{
					result.Add(neighbor);
					foundANewEdgeNeighbor = true;
				}
			}
		}
		while (foundANewEdgeNeighbor);

		return result.ToArray();
	}

	private MatrixCell<T>[] GetAllFatEdgeNeighborsFromCorner<T>(MatrixCell<T> cornerCell, int fatEdgeWidth)
	{
		if (!cornerCell.IsOnCorner)
			return new MatrixCell<T>[0];

		var result = new List<MatrixCell<T>>() { cornerCell };

		// Get all edge cells that are within defined distance of the corner cell

		bool foundANewEdgeNeighbor;
		do
		{
			foundANewEdgeNeighbor = false;
			foreach (var icell in result.ToList())
			{
				var fatNeighbors = icell.GetImmediateNeighbors(OutOfBoundsRule.Ignore).Where(z => z.Position.DistanceTo(cornerCell.Position) < fatEdgeWidth && !result.Any(zz => zz.Position == z.Position) && z.EdgeDistance == 0);
				foreach (var neighbor in fatNeighbors)
				{
					result.Add(neighbor);
					foundANewEdgeNeighbor = true;
				}
			}
		}
		while (foundANewEdgeNeighbor);

		// Add to the results the fat neighbor cells of all previously selected cells

		foreach (var nearCornerCell in result.ToList())
		{
			foreach (var neighbor in GetAllFatEdgeNeighbors(nearCornerCell, fatEdgeWidth).Where(z => !result.Any(zz => zz.Position == z.Position)))
			{
				result.Add(neighbor);
			}
		}

		return result.ToArray();
	}

	private void PrintHeatmapInStats()
	{
		var decimals = 0;
		var groundDisplayValue = -100f;
		var waterDisplayValue = 100f;
		var midValue = Mathf.Lerp(groundDisplayValue, waterDisplayValue, 0.5f);

		var result = Heatmap.ToConsoleString((cell) =>
		{
			var scaledValue = Mathf.Lerp(groundDisplayValue, waterDisplayValue, cell.Value.Probability);
			return Math.Round(Mathf.Lerp(midValue, scaledValue, cell.Value.FoundProbability ? 1f : 0f), decimals).ToString();

		}, ";");

		Stats.AddComment($"Values guessed ({groundDisplayValue} = Ground, {midValue} = 50/50, {waterDisplayValue} = Water) :\r\n{result}");
	}

	private class TestPathResults
	{
		public bool IsValid { get; private set; } = true;
		public Position StartPosition { get; private set; }
		public int WaterNeighborsMissing { get; private set; }
		public List<TestPath> PathsToTest { get; private set; }
		public List<TestPath> ValidPaths { get; private set; } = new List<TestPath>();
		public List<TestPath> InvalidPaths { get; private set; } = new List<TestPath>();
		public List<TestPath> AllPaths => PathsToTest.Union(ValidPaths).Union(InvalidPaths).ToList();

		public TestPathResults(Position startingCell, int waterNeighborsMissing)
		{
			StartPosition = startingCell;
			WaterNeighborsMissing = waterNeighborsMissing;
			PathsToTest = new List<TestPath>
			{
				new TestPath(startingCell, 1f * waterNeighborsMissing),
			};
		}

		public void ReplacePath(TestPath pathToReplace, params TestPath[] replacingPaths)
		{
			var listToUse = pathToReplace.IsValid.HasValue ? (pathToReplace.IsValid.Value ? ValidPaths : InvalidPaths) : PathsToTest;

			listToUse.Remove(pathToReplace);
			listToUse.AddRange(replacingPaths);
		}
		public void ValidatePath(TestPath path, Position? endPosition)
		{
			PathsToTest.Remove(path);
			path.Validate(endPosition);
			ValidPaths.Add(path);
		}
		public void InvalidatePath(TestPath path, Position? endPosition)
		{
			PathsToTest.Remove(path);
			path.Invalidate(endPosition);
			InvalidPaths.Add(path);
		}
		public void InvalidateEndPosition(Position endPosition)
		{
			if (StartPosition == endPosition)
				IsValid = false;
			else
			{
				ValidPaths = ValidPaths.Where(z => z.EndPosition != endPosition).ToList();
			}
		}

		public bool OnlyHasValidEnds(out Position[] positions)
		{
			var endPositions = GetValidPathEndPositions();
			if (endPositions.Any() && endPositions.All(z => z.HasValue))
			{
				positions = endPositions.Select(z => z.Value).ToArray();
				return true;
			}
			else
			{
				positions = new Position[0];
				return false;
			}
		}

		public Dictionary<Position, float> GetPathCellTotals()
		{
			var result = new Dictionary<Position, float>();
			foreach (var path in ValidPaths.Where(z => z.IsValid == true))
			{
				foreach (var position in path.Path.Skip(1))
					if (result.ContainsKey(position))
						result[position] += path.Certainty;
					else
						result.Add(position, path.Certainty);
			}
			return result;
		}

		public Position?[] GetValidPathEndPositions()
		{
			return ValidPaths.Select(z => z.EndPosition).Distinct().ToArray();
		}
		public Position?[] GetInvalidPathEndPositions()
		{
			return InvalidPaths.Select(z => z.EndPosition).Distinct().ToArray();
		}
	}

	private class TestPath
	{
		/// <summary>
		/// Null = Status Unknown, still advancing
		/// </summary>
		public bool? IsValid { get; private set; } = null;
		public float Certainty { get; private set; }
		public Position? EndPosition { get; private set; } = null;
		public List<Position> Path { get; private set; }
		public HashSet<Position> SkippedExits { get; private set; }

		private TestPath Parent { get; set; } = null;
		private List<TestPath> Children { get; set; } = new List<TestPath>();

		public TestPath(Position startPosition, float baseCertainty)
		{
			Path = new List<Position> { startPosition };
			Certainty = baseCertainty;
			SkippedExits = new HashSet<Position>();
		}
		/// <summary>
		/// Used for branching
		/// </summary>
		private TestPath(TestPath parentPath)
		{
			Path = parentPath.Path.ToList();
			SkippedExits = parentPath.SkippedExits.ToHashSet();
			Parent = parentPath;
			parentPath.Children.Add(this);
		}

		public TestPath Branch(Position nextPosition, params Position[] skippedExits)
		{
			var newChild = new TestPath(this);

			newChild.Path.Add(nextPosition);
			foreach (var skippedExit in skippedExits)
				newChild.SkippedExits.Add(skippedExit);

			RefreshCertainty();
			return newChild;
		}

		public void RefreshCertainty(float? newCertainty = null)
		{
			if (newCertainty.HasValue)
				Certainty = newCertainty.Value;

			var validChildren = Children.Where(z => z.IsValid != false).ToArray();

			if (validChildren.Any())
			{
				var childrenCertainty = Certainty / validChildren.Length;

				foreach (var child in Children.Where(z => z.IsValid != false))
				{
					child.RefreshCertainty(childrenCertainty);
				}
			}
		}

		public void Validate(Position? endPosition)
		{
			IsValid = true;
			EndPosition = endPosition;
		}
		public void Invalidate(Position? endPosition)
		{
			IsValid = false;
			EndPosition = Path.Last();

			if (Parent != null)
			{
				if (Parent.Children.Any(z => z.IsValid != false))
					Parent.RefreshCertainty();
				else
					Parent.Invalidate(endPosition);
			}

		}
	}
	private class RiverGuesserStat : Diagnostics
	{
		private const string KeyWaterCellsFound = "WaterCellsFound";
		private const string KeyGroundCellsFound = "GroundCellsFound";
		private const string KeyMinMissingWaterCells = "MinMissingWaterCells";
		private const string KeyMaxMissingWaterCells = "MaxMissingWaterCells";
		protected override string SubStatsLabel => "waves";

		public RiverGuesserStat(string key) : base(key) { }
		public RiverGuesserStat StartWave(string key)
		{
			var newSubStat = new RiverGuesserStat(key);
			SubStats.Add(newSubStat);
			return newSubStat;
		}

		public void SetMissingWaterCells(int minimum, int maximum)
		{
			SetValue(KeyMinMissingWaterCells, minimum);
			SetValue(KeyMaxMissingWaterCells, maximum);
		}
		public void FoundCell(TerrainType terrain)
		{
			switch (terrain)
			{
				case TerrainType.Water: AddToValue(KeyWaterCellsFound); break;
				case TerrainType.Ground: AddToValue(KeyGroundCellsFound); break;
			}
		}
		protected override Formatting GetFormatting(string key)
		{
			return key switch
			{
				KeyWaterCellsFound => new Formatting { Format = "F0", Label = "Water cells found", Suffix = " cells", Color = "#b5beff" },
				KeyGroundCellsFound => new Formatting { Format = "F0", Label = "Ground cells found", Suffix = " cells", Color = "#bcffb5" },
				KeyMinMissingWaterCells => new Formatting { Format = "F0", Label = "Missing water cells (minimum)", Suffix = " cells" },
				KeyMaxMissingWaterCells => new Formatting { Format = "F0", Label = "Missing water cells (maximum)", Suffix = " cells" },
				_ => base.GetFormatting(key),
			};
		}
	}
}

