//----------------------------------------------
//            Realistic Car Controller
//
// Copyright © 2014 - 2024 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//
//----------------------------------------------

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Applies emission texture to the target renderer.
/// </summary>
[System.Serializable]
public class RCC_Emission {

    public Renderer lightRenderer;      //  Renderer of the light.

    public int materialIndex = 0;       //  Index of the material.
    public bool noTexture = false;      //  Material has no texture.
    public bool applyAlpha = false;     //  Apply alpha channel.
    [Range(.1f, 10f)] public float multiplier = 1f;     //  Emission multiplier.

    private int emissionColorID;        //  ID of the emission color.
    private int emissionIntensityID;        //  ID of the emission intensity.
    private int emissionWeightID;        //  ID of the emission weight.
    private int emissionBaseID;     //  ID of the emission base.

    private Material material;
    private Color targetColor;

    private bool initialized = false;

    /// <summary>
    /// Shader keyword to enable emissive.
    /// </summary>
    public string shaderKeywordEmissionEnable = "_UseEmissiveIntensity";

    /// <summary>
    /// Shader keyword to set color of the emissive.
    /// </summary>
    public string shaderKeywordEmissionColor = "_EmissiveColor";

    /// <summary>
    /// Shader keyword to set color of the emissive.
    /// </summary>
    public string shaderKeywordEmissionIntensity = "_EmissiveIntensity";

    /// <summary>
    /// Shader keyword to set color of the emissive.
    /// </summary>
    public string shaderKeywordEmissionWeight = "_EmissiveExposureWeight";

    /// <summary>
    /// Shader keyword to set color of the emissive.
    /// </summary>
    public string shaderKeywordEmissionBase = "_AlbedoAffectEmissive";

    /// <summary>
    /// Initializes the emission.
    /// </summary>
    public void Init() {

        //  If no renderer selected, return.
        if (!lightRenderer) {

            Debug.LogError("No renderer selected for emission! Selected a renderer for this light, or disable emission.");
            return;

        }

        material = lightRenderer.materials[materialIndex];      //  Getting correct material index.
        material.SetFloat(shaderKeywordEmissionEnable, 1f);        //  Enabling keyword of the material for emission.

        emissionColorID = Shader.PropertyToID(shaderKeywordEmissionColor);        //  Getting ID of the emission color.

        //  If material has no property for emission color, return.
        if (!material.HasProperty(emissionColorID))
            Debug.LogError("Material has no emission color id!");

        emissionIntensityID = Shader.PropertyToID(shaderKeywordEmissionIntensity);        //  Getting ID of the emission intensity.

        //  If material has no property for emission color, return.
        if (!material.HasProperty(emissionIntensityID))
            Debug.LogError("Material has no emission intensity id!");

        emissionWeightID = Shader.PropertyToID(shaderKeywordEmissionWeight);        //  Getting ID of the emission weight.

        //  If material has no property for emission color, return.
        if (!material.HasProperty(emissionWeightID))
            Debug.LogError("Material has no emission intensity id!");

        emissionBaseID = Shader.PropertyToID(shaderKeywordEmissionBase);        //  Getting ID of the emission base.

        //  If material has no property for emission color, return.
        if (!material.HasProperty(emissionBaseID))
            Debug.LogError("Material has no emission base id!");

        initialized = true;     //  Emission initialized.

    }

    /// <summary>
    /// Sets emissive strength of the material.
    /// </summary>
    /// <param name="sharedLight"></param>
    public void Emission(Light sharedLight) {

        //  If not initialized, initialize and return.
        if (!initialized) {

            Init();
            return;

        }

        //  If light is not enabled, return with 0 intensity.
        if (!sharedLight.enabled)
            targetColor = Color.white * 0f;

        //  If intensity of the light is close to 0, return with 0 intensity.
        if (Mathf.Approximately(sharedLight.intensity, 0f))
            targetColor = Color.white * 0f;

        //  If no texture option is enabled, set target color with light color. Otherwise, set target color with Color.white.
        if (!noTexture)
            targetColor = Color.white * sharedLight.intensity / 10f * multiplier;
        else
            targetColor = sharedLight.color * sharedLight.intensity / 10f * multiplier;

        //  If apply alpha is enabled, set color of the material with alpha channel.
        if (applyAlpha)
            targetColor = new Color(targetColor.r, targetColor.g, targetColor.b, sharedLight.intensity * multiplier);

        //  And finally, set color of the material with correct ID.
        if (material.GetColor(emissionColorID) != (targetColor))
            material.SetColor(emissionColorID, targetColor);

        material.SetFloat(emissionIntensityID, sharedLight.intensity / 400f);
        material.SetFloat(emissionWeightID, .5f);
        material.SetFloat("_AlbedoAffectEmissive", 1f);

    }

}
