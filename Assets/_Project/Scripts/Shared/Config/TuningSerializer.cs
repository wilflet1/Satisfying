using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Satisfying.Shared
{
    /// <summary>One editable knob discovered by reflection.</summary>
    public sealed class TuneField
    {
        public string Path;         // "move.walkSpeed" / "weapons[0].rpm"
        public string Category;     // "Lean", "Weapon: AR-15", ...
        public string Label;
        public string Tip;
        public float Min;
        public float Max;
        public object Target;
        public FieldInfo Field;

        public float Get()
        {
            object v = Field.GetValue(Target);
            if (v is float) return (float)v;
            if (v is int) return (int)v;
            if (v is bool) return ((bool)v) ? 1f : 0f;
            return 0f;
        }

        public void Set(float value)
        {
            value = MathK.Clamp(value, Min, Max);
            if (Field.FieldType == typeof(float)) Field.SetValue(Target, value);
            else if (Field.FieldType == typeof(int)) Field.SetValue(Target, MathK.RoundToInt(value));
            else if (Field.FieldType == typeof(bool)) Field.SetValue(Target, value >= 0.5f);
        }

        public bool IsToggle { get { return Field.FieldType == typeof(bool) || (Min == 0f && Max == 1f && Label != null && Label.EndsWith("Enabled")); } }
    }

    /// <summary>
    /// Reflection-driven text serialisation for tuning objects. Used for three things:
    /// saving presets to disk, pushing the host's values to clients, and building the tuning UI.
    /// Format is one "path=value" per line - diffable, hand-editable, and tiny on the wire.
    /// </summary>
    public static class TuningSerializer
    {
        public static string ToText(object root)
        {
            StringBuilder sb = new StringBuilder(2048);
            Walk(root, "", (path, target, field) =>
            {
                sb.Append(path).Append('=').Append(FormatValue(field.GetValue(target))).Append('\n');
            });
            return sb.ToString();
        }

        /// <summary>
        /// Only the values that differ from a reference object. The host syncs this instead of the whole
        /// table, so a normal session pushes a few hundred bytes rather than a few kilobytes.
        /// </summary>
        public static string ToTextDiff(object root, object reference)
        {
            Dictionary<string, FieldRef> baseline = BuildMap(reference);
            StringBuilder sb = new StringBuilder(512);
            Walk(root, "", (path, target, field) =>
            {
                string mine = FormatValue(field.GetValue(target));
                FieldRef other;
                if (baseline.TryGetValue(path, out other))
                {
                    string theirs = FormatValue(other.Field.GetValue(other.Target));
                    if (mine == theirs) return;
                }
                sb.Append(path).Append('=').Append(mine).Append('\n');
            });
            return sb.ToString();
        }

        public static void FromText(object root, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Dictionary<string, FieldRef> map = BuildMap(root);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                FieldRef fr;
                if (!map.TryGetValue(key, out fr)) continue;
                ApplyValue(fr, val);
            }
        }

        public static List<TuneField> Collect(object root)
        {
            List<TuneField> list = new List<TuneField>();
            Walk(root, "", (path, target, field) =>
            {
                TuneAttribute attr = (TuneAttribute)Attribute.GetCustomAttribute(field, typeof(TuneAttribute));
                if (attr == null) return;
                TuneField tf = new TuneField();
                tf.Path = path;
                tf.Category = ResolveCategory(attr.Category, target);
                tf.Label = attr.Label != null ? attr.Label : Prettify(field.Name);
                tf.Tip = attr.Tip;
                tf.Min = attr.Min;
                tf.Max = attr.Max;
                tf.Target = target;
                tf.Field = field;
                list.Add(tf);
            });
            return list;
        }

        static string ResolveCategory(string category, object target)
        {
            WeaponTuning w = target as WeaponTuning;
            if (w != null && category == "Weapon") return "Weapon: " + w.name;
            return category;
        }

        public static string Prettify(string fieldName)
        {
            StringBuilder sb = new StringBuilder(fieldName.Length + 6);
            for (int i = 0; i < fieldName.Length; i++)
            {
                char c = fieldName[i];
                if (i == 0) { sb.Append(char.ToUpperInvariant(c)); continue; }
                if (char.IsUpper(c) && !char.IsUpper(fieldName[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        struct FieldRef
        {
            public object Target;
            public FieldInfo Field;
        }

        static Dictionary<string, FieldRef> BuildMap(object root)
        {
            Dictionary<string, FieldRef> map = new Dictionary<string, FieldRef>(256);
            Walk(root, "", (path, target, field) =>
            {
                FieldRef fr;
                fr.Target = target;
                fr.Field = field;
                map[path] = fr;
            });
            return map;
        }

        delegate void FieldVisitor(string path, object target, FieldInfo field);

        static void Walk(object obj, string prefix, FieldVisitor visit)
        {
            if (obj == null) return;
            FieldInfo[] fields = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                Type t = f.FieldType;
                string path = prefix.Length == 0 ? f.Name : prefix + "." + f.Name;

                if (t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(string))
                {
                    visit(path, obj, f);
                }
                else if (t.IsArray)
                {
                    Array arr = f.GetValue(obj) as Array;
                    if (arr == null) continue;
                    Type elem = t.GetElementType();
                    if (elem.IsPrimitive || elem == typeof(string)) continue;
                    for (int k = 0; k < arr.Length; k++)
                    {
                        object item = arr.GetValue(k);
                        Walk(item, path + "[" + k.ToString(CultureInfo.InvariantCulture) + "]", visit);
                    }
                }
                else if (t.IsClass)
                {
                    Walk(f.GetValue(obj), path, visit);
                }
            }
        }

        static string FormatValue(object v)
        {
            if (v is float) return ((float)v).ToString("R", CultureInfo.InvariantCulture);
            if (v is int) return ((int)v).ToString(CultureInfo.InvariantCulture);
            if (v is bool) return ((bool)v) ? "1" : "0";
            return v == null ? "" : v.ToString();
        }

        static void ApplyValue(FieldRef fr, string raw)
        {
            Type t = fr.Field.FieldType;
            if (t == typeof(float))
            {
                float f;
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) fr.Field.SetValue(fr.Target, f);
            }
            else if (t == typeof(int))
            {
                int i;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) fr.Field.SetValue(fr.Target, i);
            }
            else if (t == typeof(bool))
            {
                fr.Field.SetValue(fr.Target, raw == "1" || raw.ToLowerInvariant() == "true");
            }
            else if (t == typeof(string))
            {
                fr.Field.SetValue(fr.Target, raw);
            }
        }
    }
}
