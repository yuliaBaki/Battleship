using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class MapGenerationService : MonoBehaviour
{
	public GameObject earth;
	public GameObject water;

	public PlayMap CreateMapWithRiver(int width, int height, int? seed = null)
	{
		var numberOfTries = 10; // 10 tries to be super super safe. Normally we'd hit maybe 3, rarely 4...

		seed ??= Random.Range(0, int.MaxValue);

		var map = CreateBlankMap(StaticSettings.Game.MapWidth, StaticSettings.Game.MapHeight);
		for (var i = 0; i < numberOfTries; i++)
		{
			try
			{
				var generator = new RiverGenerator(map.Size, seed.Value + i);
				var pathResult = generator.CreateRiverPath();

				foreach (var position in pathResult.path)
				{
					map.Terrain[position] = TerrainType.Water;
				}

				pathResult.stats.SetValue("Try number", i + 1);
				pathResult.stats.AddComment("Preview of the river path :\r\n" + map.Terrain.ToConsoleString((cell) => cell.Value == TerrainType.Ground ? " O " : "  .  ", null));
				map.Stats = pathResult.stats;
				break;
			}
			catch (RiverPathException)
			{
				if (i == numberOfTries - 1)
				{
					throw new RiverPathException($"Sorry, the river path algorithm fucked up so badly it couldn't even get it right the {i + 1}th time.");
				}
			}
		}

		return map;
	}


	public static PlayMap CreateBlankMap(int width, int height)
	{
		var map = ScriptableObject.CreateInstance<PlayMap>();
		map.Terrain = new Matrix<TerrainType>(width, height, TerrainType.Ground);
		return map;
	}
}
public enum TerrainType
{
	Ground = 0,
	Water = 1,
}

public class RiverGenerator
{
	public Size Size { get; set; }
	public int Seed { get; set; }

	// Private settings

	public int MinimumDistanceFromEdge { get; private set; }
	public int MinimumDistanceBetweenEntranceAndExit { get; private set; }
	public int TargetWaterCellsToPlace { get; private set; }
	public int MinimumWaterCellsToPlace { get; private set; }

	// Private variables

	private Diagnostics Stats { get; set; }
	private MatrixCell<bool> EntranceCell { get; set; }
	private MatrixCell<bool> ExitCell { get; set; }
	private int InitialLooseValue => (TargetWaterCellsToPlace - 1) - EntranceCell.Position.DistanceTo(ExitCell.Position);
	private List<(RiverPotentialCellStat Cell, CellPlacementDecision Decision)> PlacedCells { get; set; } = new List<(RiverPotentialCellStat, CellPlacementDecision)>();
	private RiverPotentialCellStat CurrentCell => PlacedCells.Last().Cell;

	private int GetMaximumDistanceBetweenEnds(bool bestCase)
	{
		var padding = MinimumDistanceFromEdge * 2;

		Size innerBoxSize = new Size(Size.Width - padding, Size.Height - padding);

		if (!bestCase)
		{
			if (innerBoxSize.Height > innerBoxSize.Width)
				innerBoxSize = innerBoxSize.Flip(); // Make the size horizontal for easier management of the worst case scenario

			innerBoxSize.Width = Mathf.FloorToInt(((float)innerBoxSize.Width) / 2) + 1;
		}

		var innerMaximumDistance = innerBoxSize.Width - 1 + innerBoxSize.Height - 1;

		return innerMaximumDistance + padding; // Pour 16x16(-2) c'est 26 best case, 21 worst case
	}
	/// <summary>
	/// Maximum distance if the river starts on a corner. MinimumDistanceFromEdge needs to have been set.
	/// </summary>
	/// <returns></returns>
	public int GetBestCaseMaximumDistanceBetweenEnds()
	{
		return GetMaximumDistanceBetweenEnds(true);
	}
	/// <summary>
	/// Maximum distance if the river starts in the middle of the largest side. MinimumDistanceFromEdge needs to have been set.
	/// </summary>
	/// <returns></returns>
	public int GeWorstCaseMaximumDistanceBetweenEnds()
	{
		return GetMaximumDistanceBetweenEnds(false);
	}

	// Constructor
	public RiverGenerator(Size size, int seed)
	{
		Size = size;
		Seed = seed;

		MinimumDistanceFromEdge = StaticSettings.Maps.River_MinimumDistanceFromEdge;
		MinimumDistanceBetweenEntranceAndExit = Mathf.FloorToInt(StaticSettings.Maps.River_MinimumDistanceBetweenEntranceAndExit_RatioOfMax * GeWorstCaseMaximumDistanceBetweenEnds());
		TargetWaterCellsToPlace = Mathf.FloorToInt(StaticSettings.Maps.River_TargetWaterCellsToPlace_RatioOfMinimumDistance * MinimumDistanceBetweenEntranceAndExit);
		MinimumWaterCellsToPlace = Mathf.FloorToInt(StaticSettings.Maps.River_MinimumWaterCellsToPlace_RatioOfTarget * TargetWaterCellsToPlace);
	}

	public (Position[] path, Diagnostics stats) CreateRiverPath()
	{
		Stats = new Diagnostics("River generator");
		Stats.IncludeSubStatDetails = false;

		var randomOriginalState = Random.state;
		try
		{
			PlacedCells.Clear();
			Stats.SetValue("Seed used", Seed);
			Stats.SetValue("Minimum distance from edge", MinimumDistanceFromEdge);
			Stats.SetValue("Minimum distance between entrance and exit", MinimumDistanceBetweenEntranceAndExit);
			Stats.SetValue("Target water cells to place", TargetWaterCellsToPlace);
			Stats.SetValue("Minimum water cells to place", MinimumWaterCellsToPlace);
			Random.InitState(Seed);


			SetupEntranceAndExit();
			BuildPath();

			var history = string.Empty;
			for (var i = 0; i < PlacedCells.Count; i++)
				history += $"{i}: {PlacedCells[i].Decision}\r\n";
			Stats.AddComment("Summary of path decisions :\r\n" + history);

			if (PlacedCells.Count < MinimumWaterCellsToPlace)
				throw new RiverPathException($"The number of water cells is too short ({PlacedCells.Count}), start that shit again, I'm not satisfied.");
		}
		catch (Exception ex)
		{
			Stats.LogNow(ex);
			throw;
		}
		finally
		{
			Random.state = randomOriginalState;
		}

		Stats.LogLater();
		return (PlacedCells.Select(z => z.Decision.Position).ToArray(), Stats);
	}

	private void SetupEntranceAndExit()
	{
		var stat = Stats.StartSubstat("Setup river ends");
		var dummyMap = new Matrix<bool>(Size);
		var edgeCells = dummyMap.GetEdgeCells();
		var cornerCells = dummyMap.GetCornerCells();

		var availableEnds = edgeCells.Where(z => cornerCells.Select(zz => z.Position.DistanceTo(zz.Position)).Min() >= MinimumDistanceFromEdge).ToArray();

		EntranceCell = availableEnds.ElementAt(Random.Range(0, availableEnds.Length - 1));

		var availableExitCells = availableEnds.Where(z => z.Position.DistanceTo(EntranceCell.Position) >= MinimumDistanceBetweenEntranceAndExit).ToArray();
		ExitCell = availableExitCells.ElementAt(Random.Range(0, availableExitCells.Length - 1));

		var actualDistanceBetweenEnds = EntranceCell.Position.DistanceTo(ExitCell.Position);
		TargetWaterCellsToPlace += (actualDistanceBetweenEnds - MinimumDistanceBetweenEntranceAndExit);

		stat.SetValue("Distance between ends", actualDistanceBetweenEnds);
		stat.AddComment($"Entrance: {EntranceCell.Position}");
		stat.AddComment($"Exit: {ExitCell.Position}");
		stat.StopTimer();
	}

	private void BuildPath()
	{
		Stats.StartSubstat("Choose next cell").StopTimer();
		PlacedCells.Add((GenerateCellStat(EntranceCell), new CellPlacementDecision
		{
			Position = EntranceCell.Position,
			NumberOfCandidates = 1,
			Choice = RiverPathChoiceMade.Forced,
			HorizontalMove = EntranceCell.Position.X == 0 || EntranceCell.Position.X == Size.Width - 1,
			LooseValue = InitialLooseValue,
		}));

		while (PlacedCells.Count < TargetWaterCellsToPlace)
		{
			if (CurrentCell.Cell == ExitCell)
			{
				break; // It's OK to not finish exactly on target
			}
			var cellStat = Stats.StartSubstat("Choose next cell");

			var potentialNextCells = CurrentCell.Cell.GetImmediateNeighbors(OutOfBoundsRule.Ignore).Select(z => GenerateCellStat(z)).ToArray();
			potentialNextCells = FilterPotentialNextCells(potentialNextCells);

			var decision = ChooseNextCell(potentialNextCells);

			PlacedCells.Add((potentialNextCells.First(z => z.Cell.Position == decision.Position), decision));

			cellStat.StopTimer();
		}

		if (CurrentCell.Cell != ExitCell)
		{
			throw new RiverPathException("The path was not able to reach the exit.");
		}
	}

	private RiverPotentialCellStat[] FilterPotentialNextCells(IEnumerable<RiverPotentialCellStat> potentialCells)
	{
		var result = potentialCells.ToArray();

		// Exclude cells that touch (3x3) water, with an exception for the 2 previous cells
		result = result.Where(z => !z.Cell.GetSurroundingNeighbors(OutOfBoundsRule.Ignore).Any(zz => HasBeenPlacedRecently(zz.Position).HasValue && HasBeenPlacedRecently(zz.Position).Value > 2)).ToArray();

		// Exclude cells too close to the edge, unless they are next to the entrance or exit
		if (result.All(z => z.EntranceDistance < MinimumDistanceFromEdge))
			result = result.Where(z => z.EdgeDistance == result.Max(zz => zz.EdgeDistance)).ToArray();
		else if (result.Any(z => z.ExitDistance < MinimumDistanceFromEdge))
			result = result.Where(z => z.ExitDistance == result.Min(zz => zz.EdgeDistance)).ToArray();
		else
			result = result.Where(z => z.EdgeDistance >= MinimumDistanceFromEdge).ToArray();

		// TODO Maybe later, exclude cells that are close-ish (2 cells) from a previous path, to prevent narrow alleyways

		return result;
	}

	private CellPlacementDecision ChooseNextCell(IEnumerable<RiverPotentialCellStat> potentialCells)
	{
		if (!potentialCells.Any())
			throw new RiverPathException("The algorithm took a wrong path and is now blocked.");

		var remainingCellsToPlace = TargetWaterCellsToPlace - PlacedCells.Count;
		var looseValue = remainingCellsToPlace - CurrentCell.ExitDistance;

		// Now make a choice

		RiverPathChoiceMade choice;
		var choiceComment = "";

		if (potentialCells.Count() == 1)
		{
			choice = RiverPathChoiceMade.Forced;
			choiceComment = "Only 1 available move";
		}
		else if (looseValue < 2)
		{
			choice = RiverPathChoiceMade.Closest;
			choiceComment = "LooseValue too low";
		}
		else if (CurrentCell.Cell.Position.DistanceTo(ExitCell.Position) <= (MinimumDistanceFromEdge + 1))
		{
			choice = RiverPathChoiceMade.Closest;
			choiceComment = "Too close to exit to afford doing a U-turn";
		}
		else if (CurrentCell.EdgeDistance == MinimumDistanceFromEdge
			&& CurrentCell.EntranceDistance > MinimumDistanceFromEdge
			&& potentialCells.All(z => z.EdgeDistance == CurrentCell.EdgeDistance))
		{
			choice = RiverPathChoiceMade.Closest;
			choiceComment = "Faced an edge, should turn in direction of the exit so the path doesn't block itself";
		}
		else
		{
			(choice, choiceComment) = RandomDecision(looseValue);
		}

		// Now apply the choice

		var chosenCells = FilterCellsAccordingToChoice(potentialCells, choice);
		chosenCells = FilterToBreakLongLines(chosenCells);

		var finalChoice = chosenCells.ElementAt(Random.Range(0, chosenCells.Count()));

		return new CellPlacementDecision
		{
			Position = finalChoice.Cell.Position,
			NumberOfCandidates = potentialCells.Count(),
			Choice = choice,
			HorizontalMove = finalChoice.Cell.Position.Y == CurrentCell.Cell.Position.Y,
			LooseValue = looseValue,
			Comment = choiceComment,
		};
	}

	/// <summary>
	/// Logic controlling the randomness of the path
	/// </summary>
	/// <param name="looseValue"></param>
	/// <returns></returns>
	private (RiverPathChoiceMade, string) RandomDecision(float looseValue)
	{
		var probabilities = new Dictionary<RiverPathChoiceMade, float>
		{
			{ RiverPathChoiceMade.Closest, GetClosestDecisionImportance(looseValue) * StaticSettings.Maps.River_DecisionImportanceModifier_Closest },
			{ RiverPathChoiceMade.Furthest, GetFurthestDecisionImportance(looseValue) * StaticSettings.Maps.River_DecisionImportanceModifier_Furthest },
			{ RiverPathChoiceMade.Center, GetCenterDecisionImportance(looseValue) * StaticSettings.Maps.River_DecisionImportanceModifier_Center },
		};
		// Add Any option to fill 100%
		if (probabilities.Sum(z => z.Value) < 1)
			probabilities.Add(RiverPathChoiceMade.Any, 1 - probabilities.Sum(z => z.Value));

		var result = SelectRandomByProportion(probabilities);
		var summary = string.Join(" / ", probabilities.Select(z => $"{z.Key}:  {z.Value:P}"));

		return (result, summary);
	}

	private float GetClosestDecisionImportance(float looseValue)
	{
		float noLoosenessRatio = looseValue / InitialLooseValue;
		float result = Mathf.Clamp01(1 - noLoosenessRatio);

		return result;
	}

	private float GetFurthestDecisionImportance(float looseValue)
	{
		float loosenessTreshold = 3;

		//float loosenessToGoFurther = Min0(looseValue - loosenessTreshold);
		//float initialLooseness = Mathf.Clamp(InitialLooseValue - loosenessTreshold, 1, float.MaxValue); // Give initialLooseness a minimum of 1 so the division doesn't crash
		//var result = Mathf.Clamp01(loosenessToGoFurther / initialLooseness);

		var result = Mathf.Clamp01(looseValue - loosenessTreshold);

		return result;
	}

	private float GetCenterDecisionImportance(float looseValue)
	{
		float loosenessTreshold = 3;

		float bestPossibleEdgeDistance = Mathf.CeilToInt((float)Mathf.Min(Size.Width, Size.Height) / 2) - 1;
		float riverMiddleIndex = (float)TargetWaterCellsToPlace / 2 - 5;
		float currentDistanceFromRiverMiddle = Mathf.Abs(PlacedCells.Count - riverMiddleIndex);
		float currentClosenessToRiverMiddleRatio = Mathf.Clamp01(1 - (currentDistanceFromRiverMiddle / riverMiddleIndex));
		float currentDistanceFromMapCenterRatio = Mathf.Clamp01(1 - (CurrentCell.EdgeDistance / bestPossibleEdgeDistance));
		float loosenessAvailabilityRatio = Mathf.Clamp01(looseValue / loosenessTreshold);

		return currentClosenessToRiverMiddleRatio * currentDistanceFromMapCenterRatio * loosenessAvailabilityRatio;
	}

	private T SelectRandomByProportion<T>(Dictionary<T, float> probabilityChart)
	{
		var total = probabilityChart.Sum(z => z.Value);

		var rand = Random.Range(0, probabilityChart.Sum(z => z.Value));

		foreach (var option in probabilityChart)
		{
			rand -= option.Value;
			if (rand <= 0)
			{
				return option.Key;
			}
		}
		throw new Exception("Failed to select a winning option");
	}

	/// <summary>
	/// Insert a break in a long line, if possible, every 4 cells.
	/// </summary>
	private RiverPotentialCellStat[] FilterToBreakLongLines(IEnumerable<RiverPotentialCellStat> chosenCells)
	{
		var result = chosenCells.ToArray();

		var lastPlacedCellsByDirection = PlacedCells.TakeLast(3).GroupBy(z => z.Decision.HorizontalMove);
		var chosenCellsByDirection = result.GroupBy(z => z.Cell.Position.Y == CurrentCell.Cell.Position.Y);

		if (chosenCellsByDirection.Count() > 1 // if we have a choice between a vertical or horizontal move
			&& lastPlacedCellsByDirection.Count() == 1) // the last placed cells all have the same direction
		{
			result = chosenCellsByDirection.First(z => z.Key != lastPlacedCellsByDirection.First().Key).ToArray();
		}

		return result;
	}
	/// <summary>
	/// Insert a break in a long line, if possible, every 4 cells.
	/// </summary>
	private RiverPotentialCellStat[] FilterCellsAccordingToChoice(IEnumerable<RiverPotentialCellStat> potentialCells, RiverPathChoiceMade choice)
	{
		var result = potentialCells.ToArray();

		switch (choice)
		{
			case RiverPathChoiceMade.Closest:
				result = result.Where(z => z.ExitDistance == result.Min(zz => zz.ExitDistance)).ToArray();
				result = FilterCellsAccordingToChoice(result, RiverPathChoiceMade.Center);
				break;
			case RiverPathChoiceMade.Furthest:
				result = result.Where(z => z.ExitDistance == result.Max(zz => zz.ExitDistance)).ToArray();
				result = FilterCellsAccordingToChoice(result, RiverPathChoiceMade.Center);
				break;
			case RiverPathChoiceMade.Center:
				result = result.Where(z => z.EdgeDistance == result.Max(zz => zz.EdgeDistance)).ToArray();
				break;
			case RiverPathChoiceMade.Forced:
			case RiverPathChoiceMade.Any:
				break;

		}
		return result;
	}

	/// <summary>
	/// Searches the cell in the PlacedCells list. Returns null if not placed, returns a number if it has been placed.
	/// </summary>
	/// <returns>How many cells ago (previous is 1) has the cell been placed</returns>
	private int? HasBeenPlacedRecently(Position position)
	{
		var index = PlacedCells.Select(z => z.Decision.Position).ToList().IndexOf(position);
		if (index == -1)
			return null;
		else
			return PlacedCells.Count - index;
	}

	private RiverPotentialCellStat GenerateCellStat(MatrixCell<bool> cell)
	{
		return new RiverPotentialCellStat
		{
			Cell = cell,
			EntranceDistance = cell.Position.DistanceTo(EntranceCell.Position),
			ExitDistance =cell.Position.DistanceTo(ExitCell.Position),
			EdgeDistance = cell.EdgeDistance,
		};
	}
	private class RiverGeneratorStat : Diagnostics
	{
		private const string KeyWaterCellsFound = "WaterCellsFound";
		private const string KeyGroundCellsFound = "GroundCellsFound";
		private const string KeyMinMissingWaterCells = "MinMissingWaterCells";
		private const string KeyMaxMissingWaterCells = "MaxMissingWaterCells";
		protected override string SubStatsLabel => "cells";

		public RiverGeneratorStat(string key) : base(key) { }
		public RiverGeneratorStat StartNextCell(string key)
		{
			var newSubStat = new RiverGeneratorStat(key);
			SubStats.Add(newSubStat);
			return newSubStat;
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

public class RiverPathException : Exception
{
	public RiverPathException(string message) : base(message)
	{
	}
}

public struct RiverPotentialCellStat
{
	public MatrixCell<bool> Cell { get; set; }
	public int EntranceDistance { get; set; }
	public int ExitDistance { get; set; }
	public int EdgeDistance { get; set; }
}

public struct CellPlacementDecision
{
	public Position Position { get; set; }
	public RiverPathChoiceMade Choice { get; set; }
	public int NumberOfCandidates { get; set; }
	public int LooseValue { get; set; }
	public bool HorizontalMove { get; set; }
	public string Comment { get; set; }

	public override string ToString()
	{
		return $"[{ChoiceToString()}]   [{(HorizontalMove ? "--" : " | ")}]   [{Position}]   {Choice}/{NumberOfCandidates} ({LooseValue})   ...   {Comment}";
	}

	private string ChoiceToString()
	{
		return Choice switch
		{
			RiverPathChoiceMade.Any => "x",
			RiverPathChoiceMade.Closest => ">",
			RiverPathChoiceMade.Furthest => "<",
			RiverPathChoiceMade.Forced => " . ",
			RiverPathChoiceMade.Center => "o",
			_ => "?"
		};
	}
}

public enum RiverPathChoiceMade
{
	Closest,
	Any,
	Furthest,
	Forced,
	Center,
}