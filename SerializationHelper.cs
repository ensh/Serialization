using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace VTBF.BackOffice.Infrastructure.Utils.Serialization
{
    using ConvertGetter = Func<Type, IEnumerable<SerializationHelper.PropertyConverter>>;

    public static class SerializationHelper
    {
        public static string SerializeEntry(object entry, bool useDeepSerialization = false)
        {
            using (var serializer = new XmlSerializer { SerializePrimaryPropertiesOnly = true, UseDeepSerialization = useDeepSerialization })
            {
                XmlDocument xmlDocument = serializer.Serialize(entry);
                using (var stringWriter = new StringWriter())
                {
                    using (var textWriter = new XmlTextWriter(stringWriter))
                    {
                        xmlDocument.WriteTo(textWriter);
                        return stringWriter.ToString();
                    }
                }
            }
        }

        public static string SerializeArray<T>(params T[] entry)
            where T : struct
        {
            return string.Join(valueDelimiter[0], entry.Select(e => e.ToString()));
        }

        public static string SerializeEmptyEnumerable()
        {
            return "";
        }

        public static string SerializeEnumerable<T>(this IEnumerable<T> entry)
            where T : struct
        {
            return string.Join(valueDelimiter[0], (entry ?? Enumerable.Empty<T>()).Select(e => e.ToString()));
        }

        public static string SerializeCollection(this ICollection entry)
        {
            return string.Join(valueDelimiter[0], entry.OfType<object>().Select(e => e.ToString()));
        }

        public static string SerializeEntryEnumerable<T>(this IEnumerable<T> entry)
        {
            return string.Join(valueDelimiter[0], (entry ?? Enumerable.Empty<T>()).Select(e => SerializeEntryProperties(e)));
        }

        public static string SerializeEntryArray<T>(params T[] entries)
        {
            return SerializeEntryEnumerable(entries);
        }

        public static string DefaultValueTextGetter<T>(T entry, PropertyConverter converter)
        {
            if (TypeInfo.IsCollection(converter))
            {
                return string.Join(arrayValueDelimiter[0], converter.GetCollection(entry).ToStringCollection());
            }

            return converter.Convert(entry);
        }

        public static string EntryValueTextGetter(this object entry, PropertyConverter converter)
        {
            if (converter.IsCollection)
            {
                return string.Concat(arrayValueDelimiter[1],
                    string.Join(valueDelimiter[0], converter.GetCollection(entry).EntryCollectionToString(converter)),
                    arrayValueDelimiter[2]);
            }

            string result;
            if (converter.TryConvert(entry, out result))
                return result;

            return SerializeEntryProperties(converter.GetValue(entry), EntryValueTextGetter, converter);
        }

        public static string SerializeEntryProperties<T>(this T entry)
        {
            return SerializeEntryProperties(entry, DefaultValueTextGetter);
        }

        public static string SerializeEntryProperties(this object entry)
        {
            return SerializeEntryProperties(entry, EntryValueTextGetter, null);
        }

        public static string SerializeEntryProperties<T>(this T entry, Func<T, PropertyConverter, string> valueTextGetter)
        {
            var converters = GetEntryConverter<T>();

            return string.Concat(entryDelimiter[1],
                string.Join(valueDelimiter[0], converters.Select(converter => 
                {
                    var valueText = valueTextGetter(entry, converter);
                    if (valueText == null)
                        return null;

                    return string.Concat(memberDelimiter, converter.Name, propertyValueDelimiter[0],
                        valueText, memberDelimiter);

                }).OfType<string>())
                , entryDelimiter[2]);
        }

        public static string SerializeEntryProperties(this object entry, Func<object, PropertyConverter, string> valueTextGetter, ConvertGetter convertGetter = null)
        {
            if (entry == null)
                return null;

            var converters = GetEntryConverter(entry.GetType(), convertGetter);

            return string.Concat(entryDelimiter[1],
                string.Join(valueDelimiter[0], converters.Select(converter => 
                {
                    var valueText = valueTextGetter(entry, new PropertyConverter(converter, convertGetter));
                    if (valueText == null)
                        return null;

                    if (valueText.StartsWith(entryDelimiter[1]) || valueText.StartsWith(arrayValueDelimiter[1]))
                    {
                        // объект
                        return string.Concat(memberDelimiter, converter.Name, propertyValueDelimiter[1],
                            valueText);
                    }

                    return string.Concat(memberDelimiter, converter.Name, propertyValueDelimiter[0],
                        valueText, memberDelimiter);
                }).OfType<string>())
                , entryDelimiter[2]);
        }

        private static IEnumerable<string> ToStringCollection(this ICollection collection)
        {
            foreach (var element in collection)
            {
                yield return element.ToString();
            }
        }

        public static IEnumerable<string> EntryCollectionToString(this ICollection collection, ConvertGetter convertGetter = null)
        {
            var elements = collection.GetEnumerator();
            if (elements.MoveNext())
            {
                // на всякий случай, найдем первый ненулевой
                while (elements.Current == null)
                {
                    if (!elements.MoveNext())
                        yield break;
                }

                if (elements.Current is string)
                {
                    do
                    {
                        yield return (string)elements.Current;

                    } while (elements.MoveNext());
                    yield break;
                }

                if (elements.Current.GetType().IsPrimitive)
                {
                    do
                    {
                        yield return elements.Current.ToString();

                    } while (elements.MoveNext());
                    yield break;
                }

                do
                {
                    yield return SerializeEntryProperties(elements.Current, EntryValueTextGetter, convertGetter);

                } while (elements.MoveNext());
            }
        }

        static IDictionary<Type, IEnumerable<PropertyConverter>> entryConverters = new ConcurrentDictionary<Type, IEnumerable<PropertyConverter>>();
        public static IEnumerable<PropertyConverter> GetEntryConverter<T>(ConvertGetter converter = null)
        {
            return GetEntryConverter(typeof(T), converter);
        }

        public static IEnumerable<PropertyConverter> GetEntryConverter(Type type, ConvertGetter converter = null)
        {
            return (converter ?? GetEntryConverter)(type);
        }

        public static IEnumerable<PropertyConverter> GetEntryConverter(Type type)
        {
            IEnumerable<PropertyConverter> converters;
            if (!entryConverters.TryGetValue(type, out converters))
            {
                converters = type.GetProperties().Select(propertyInfo => new PropertyConverter(propertyInfo))
                    .Where(pc => (TypeConverter)pc != null)
                    .ToArray();

                entryConverters.Add(type, converters);
            }

            return converters;
        }

        public static Dictionary<string, string> DeserializeContext(string entry)
        {
            var result = DeserializeEntry<Dictionary<string, string>>(entry);
            return UpgradeContext(result);
        }

        public static T DeserializeEntry<T>(string entry)
        {
            using (var Deserializer = new XmlDeserializer())
            {
                return Deserializer.Deserialize<T>(entry);
            }
        }

        public static Dictionary<string, string> UpgradeContext(Dictionary<string, string> context)
        {
            return UpgradeContext((IDictionary<string, string>)context) as Dictionary<string, string>;
        }

        public static IDictionary<string, string> UpgradeContext(IDictionary<string, string> context)
        {
            var entries = context.ToArray();

            foreach (var entry in entries)
            {
                if (entry.Value != null && entry.Value.StartsWith("<?xml"))
                {
                    //старая версия контекста пытаемся переварить ее, учитываем простые типы
                    var xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml(entry.Value);

                    var stringValue = string.Join(", ", xmlDocument
                        .SelectNodes("//object/items/item").OfType<XmlNode>()
                        .Select(node => node.InnerText));

                    context[entry.Key] = stringValue;
                }
            }
            return context;
        }

        public static readonly string[] valueDelimiter = new[] { ", " };
        public static readonly string[] arrayValueDelimiter = new[] { "; ", "[", "]" };
        public static readonly string[] entryDelimiter = new[] { " }, { ", "{ ", " }" };
        public static readonly string[] propertyValueDelimiter = new[] { "\" : \"", "\" : " };
        static Type[] stringTypeParam = new[] { typeof(string) };
        static Type[] enumTypeParam = new[] { typeof(Type), typeof(string) };
        static IDictionary<Type, TypeConverter> typeConverters = new ConcurrentDictionary<Type, TypeConverter>();

        public static TypeConverter GetConverter<T>()
        {
            return GetConverter(typeof(T));
        }

        public static TypeConverter GetConverter(this Type type)
        {
            TypeConverter converter;
            if (!typeConverters.TryGetValue(type, out converter))
            {
                typeConverters.Add(type, converter = TypeDescriptor.GetConverter(type));
            }
            return converter;
        }

        public static IEnumerable<T> DeserializeEnumerable<T>(string entry)
        {
            var converter = GetConverter<T>();
            foreach (var value in (entry ?? "").Split(valueDelimiter, StringSplitOptions.RemoveEmptyEntries))
                yield return (T)converter.ConvertFrom(value);
        }

        public static IEnumerable DeserializeEnumerable(Type type, string entry, out int count)
        {
            var converter = GetConverter(type);
            var values = (entry ?? "").Split(valueDelimiter, StringSplitOptions.RemoveEmptyEntries);

            count = values.Length;

            return values.Select(value => converter.ConvertFrom(value));
        }

        public static T[] DeserializeArray<T>(string entry)
        {
            return DeserializeEnumerable<T>(entry).ToArray();
        }

        public static T DeserializeEntry<T>(string entry, Action<T, PropertyInfo, string> applyDataMethod)
        {
            return DeserializeEntryEnumerable<T>(entry, applyDataMethod).FirstOrDefault();
        }

        public static IEnumerable<T> DeserializeEntryEnumerable<T>(string entry, Action<T, PropertyInfo, string> applyDataMethod = null)
        {
            foreach (var entryText in EntryEnumerable(entry ?? ""))
            {
                var newEntry = Activator.CreateInstance<T>();

                foreach (var property in EntryProperties(entryText).Select(propertyText => 
                    new
                    {
                        Info = typeof(T).GetProperty(propertyText.Key),
                        propertyText.Value
                    }))
                {
                    if (property.Info != null)
                    {
                        if (property.Info.PropertyType == typeof(string))
                            property.Info.SetValue(newEntry, property.Value);
                        else
                        if (property.Info.PropertyType.IsValueType)
                        {
                            var converter = GetConverter(property.Info.PropertyType);
                            if (converter != null)
                            {
                                property.Info.SetValue(newEntry, converter.ConvertFrom(property.Value));
                            }
                        }
                        else
                        if (property.Info.PropertyType.IsArray)
                        {
                            var elementType = property.Info.PropertyType.GetElementType();
                            var converter = GetConverter(elementType);
                            if (converter != null)
                            {
                                Func<string, TypeConverter, object> arrayGetter;
                                if (arrayConverters.TryGetValue(elementType, out arrayGetter))
                                {
                                    property.Info.SetValue(newEntry, arrayGetter(property.Value, converter));
                                }
                            }
                        }
                        else if (applyDataMethod != null)
                        {
                            applyDataMethod(newEntry, property.Info, property.Value);
                        }
                        else
                            throw new InvalidDataException();
                    }
                }

                yield return newEntry;
            }
        }

        private static Dictionary<Type, Func<string, TypeConverter, object>> arrayConverters = new Dictionary<Type, Func<string, TypeConverter, object>>()
            {
                { typeof(short), (values, converter) => Array.ConvertAll<string, short>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (short)converter.ConvertFrom(s))
                },
                { typeof(ushort), (values, converter) => Array.ConvertAll<string, ushort>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (ushort)converter.ConvertFrom(s))
                },
                { typeof(int), (values, converter) => Array.ConvertAll<string, int>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (int)converter.ConvertFrom(s))
                },
                { typeof(uint), (values, converter) => Array.ConvertAll<string, uint>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (uint)converter.ConvertFrom(s))
                },
                { typeof(long), (values, converter) => Array.ConvertAll<string, long>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (long)converter.ConvertFrom(s))
                },
                { typeof(ulong), (values, converter) => Array.ConvertAll<string, ulong>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (ulong)converter.ConvertFrom(s))
                },
                { typeof(decimal), (values, converter) => Array.ConvertAll<string, decimal>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (decimal)converter.ConvertFrom(s))
                },
                { typeof(float), (values, converter) => Array.ConvertAll<string, float>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (float)converter.ConvertFrom(s))
                },
                { typeof(double), (values, converter) => Array.ConvertAll<string, double>(
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries),
                        (s) => (double)converter.ConvertFrom(s))
                },

                { typeof(string), (values, converter) => 
                        values.Split(arrayValueDelimiter, StringSplitOptions.RemoveEmptyEntries)
                },
            };

        public static T[] DeserializeEntryArray<T>(string entry)
        {
            return DeserializeEntryEnumerable<T>(entry).ToArray();
        }

        public static IEnumerable<string> EntryEnumerable(string entry)
        {
            return EntryEnumerable(entry, 0).Select(e => e.Value);
        }

        public const char memberDelimiter = '"';
        public const char startClassDelimiter = '{';
        public const char finishClassDelimiter = '}';
        enum EntryState { None, Start, Finish}
        public static IEnumerable<KeyValuePair<int, string>> EntryEnumerable(string entry, int from)
        {
            EntryState state = EntryState.None; // 1 - объект, 2 - конец объекта

            int startEntryCounter = 0;
            for (int i = from, j = 0; i < entry.Length; i++)
            {
                switch (entry[i])
                {
                    case memberDelimiter:
                        switch(state)
                        {
                            case EntryState.Start:
                                i = entry.IndexOf(memberDelimiter, i + 1);
                                break;
                            case EntryState.Finish:
                                yield break;
                        }
                        break;
                    case startClassDelimiter:
                        if (state == EntryState.None || state == EntryState.Finish)
                        {
                            state = EntryState.Start;
                            j = i;
                        }
                        startEntryCounter++;
                        break;
                    case finishClassDelimiter:
                        if (state != EntryState.None && startEntryCounter > 0)
                        {
                            startEntryCounter--;

                            if (startEntryCounter == 0)
                            {
                                yield return new KeyValuePair<int, string>(i, entry.Substring(++j, i - j).Trim());

                                state = EntryState.Finish;
                            }
                        }
                        break;
                }
            }

            from = entry.Length;
        }

        public static char[] memberDelimeterArray = new[] { memberDelimiter }; 
        enum PropertyState { None, Start }
        public static IEnumerable<KeyValuePair<string, string>> EntryProperties(string entry)
        {
            PropertyState state = PropertyState.None; // 1 - свойство

            for (int i = 0, j = 0; i < entry.Length; i++)
            {
                switch (entry[i])
                {
                    case memberDelimiter:
                        switch(state)
                        {
                            case PropertyState.None:
                                j = i;
                                i = entry.IndexOf(memberDelimiter, i + 1);
                                state = PropertyState.Start;
                                break;
                            case PropertyState.Start:
                                state = PropertyState.None;

                                if (entry[i + 1] != startClassDelimiter)
                                {
                                    i = entry.IndexOf(memberDelimiter, i + 1);

                                    var propertyEntry = entry.Substring(++j, i - j).Trim();
                                    var propertyValuePair = propertyEntry.Split(memberDelimeterArray, StringSplitOptions.RemoveEmptyEntries);
                                    if (propertyValuePair.Length == 3)
                                    {
                                        yield return new KeyValuePair<string, string>(propertyValuePair[0], propertyValuePair[2]);
                                    }
                                }
                                else
                                {
                                    var propertyEntry = entry.Substring(++j, i - j).Trim();
                                    var propertyValuePair = propertyEntry.Split(memberDelimeterArray, StringSplitOptions.RemoveEmptyEntries);

                                    i++;
                                    yield return new KeyValuePair<string, string>(propertyValuePair[0],
                                        string.Join(valueDelimiter[0], EntryEnumerable(entry, i)
                                            .Select(e => 
                                            {
                                                i = e.Key + 1;
                                                return string.Concat(entryDelimiter[1], e.Value, entryDelimiter[2]);
                                            })
                                        )
                                    );
                                };
                                break;
                        }
                        break;
                }
            }
        }

        public struct PropertyConverter
        {
            private readonly PropertyInfo propertyInfo;
            private readonly TypeConverter typeConverter;
            private readonly ConvertGetter entryConverter;

            public PropertyConverter(PropertyInfo propertyInfo, ConvertGetter entryConverter = null)
            {
                this.propertyInfo = propertyInfo;
                this.entryConverter = entryConverter;
                typeConverter = propertyInfo.PropertyType.GetConverter();
            }

            public string Name
            {
                get
                {
                    return propertyInfo.Name;
                }
            }

            public bool IsCollection
            {
                get
                {
                    return TypeInfo.IsCollection(propertyInfo.PropertyType);
                }
            }

            public static implicit operator PropertyInfo(PropertyConverter pc)
            {
                return pc.propertyInfo;
            }

            public static implicit operator TypeConverter(PropertyConverter pc)
            {
                return pc.typeConverter;
            }

            public static implicit operator Type(PropertyConverter pc)
            {
                return pc.propertyInfo.PropertyType;
            }

            public static implicit operator ConvertGetter(PropertyConverter pc)
            {
                return pc.entryConverter;
            }

            public object GetValue(object instance)
            {
                return propertyInfo.GetValue(instance);
            }

            public ICollection GetCollection(object instance)
            {
                return (ICollection)propertyInfo.GetValue(instance);
            }

            public bool TryConvert(object instance, out string result)
            {
                result = default(string);

                if (propertyInfo.PropertyType == typeof(string))
                {
                    result = (string)propertyInfo.GetValue(instance);
                    return true;
                }

                if (propertyInfo.PropertyType.IsValueType)
                {
                    result = typeConverter.ConvertToString(propertyInfo.GetValue(instance));
                    return true;
                }

                return false;
            }

            public string Convert(object instance)
            {
                string result;
                TryConvert(instance, out result);
                return result;
            }
        }
    }
}