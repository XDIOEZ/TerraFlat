using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 仅根据 CharacterSpeechContext.Facts 计算结构化条件，不执行脚本或自由表达式。
    /// </summary>
    public static class CharacterSpeechConditionEvaluator
    {
        private static readonly HashSet<string> LoggedErrors = new(StringComparer.Ordinal);

        #region 条件计算

        public static bool EvaluateAll(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechContext context)
        {
            if (entry?.Conditions == null || context == null)
                return false;

            for (int i = 0; i < entry.Conditions.Count; i++)
            {
                if (!Evaluate(entry, entry.Conditions[i], context.Facts))
                    return false;
            }

            return true;
        }

        private static bool Evaluate(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechCondition condition,
            IReadOnlyDictionary<string, string> facts)
        {
            bool exists = facts.TryGetValue(condition.Fact, out string actualValue);
            switch (condition.Operator)
            {
                case CharacterSpeechConditionOperator.Exists:
                    return exists;
                case CharacterSpeechConditionOperator.NotExists:
                    return !exists;
            }

            if (!exists)
                return false;

            string expectedValue = condition.Value ?? string.Empty;
            switch (condition.Operator)
            {
                case CharacterSpeechConditionOperator.Equal:
                    return AreEqual(actualValue, expectedValue);
                case CharacterSpeechConditionOperator.NotEqual:
                    return !AreEqual(actualValue, expectedValue);
                case CharacterSpeechConditionOperator.Greater:
                    return TryCompareNumbers(entry, condition, actualValue, expectedValue, out int greater) &&
                           greater > 0;
                case CharacterSpeechConditionOperator.GreaterOrEqual:
                    return TryCompareNumbers(entry, condition, actualValue, expectedValue, out int greaterOrEqual) &&
                           greaterOrEqual >= 0;
                case CharacterSpeechConditionOperator.Less:
                    return TryCompareNumbers(entry, condition, actualValue, expectedValue, out int less) &&
                           less < 0;
                case CharacterSpeechConditionOperator.LessOrEqual:
                    return TryCompareNumbers(entry, condition, actualValue, expectedValue, out int lessOrEqual) &&
                           lessOrEqual <= 0;
                default:
                    LogErrorOnce(entry, condition, "不支持的条件操作符。", actualValue);
                    return false;
            }
        }

        #endregion

        #region 数值与错误处理

        private static bool AreEqual(string actualValue, string expectedValue)
        {
            if (TryParseNumber(actualValue, out double actualNumber) &&
                TryParseNumber(expectedValue, out double expectedNumber))
            {
                return actualNumber.Equals(expectedNumber);
            }

            return string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryCompareNumbers(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechCondition condition,
            string actualValue,
            string expectedValue,
            out int comparison)
        {
            comparison = 0;
            if (!TryParseNumber(actualValue, out double actualNumber) ||
                !TryParseNumber(expectedValue, out double expectedNumber))
            {
                LogErrorOnce(
                    entry,
                    condition,
                    $"数值比较失败，实际值 '{actualValue}'，配置值 '{expectedValue}'。",
                    actualValue);
                return false;
            }

            comparison = actualNumber.CompareTo(expectedNumber);
            return true;
        }

        private static bool TryParseNumber(string value, out double number)
        {
            return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number);
        }

        private static void LogErrorOnce(
            CharacterSpeechConfigEntry entry,
            CharacterSpeechCondition condition,
            string message,
            string actualValue)
        {
            string key = $"{entry?.SourceName}|{entry?.Id}|{condition?.Fact}|{actualValue}|{message}";
            if (!LoggedErrors.Add(key))
                return;

            CharacterSpeechConfigIssue issue = new(
                entry?.SourceName,
                entry?.Id,
                condition?.Fact,
                message);
            Debug.LogError(issue.ToString());
        }

        #endregion
    }
}
