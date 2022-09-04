using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateItem : MonoBehaviour
{
    [SerializeField] private Vector3 _rotation;
    public BuildingConfig build;

    private void Start()
    {
      //  build.Model;
    }

    void Update()
    {
        transform.Rotate( _rotation * Time.deltaTime);
    }
}
