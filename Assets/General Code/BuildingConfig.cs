using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "New building", menuName = "Building")]
public class BuildingConfig : ScriptableObject
{
    public string BuildingName;
    public string Description;
    public GameObject Model;

    /// <summary>
    /// Describe the map as a string. 
    /// Seperate cells with a dot (.) 
    /// Separate rows with a slash (/) 
    /// Each cell has a number for every property: IsPartOfBuilding (0/1), LandPlacement (0/1/2), WaterPlacement (0/1/2)
    /// </summary>
    public string CellsAsString;

    /// <summary>
    /// Built from CellsAsString
    /// </summary>
    public Matrix<BuildingConfigCell> Cells
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CellsAsString))
                return new Matrix<BuildingConfigCell>();

            var rows = CellsAsString.Split("/", StringSplitOptions.RemoveEmptyEntries);

            var result = new Matrix<BuildingConfigCell>(rows.First().Split(".", StringSplitOptions.RemoveEmptyEntries).Length, rows.Length);

            for (var y = 0; y < rows.Length; y++)
            {
                var cells = rows[y].Split(".", StringSplitOptions.RemoveEmptyEntries);

                for (var x = 0; x < cells.Length; x++)
                {
                    var cell = cells[x];
                    int charToInt(int charPosition) => Int32.Parse(cell.Substring(charPosition, 1));


                    var configCell = new BuildingConfigCell
                    {
                        IsPartOfBuilding = Convert.ToBoolean(charToInt(0)),
                        LandPlacement = (CellPlacementPossibility)charToInt(1),
                        WaterPlacement = (CellPlacementPossibility)charToInt(2),
                    };
                    result[x, y] = configCell;
                }
            }
            return result;
        }
    }
    
    public int[] GetDifferentRotations()
    {
        var currentForm = Cells;

        // Check if symmetrical on both axis
        var rotatedOnce = currentForm.Rotate90Clockwise();
        if (rotatedOnce.Size == currentForm.Size && currentForm.MatchCellsIntersection(new Position(0,0), rotatedOnce).All(z => z.MatrixCell.Value == z.SubMatrixCell.Value))
        {
            return new int[1] { 0 };
        }

        // Check if symmetrical on only one axis
        var rotatedTwice = rotatedOnce.Rotate90Clockwise();
        if (currentForm.MatchCellsIntersection(new Position(0, 0), rotatedTwice).All(z => z.MatrixCell.Value == z.SubMatrixCell.Value))
        {
            return new int[2] { 0, 2 };
        }

        // Else

        return new int[4] { 0, 1, 2, 3 };
    }
}


public struct BuildingConfigCell
{
    public bool IsPartOfBuilding { get; set; }
    public CellPlacementPossibility LandPlacement { get; set; }
    public CellPlacementPossibility WaterPlacement { get; set; }

    public static bool operator ==(BuildingConfigCell c1, BuildingConfigCell c2)
    {
        return c1.Equals(c2);
    }

    public static bool operator !=(BuildingConfigCell c1, BuildingConfigCell c2)
    {
        return !c1.Equals(c2);
    }

    public override bool Equals(object obj)
    {
        return obj is BuildingConfigCell cell &&
               IsPartOfBuilding == cell.IsPartOfBuilding &&
               LandPlacement == cell.LandPlacement &&
               WaterPlacement == cell.WaterPlacement;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsPartOfBuilding, LandPlacement, WaterPlacement);
    }
}

public enum CellPlacementPossibility
{
    Impossible = 0,
    Possible = 1,
    Required = 2,
}