using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class StaticSettings
{
    public static class Game
    {
        public static readonly int MapWidth = 16;
        public static readonly int MapHeight = 16;

    }

    public static class Maps
    {
        public static readonly int River_MinimumDistanceFromEdge = 2;
        public static readonly float River_MinimumDistanceBetweenEntranceAndExit_RatioOfMax = 1f;
        public static readonly float River_TargetWaterCellsToPlace_RatioOfMinimumDistance = 1.7f;
        public static readonly float River_MinimumWaterCellsToPlace_RatioOfTarget = 0.85f;

        public static readonly float River_DecisionImportanceModifier_Closest = 1f;
        public static readonly float River_DecisionImportanceModifier_Furthest = 0.5f;
        public static readonly float River_DecisionImportanceModifier_Center = 3f;


    }
    public static class AI
    {
        public static readonly float BuildingPlacement_MinBuildingDistanceFactor = 0.5f; // Cause why not
        public static readonly float BuildingPlacement_MaxBuildingDistanceFactor = 1f;
        public static readonly float BuildingPlacement_MinEdgeDistanceFactor = 0f;
        public static readonly float BuildingPlacement_MaxEdgeDistanceFactor = 1f;
        public static readonly float BuildingPlacement_MinWaterDistanceFactor = 0f;
        public static readonly float BuildingPlacement_MaxWaterDistanceFactor = 1f;


    }
}