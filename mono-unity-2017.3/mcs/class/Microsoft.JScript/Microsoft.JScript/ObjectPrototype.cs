//
// ObjectPrototype.cs
//
// Author:
//	Cesar Lopez Nataren (cesar@ciencias.unam.mx)
//
// (C) 2003, Cesar Lopez Nataren
//

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

using System;
using System.Reflection;

namespace Microsoft.JScript {

	public class ObjectPrototype : JSObject	{

		internal ObjectPrototype ()
		{
		}

		internal static ObjectPrototype Proto = new ObjectPrototype ();

		public static ObjectConstructor constructor {
			get { return ObjectConstructor.Ctr; }
		}

		[JSFunctionAttribute (JSFunctionAttributeEnum.HasThisObject, JSBuiltin.Object_hasOwnProperty)]
		public static bool hasOwnProperty (object thisObj, object name)
		{
			ScriptObject obj = thisObj as ScriptObject;
			if (obj == null)
				return false;
			string key = Convert.ToString (name);
			return LateBinding.DirectHasObjectProperty (obj, key);
		}

		[JSFunctionAttribute (JSFunctionAttributeEnum.HasThisObject, JSBuiltin.Object_isPrototypeOf)]
		public static bool isPrototypeOf (object thisObj, object obj)
		{
			throw new NotImplementedException ();
		}

		[JSFunctionAttribute (JSFunctionAttributeEnum.HasThisObject, JSBuiltin.Object_propertyIsEnumerable)]
		public static bool propertyIsEnumerable (object thisObj, object name)
		{
			if (thisObj == null || name == null)
				return false;
			// TODO: Implement me.
			// Type type = thisObj.GetType ();
			// FieldInfo res = type.GetField (Convert.ToString (name));
			return true;
		}

		[JSFunctionAttribute (JSFunctionAttributeEnum.HasThisObject, JSBuiltin.Object_toLocaleString)]
		public static string toLocaleString (object thisObj)
		{
			return toString (thisObj);
		}

		[JSFunctionAttribute (JSFunctionAttributeEnum.HasThisObject, JSBuiltin.Object_toString)]
		public static string toString (object thisObj)
		{
			if (thisObj is ScriptObject) {
				ScriptObject obj = (ScriptObject) thisObj;
				return "[object " + obj.ClassName + "]";
			} else
				throw new NotImplementedException ();
		}

		[JSFunctionAttribute (JSFunctionAttributeEnum.HasThisObject, JSBuiltin.Object_valueOf)]
		public static object valueOf (object thisObj)
		{
			return thisObj;
		}

		internal static object smartToString (JSObject thisObj)
		{
			JSObject obj = (JSObject) thisObj;
			object val = obj.GetDefaultValue (typeof (string), true);
			if (val == thisObj)
				return toString (thisObj);
			else
				return Convert.ToString (val);
		}
	}
}
