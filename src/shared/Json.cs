// A small JSON reader and writer with no dependencies, so the same code builds for
// .NET 8 (trimmed single file) and .NET Framework 4.8 (the tray).
//
// Parse returns Dictionary<string, object> for objects, object[] for arrays, string,
// double, bool or null. Write accepts those plus any IDictionary and IEnumerable.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

static class Json
{
    public static object Parse(string text)
    {
        int index = 0;
        object value = ReadValue(text, ref index);
        SkipSpace(text, ref index);
        if (index != text.Length) throw new FormatException("Unexpected text after JSON value at " + index);
        return value;
    }

    public static string Write(object value, bool indented)
    {
        var output = new StringBuilder();
        WriteValue(output, value, indented, 0);
        return output.ToString();
    }

    // ---- reading ----------------------------------------------------------

    static void SkipSpace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
    }

    static object ReadValue(string text, ref int index)
    {
        SkipSpace(text, ref index);
        if (index >= text.Length) throw new FormatException("Unexpected end of JSON");

        char c = text[index];
        if (c == '{') return ReadObject(text, ref index);
        if (c == '[') return ReadArray(text, ref index);
        if (c == '"') return ReadString(text, ref index);
        if (Matches(text, index, "true")) { index += 4; return true; }
        if (Matches(text, index, "false")) { index += 5; return false; }
        if (Matches(text, index, "null")) { index += 4; return null; }
        return ReadNumber(text, ref index);
    }

    static bool Matches(string text, int index, string word)
    {
        return string.CompareOrdinal(text, index, word, 0, word.Length) == 0;
    }

    static Dictionary<string, object> ReadObject(string text, ref int index)
    {
        var result = new Dictionary<string, object>();
        index++;
        SkipSpace(text, ref index);
        if (index < text.Length && text[index] == '}')
        {
            index++;
            return result;
        }
        while (true)
        {
            SkipSpace(text, ref index);
            if (index >= text.Length || text[index] != '"') throw new FormatException("Expected a key at " + index);
            string key = ReadString(text, ref index);
            SkipSpace(text, ref index);
            Expect(text, ref index, ':');
            result[key] = ReadValue(text, ref index);
            SkipSpace(text, ref index);
            if (index < text.Length && text[index] == ',')
            {
                index++;
                continue;
            }
            Expect(text, ref index, '}');
            return result;
        }
    }

    static object[] ReadArray(string text, ref int index)
    {
        var result = new List<object>();
        index++;
        SkipSpace(text, ref index);
        if (index < text.Length && text[index] == ']')
        {
            index++;
            return result.ToArray();
        }
        while (true)
        {
            result.Add(ReadValue(text, ref index));
            SkipSpace(text, ref index);
            if (index < text.Length && text[index] == ',')
            {
                index++;
                continue;
            }
            Expect(text, ref index, ']');
            return result.ToArray();
        }
    }

    static void Expect(string text, ref int index, char c)
    {
        if (index >= text.Length || text[index] != c) throw new FormatException("Expected '" + c + "' at " + index);
        index++;
    }

    static string ReadString(string text, ref int index)
    {
        var result = new StringBuilder();
        index++;
        while (true)
        {
            if (index >= text.Length) throw new FormatException("Unterminated string");
            char c = text[index++];
            if (c == '"') return result.ToString();
            if (c != '\\')
            {
                result.Append(c);
                continue;
            }
            if (index >= text.Length) throw new FormatException("Unterminated escape");
            char e = text[index++];
            switch (e)
            {
                case 'b': result.Append('\b'); break;
                case 'f': result.Append('\f'); break;
                case 'n': result.Append('\n'); break;
                case 'r': result.Append('\r'); break;
                case 't': result.Append('\t'); break;
                case 'u':
                    if (index + 4 > text.Length) throw new FormatException("Bad unicode escape");
                    result.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += 4;
                    break;
                default: result.Append(e); break;
            }
        }
    }

    static double ReadNumber(string text, ref int index)
    {
        int start = index;
        while (index < text.Length && "+-0123456789.eE".IndexOf(text[index]) >= 0) index++;
        if (index == start) throw new FormatException("Unexpected character at " + index);
        return double.Parse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // ---- writing ----------------------------------------------------------

    static void WriteValue(StringBuilder output, object value, bool indented, int depth)
    {
        if (value == null) output.Append("null");
        else if (value is string) WriteString(output, (string)value);
        else if (value is bool) output.Append((bool)value ? "true" : "false");
        else if (value is double || value is float || value is decimal || value is int || value is long)
            output.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
        else if (value is IDictionary) WriteObject(output, (IDictionary)value, indented, depth);
        else if (value is IEnumerable) WriteArray(output, (IEnumerable)value, indented, depth);
        else WriteString(output, value.ToString());
    }

    static void WriteObject(StringBuilder output, IDictionary value, bool indented, int depth)
    {
        output.Append('{');
        bool first = true;
        foreach (DictionaryEntry entry in value)
        {
            if (!first) output.Append(',');
            first = false;
            NewLine(output, indented, depth + 1);
            WriteString(output, entry.Key.ToString());
            output.Append(indented ? ": " : ":");
            WriteValue(output, entry.Value, indented, depth + 1);
        }
        if (!first) NewLine(output, indented, depth);
        output.Append('}');
    }

    static void WriteArray(StringBuilder output, IEnumerable value, bool indented, int depth)
    {
        output.Append('[');
        bool first = true;
        foreach (object item in value)
        {
            if (!first) output.Append(',');
            first = false;
            NewLine(output, indented, depth + 1);
            WriteValue(output, item, indented, depth + 1);
        }
        if (!first) NewLine(output, indented, depth);
        output.Append(']');
    }

    static void NewLine(StringBuilder output, bool indented, int depth)
    {
        if (indented) output.Append('\n').Append(' ', depth * 2);
    }

    static void WriteString(StringBuilder output, string value)
    {
        output.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\n': output.Append("\\n"); break;
                case '\r': output.Append("\\r"); break;
                case '\t': output.Append("\\t"); break;
                case '\b': output.Append("\\b"); break;
                case '\f': output.Append("\\f"); break;
                default:
                    if (c < 0x20) output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else output.Append(c);
                    break;
            }
        }
        output.Append('"');
    }
}
