using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using XLua;

namespace FlatWorld.Gameplay.Events
{
    internal static class GameEventLuaData
    {
        #region Lua 与存档数据转换

        // 只允许有限的纯数据，避免把闭包、游戏对象或循环引用写进事件存档。
        public static JToken Read(object value)
        {
            int remaining = 16384;
            return Read(value, 0, ref remaining);
        }

        private static JToken Read(object value, int depth, ref int remaining)
        {
            if (depth > 32 || --remaining < 0)
                throw new InvalidOperationException("Lua 事件数据超过深度或数量限制，可能存在循环引用。");
            if (value == null) return JValue.CreateNull();
            if (value is string text) return new JValue(text);
            if (value is bool flag) return new JValue(flag);
            if (value is LuaTable table)
            {
                List<object> keys = new();
                object markerValue = table.Get<object>("__eventArray");
                if (markerValue != null && markerValue is not bool)
                {
                    (markerValue as LuaBase)?.Dispose();
                    throw new InvalidOperationException("__eventArray 是保留的布尔数组标记。");
                }
                bool array = markerValue is true;
                bool numeric = true;
                foreach (object key in table.GetKeys<object>())
                {
                    if (key is string marker && marker == "__eventArray") continue;
                    if (keys.Count >= remaining)
                        throw new InvalidOperationException("Lua 事件表过大。");
                    if (key is not string && key is not double && key is not long && key is not int)
                    {
                        (key as LuaBase)?.Dispose();
                        throw new InvalidOperationException("Lua 事件表只能使用字符串或连续整数键。");
                    }
                    keys.Add(key);
                    numeric &= key is not string;
                }
                array |= keys.Count > 0 && numeric;
                if (array)
                {
                    HashSet<int> indexes = new();
                    foreach (object key in keys)
                    {
                        if (key is string) throw new InvalidOperationException("Lua 数组不能包含字符串键。");
                        double number = Convert.ToDouble(key, CultureInfo.InvariantCulture);
                        if (number < 1 || number > keys.Count || number != Math.Floor(number))
                            throw new InvalidOperationException("Lua 数组必须从 1 开始且没有空洞。");
                        indexes.Add((int)number);
                    }
                    if (indexes.Count != keys.Count) throw new InvalidOperationException("Lua 数组键重复。");
                    JArray result = new();
                    for (int i = 1; i <= keys.Count; i++)
                        result.Add(ReadField(table, i, depth, ref remaining));
                    return result;
                }
                JObject map = new();
                foreach (object key in keys)
                {
                    if (key is not string name)
                        throw new InvalidOperationException("Lua 对象不能混用数字和字符串键。");
                    map[name] = ReadField(table, name, depth, ref remaining);
                }
                return map;
            }
            if (value is double or float or int or long or uint or ulong or short or byte or decimal)
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number))
                    throw new InvalidOperationException("Lua 事件数据不能包含 NaN 或 Infinity。");
                // JSON 整数与 Lua 数值统一表示，避免读档后相同子行动参数因数值类型不同而被拒绝。
                return number == Math.Floor(number) && Math.Abs(number) <= 9007199254740991d
                    ? new JValue((long)number) : new JValue(number);
            }
            throw new InvalidOperationException($"Lua 事件数据不支持 {value.GetType().Name}。");
        }

        private static JToken ReadField<TKey>(LuaTable table, TKey key, int depth, ref int remaining)
        {
            table.Get(key, out object value);
            try { return Read(value, depth + 1, ref remaining); }
            finally { (value as LuaBase)?.Dispose(); }
        }

        public static LuaTable CreateTable(LuaEnv environment, JToken token)
        {
            LuaTable table = environment.NewTable();
            try
            {
                if (token is JArray array)
                {
                    table.Set("__eventArray", true);
                    for (int i = 0; i < array.Count; i++) Set(table, i + 1, environment, array[i]);
                }
                else if (token is JObject map)
                {
                    foreach (JProperty field in map.Properties()) Set(table, field.Name, environment, field.Value);
                }
                else throw new InvalidOperationException("Lua 事件参数或状态必须是对象或数组。");
                return table;
            }
            catch { table.Dispose(); throw; }
        }

        private static void Set<TKey>(LuaTable parent, TKey key, LuaEnv environment, JToken token)
        {
            if (token is JContainer)
            {
                using LuaTable nested = CreateTable(environment, token);
                parent.Set(key, nested);
            }
            else
            {
                object value = token.Type switch
                {
                    JTokenType.Integer => token.Value<double>(),
                    JTokenType.Float => token.Value<double>(),
                    JTokenType.Boolean => token.Value<bool>(),
                    JTokenType.String => token.Value<string>(),
                    JTokenType.Null => null,
                    _ => throw new InvalidOperationException($"不支持事件数据类型 {token.Type}。")
                };
                parent.Set(key, value);
            }
        }

        #endregion
    }
}
