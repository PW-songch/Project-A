using UnityEngine;

/// <summary>
/// �׼�ī�� ��ȭ �ɷ�ġ
/// </summary>
public class ActionCardEvolutionAbility : EvolutionAbility
{
    private ActionCard actionCard;

    public ActionCardEvolutionAbility(ActionCard actionCard, EvolutionAbilityData[] evoAbilities) : base(actionCard.MetaID, evoAbilities)
    {
        this.actionCard = actionCard;
    }

    /// <summary>
    /// ��ȭ �ɷ�ġ ����
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
