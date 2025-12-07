//----------------------------------------------
//            Realistic Car Controller
//
// Copyright © 2014 - 2025 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//
//----------------------------------------------

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Upgradable neon.
/// </summary>
[RequireComponent(typeof(DecalProjector))]
public class RCC_Customizer_Neon : RCC_Core {

    private DecalProjector neonRenderer;     //  Renderer, actually a box.

    /// <summary>
    /// Sets target material of the neon.
    /// </summary>
    /// <param name="material"></param>
    public void SetNeonMaterial(Material material) {

        //  Getting the mesh renderer.
        if (!neonRenderer)
            neonRenderer = GetComponentInChildren<DecalProjector>();

        //  Return if renderer not found.
        if (!neonRenderer)
            return;

        //  Setting material of the renderer.
        neonRenderer.material = material;

    }

    public void OnValidate() {

        if (!GetComponent<DecalProjector>())
            return;

        DecalProjector dp = GetComponent<DecalProjector>();

        if (dp.scaleMode == DecalScaleMode.ScaleInvariant && dp.pivot == new Vector3(0, 0, .5f) && dp.drawDistance == 1000) {

            dp.scaleMode = DecalScaleMode.InheritFromHierarchy;
            dp.pivot = Vector3.zero;
            dp.drawDistance = 500f;

        }

    }

}
