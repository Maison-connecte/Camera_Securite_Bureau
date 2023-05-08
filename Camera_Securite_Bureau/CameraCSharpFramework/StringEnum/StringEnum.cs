using System;
using System.Collections.Generic;
using System.Reflection;

namespace CameraCSharpFramework.Misc
{
    public class StringEnum
    {
        protected string _value;

        public StringEnum() { }
        public StringEnum(string str)
        {
            _value = str;
        }


        public static List<T> GetValues<T>() where T : class
        {
            List<T> newList = new List<T>();
            Type type = typeof(T);

            foreach(FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                T instance = Activator.CreateInstance<T>();
                FieldInfo fieldInfo = instance.GetType().GetField("_value", BindingFlags.NonPublic | BindingFlags.Instance);

                if(fieldInfo != null)
                {
                    TypedReference reference = __makeref(instance);
                    fieldInfo.SetValueDirect(reference, field.GetValue(null));
                }
                newList.Add(instance);   
            }

            return newList;
        }

        public static T Parse<T>(string toParse) where T : class
        {
            Dictionary<string, string> values = GetKeyValuePairs<T>();

            foreach(KeyValuePair<string, string> pair in values)
            {
                if(pair.Value.ToLower() == toParse.ToLower())
                {
                    T instance = Activator.CreateInstance<T>();
                    TypedReference reference = __makeref(instance);
                    instance.GetType().GetField("_value", BindingFlags.NonPublic | BindingFlags.Instance).SetValueDirect(reference, pair.Value);

                    return instance;
                }
            }

            return null;
        }

        public static Dictionary<string, string> GetKeyValuePairs<T>() where T : class
        {
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>();
            Type type = typeof(T);

            foreach(FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                keyValuePairs.Add(field.Name, field.GetValue(null).ToString());
            }

            return keyValuePairs;
        }

        public static bool Contains<T>(string toCheck) where T : class
        {
            bool contains = false;

            Dictionary<string, string> keyValuePairs = GetKeyValuePairs<T>();

            foreach(KeyValuePair<string, string> value in keyValuePairs)
            {
                if(toCheck.Contains(value.Value))
                {
                    contains = true;
                }
            }

            return contains;
        }

        public static implicit operator StringEnum(string str)
        {
            return new StringEnum(str);
        }

        public static implicit operator string(StringEnum str)
        {
            return str._value;
        }

        public static bool operator ==(StringEnum strEnum, string str) {
            return strEnum.ToString() == str;
        }

        public static bool operator ==(string str, StringEnum strEnum)
        {
            return strEnum.ToString() == str;
        }

        public static bool operator !=(StringEnum strEnum, string str)
        {
            return strEnum.ToString() != str;
        }

        public static bool operator !=(string str, StringEnum strEnum)
        {
            return strEnum.ToString() != str;
        }

        public override string ToString()
        {
            return _value;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (ReferenceEquals(obj, null))
            {
                return false;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return 0;
        }
    }
}
