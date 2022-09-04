using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameSetupManager : MonoBehaviour
{
    public MapGenerationService mapService;
    public GameObject playerMapObject;
    public Camera cam;

    // Start is called before the first frame update
    void Start()
    {
        var map = mapService.CreateMapWithRiver(StaticSettings.Game.MapWidth, StaticSettings.Game.MapHeight, 0);

        map.BaseLayer = new Matrix<GameObject>(map.Terrain.Size);

        foreach (var terrainCell in map.Terrain.ToList())
        {
            var objectToCopy = terrainCell.Value == TerrainType.Ground ? mapService.earth : mapService.water;

            var cube = Instantiate(objectToCopy, new Vector3(terrainCell.Position.X, terrainCell.Position.Y * -1, 0), Quaternion.identity, playerMapObject.transform);
            cube.name = terrainCell.Position.ToString();

            map.BaseLayer[terrainCell.Position] = cube;
        }

        // Fit camera to map
        {
            var totalBounds = new UnityEngine.Bounds(playerMapObject.transform.position, Vector3.one);
            foreach (var renderer in playerMapObject.GetComponentsInChildren<Renderer>())
            {
                totalBounds.Encapsulate(renderer.bounds);
            }

            float cameraDistance = 1f; // Constant factor (that's the number you play with)
            Vector3 objectSizes = totalBounds.max - totalBounds.min;
            float objectSize = Mathf.Max(objectSizes.x, objectSizes.y, objectSizes.z);
            float cameraView = 2.0f * Mathf.Tan(0.5f * Mathf.Deg2Rad * cam.fieldOfView); // Visible height 1 meter in front
            float distance = cameraDistance * objectSize / cameraView; // Combined wanted distance from the object
            distance += 0.5f * objectSize; // Estimated offset from the center to the outside of the object
            cam.transform.position = totalBounds.center - distance * cam.transform.forward;
        }

        ArtificialIntelligence.RunAITest(map);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
