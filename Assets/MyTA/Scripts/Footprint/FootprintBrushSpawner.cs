using UnityEngine;
using UnityEngine.Rendering;

public class FootprintBrushSpawner : MonoBehaviour
{
    public GameObject brushPrefab;
    public Transform player;

    [Header("Step")]
    public float stepDistance = 0.7f;
    public float footSideOffset = 0.18f;
    public float groundOffset = 0.05f;
    public float brushLife = 0.08f;

    private Vector3 lastStepPos;
    private bool leftFoot;

    private void Start()
    {
        lastStepPos = player.position;
    }

    private void Update()
    {
        if (player == null || brushPrefab == null)
            return;

        Vector3 flatNow = new Vector3(player.position.x, 0, player.position.z);
        Vector3 flatLast = new Vector3(lastStepPos.x, 0, lastStepPos.z);

        if (Vector3.Distance(flatNow, flatLast) < stepDistance)
            return;

        SpawnFootprint();

        lastStepPos = player.position;
        leftFoot = !leftFoot;
    }

    private void SpawnFootprint()
    {
        Vector3 forward = player.forward;
        forward.y = 0;
        forward.Normalize();

        Vector3 right = player.right;
        right.y = 0;
        right.Normalize();

        float side = leftFoot ? -footSideOffset : footSideOffset;

        Vector3 pos = player.position + right * side + Vector3.up * groundOffset;

        GameObject brush = Instantiate(brushPrefab, pos, Quaternion.identity);
        
        if (FootprintRTManager.Active != null)
        {
            FootprintRTManager.Active.NotifyBrushSpawned();
        }

        // Quad 默认在 XY 平面，这里让 Quad 平躺，并让贴图朝向角色前方
        brush.transform.rotation = Quaternion.LookRotation(Vector3.down, forward);

        foreach (Renderer r in brush.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        
        Destroy(brush, brushLife);
    }
}