using CHAR;

/// <summary>
/// 유닛 진화 능력치
/// </summary>
public class UnitEvolutionAbility : EvolutionAbility
{
    private Character character;

    public UnitEvolutionAbility(Character aChar, EvolutionAbilityData[] evoAbilities) : base(aChar.MetaID, evoAbilities)
    {
        character = aChar;
    }

    /// <summary>
    /// 진화 능력치 적용
    /// </summary>
    protected override void ApplyEvolutionAbility(EvolutionAbilityData ability, params object[] param)
    {
        if (ability != null)
        {
            character?.ApplyEvolutionAbility(ability, param);
            base.ApplyEvolutionAbility(ability);
        }
    }
}
