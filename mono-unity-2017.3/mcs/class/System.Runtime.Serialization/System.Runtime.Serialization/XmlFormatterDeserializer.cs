//
// XmlFormatterDeserializer.cs
//
// Author:
//	Atsushi Enomoto <atsushi@ximian.com>
//
// Copyright (C) 2005 Novell, Inc.  http://www.novell.com
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
#if NET_2_0
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml;
using System.Xml.Schema;

using QName = System.Xml.XmlQualifiedName;

namespace System.Runtime.Serialization
{
	internal class XmlFormatterDeserializer
	{
		KnownTypeCollection types;
		IDataContractSurrogate surrogate;
		// 3.5 SP1 supports deserialization by reference (id->obj).
		// Though unlike XmlSerializer, it does not support forward-
		// reference resolution i.e. a referenced object must appear
		// before any references to it.
		Hashtable references = new Hashtable ();

		public static object Deserialize (XmlReader reader, Type type,
			KnownTypeCollection knownTypes, IDataContractSurrogate surrogate,
			string name, string ns, bool verifyObjectName)
		{
			reader.MoveToContent ();
			if (verifyObjectName)
				if (reader.NodeType != XmlNodeType.Element ||
				    reader.LocalName != name ||
				    reader.NamespaceURI != ns)
					throw new SerializationException (String.Format ("Expected element '{0}' in namespace '{1}', but found {2} node '{3}' in namespace '{4}'", name, ns, reader.NodeType, reader.LocalName, reader.NamespaceURI));
//				Verify (knownTypes, type, name, ns, reader);
			return new XmlFormatterDeserializer (knownTypes, surrogate).Deserialize (type, reader);
		}

		// Verify the top element name and namespace.
		private static void Verify (KnownTypeCollection knownTypes, Type type, string name, string Namespace, XmlReader reader)
		{
			QName graph_qname = new QName (reader.Name, reader.NamespaceURI);
			if (graph_qname.Name == name && graph_qname.Namespace == Namespace)
				return;

			// <BClass .. i:type="EClass" >..</BClass>
			// Expecting type EClass : allowed
			// See test Serialize1b, and Serialize1c (for
			// negative cases)

			// Run through inheritance heirarchy .. 
			for (Type baseType = type; baseType != null; baseType = baseType.BaseType)
				if (knownTypes.GetQName (baseType) == graph_qname)
					return;

			QName typeQName = knownTypes.GetQName (type);
			throw new SerializationException (String.Format (
				"Expecting element '{0}' from namespace '{1}'. Encountered 'Element' with name '{2}', namespace '{3}'",
				typeQName.Name, typeQName.Namespace, graph_qname.Name, graph_qname.Namespace));
		}

		private XmlFormatterDeserializer (
			KnownTypeCollection knownTypes,
			IDataContractSurrogate surrogate)
		{
			this.types = knownTypes;
			this.surrogate = surrogate;
		}

		public Hashtable References {
			get { return references; }
		}

		// At the beginning phase, we still have to instantiate a new
		// target object even if fromContent is true.
		public object Deserialize (Type type, XmlReader reader)
		{
			string label = reader.GetAttribute ("Id", KnownTypeCollection.MSSimpleNamespace);
			object o = DeserializeCore (type, reader);

			if (label != null)
				references.Add (label, o);

			return o;
		}

		public object DeserializeCore (Type type, XmlReader reader)
		{
			QName graph_qname = types.GetQName (type);
			string itype = reader.GetAttribute ("type", XmlSchema.InstanceNamespace);
			if (itype != null) {
				string[] parts = itype.Split (':');
				if (parts.Length > 1)
					graph_qname = new QName (parts [1], reader.LookupNamespace (reader.NameTable.Get (parts[0])));
				else
					graph_qname = new QName (itype, reader.NamespaceURI);
			}

			string label = reader.GetAttribute ("Ref", KnownTypeCollection.MSSimpleNamespace);
			if (label != null) {
				object o = references [label];
				if (o == null)
					throw new SerializationException (String.Format ("Deserialized object with reference Id '{0}' was not found", label));
				reader.Skip ();
				return o;
			}

			bool isNil = reader.GetAttribute ("nil", XmlSchema.InstanceNamespace) == "true";

			if (isNil) {
				reader.Skip ();
				if (!type.IsValueType)
					return null;
				else if (type.IsGenericType && type.GetGenericTypeDefinition () == typeof (Nullable<>))
					return null;
				else 
					throw new SerializationException (String.Format ("Value type {0} cannot be null.", type));
			}

			if (KnownTypeCollection.GetPrimitiveTypeFromName (graph_qname.Name) != null) {
				string value;
				if (reader.IsEmptyElement) {
					reader.Read (); // advance
					if (type.IsValueType)
						return Activator.CreateInstance (type);
					else
						// FIXME: Workaround for creating empty objects of the correct type.
						value = String.Empty;
				}
				else
					value = reader.ReadElementContentAsString ();
				return KnownTypeCollection.PredefinedTypeStringToObject (value, graph_qname.Name, reader);
			}

			return DeserializeByMap (graph_qname, type, reader);
		}

		object DeserializeByMap (QName name, Type type, XmlReader reader)
		{
			SerializationMap map = types.FindUserMap (name);
			if (map == null && (name.Namespace == KnownTypeCollection.MSArraysNamespace ||
			    name.Namespace.StartsWith (KnownTypeCollection.DefaultClrNamespaceBase, StringComparison.Ordinal))) {
				var it = GetTypeFromNamePair (name.Name, name.Namespace);
				types.TryRegister (it);
				map = types.FindUserMap (name);
			}
			if (map == null)
				throw new SerializationException (String.Format ("Unknown type {0} is used for DataContract with reference of name {1}. Any derived types of a data contract or a data member should be added to KnownTypes.", type, name));

			return map.DeserializeObject (reader, this);
		}

		Type GetTypeFromNamePair (string name, string ns)
		{
			Type p = KnownTypeCollection.GetPrimitiveTypeFromName (name); // FIXME: namespace?
			if (p != null)
				return p;
			if (name.StartsWith ("ArrayOf", StringComparison.Ordinal) && ns == KnownTypeCollection.MSArraysNamespace)
				return GetTypeFromNamePair (name.Substring (7), String.Empty).MakeArrayType ();

			int xlen = KnownTypeCollection.DefaultClrNamespaceBase.Length;
			string clrns = ns.Length > xlen ?  ns.Substring (xlen) : null;

			foreach (var ass in AppDomain.CurrentDomain.GetAssemblies ()) {
				foreach (var t in ass.GetTypes ()) {
					var dca = t.GetCustomAttribute<DataContractAttribute> (true);
					if (dca != null && dca.Name == name && dca.Namespace == ns)
						return t;
					if (clrns != null && t.Name == name && t.Namespace == clrns)
						return t;
				}
			}
			throw new XmlException (String.Format ("Type not found; name: {0}, namespace: {1}", name, ns));
		}
	}
}
#endif
