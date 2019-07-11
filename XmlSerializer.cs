using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

namespace VTBF.BackOffice.Infrastructure.Utils.Serialization
{
    public class XmlSerializer : IDisposable
    {
        #region Members

        private readonly HashSet<int> objlist = new HashSet<int>();

        private readonly XmlSerializationTag taglib = new XmlSerializationTag();

        #endregion Members

        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether [use deep serialization].
        /// </summary>
        /// <value>
        /// 	<c>true</c> if [use deep serialization]; otherwise, <c>false</c>.
        /// </value>
        public bool UseDeepSerialization { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [SerializeEntry primary properties only].
        /// </summary>
        /// <value>
        /// 	<c>true</c> if [SerializeEntry primary properties only]; otherwise, <c>false</c>.
        /// </value>
        public bool SerializePrimaryPropertiesOnly { get; set; }

        public bool SaveRootType { get; set; }

        #endregion Properties

        #region Serialize

        /// <summary>
        /// Serializes an Object to a new XmlDocument.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public XmlDocument Serialize(object obj)
        {
            var doc = new XmlDocument();
            Serialize(obj, null, doc);
            return doc;
        }

        /// <summary>
        /// Serializes an Object and appends it to root (DocumentElement) of the specified XmlDocument.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="doc"></param>
        public void Serialize(object obj, string name, XmlDocument doc)
        {
            XmlElement root = doc.CreateElement(taglib.OBJECT_TAG);

            var rootType = obj.GetType();
            rootType = (SaveRootType || rootType.IsArray) ? rootType : null;
            SetObjectInfoAttributes(name, rootType, root);

            if (doc.DocumentElement == null)
                doc.AppendChild(root);
            else
                doc.DocumentElement.AppendChild(root);

            SerializeProperties(obj, root);
        }

        /// <summary>
        /// Serializes an Object and appends it to the specified XmlNode.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="parent"></param>
        public void Serialize(object obj, string name, XmlNode parent)
        {
            XmlDocument doc = parent.OwnerDocument;

            XmlElement root = doc.CreateElement(taglib.OBJECT_TAG);
            parent.AppendChild(root);

            var rootType = obj.GetType();
            rootType = (SaveRootType || rootType.IsArray) ? rootType : null;
            SetObjectInfoAttributes(name, rootType, root);
            SerializeProperties(obj, root);
        }

        #endregion Serialize

        #region ObjectInfo

        /// <summary>
        /// Sets the property attributes of a Property to an XmlNode.
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="type"></param>
        /// <param name="node"></param>
        private void SetObjectInfoAttributes(Type type, XmlNode node)
        {
            if (type != null)
            {
                if (type != typeof(string))
                {
                    var att = node.OwnerDocument.CreateAttribute(taglib.TYPE_TAG);
                    att.Value = type.FullName;
                    node.Attributes.Append(att);
                }

                var assemblyName = type.Assembly.GetName().Name;
                if (assemblyName != XmlSerializationTag.MSCORE_LIBRARY)
                {
                    var att = node.OwnerDocument.CreateAttribute(taglib.ASSEMBLY_TAG);
                    att.Value = assemblyName;
                    node.Attributes.Append(att);
                }
            }
        }

        /// <summary>
        /// Sets the property attributes of a Property to an XmlNode.
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="type"></param>
        /// <param name="node"></param>
        private void SetObjectInfoAttributes(string propertyName, Type type, XmlNode node)
        {
            if (propertyName != null)
            {
                var att = node.OwnerDocument.CreateAttribute(taglib.NAME_TAG);
                att.Value = propertyName;
                node.Attributes.Append(att);
            }

            SetObjectInfoAttributes(type, node);
        }

        #endregion ObjectInfo

        #region Properties

        /// <summary>
        /// Returns wether the Property has to be serialized or not (depending on SerializationIgnoredAttributeType).
        /// </summary>
        /// <param name="pi"></param>
        /// <returns></returns>
        private bool CheckPropertyHasToBeSerialized(PropertyInfo pi)
        {
            // TODO : Add a custom check here 
            var attributes = pi.GetCustomAttributes(typeof(XmlIgnoreAttribute), false);
            return attributes.Length == 0;
        }

        /// <summary>
        /// Serializes the properties an Object and appends them to the specified XmlNode.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="parent"></param>
        private void SerializeProperties(object obj, XmlNode parent)
        {
            if (TypeInfo.IsCollection(obj.GetType()))
            {
                SetCollectionItems((ICollection)obj, parent);
            }
            else
            {
                XmlElement node = parent.OwnerDocument.CreateElement(taglib.PROPERTIES_TAG);
                SetProperties(obj, node);
                parent.AppendChild(node);
            }
        }

        private void SetProperties(object obj, XmlElement node, bool includeTypeInfo = true)
        {
            foreach (PropertyInfo pi in obj.GetType().GetProperties())
            {
                if (pi.GetIndexParameters().Length == 0)
                    SetXmlPropertyValue(obj, pi, node, includeTypeInfo);
            }
        }

        /// <summary>
        /// Sets a property.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="propertyInfo"></param>
        /// <param name="parent"></param>
        private void SetXmlPropertyValue(object obj, PropertyInfo propertyInfo, XmlNode parent, bool includeTypeInfo = true)
        {
            objlist.Add(obj.GetHashCode());
            object val = propertyInfo.GetValue(obj, null);
            // If the value there's nothing to do
            // Empty values are ignored (no need to restore null references or empty strings)
            if (val == null || val.Equals(string.Empty))
                return;

            // If the the value already exists in the list of processed objects/properties
            // ignore it o avoid circular calls.
            if (objlist.Contains(val.GetHashCode()))
                return;

            SetXmlPropertyValue(obj, val, propertyInfo, parent, includeTypeInfo);
        }

        /// <summary>
        /// Sets a property.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="propertyInfo"></param>
        /// <param name="parent"></param>
        /// <remarks>
        /// This is the central method which is called recursively!
        /// </remarks>
        private void SetXmlPropertyValue(object obj, object propertyValue, PropertyInfo propertyInfo, XmlNode parent, bool includeTypeInfo)
        {
            try
            {
                // Get the Type
                Type propertyType = propertyValue.GetType();

                // Check whether this property can be serialized and deserialized
                if (CheckPropertyHasToBeSerialized(propertyInfo) && (propertyType.IsPublic || propertyType.IsEnum))
                {
                    if (TypeInfo.IsCollection(propertyType))
                    {
                        if (UseDeepSerialization || (SerializePrimaryPropertiesOnly
                            // TODO: Implement
                            //&& pt.GetGenericArguments().Where(x => x.IsSubclassOf(typeof(EntityPropertyBase))).Count() != 0))
                            ))
                        {
                            XmlElement prop = parent.OwnerDocument.CreateElement(taglib.PROPERTY_TAG);
                            SetObjectInfoAttributes(propertyInfo.Name, (includeTypeInfo) ? propertyType : null, prop);
                            SetCollectionItems((ICollection)propertyValue, prop);
                            // Append the property node to the paren XmlNode
                            parent.AppendChild(prop);
                        }
                    }
                    else
                    {
                        if (
                            // normal classes like guid, decimal etc
                            (propertyType.IsAnsiClass && propertyType.IsSealed)
                            ||
                            // classes with deep serialization turned on
                            (!propertyType.IsSealed && propertyType.IsClass && UseDeepSerialization)
                            // TODO: Implement
                            // ||
                            // object state with turned property
                            //(pt.IsSubclassOf(typeof(EntityBase)) && serializePrimaryPropertiesOnly)
                            )
                        {
                            XmlElement prop = parent.OwnerDocument.CreateElement(taglib.PROPERTY_TAG);
                            SetObjectInfoAttributes(propertyInfo.Name, (includeTypeInfo) ? propertyType : null, prop);
                            SetXmlElementFromBasicPropertyValue(prop, propertyType, propertyValue, parent);
                            // Append the property node to the paren XmlNode
                            parent.AppendChild(prop);
                        }
                    }
                }
            }
            catch (Exception)
            { }
        }

        private void SetXmlElementFromBasicPropertyValue(XmlElement prop, Type type, object value, XmlNode parent)
        {
            // If possible, convert this property to a string
            if (value is string || type == typeof(string))
            {
                prop.InnerText = value.ToString();
                return;
            }

            TypeConverter tc = TypeDescriptor.GetConverter(type);
            if (tc.CanConvertFrom(typeof(string)) && tc.CanConvertTo(typeof(string)))
            {
                prop.InnerText = (string)tc.ConvertTo(value, typeof(string));
                return;
            }

            // TODO: implement property serialization
            //if (pt.IsSubclassOf(typeof(EntityPropertyBase))
            //    && !useDeepSerialization)
            //{
            //    if (value is EntityPropertyBase)
            //    {
            //        prop.InnerText = (value as EntityPropertyBase).PropertyName;
            //        return;
            //    }
            //}

            //if (pt.IsSubclassOf(typeof(EntityBase))
            //    && !useDeepSerialization)
            //{
            //    if (value is DictionaryEntityBase)
            //    {
            //        prop.InnerText = (value as DictionaryEntityBase).Name;
            //        return;
            //    }
            //    if (value is EntityBase)
            //    {
            //        prop.InnerText = (value as EntityBase).Id.ToString();
            //        return;
            //    }
            //}
            bool complexclass = false; // Holds whether the propertys type is an complex type (the properties of objects have to be iterated, either)

            // Get all properties
            PropertyInfo[] piarr2 = type.GetProperties();
            XmlElement proplist = null;

            // Loop all properties
            for (int j = 0; j < piarr2.Length; j++)
            {
                PropertyInfo pi2 = piarr2[j];
                // Check whether this property can be serialized and deserialized
                if (CheckPropertyHasToBeSerialized(pi2) && (pi2.CanWrite) && ((pi2.PropertyType.IsPublic) || (pi2.PropertyType.IsEnum)))
                {
                    // Seems to be a complex type
                    complexclass = true;

                    // Add a properties parent node
                    if (proplist == null)
                    {
                        proplist = parent.OwnerDocument.CreateElement(taglib.PROPERTIES_TAG);
                        prop.AppendChild(proplist);
                    }

                    // Set the property (recursive call of this method!)
                    SetXmlPropertyValue(value, pi2, proplist);
                }
            }

            // Ok, that was not a complex class either
            if (UseDeepSerialization && !complexclass)
            {
                // Converting to string was not possible, just set the value by Tostring()
                prop.InnerText = value.ToString();
            }
        }

        #endregion Properties

        #region SetCollectionItems

        /// <summary>
        /// Sets the items on a collection.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="pi"></param>
        /// <param name="parent"></param>
        /// <remarks>
        /// This method could be simplified since it's mainly the same code you can find in SetProperty()
        /// </remarks>
        private void SetCollectionItems(ICollection collection, XmlNode parent)
        {
            // Validating the parameters
            if (collection == null || parent == null)
                return;

            try
            {
                XmlElement collnode = parent.OwnerDocument.CreateElement(taglib.ITEMS_TAG);
                parent.AppendChild(collnode);

                var collectionType = collection.GetType();
                Action<object, XmlElement> applyCurrent = SetCollectionItem;

                // What kind of Collection?
                if (TypeInfo.IsDictionary(collectionType))
                {
                    // IDictionary
                    bool includeTypeInfo = !collection.GetType().IsGenericType;
                    applyCurrent = (o, p) => SetDictionaryItem(o, p, includeTypeInfo);
                }
                else
                {
                    if (collectionType.IsGenericType)
                    {
                        var itemType = collectionType.GetGenericArguments()[0];
                        applyCurrent = (o, p) => SetCollectionItem(o, itemType, p);
                    }

                    if (collectionType.IsArray)
                    {
                        var itemType = collectionType.GetElementType();
                        applyCurrent = (o, p) => SetCollectionItem(o, itemType, p);
                    }
                }

                // Everything else
                IEnumerator ie = collection.GetEnumerator();
                while (ie.MoveNext())
                {
                    applyCurrent(ie.Current, collnode);
                }
            }
            catch (Exception)
            { }
        }

        private void SetDictionaryItem(object obj, XmlNode parent, bool includeTypeInfo)
        {
            XmlElement itemnode = parent.OwnerDocument.CreateElement(taglib.ITEM_TAG);
            parent.AppendChild(itemnode);

            XmlElement propsnode = parent.OwnerDocument.CreateElement(taglib.PROPERTIES_TAG);
            itemnode.AppendChild(propsnode);

            SetProperties(obj, propsnode, includeTypeInfo);
        }

        private void SetCollectionItem(object item, XmlNode parent)
        {
            SetCollectionItem(item, null, parent);
        }

        private void SetCollectionItem(object item, Type type, XmlNode parent)
        {
            XmlElement itemnode = parent.OwnerDocument.CreateElement(taglib.ITEM_TAG);

            if (type == null)
            {
                type = item.GetType();
                SetObjectInfoAttributes(type, itemnode);
            }

            parent.AppendChild(itemnode);

            if (item != null)
            {
                if (TypeInfo.IsCollection(type))
                {
                    SetCollectionItems((ICollection)item, itemnode);
                }
                else
                {
                    SetXmlElementFromBasicPropertyValue(itemnode, type, item, parent);
                }
            }
        }

        #endregion SetCollectionItems

        #region Misc

        /// <summary>
        /// Dispose, release references.
        /// </summary>
        public void Dispose()
        {
            Reset();
        }

        /// <summary>
        /// Clears the Collections.
        /// </summary>
        public void Reset()
        {
            if (objlist != null)
                objlist.Clear();
        }

        #endregion Misc
    }
}