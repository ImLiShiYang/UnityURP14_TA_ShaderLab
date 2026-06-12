using UnityEngine;

public class PlayerAttack : MonoBehaviour
{
    public Animator animator; // 拖角色 Animator 组件进来

    void Update()
    {
        // 鼠标左键点击
        if (Input.GetMouseButtonDown(0))
        {
            animator.SetTrigger("Attack");
        }
    }
}