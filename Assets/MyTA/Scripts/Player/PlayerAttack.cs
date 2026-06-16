using UnityEngine;

public class PlayerAttack : MonoBehaviour
{
    public Animator animator;
    public string attackTrigger = "Attack";
    public string attackStateName = "Standing Melee Attack Backhand";

    [Header("Root Motion")]
    public bool useRootMotionDuringAttack = true;

    [Range(0f, 1f)]
    public float retriggerNormalizedTime = 0.85f;

    public bool IsAttackRootMotionActive { get; private set; }
    public bool IsAttackActive => animator != null && IsAttackStateActive();

    private int _attackTriggerHash;
    private int _attackStateHash;

    private void Awake()
    {
        if (string.IsNullOrEmpty(attackTrigger))
        {
            attackTrigger = "Attack";
        }

        if (string.IsNullOrEmpty(attackStateName))
        {
            attackStateName = "Standing Melee Attack Backhand";
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        _attackTriggerHash = Animator.StringToHash(attackTrigger);
        _attackStateHash = Animator.StringToHash(attackStateName);

        if (useRootMotionDuringAttack)
        {
            SetAttackRootMotion(false);
        }
    }

    private void Update()
    {
        if (animator == null)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0) && CanStartAttack())
        {
            if (useRootMotionDuringAttack)
            {
                SetAttackRootMotion(true);
            }

            animator.ResetTrigger(_attackTriggerHash);
            animator.SetTrigger(_attackTriggerHash);
        }
    }

    private void LateUpdate()
    {
        if (animator == null || !useRootMotionDuringAttack)
        {
            return;
        }

        SetAttackRootMotion(IsAttackStateActive());
    }

    private void OnDisable()
    {
        if (animator != null && useRootMotionDuringAttack)
        {
            SetAttackRootMotion(false);
        }
    }

    private bool CanStartAttack()
    {
        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            return nextState.shortNameHash != _attackStateHash;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.shortNameHash != _attackStateHash)
        {
            return true;
        }

        return currentState.normalizedTime >= retriggerNormalizedTime;
    }

    private bool IsAttackStateActive()
    {
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.shortNameHash == _attackStateHash)
        {
            return true;
        }

        if (!animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        return nextState.shortNameHash == _attackStateHash;
    }

    private void SetAttackRootMotion(bool active)
    {
        IsAttackRootMotionActive = active;

        if (animator.applyRootMotion != active)
        {
            animator.applyRootMotion = active;
        }
    }
}
