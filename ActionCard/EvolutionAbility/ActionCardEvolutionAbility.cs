using UnityEngine;

/// <summary>
/// 액션카드 진화 능력치
/// </summary>
public class ActionCardEvolutionAbility : EvolutionAbility
{
    private ActionCard actionCard;

    public ActionCardEvolutionAbility(ActionCard actionCard, EvolutionAbilityData[] evoAbilities) : base(actionCard.MetaID, evoAbilities)
    {
        this.actionCard = actionCard;
    }

    /// <summary>
    /// 진화 능력치 적용
    /// </summary>
    protected override void ApplyEvolutionAbility(EvolutionAbilityData ability, params object[] param)
    {
        if (ability != null)
        {
            actionCard?.ApplyEvolutionAbility(ability, param);
            base.ApplyEvolutionAbility(ability);
        }
    }
}
