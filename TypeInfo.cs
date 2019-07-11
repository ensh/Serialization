using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace VTBF.BackOffice.Infrastructure.Utils.Serialization
{
    [Serializable]
    internal class TypeInfo
    {
        #region Members & Properties

        private string typename;
        private string assemblyname;

        /// <summary>
        /// Gets or sets the Types name.
        /// </summary>
        public string TypeName
        {
            get { return typename; }
            set { typename = value; }
        }

        /// <summary>
        /// Gets or sets the Assemblys name.
        /// </summary>
        public string AssemblyName
        {
            get { return assemblyname; }
            set { assemblyname = value; }
        }

        #endregion Members & Properties

        #region Constructors

        /// <summary>
        /// Constructor.
        /// </summary>
        public TypeInfo()
        {
        }

        #endregion Constructors

        #region static Helpers

        /// <summary>
        /// Determines whether a Type is a Collection type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool IsCollection(Type type)
        {
            return typeof(ICollection).IsAssignableFrom(type);
        }

        /// <summary>
        /// Determines whether a Type is a Dictionary type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool IsDictionary(Type type)
        {
            return typeof(IDictionary).IsAssignableFrom(type);
        }
        /// <summary>
        /// Determines whether a Type is a List type.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public static bool IsList(Type type)
        {
            return typeof(IList).IsAssignableFrom(type);
        }

        #endregion static Helpers
    }
}