using UnityEngine;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// 진화 능력치 통합 정보
/// </summary>
public class EvolutionAbility
{
    protected int ownerMetaID;
    protected Dictionary<eEvoAbilityType, List<EvolutionAbilityData>> evoAbilities;

    /// <summary>
    /// 진화 능력치 초기화
    /// </summary>
    public EvolutionAbility(int ownerMetaID, EvolutionAbilityData[] evoAbilities)
    {
        this.ownerMetaID = ownerMetaID;

        if (evoAbilities != null && evoAbilities.Length > 0)
        {
            if (this.evoAbilities == null)
                this.evoAbilities = new Dictionary<eEvoAbilityType, List<EvolutionAbilityData>>();
            else
                this.evoAbilities.Clear();

            AddEvolutionAbilities(evoAbilities);
        }
    }

    /// <summary>
    /// 진화 능력치 리스트 리턴
    /// </summary>
    public EvolutionAbilityData[] GetEvolutionAbilities()
    {
        if (evoAbilities != null)
        {
            List<EvolutionAbilityData> list = new List<EvolutionAbilityData>();
            var e = evoAbilities.Values.GetEnumerator();
            while (e.MoveNext())
                list.AddRange(e.Current);
            return list.ToArray();
        }

        return null;
    }

    /// <summary>
    /// 진화 능력치 추가
    /// </summary>
    public void AddEvolutionAbilities(params EvolutionAbilityData[] evoAbilities)
    {
        if (evoAbilities != null && evoAbilities.Length > 0)
        {
            if (this.evoAbilities == null)
                this.evoAbilities = new Dictionary<eEvoAbilityType, List<EvolutionAbilityData>>();

            for (int i = 0; i < evoAbilities.Length; ++i)
            {
                EvolutionAbilityData ability = evoAbilities[i];
                if (ability != null)
                {
                    if (!this.evoAbilities.TryGetValue(ability.AbilityType, out List<EvolutionAbilityData> list))
                    {
                        list = new List<EvolutionAbilityData>();
                        this.evoAbilities.Add(ability.AbilityType, list);
                    }
                    list.Add(ability);
                }
            }
        }
    }

    /// <summary>
    /// 타입에 따른 진화 능력치들 리턴
    /// </summary>
    private EvolutionAbilityData[] GetEvolutionAbilities(eEvoAbilityType abilityType, string subType = "")
    {
        List<EvolutionAbilityData> ablilities = null;
        if (evoAbilities != null && evoAbilities.TryGetValue(abilityType, out List<EvolutionAbilityData> list))
        {
            ablilities = new List<EvolutionAbilityData>();
            for (int i = 0; i < list.Count; ++i)
            {
                if (string.IsNullOrEmpty(subType) || list[i].AbilitySubType.Equals(subType))
                    ablilities.Add(list[i]);
            }

            return ablilities?.ToArray();
        }

        return null;
    }

    /// <summary>
    /// 조건에 따른 진화 능력치 적용
    /// </summary>
    public void ApplyEvolutionAbilities(eEvoCondition condition, bool isApply, bool isSetCoolTime = false, object[] conditionValues = null, params object[] param)
    {
        if (evoAbilities != null)
        {
            var e = evoAbilities.Values.GetEnumerator();
            while (e.MoveNext())
            {
                for (int i = 0; i < e.Current.Count; ++i)
                {
                    EvolutionAbilityData ability = e.Current[i];
                    if (ability != null && ability.SetApply(isApply, condition, isSetCoolTime, conditionValues))
                    {
                        ApplyEvolutionAbility(ability, param);

                        if (ability.IsApply)
                        {
                            switch (condition)
                            {
                                case eEvoCondition.IN_HOME:
                                    ApplyEvolutionAbilities(eEvoCondition.IN_AWAY, false, isSetCoolTime, conditionValues);
                                    break;
                                case eEvoCondition.IN_AWAY:
                                    ApplyEvolutionAbilities(eEvoCondition.IN_HOME, false, isSetCoolTime, conditionValues);
                                    break;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 적용중인 진화 능력치 적용
    /// </summary>
    public void ApplyAppliedEvolutionAbilities(KeyValuePair<Enum, string> abilityType, params object[] param)
    {
        EvolutionAbilityData[] abilities = GetAppliedEvolutionAbilities(abilityType);
        if (abilities != null && abilities.Length > 0)
        {
            for (int i = 0; i < abilities.Length; ++i)
            {
                ApplyEvolutionAbility(abilities[i], param);
            }
        }
    }

    /// <summary>
    /// 진화 능력치 적용
    /// </summary>
    protected virtual void ApplyEvolutionAbility(EvolutionAbilityData ability, params object[] param)
    {
        //if (ability != null)
        //{
        //    if (ability.IsApply == false)
        //    {
        //        if (ability.HasCoolTime)
        //            ApplyEvolutionAbilities(eEvoCondition.ALL, false, true);
        //    }
        //}
    }

    /// <summary>
    /// 진화 능력치 적용된 값 리턴
    /// </summary>
    /// <param name="ability"></param>
    /// <param name="value"></param>
    /// <param name="isNoneTypeValueChange"></param> NONE 타입인 경우 값을 진화 능력치 값으로 변경
    /// <returns></returns>
    public float GetApplyValue(EvolutionAbilityData ability, float value, bool isNoneTypeValueChange = true)
    {
        float v = value;
        if (ability != null)
            v = ability.ApplyFloatValue(isNoneTypeValueChange && ability.ValueType == eValueType.NONE ? 0 : value, false);
        return v;
    }

    /// <summary>
    /// 진화 능력치 적용된 값 리턴
    /// </summary>
    /// <param name="value"></param>
    /// <param name="abilityType"></param>
    /// <param name="isActiveAbility"></param>
    /// <param name="appliedConditions"></param> 적용된 조건 타입들
    /// <returns></returns>
    public object GetAppliedEvolutionAbilityValue(object value, 
        KeyValuePair<Enum, string> abilityType, bool isActiveAbility = true, params Enum[] appliedConditions)
    {
        object oriValue = value;
        object applyValue = value;
        var abilities = GetAppliedEvolutionAbilities(abilityType, appliedConditions);
        if (abilities != null && abilities.Length > 0)
        {
            for (int i = 0; i < abilities.Length; ++i)
            {
                switch ((eEvoAbilityType)abilityType.Key)
                {
                    case eEvoAbilityType.ATK_DMG:
                    case eEvoAbilityType.ATK_DMG_POS:
                        // 초기값에 개별 적용
                        applyValue = (float)applyValue + (float)abilities[i].ApplyValue(oriValue, isActiveAbility) - (float)oriValue;
                        break;
                    default:
                        // 누적 적용
                        applyValue = abilities[i].ApplyValue(applyValue, isActiveAbility);
                        break;
                }
            }
        }

        return applyValue;
    }

    /// <summary>
    /// 적용된 진화 능력치 리턴
    /// </summary>
    public EvolutionAbilityData[] GetAppliedEvolutionAbilities(KeyValuePair<Enum, string> abilityType, params Enum[] appliedConditions)
    {
        var abilities = GetEvolutionAbilities((eEvoAbilityType)abilityType.Key, abilityType.Value);
        if (abilities != null && abilities.Length > 0)
        {
            List<EvolutionAbilityData> list = new List<EvolutionAbilityData>();
            for (int i = 0; i < abilities.Length; ++i)
            {
                EvolutionAbilityData ability = abilities[i];
                if (ability != null && ability.IsApply && ability.IsPossibleApply())
                {
                    if (appliedConditions != null && appliedConditions.Length > 0)
                    {
                        bool isExcept = true;
                        for (int j = 0; j < appliedConditions.Length; ++j)
                        {
                            Enum condition = appliedConditions[j];
                            if (condition != null && condition is eEvoCondition evoCondition && evoCondition == ability.Condition)
                            {
                                isExcept = false;
                                break;
                            }
                        }

                        if (isExcept)
                            continue;
                    }

                    list.Add(ability);
                }
            }

            return list.ToArray();
        }

        return null;
    }

    /// <summary>
    /// 조건에 맞는 적용된 진화 능력치 리턴
    /// </summary>
    public EvolutionAbilityData[] GetAppliedEvolutionAbilities(params Enum[] appliedConditions)
    {
        if (evoAbilities != null)
        {
            List<EvolutionAbilityData> list = new List<EvolutionAbilityData>();
            var e = evoAbilities.Values.GetEnumerator();
            while (e.MoveNext())
            {
                var abilityList = e.Current;
                if (abilityList != null && abilityList.Count > 0)
                {
                    for (int i = 0; i < abilityList.Count; ++i)
                    {
                        EvolutionAbilityData ability = abilityList[i];
                        if (ability != null && ability.IsApply)
                        {
                            if (appliedConditions != null && appliedConditions.Length > 0)
                            {
                                bool isExcept = true;
                                for (int j = 0; j < appliedConditions.Length; ++j)
                                {
                                    Enum condition = appliedConditions[j];
                                    if (condition != null && condition is eEvoCondition evoCondition && evoCondition == ability.Condition)
                                    {
                                        isExcept = false;
                                        break;
                                    }
                                }

                                if (isExcept)
                                    continue;
                            }

                            list.Add(ability);
                        }
                    }
                }
            }

            return list.ToArray();
        }

        return null;
    }

    /// <summary>
    /// 진화 능력치 적용 여부
    /// </summary>
    public bool IsAppliedEvolutionAbility(KeyValuePair<Enum, string> abilityType, bool isActiveAbility = true)
    {
        var abilities = GetEvolutionAbilities((eEvoAbilityType)abilityType.Key, abilityType.Value);
        if (abilities != null && abilities.Length > 0)
        {
            for (int i = 0; i < abilities.Length; ++i)
            {
                EvolutionAbilityData ability = abilities[i];
                if (ability != null && ability.IsApply)
                {
                    // 적용 처리
                    if (isActiveAbility)
                        ability.AppliedProcessing();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 적용 후 처리
    /// </summary>
    public void PostAppliedProcessing(eEvoCondition condition)
    {
        if (evoAbilities != null)
        {
            var e = evoAbilities.Values.GetEnumerator();
            while (e.MoveNext())
            {
                for (int i = 0; i < e.Current.Count; ++i)
                {
                    EvolutionAbilityData ability = e.Current[i];
                    if (ability.Condition == condition)
                    {
                        if (ability.PostAppliedProcessing())
                            ApplyEvolutionAbility(ability);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 진화 능력치 갱신
    /// </summary>
    public void UpdateEvolutionAbilities(float frameDelta)
    {
        if (evoAbilities != null)
        {
            var e = evoAbilities.Values.GetEnumerator();
            while (e.MoveNext())
            {
                for (int i = 0; i < e.Current.Count; ++i)
                {
                    EvolutionAbilityData ability = e.Current[i];
                    if (ability != null && ability.Update(frameDelta))
                        ApplyEvolutionAbility(ability);
                }
            }
        }
    }
}
