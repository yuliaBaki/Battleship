using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuildingCardDisplay : MonoBehaviour
{
    public BuildingConfig building;
    public TextMeshProUGUI cardTitle;

    // Start is called before the first frame update
    void Start()
    {
        cardTitle.SetText(building.BuildingName);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    //List<BuildingConfig> CreateRandomListTower()
   // {
        
    //}
}
