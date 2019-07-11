using System;

namespace VTBF.BackOffice.Infrastructure.Utils.Serialization
{
    internal struct ObjectInfo
    {
        public string Name;
        public string Type;
        public string Assembly;
        public string Value;
        public string ConstructorParamType { get; set; }
        public string ConstructorParamAssembly { get; set; }

        /// <summary>
        /// ToString()
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string n = Name;
            if (String.IsNullOrEmpty(n))
                n = "<Name not set>";

            string t = Type;
            if (String.IsNullOrEmpty(t))
                t = "<Type not set>";

            string a = Type;
            if (String.IsNullOrEmpty(a))
                a = "<Assembly not set>";

            return n + "; " + t + "; " + a;
        }
        /// <summary>
        /// Determines whether the values are sufficient to create an instance.
        /// </summary>
        /// <returns></returns>
        public bool IsSufficient
        {
            get
            {
                // Type and Assembly should be enough
                if (string.IsNullOrEmpty(Type) || string.IsNullOrEmpty(Assembly))
                    return false;

                return true;
            }
        }

        public Type TypeOf(Func<string, string, Type> typeGetter)
        {
            Type type = typeGetter(Assembly, Type);

            if (type == null)
            {
                throw new Exception("Assembly or Type not found.");
            }

            return type;
        }
    }
}