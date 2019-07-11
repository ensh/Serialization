namespace VTBF.BackOffice.Infrastructure.Utils.Serialization
{
    public class XmlSerializationTag
    {
        private const string OBJECT = "object";
        private const string NAME = "name";
        private const string TYPE = "type";
        private const string ASSEMBLY = "assembly";
        private const string PROPERTIES = "properties";
        private const string PROPERTY = "property";
        private const string ITEMS = "items";
        private const string ITEM = "item";
        private const string INDEX = "index";
        private const string NAME_ATT_KEY = "Key";
        private const string NAME_ATT_VALUE = "Value";
        private const string TYPE_DICTIONARY = "typedictionary";
        private const string GENERIC_TYPE_ARGUMENTS = "generictypearguments";
        private const string CONSTRUCTOR = "constructor";
        private const string BINARY_DATA = "binarydata";

        public const string MSCORE_LIBRARY = "mscorlib";
        public const string SYSTEM_STRING_TYPE = "System.String";

        public virtual string GENERIC_TYPE_ARGUMENTS_TAG
        {
            get { return GENERIC_TYPE_ARGUMENTS; }
        }

        public virtual string OBJECT_TAG
        {
            get { return OBJECT; }
        }

        public virtual string NAME_TAG
        {
            get { return NAME; }
        }

        public virtual string TYPE_TAG
        {
            get { return TYPE; }
        }

        public virtual string ASSEMBLY_TAG
        {
            get { return ASSEMBLY; }
        }

        public virtual string PROPERTIES_TAG
        {
            get { return PROPERTIES; }
        }

        public virtual string PROPERTY_TAG
        {
            get { return PROPERTY; }
        }

        public virtual string ITEMS_TAG
        {
            get { return ITEMS; }
        }

        public virtual string ITEM_TAG
        {
            get { return ITEM; }
        }

        public virtual string INDEX_TAG
        {
            get { return INDEX; }
        }

        public virtual string NAME_ATT_KEY_TAG
        {
            get { return NAME_ATT_KEY; }
        }

        public virtual string NAME_ATT_VALUE_TAG
        {
            get { return NAME_ATT_VALUE; }
        }

        public virtual string TYPE_DICTIONARY_TAG
        {
            get { return TYPE_DICTIONARY; }
        }

        public string CONSTRUCTOR_TAG
        {
            get { return CONSTRUCTOR; }
        }

        public string BINARY_DATA_TAG
        {
            get { return BINARY_DATA; }
        }
    }
}