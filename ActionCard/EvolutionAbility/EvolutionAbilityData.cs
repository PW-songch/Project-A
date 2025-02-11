using UnityEngine;
using Pathfinding.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using Boomlagoon.JSON;

/// <summary>
/// 진화 능력치 정보
/// </summary>
public class EvolutionAbilityData
{
    private EvolutionAbilityInfo abilityInfo;
    private StatString[] arrValue;
    private StatBool isApply;

    private int appliedCount;
    private int remainCount;
    private float remainDuration;
    private float remainCoolTime;

    public EvolutionAbilityInfo AbilityInfo { get { return abilityInfo; } }
    public int MetaID { get { return abilityInfo.MetaID; } }
    public object Value { get { return GetValue(0); } }
    public string StrValue { get { return abilityInfo.StrValue; } }
    public bool IsApply { get { return isApply.Value; } }
    public eEvoCondition Condition { get { return abilityInfo.Condition; } }
    public string ConditionValueType { get { return abilityInfo.ConditionValueType; } }
    public object ConditionValue { get { return abilityInfo.ConditionValue; } }
    public eEvoAbilityType AbilityType { get { return abilityInfo.AbilityType;  } }
    public string AbilitySubType { get { return abilityInfo.AbilitySubType; } }
    public eEvoAbilityChangeType AbilityChangeType { get { return abilityInfo.AbilityChangeType; } }
    public int ApplyMetaID { get { return abilityInfo.ApplyMetaID; } }
    public string StrValueType { get { return abilityInfo.StrValueType; } }
    public eValueType ValueType { get { return abilityInfo.ValueType; } }
    public eEvoAbilityExternalApplyType ExternalApplyType { get { return abilityInfo.ExternalApplyType; } }
    public eEvoAbilityChangeType ExternalChangeType { get { return abilityInfo.ExternalChangeType; } }
    public eValueType ExternalValueType { get { return abilityInfo.ExternalValueType; } }
    public int Count { get { return abilityInfo.Count; } }
    public float Duration { get { return abilityInfo.Time; } }
    public float CoolTime { get { return abilityInfo.CoolTime; } }
    public bool HasCoolTime { get { return CoolTime > 0f; } }
    public bool IsRunningCoolTime { get { return remainCoolTime > 0f; } }
    public bool IsApplying { get { return remainDuration > 0f; } }

    public EvolutionAbilityData(EvolutionAbilityInfo abilityInfo)
    {
        this.abilityInfo = abilityInfo.Copy();
        arrValue = new StatString[abilityInfo.ValueLength];

        for (int i = 0; i < arrValue.Length; ++i)
        {
            // 감소에 대한 타입의 값이 양수면 음수로 변경
            string value = abilityInfo.GetValueString(i);
            if (abilityInfo.IsSingleValueType(i))
            {
                float v = (float)abilityInfo.GetValue(i);
                switch (abilityInfo.GetChangeType(i))
                {
                    case eEvoAbilityChangeType.DECREASE:
                        if (v > 0f)
                            value = (-v).ToString();
                        break;
                }
            }

            arrValue[i] = new StatString(value, true);
        }

        isApply = new StatBool(false);
        ApplyCount();
    }

    public bool IsValid()
    {
        return Condition != eEvoCondition.NONE;
    }

    /// <summary>
    /// 조건 값 리턴
    /// </summary>
    private object GetConditionValue(object[] conditionValues, int index = 0)
    {
        if (conditionValues != null && index >= conditionValues.Length)
            index = conditionValues.Length - 1;
        return conditionValues != null && conditionValues.Length > index ? conditionValues[index] : null;
    }

    /// <summary>
    /// 적용 값 리턴
    /// </summary>
    public object GetValue(int index = 0, object defaultValue = default)
    {
        return 0 <= index && index < arrValue.Length ? Convert.ChangeType(
            arrValue[index].Value, abilityInfo.ConvertTypeCodeFromValueType(arrValue[index].Value, AbilityType)) : defaultValue;
    }

    /// <summary>
    /// 변경 타입 리턴
    /// </summary>
    public eEvoAbilityChangeType GetChangeType(int index = 0)
    {
        return abilityInfo.GetChangeType(index);
    }

    /// <summary>
    /// 외부 변경 타입 리턴
    /// </summary>
    public eEvoAbilityChangeType GetExternalChangeType(int index = 0)
    {
        return abilityInfo.GetExternalChangeType(index);
    }

    /// <summary>
    /// 적용 가능 여부
    /// </summary>
    public bool IsPossibleApply()
    {
        return IsValid() && IsRunningCoolTime == false && (Count == 0 || remainCount > 0);
    }

    /// <summary>
    /// 적용 가능 여부
    /// </summary>
    public bool IsPossibleApply(object[] conditionValues)
    {
        if (IsPossibleApply())
        {
            switch (Condition)
            {
                default:
                    return true;
                case eEvoCondition.HP_LESS_THAN:
                    {
                        var cv = ConditionValue;
                        if (cv != null)
                        {
                            if (Enum.TryParse(ConditionValueType, Application.isEditor ? true : false, out eValueType vt))
                            {
                                int hp = (int)(GetConditionValue(conditionValues, 0) ?? 0);
                                int hpMax = (int)(GetConditionValue(conditionValues, 1) ?? 0);
                                if (vt.HasFlag(eValueType.RATIO))
                                    return (float)cv >= hp / hpMax;
                                if (vt.HasFlag(eValueType.ABS))
                                    return (float)cv >= hp;
                            }
                        }
                    }
                    break;
                case eEvoCondition.ATTACK:
                case eEvoCondition.DMGED:
                    {
                        return CheckUnitType(GetConditionValue(conditionValues, 0), GetConditionValue(conditionValues, 1));
                    }
                case eEvoCondition.SPECIFIC_SKILL:
                    {
                        int.TryParse(ConditionValue?.ToString(), out int cv);
                        if (cv > 0)
                        {
                            object inCV = GetConditionValue(conditionValues, 0);
                            if (inCV != null && int.TryParse(inCV.ToString(), out int value))
                                return cv == value;
                        }
                        else
                            return true;
                    }
                    break;
                case eEvoCondition.APPLY_SUB_EFFECT:
                    {
                        Type type = Type.GetType(ConditionValueType, Application.isEditor ? true : false, true);
                        if (type != null && type == typeof(eBattleTag))
                        {
                            string cv = ConditionValue?.ToString();
                            if (string.IsNullOrEmpty(cv) == false)
                            {
                                eBattleTag bt = Utility.ConvertEnum<eBattleTag>(cv);
                                return bt == (eBattleTag)(GetConditionValue(conditionValues, 0) ?? eBattleTag.NONE);
                            }
                        }
                    }
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// 유닛 타입 체크
    /// </summary>
    private bool CheckUnitType(object unitType, object categoryType)
    {
        string cv = ConditionValue?.ToString();
        if (string.IsNullOrEmpty(cv) == false)
        {
            Type type = Type.GetType(ConditionValueType, Application.isEditor ? true : false, true);
            if (type != null)
            {
                if (type == typeof(eUnitType))
                {
                    eUnitType ut = Utility.ConvertEnum<eUnitType>(cv);
                    return ut.HasFlag((eUnitType)(unitType ?? eUnitType.N));
                }
                else
                {
                    if (type == typeof(eUnitCategory))
                    {
                        eUnitCategory uc = Utility.ConvertEnum<eUnitCategory>(cv);
                        return uc.HasFlag((eUnitCategory)(categoryType ?? eUnitCategory.N));
                    }
                    else if (Value.GetType() == typeof(string))
                        return cv.ToLower().Contains(arrValue.ToString().ToLower());
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 적용상태 유지해야 하는 조건인지 여부
    /// </summary>
    public bool IsMaintainApplyCondition(bool checkType = true)
    {
        if (checkType)
        {
            switch (AbilityType)
            {
                case eEvoAbilityType.SPECIFIC_SKILL_META_DATA:
                case eEvoAbilityType.SPECIFIC_SKILL_META_DATA_VALUE:
                case eEvoAbilityType.SUB_EFFECT_META_DATA:
                case eEvoAbilityType.SUB_EFFECT_META_DATA_VALUE:
                    return true;
            }
        }

        switch (Condition)
        {
            case eEvoCondition.ALWAYS:
            case eEvoCondition.SPAWN:
            case eEvoCondition.DEATH:
            case eEvoCondition.IN_HOME:
            case eEvoCondition.IN_AWAY:
                return true;
        }

        return false;
    }

    /// <summary>
    /// 조건에 따른 능력치 적용
    /// </summary>
    public bool SetApply(bool isApply, eEvoCondition condition, bool isSetCoolTime = false, object[] conditionValues = null)
    {
        if (condition == eEvoCondition.ALL || condition == Condition)
        {
            if (PostAppliedProcessing() && isApply == IsApply)
                return true;

            if (IsPossibleApply(conditionValues))
            {
                if (SetApply(isApply, isSetCoolTime))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 적용 설정
    /// </summary>
    public bool SetApply(bool isApply, bool isSetCoolTime = false)
    {
        if (IsValid())
        {
            if (IsApply != isApply || (isApply && (ValueType.HasFlag(eValueType.ACCUMULATE) || ValueType.HasFlag(eValueType.COMPOUND_INTEREST))))
            {
                if (isApply && !IsApply && IsRunningCoolTime)
                    return false;

                #if UNITY_EDITOR
                LogConsole.WarningFormat(E_Category.Battle_Evo,
                    "[SetApply - {0}{1}{2}] Condition: {3}, AbilityType: {4}, Value : {5})", 
                    isApply ? "" : "<color=red>", isApply, isApply ? "" : "</color>", Condition, AbilityType, Value);
                #endif
                
                this.isApply.Value = isApply;
                if (isApply)
                {
                    if (ValueType.HasFlag(eValueType.ACCUMULATE) || ValueType.HasFlag(eValueType.COMPOUND_INTEREST))
                        appliedCount++;
                    remainCoolTime = 0f;
                    ApplyDuration();
                }
                else
                {
                    if (!ValueType.HasFlag(eValueType.ACCUMULATE))
                        appliedCount = 0;
                    if (isSetCoolTime)
                        ApplyCoolTime();
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 적용 처리
    /// </summary>
    public void AppliedProcessing()
    {
        if (IsApply)
        {
            // 횟수 차감
            if (remainCount > 0)
            {
                remainCount = Mathf.Max(0, remainCount - 1);
                if (remainCount == 0 && !IsApplying)
                    SetApply(false, true);
            }
        }
    }

    /// <summary>
    /// 적용 후 처리
    /// </summary>
    public bool PostAppliedProcessing(bool checkType = true)
    {
        if (IsApply && !IsApplying && !IsMaintainApplyCondition(checkType))
            return SetApply(false);
        return false;
    }

    /// <summary>
    /// 적용된 능력치 리턴
    /// </summary>
    public object ApplyValue(object value = null, bool isActiveAbility = true, int index = 0, object[] param = null)
    {
        object applyValue = null;
        if (AbilityInfo.IsSingleValueType(index))
        {
            float fValue = value != null ? Convert.ToSingle(value) : 0f;
            float fApplyValue = GetChangeType(index) == eEvoAbilityChangeType.CHANGE ? 0f : fValue;
            float fAbilityValue = (float)GetValue(index, value) * (IsApply ? 1f : -1f);

            // 외부 값 적용
            if (param != null && param.Length > 0)
            {
                string externalApplyType = ExternalApplyType.ToString();
                for (int i = 0; i < param.Length; ++i)
                {
                    if (param[i] != null && param[i] is JSONObject obj && obj.ContainsKey(externalApplyType))
                    {
                        float externalValue = obj.GetValue(externalApplyType).Float *
                            (GetExternalChangeType(index) == eEvoAbilityChangeType.DECREASE ? -1f : 1f);
                        fAbilityValue = CalculateValue(externalValue, 0, fAbilityValue, ExternalValueType);
                        break;
                    }
                }
            }

            applyValue = CalculateValue(fValue, fApplyValue, fAbilityValue, ValueType, appliedCount);

            #if UNITY_EDITOR
            if (isActiveAbility)
            {
                int i = 0;
                LogConsole.NormalFormat(E_Category.Battle_Evo, "ApplyValue: {" + i++ + "}->{" + i++ + "} " +
                    "(Condition: {" + i++ + "}, ConditionValue: {" + i++ + "}, AbilityType: {" + i++ + "}, " +
                    "SubType: {" + i++ + "}, ValueType: {" + i++ + "}, Value: {" + i++ + "}, AppliedCount: {" + i + "})",
                    value, applyValue, Condition, ConditionValue, AbilityType, AbilitySubType, ValueType, fAbilityValue, appliedCount);
            }
            #endif
        }
        else
        {
            switch (ValueType)
            {
                case eValueType.ENUM:
                    if (IsApply)
                    {
                        if (value != null)
                        {
                            Type valueType = value.GetType();
                            Type enumType = IsStatEnumType(valueType) ? valueType.GenericTypeArguments[0] : value.GetType();
                            int inValue = (int)Enum.Parse(enumType, value.ToString(), Application.isEditor ? true : false);
                            int setValue = (int)Enum.Parse(enumType, GetValue(index)?.ToString(), Application.isEditor ? true : false);
                            int result = 0;

                            switch (GetChangeType(index))
                            {
                                case eEvoAbilityChangeType.INCREASE:
                                case eEvoAbilityChangeType.ENABLE:
                                    result = Utility.SetFlag(inValue, setValue);
                                    break;
                                case eEvoAbilityChangeType.DECREASE:
                                case eEvoAbilityChangeType.DISABLE:
                                    result = Utility.UnsetFlag(inValue, setValue);
                                    break;
                                case eEvoAbilityChangeType.CHANGE:
                                default:
                                    result = setValue;
                                    break;
                            }

                            applyValue = Enum.ToObject(enumType, result);
                        }
                        else
                            applyValue = GetValue(index, value);
                    }
                    else
                        applyValue = value;
                    break;
                default:
                    applyValue = IsApply ? GetValue(index, value) : value;
                    break;
            }

            if (isActiveAbility)
            {
                #if UNITY_EDITOR
                int i = 0;
                LogConsole.NormalFormat(E_Category.Battle_Evo, "ApplyValue: {" + i++ + "}->{" + i++ + "} " +
                    "(Condition: {" + i++ + "}, ConditionValue: {" + i++ + "}, AbilityType: {" + i++ + "}, " +
                    "SubType: {" + i++ + "}, ValueType: {" + i++ + "}, Value: {" + i++ + "}, AppliedCount: {" + i + "})",
                    value, applyValue, Condition, ConditionValue, AbilityType, AbilitySubType, ValueType, applyValue, appliedCount);
                #endif
            }
        }

        if (isActiveAbility)
        {
            // 능력 발동 적용시
            AppliedProcessing();
        }

        return applyValue;
    }

    /// <summary>
    /// 연산 방법에 따라 값 적용
    /// </summary>
    private float CalculateValue(float value, float applyValue, float abilityValue, eValueType valueType, int applyCount = 1)
    {
        float result = applyValue;
        if (valueType.HasFlag(eValueType.RATIO))
        {
            if (value == 0f)
                result += abilityValue;
            else
            {
                if (valueType.HasFlag(eValueType.COMPOUND_INTEREST))
                    result += FixedUtility.CalculateCompoundInterest(value, abilityValue / 100f, applyCount);
                else if (valueType.HasFlag(eValueType.ACCUMULATE))
                    result += FixedUtility.CalculatePercent(value, abilityValue / 100f * applyCount);
                else
                    result += FixedUtility.CalculatePercent(value, abilityValue / 100f);
            }
        }
        else if (valueType.HasFlag(eValueType.ABS))
        {
            if (valueType.HasFlag(eValueType.ACCUMULATE))
                result += abilityValue * applyCount;
            else if (valueType.HasFlag(eValueType.MULTIPLY))
                result += value * abilityValue * applyCount;
            else
                result += abilityValue;
        }
        else if (IsApply && (value == 0 || valueType == eValueType.NONE))
        {
            switch (AbilityChangeType)
            {
                case eEvoAbilityChangeType.INCREASE:
                    result = value + abilityValue;
                    break;
                case eEvoAbilityChangeType.DECREASE:
                    result = value - abilityValue;
                    break;
                case eEvoAbilityChangeType.ENABLE:
                    if (applyValue != 0)
                        result = applyValue;
                    break;
                case eEvoAbilityChangeType.CHANGE:
                    result = abilityValue;
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// 누적값 적용
    /// </summary>
    public object ApplyAccumulateValue(object value, object defaultValue, object accumulateValue, int index = 0)
    {
        object applyValue = value;
        Type type = value.GetType();
        if (type.IsArray)
        {
            Array arr = value as Array;
            Array defaultArr = defaultValue as Array;
            Array accumulateArr = accumulateValue as Array;
            Type elementType = type.GetElementType();
            Array arrResult = Array.CreateInstance(elementType, arr.Length);

            for (int i = 0; i < arr.Length; ++i)
            {
                arrResult.SetValue(ApplyAccumulateValue(arr.GetValue(i), 
                    i < defaultArr.Length ? defaultArr.GetValue(i) : null, 
                    i < accumulateArr.Length ? accumulateArr.GetValue(i) : null, i), i);
            }

            applyValue = arrResult;
        }
        else
        {
            eEvoAbilityChangeType changeType = GetChangeType(index);
            if ((changeType == eEvoAbilityChangeType.INCREASE || changeType == eEvoAbilityChangeType.DECREASE) && AbilityInfo.IsSingleValueType(index))
            {
                float fValue = value != null ? float.Parse(value.ToString()) : 0f;
                float fAccumulateValue = accumulateValue != null ? float.Parse(accumulateValue.ToString()) : 0f;
                float fDefaultValue = defaultValue != null ? float.Parse(defaultValue.ToString()) : 0f;

                if (fAccumulateValue != fDefaultValue)
                {
                    float fResult = fAccumulateValue + fValue - fDefaultValue;
                    applyValue = CreateValue(fResult, type);

                    #if UNITY_EDITOR
                    int i = 0;
                    LogConsole.NormalFormat(E_Category.Battle_Evo, "Apply accumulate value: " +
                        "Result: {" + i++ + "}, Value: {" + i++ + "}, DefaultValue: {" + i++ + "}, AccumulateValue: {" + i++ + "}",
                        fResult, fValue, fDefaultValue, fAccumulateValue);
                    #endif
                }
            }
        }

        return applyValue;
    }

    /// <summary>
    /// 적용한 값 int로 리턴
    /// </summary>
    public int ApplyIntValue(object value = null, bool isActiveAbility = true, int index = 0)
    {
        return Convert.ToInt32(ApplyValue(value, isActiveAbility, index));
    }

    /// <summary>
    /// 적용한 값 float으로 리턴
    /// </summary>
    public float ApplyFloatValue(object value = null, bool isActiveAbility = true, int index = 0)
    {
        return Convert.ToSingle(ApplyValue(value, isActiveAbility, index));
    }

    /// <summary>
    /// 적용한 값 string으로 리턴
    /// </summary>
    public string ApplyStringValue(object value = null, bool isActiveAbility = true, int index = 0)
    {
        return Convert.ToString(ApplyValue(value, isActiveAbility, index));
    }

    /// <summary>
    /// 적용 횟수 설정
    /// </summary>
    private void ApplyCount()
    {
        if (Count > 0)
            remainCount = Count;
    }

    /// <summary>
    /// 적용 시간 설정
    /// </summary>
    private void ApplyDuration()
    {
        if (IsApply && Duration > 0f)
            remainDuration = Duration;
    }

    /// <summary>
    /// 쿨타임 적용
    /// </summary>
    private void ApplyCoolTime()
    {
        if (!IsApply && CoolTime > 0f)
            remainCoolTime = CoolTime;
    }

    public bool Update(float delta)
    {
        if (remainCoolTime > 0f)
            remainCoolTime = Mathf.Max(0f, remainCoolTime - delta);

        if (remainDuration > 0f)
        {
            remainDuration = Mathf.Max(0f, remainDuration - delta);
            if (remainDuration == 0f)
            {
                if (IsApply)
                    SetApply(false, true);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 능력치 적용한 값 생성
    /// </summary>
    public object CreateAppliedValue(object value, object[] param)
    {
        if (value == null)
            return null;

        object result;
        Type type = value.GetType();
        if (type.IsArray)
        {
            Array arr = value as Array;
            int length = arr.Length;
            switch (GetChangeType())
            {
                default:
                    length = Mathf.Max(arr.Length, arrValue.Length);
                    break;
                case eEvoAbilityChangeType.ENABLE:
                    length = arr.Length + arrValue.Length;
                    break;
                case eEvoAbilityChangeType.CHANGE:
                    length = arrValue.Length;
                    break;
            }

            Type elementType = type.GetElementType();
            Array arrResult = Array.CreateInstance(elementType, length);
            for (int i = 0; i < length; ++i)
            {
                eEvoAbilityChangeType changeType = GetChangeType(i);
                switch (changeType)
                {
                    default:
                        if (i < arr.Length)
                        {
                            if (i < arrValue.Length)
                                arrResult.SetValue(CreateAppliedValue(arr.GetValue(i), i, param), i);
                            else
                                arrResult.SetValue(arr.GetValue(i), i);
                        }
                        else
                        {
                            arrResult.SetValue(CreateValue(GetValue(i), elementType), i);
                        }
                        break;
                    case eEvoAbilityChangeType.ENABLE:
                        if (i < arr.Length)
                            arrResult.SetValue(arr.GetValue(i), i);
                        else
                            arrResult.SetValue(CreateValue(GetValue(i - arr.Length), elementType), i);
                        break;
                    case eEvoAbilityChangeType.CHANGE:
                        arrResult.SetValue(CreateValue(GetValue(i), elementType), i);
                        break;
                }
            }

            result = arrResult;
        }
        else
        {
            result = CreateAppliedValue(value, 0, param);
        }

        return result;
    }

    /// <summary>
    /// 능력치 적용한 값 생성
    /// </summary>
    public object CreateAppliedValue(object value, int index = 0, object[] param = null)
    {
        if (value == null)
            return null;

        object result = null;
        Type type = value.GetType();
        if (IsStatEnumType(type))
            result = ApplyStringValue(value, index: index);
        else
        {
            // 그 외 타입의 값 생성
            if (type.Name.Contains("Stat"))
            {
                // Stat 타입의 값 생성
                if (type.Equals(typeof(StatString)))
                    result = ApplyValue(index: index, param: param);
            }

            // 일반 값 생성
            if (result == null)
            {
                if (float.TryParse(value.ToString(), out float r))
                    result = ApplyValue(r, index: index, param: param);
                else
                    result = ApplyValue(index: index, param: param);
            }
        }

        return CreateValue(result != null ? result : value, type);
    }

    /// <summary>
    /// 값 생성
    /// </summary>
    public object CreateValue(object value, Type type = null)
    {
        if (value == null)
            return null;

        object result = value;
        if (type == null)
            type = value.GetType();

        if (IsStatEnumType(type))
        {
            // StatEnum<T> 타입의 값 생성
            result = Activator.CreateInstance(type, Enum.Parse(type.GenericTypeArguments[0], value.ToString()), true);
        }
        else
        {
            // 그 외 타입의 값 생성
            if (type.Name.Contains("Stat"))
            {
                // Stat 타입의 값 생성
                if (type.Equals(typeof(StatString)))
                    result = Activator.CreateInstance(type, value, true);
                else if (float.TryParse(value.ToString(), out float r))
                {
                    float v = type == typeof(float) || type == typeof(StatFloat) ? (float)value : Mathf.Floor((float)value);
                    result = Activator.CreateInstance(type, Convert.ChangeType(v, type.GetProperty("Value").PropertyType), true);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 액션카드 진화 능력치 리스트 리턴
    /// </summary>
    public static EvolutionAbilityData[] CreateActionCardEvoAbilityDataList(int[] aEvoMetaIDs)
    {
        if (aEvoMetaIDs != null && aEvoMetaIDs.Length > 0)
        {
            List<EvolutionAbilityData> evoList = new List<EvolutionAbilityData>();
            for (int i = 0; i < aEvoMetaIDs.Length; ++i)
            {
                EvolutionAbilityMetaData emd = MetaDataManager.Instance.GetActionCardEvolutionMetaData(aEvoMetaIDs[i]);
                if (emd != null && emd.AbilityInfoArr != null && emd.AbilityInfoArr.Length > 0)
                {
                    for (int j = 0; j < emd.AbilityInfoArr.Length; ++j)
                        evoList.Add(new EvolutionAbilityData(emd.AbilityInfoArr[j]));
                }
            }

            return evoList.ToArray();
        }

        return null;
    }

    /// <summary>
    /// StatEnum 타입인지 체크
    /// </summary>
    private bool IsStatEnumType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition().Equals(typeof(StatEnum<>));
    }
}
