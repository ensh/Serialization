using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace VTBF.BackOffice.Infrastructure.Utils.Serialization
{
    public class XmlDeserializer : IDisposable
    {
        private readonly Dictionary<string, Assembly> assemblyCache = new Dictionary<string, Assembly>(); // Found Assemblies
        private readonly Dictionary<string, Assembly> assemblyRegister = new Dictionary<string, Assembly>(); // Predefined Assemblies
        private readonly Dictionary<Type, TypeConverter> typeConverterCache = new Dictionary<Type, TypeConverter>(); // used TypeConverters

        private XmlSerializationTag taglib = new XmlSerializationTag();
        private Dictionary<string, TypeInfo> typeDictionary = new Dictionary<string, TypeInfo>(); // Parsed Types

        public XmlSerializationTag TagLib
        {
            get { return taglib; }
            set { taglib = value; }
        }

        #region XmlDeserializer Properties

        /// <summary>
        /// Gets whether the current root node provides a type dictionary.
        /// </summary>
        protected bool HasTypeDictionary
        {
            get
            {
                if (typeDictionary != null && typeDictionary.Count > 0)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Gets or sets whether creation errors shall be ignored.
        /// Creation errors can occur if e.g. a type has no parameterless constructor
        /// and an instance cannot be instantiated from String.
        /// </summary>
        [Description("Gets or sets whether creation errors shall be ignored.")]
        public bool IgnoreCreationErrors { get; set; }

        #endregion XmlDeserializer Properties

        #region Deserialize

        /// <summary>
        /// Deserializes a type T from string.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="str"></param>
        /// <returns></returns>
        public T Deserialize<T>(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                var document = new XmlDocument();
                document.LoadXml(str);
                return Deserialize<T>(document);
            }
            return default(T);
        }

        /// <summary>
        /// Deserialzes an object from XmlDocument.
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        public T Deserialize<T>(XmlDocument document)
        {
            XmlNode node = document.SelectSingleNode(taglib.OBJECT_TAG);
            return (T)Deserialize(node, typeof(T));
        }


        /// <summary>
        /// Deserializes an Object from the specified XmlNode. 
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public object Deserialize(XmlNode node, Type rootType = null)
        {
            // Clear previous collections
            Reset();
            AddAssemblyRegisterToCache();

            XmlNode rootNode = node;
            if (!rootNode.Name.Equals(taglib.OBJECT_TAG))
            {
                if ((rootNode = node.SelectSingleNode(taglib.OBJECT_TAG)) == null)
                {
                    throw new ArgumentException(string.Format("Invalid node. The specified node or its direct children do not contain a {0} tag.", taglib.OBJECT_TAG), "node");
                }
            }

            // Load TypeDictionary
            typeDictionary = ParseTypeDictionary(rootNode);

            // Get the Object            
            var objectInstance = (rootType == null || rootType .IsArray) ? GetObject(rootNode) : Activator.CreateInstance(rootType);
            return GetProperties(objectInstance, rootNode);
        }

        /// <summary>
        /// Parses the TypeDictionary (if given).
        /// </summary>
        /// <param name="parentNode"></param>
        /// <returns></returns>
        /// <remarks>
        /// The TypeDictionary is Hashtable in which TypeInfo items are stored.
        /// </remarks>
        private Dictionary<string, TypeInfo> ParseTypeDictionary(XmlNode parentNode)
        {
            var dict = new Dictionary<string, TypeInfo>();

            XmlNode dictNode = parentNode.SelectSingleNode(taglib.TYPE_DICTIONARY_TAG);
            if (dictNode != null)
            {
                object obj = GetObject(dictNode);

                if (obj is Dictionary<string, TypeInfo>)
                {
                    dict = (Dictionary<string, TypeInfo>)obj;
                    GetProperties(dict, dictNode);
                }
            }
            return dict;
        }

        #endregion Deserialize

        #region Properties & values

        /// <summary>
        /// Reads the properties of the specified node and sets them an the parent object.
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="node"></param>
        /// <remarks>
        /// This is the central method which is called recursivly!
        /// </remarks>
        protected object GetProperties(object parent, XmlNode node)
        {
            if (parent == null)
                return parent;

            // Get the properties
            XmlNodeList nl = node.SelectNodes(string.Concat(taglib.PROPERTIES_TAG, "/", taglib.PROPERTY_TAG));

            // Properties found?
            if (nl == null || nl.Count == 0)
            {
                // No properties found... perhaps a collection?
                if (TypeInfo.IsCollection(parent.GetType()))
                {
                    SetCollectionValues((ICollection) parent, node);
                }
                else
                {
                    // Nothing to do here
                    return parent;
                }
            }

            // Loop the properties found
            foreach (XmlNode prop in nl)
            {
                // Collect the nodes type information about the property to deserialize
                ObjectInfo oi = GetObjectInfo(prop);

                // Enough info?
                if (oi.IsSufficient && !string.IsNullOrEmpty(oi.Name))
                {
                    object obj = null;
                    var type = oi.TypeOf(CreateType);

                    // Create an instance, but note: arrays always need the size for instantiation
                    if (type.IsArray)
                    {
                        obj = Array.CreateInstance(type.GetElementType(), GetArrayLength(prop));
                    }
                    else
                    {
                        obj = CreateInstance(oi, type);
                    }

                    // Process the property's properties (recursive call of this method)
                    if (obj != null)
                    {
                        GetProperties(obj, prop);
                    }

                    // Setting the instance (or null) as the property's value
                    PropertyInfo pi = parent.GetType().GetProperty(oi.Name);
                    if (obj != null && pi != null)
                    {
                        pi.SetValue(parent, obj, null);
                    }
                }
            }

            return parent;
        }

        #region Collections

        /// <summary>
        /// Sets the entries on an ICollection implementation.
        /// </summary>
        /// <param name="collection"></param>
        /// <param name="parentNode"></param>
        protected void SetCollectionValues(ICollection collection, XmlNode parentNode)
        {
            var collectionType = collection.GetType();
            if (TypeInfo.IsDictionary(collectionType))
            {
                // IDictionary
                SetDictionaryValues((IDictionary)collection, parentNode);
                return;
            }

            if (TypeInfo.IsList(collectionType))
            {
                // IList
                SetListValues((IList)collection, parentNode);
                return;
            }
        }

        /// <summary>
        /// Sets the entries on an IList implementation.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="parentNode"></param>
        protected void SetListValues(IList list, XmlNode parentNode)
        {
            // Get the item nodes
            XmlNodeList nlitems = parentNode.SelectNodes(string.Concat(taglib.ITEMS_TAG, "/", taglib.ITEM_TAG));

            var listType = list.GetType();
            Func<XmlNode, object> objectGetter = (n) => GetObject(n);

            if (listType.IsGenericType)
            {
                var type = listType.GetGenericArguments()[0];
                objectGetter = n => CreateInstance(type, n.InnerText);
            }

            if (listType.IsArray)
            {
                var type = listType.GetElementType();
                objectGetter = n => CreateInstance(type, n.InnerText);
            }

            // Loop them
            for (int i = 0; i < nlitems.Count; i++)
            {
                XmlNode nodeitem = nlitems[i];

                // Create an instance
                object obj = objectGetter(nodeitem);

                // Process the properties
                GetProperties(obj, nodeitem);

                if (list.IsFixedSize)
                    list[i] = obj;
                else
                    list.Add(obj);
            }
        }

        /// <summary>
        /// Sets the entries of an IDictionary implementation.
        /// </summary>
        /// <param name="dictionary"></param>
        /// <param name="parentNode"></param>
        protected void SetDictionaryValues(IDictionary dictionary, XmlNode parentNode)
        {
            // Get the item nodes
            XmlNodeList nlitems = parentNode.SelectNodes(string.Concat(taglib.ITEMS_TAG,  "/", taglib.ITEM_TAG));

            Func<XmlNode, object> objectKeyGetter = (n) => GetObject(n);
            Func<XmlNode, object> objectValGetter = (n) => GetObject(n);

            if (dictionary.GetType().IsGenericType)
            {
                var type = dictionary.GetType().GetInterfaces()
                    .First(it => it.IsGenericType && it.Name.StartsWith("IEnumerable"))
                    .GetGenericArguments()[0];

                objectKeyGetter = n => CreateInstance(type.GetGenericArguments()[0], n.InnerText);
                objectValGetter = n => CreateInstance(type.GetGenericArguments()[1], n.InnerText);
            }

            var propertyRequest = string.Concat(taglib.PROPERTIES_TAG, "/", taglib.PROPERTY_TAG, "[@", taglib.NAME_TAG,"='");

            // Loop them
            for (int i = 0; i < nlitems.Count; i++)
            {
                XmlNode nodeitem = nlitems[i];

                // Retrieve the single property
                string path = string.Concat(propertyRequest, taglib.NAME_ATT_KEY_TAG, "']");
                XmlNode nodekey = nodeitem.SelectSingleNode(path);

                path = string.Concat(propertyRequest, taglib.NAME_ATT_VALUE_TAG, "']");
                XmlNode nodeval = nodeitem.SelectSingleNode(path);

                // Create an instance of the key
                object objkey = null, objval = null;

                // Set the entry if the key is not null
                if ((objkey = objectKeyGetter(nodekey)) != null)
                {
                    // Try to get the value
                    if (nodeval != null)
                    {
                        if ((objval = objectValGetter(nodeval)) != null)
                            GetProperties(objval, nodeval);
                    }
                    dictionary.Add(objkey, objval);
                }
            }
        }

        #endregion Collections

        #endregion Properties & values

        #region Creating instances and types

        /// <summary>
        /// Creates an instance by the contents of the given XmlNode.
        /// </summary>
        /// <param name="node"></param>
        protected object GetObject(XmlNode node)
        {
            ObjectInfo oi = GetObjectInfo(node);
            var type = oi.TypeOf(CreateType);

            if (type.IsArray)
            {
                return Array.CreateInstance(type.GetElementType(), GetArrayLength(node));
            }

            return CreateInstance(oi, type);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        protected Assembly GetAssembly(string assemblyName)
        {
            Assembly assembly = null;

            // Cached already?
            if (assemblyCache.TryGetValue(assemblyName, out assembly))
            {
                return assembly;
            }

            // Shortnamed, version independent assembly name
            var assemblyShortName = assemblyName.Split(",".ToCharArray())[0];
            // Cached already?
            if (assemblyCache.TryGetValue(assemblyShortName, out assembly))
            {
                return assembly;
            }

            // TODO: Clean the upcoming code. Caching is handled above.

            // Try to get the Type in any way
            try
            {
                var path = Path.GetDirectoryName(assemblyName);
                if (!string.IsNullOrEmpty(path))
                {
                    // Assembly cached already?
                    if (!assemblyCache.TryGetValue(assemblyName, out assembly))
                    {
                        // Not cached, yet
                        assembly = Assembly.LoadFrom(path);
                        assemblyCache.Add(assemblyName, assembly);
                    }
                }
                else
                {
                    try
                    {
                        // Try to load the assembly version independent
                        if ((assembly = Assembly.Load(assemblyShortName)) != null)
                        { 
                            assemblyCache.Add(assemblyShortName, assembly);
                        }
                        else
                        {
                            if ((assembly = Assembly.Load(assemblyName)) != null)
                            {
                                assemblyCache.Add(assemblyName, assembly);
                            }
                        }
                    }
                    catch
                    {
                        // Loading the assembly version independent failed: load it with the given version.

                        if (!assemblyCache.TryGetValue(assemblyName, out assembly))
                        { 
                            if ((assembly = Assembly.Load(assemblyName)) != null)
                            {
                                assemblyCache.Add(assemblyName, assembly);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // ok, we did not get the Assembly 
            }

            return assembly;
        }


        /// <summary>
        /// Creates a type from the specified assembly and type names. 
        /// In case of failure null will be returned.
        /// </summary>
        /// <param name="assembly"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        protected Type CreateType(string assembly, string type)
        {
            try
            {
                Assembly a = default(Assembly);
                if ((a = GetAssembly(assembly)) != null)
                {
                    return a.GetType(type);
                }
            }
            catch (Exception)
            {
                // ok, we did not get the Type 
            }

            return default(Type);
        }

        /// <summary>
        /// Creates an instance by the specified ObjectInfo.
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        private object CreateInstance(ObjectInfo info, Type type = null)
        {
            // Enough information to create an instance?
            if (!info.IsSufficient)
                return null;

            // Get the Type
            if (type == null)
                type = info.TypeOf(CreateType);

            // Ok, we've got the Type, now try to create an instance.

            // Is there a binary constructor?
            if (!string.IsNullOrEmpty(info.ConstructorParamType))
            {
                object ctorparam = null;

                if (!String.IsNullOrEmpty(info.Value))
                {
                    byte[] barr = Convert.FromBase64String(info.Value);

                    Type ctorparamtype = CreateType(info.ConstructorParamAssembly, info.ConstructorParamType);

                    // What type of parameter is needed?
                    if (typeof(Stream).IsAssignableFrom(ctorparamtype))
                    {
                        // Stream
                        ctorparam = new MemoryStream(barr);
                    }
                    else if (typeof(byte[]).IsAssignableFrom(ctorparamtype))
                    {
                        // byte[]
                        ctorparam = barr;
                    }
                }

                return Activator.CreateInstance(type, new[] { ctorparam });
            }

            return CreateInstance(type, info.Value);
        }

        private object CreateInstance(Type type, string value)
        {
            try
            {
                // Ok, we've got the Type, now try to create an instance.

                // Until now only properties with binary data support constructors with parameters

                // Problem: only parameterless constructors or constructors with one parameter
                // which can be converted from String are supported.
                // Failure Example:
                // string s = new string();
                // string s = new string("");
                // This cannot be compiled, but the follwing works;
                // string s = new string("".ToCharArray());
                // The TypeConverter provides a way to instantite objects by non-parameterless 
                // constructors if they can be converted fro String
                try
                {
                    TypeConverter tc = GetConverter(type);
                    if (tc.CanConvertFrom(typeof(string)))
                    {
                        return tc.ConvertFrom(value);
                    }
                }
                catch (Exception)
                {
                }

                return Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                string msg = string.Format("Creation of an instance failed. Type: {0} Assembly: {1} Cause: {2}", type, type.Assembly, e.Message);
                if (IgnoreCreationErrors)
                {
                    return null;
                }
                throw new Exception(msg, e);
            }
        }


        #endregion Creating instances and types

        #region Misc

        /// <summary>
        /// Dispose, release references.
        /// </summary>
        public void Dispose()
        {
            Reset();

            if (assemblyCache != null)
                assemblyCache.Clear();

            if (assemblyRegister != null)
                assemblyRegister.Clear();

            if (typeConverterCache != null)
                typeConverterCache.Clear();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private TypeInfo TranslateTypeByKey(string key)
        {
            if (HasTypeDictionary)
            {
                TypeInfo ti = default(TypeInfo);
                if (typeDictionary.TryGetValue(key, out ti))
                {
                    return ti;
                }
            }

            return default(TypeInfo);
        }

        /// <summary>
        /// Gets an ObjectInfo instance by the attributes of the specified XmlNode.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private ObjectInfo GetObjectInfo(XmlNode node)
        {
            var oi = new ObjectInfo();

            string typekey = GetAttributeValue(node, taglib.TYPE_TAG) ?? XmlSerializationTag.SYSTEM_STRING_TYPE;
            TypeInfo ti = TranslateTypeByKey(typekey);

            if (ti != null)
            {
                oi.Type = ti.TypeName;
                oi.Assembly = ti.AssemblyName;
            }

            // If a TypeDictionary is given, did we find the necessary information to create an instance?
            // If not, try to get information by the Node itself
            if (!oi.IsSufficient)
            {
                oi.Type = GetAttributeValue(node, taglib.TYPE_TAG) ?? XmlSerializationTag.SYSTEM_STRING_TYPE;
                oi.Assembly = GetAttributeValue(node, taglib.ASSEMBLY_TAG) ?? XmlSerializationTag.MSCORE_LIBRARY;
            }

            // Name and Value
            oi.Name = GetAttributeValue(node, taglib.NAME_TAG);
            oi.Value = node.InnerText;
            
            return oi;
        }

        /// <summary>
        /// Returns the length of the array of a arry-XmlNode.
        /// </summary>
        /// <param name="parent"></param>
        /// <returns></returns>
        protected int GetArrayLength(XmlNode parent)
        {
            XmlNodeList nl = parent.SelectNodes(taglib.ITEMS_TAG + "/" + taglib.ITEM_TAG);
            return (nl != null) ? nl.Count : 0;
        }

        /// <summary>
        /// Returns the value or the attribute with the specified name from the given node if it is not null or empty.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        protected string GetAttributeValue(XmlNode node, string name)
        {
            if (node == null || string.IsNullOrEmpty(name))
                return null;

            string val = null;
            XmlAttribute att = node.Attributes[name];

            if (att != null)
            {
                val = att.Value;
                if (val.Equals(""))
                    val = null;
            }
            return val;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected bool HasBinaryConstructor(XmlNode node)
        {
            if (node == null)
                return false;

            XmlNode ctornode = node.SelectSingleNode(taglib.CONSTRUCTOR_TAG);
            if (ctornode == null)
                return false;

            XmlNode binnode = ctornode.SelectSingleNode(taglib.BINARY_DATA_TAG);
            if (binnode == null)
                return false;

            return true;
        }

        /// <summary>
        /// Returns the TypeConverter of a Type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        protected TypeConverter GetConverter(Type type)
        {
            TypeConverter retConverter = null;

            if (!typeConverterCache.TryGetValue(type, out retConverter))
            {
                retConverter = TypeDescriptor.GetConverter(type);
                typeConverterCache[type] = retConverter;
            }

            return retConverter;
        }

        /// <summary>
        /// Registers an Assembly.
        /// </summary>
        /// <param name="assembly"></param>
        /// <remarks>
        /// Register Assemblies which are not known at compile time, e.g. PlugIns or whatever.
        /// </remarks>
        public void RegisterAssembly(Assembly assembly)
        {
            string ass = assembly.FullName;

            int x = ass.IndexOf(",");
            if (x > 0)
                ass = ass.Substring(0, x);

            assemblyRegister[ass] = assembly;
        }

        /// <summary>
        /// Registers a list of assemblies.
        /// </summary>
        /// <param name="assemblies"></param>
        /// <returns></returns>
        public int RegisterAssemblies(List<Assembly> assemblies)
        {
            if (assemblies == null)
                return 0;

            int cnt = 0;

            foreach (Assembly ass in assemblies)
            {
                RegisterAssembly(ass);
                cnt++;
            }

            return cnt;
        }

        /// <summary>
        /// Registers a list of assemblies.
        /// </summary>
        /// <param name="assemblies"></param>
        /// <returns></returns>
        public int RegisterAssemblies(Assembly[] assemblies)
        {
            if (assemblies == null)
                return 0;

            int cnt = 0;

            for (int i = 0; i < assemblies.Length; i++)
            {
                RegisterAssembly(assemblies[i]);
                cnt++;
            }

            return cnt;
        }

        /// <summary>
        /// Adds the assembly register items to the assembly cache.
        /// </summary>
        protected void AddAssemblyRegisterToCache()
        {
            if (assemblyRegister != null)
            {
                foreach (var item in assemblyRegister)
                {
                    assemblyCache[item.Key] = item.Value;
                }
            }
        }

        /// <summary>
        /// Clears the typedictionary collection.
        /// </summary>
        public void Reset()
        {
            if (typeDictionary != null)
                typeDictionary.Clear();
        }

        #endregion Misc
    }
}