using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Satisfying.Game
{
    /// <summary>
    /// A small recursive-descent JSON reader.
    ///
    /// Unity ships JsonUtility, which maps JSON onto typed fields and cannot cope with glTF: the
    /// format is full of heterogeneous arrays, optional members and objects whose keys are attribute
    /// names rather than field names. So this is the fifty lines of parser that a glTF loader needs,
    /// and no more than that - it reads a document into nested dictionaries, lists, strings, doubles
    /// and bools, and everything above it asks questions of the result.
    ///
    /// It is not a validating parser and it is not trying to be. It reads well-formed JSON produced by
    /// an exporter, which is the only JSON it will ever see.
    /// </summary>
    public sealed class Json
    {
        readonly string _text;
        int _at;

        Json(string text) { _text = text; _at = 0; }

        public static object Parse(string text)
        {
            Json reader = new Json(text);
            reader.SkipWhitespace();
            return reader.ReadValue();
        }

        // ------------------------------------------------------------------ typed access helpers
        public static Dictionary<string, object> Object(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> Array(object value)
        {
            return value as List<object>;
        }

        public static object Member(object value, string key)
        {
            Dictionary<string, object> map = value as Dictionary<string, object>;
            object found;
            return map != null && map.TryGetValue(key, out found) ? found : null;
        }

        public static bool Has(object value, string key)
        {
            Dictionary<string, object> map = value as Dictionary<string, object>;
            return map != null && map.ContainsKey(key);
        }

        public static int Int(object value, int fallback = 0)
        {
            return value is double ? (int)(double)value : fallback;
        }

        public static float Float(object value, float fallback = 0f)
        {
            return value is double ? (float)(double)value : fallback;
        }

        public static string String(object value, string fallback = null)
        {
            return value as string ?? fallback;
        }

        public static int MemberInt(object value, string key, int fallback = 0)
        {
            return Int(Member(value, key), fallback);
        }

        public static float MemberFloat(object value, string key, float fallback = 0f)
        {
            return Float(Member(value, key), fallback);
        }

        public static string MemberString(object value, string key, string fallback = null)
        {
            return String(Member(value, key), fallback);
        }

        public static int Count(object value)
        {
            List<object> list = value as List<object>;
            return list != null ? list.Count : 0;
        }

        public static object At(object value, int index)
        {
            List<object> list = value as List<object>;
            return list != null && index >= 0 && index < list.Count ? list[index] : null;
        }

        // ------------------------------------------------------------------ the parser
        object ReadValue()
        {
            SkipWhitespace();
            if (_at >= _text.Length) return null;

            char c = _text[_at];
            switch (c)
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': _at += 4; return true;
                case 'f': _at += 5; return false;
                case 'n': _at += 4; return null;
                default: return ReadNumber();
            }
        }

        Dictionary<string, object> ReadObject()
        {
            Dictionary<string, object> map = new Dictionary<string, object>();
            _at++;                                  // {
            SkipWhitespace();
            if (_at < _text.Length && _text[_at] == '}') { _at++; return map; }

            while (_at < _text.Length)
            {
                SkipWhitespace();
                string key = ReadString();
                SkipWhitespace();
                _at++;                              // :
                map[key] = ReadValue();
                SkipWhitespace();

                if (_at >= _text.Length) break;
                if (_text[_at] == ',') { _at++; continue; }
                if (_text[_at] == '}') { _at++; break; }
                _at++;
            }
            return map;
        }

        List<object> ReadArray()
        {
            List<object> list = new List<object>();
            _at++;                                  // [
            SkipWhitespace();
            if (_at < _text.Length && _text[_at] == ']') { _at++; return list; }

            while (_at < _text.Length)
            {
                list.Add(ReadValue());
                SkipWhitespace();

                if (_at >= _text.Length) break;
                if (_text[_at] == ',') { _at++; continue; }
                if (_text[_at] == ']') { _at++; break; }
                _at++;
            }
            return list;
        }

        string ReadString()
        {
            _at++;                                  // opening quote
            StringBuilder builder = new StringBuilder();

            while (_at < _text.Length)
            {
                char c = _text[_at++];
                if (c == '"') break;

                if (c != '\\') { builder.Append(c); continue; }

                char escape = _text[_at++];
                switch (escape)
                {
                    case 'n': builder.Append('\n'); break;
                    case 't': builder.Append('\t'); break;
                    case 'r': builder.Append('\r'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        builder.Append((char)int.Parse(_text.Substring(_at, 4), NumberStyles.HexNumber,
                                                       CultureInfo.InvariantCulture));
                        _at += 4;
                        break;
                    default: builder.Append(escape); break;
                }
            }
            return builder.ToString();
        }

        object ReadNumber()
        {
            int start = _at;
            while (_at < _text.Length)
            {
                char c = _text[_at];
                bool part = (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E';
                if (!part) break;
                _at++;
            }

            double value;
            double.TryParse(_text.Substring(start, _at - start), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out value);
            return value;
        }

        void SkipWhitespace()
        {
            while (_at < _text.Length)
            {
                char c = _text[_at];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') break;
                _at++;
            }
        }
    }
}
