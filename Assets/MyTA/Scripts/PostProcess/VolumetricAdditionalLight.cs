using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public class VolumetricAdditionalLight : MonoBehaviour
{
    [Range(-0.85f, 0.85f)]
    public float anisotropy = 0.35f;

    [Range(0f, 16f)]
    public float scattering = 1f;

    [Range(0f, 2f)]
    public float radius = 0.2f;
}