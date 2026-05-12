using UnityEngine;

/// <summary>
/// 动画事件接收器。
/// 
/// 这个脚本挂在带 Animator 的模型物体上，
/// 例如 player / X Bot。
/// Animation Event 会调用这里的方法，
/// 然后再转发给父物体上的 FootprintDecalSpawner。
/// </summary>
public class FootstepEventReceiver : MonoBehaviour
{
    public FootprintDecalSpawner spawner;

    private void Awake()
    {
        if (spawner == null)
        {
            spawner = GetComponentInParent<FootprintDecalSpawner>();
        }
    }

    public void SpawnLeftFootprint()
    {
        if (spawner != null)
        {
            spawner.SpawnLeftFootprint();
        }
    }

    public void SpawnRightFootprint()
    {
        if (spawner != null)
        {
            spawner.SpawnRightFootprint();
        }
    }
}